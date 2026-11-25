using System;
using System.Threading.Tasks;
using SharpCompute.Benchmarks.Native;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary>
/// Single-precision <c>C = A · B</c> for n × n row-major matrices, every way the native baselines
/// time it: the generated naive and threadgroup-tiled kernels, the naive kernel written by hand,
/// SGEMM on Metal's matrix units written by hand, and <c>MPSMatrixMultiplication</c>. All of them
/// read the same two buffers and write the same third.
/// </summary>
/// <remarks>
/// The inputs are uniform random numbers in [0, 1) from a fixed seed. A periodic fill (a short
/// repeating pattern) would make rows of A and columns of B repeat, which says nothing about speed
/// but lets a kernel that reads the wrong block of a matrix still produce the right product.
/// </remarks>
public sealed class Sgemm : IDisposable
{
    public Sgemm(int n, GraphicsDevice device)
    {
        if (n <= 0 || n % 64 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "The SGEMM kernels need n to be a positive multiple of 64.");
        }

        N = n;
        Random random = new(20261126 + n);
        A = new float[n * n];
        B = new float[n * n];

        for (int i = 0; i < A.Length; i++)
        {
            A[i] = random.NextSingle();
            B[i] = random.NextSingle();
        }

        Device = device;
        GpuA = device.AllocateReadOnlyBuffer<float>(A);
        GpuB = device.AllocateReadOnlyBuffer<float>(B);
        GpuC = device.AllocateReadWriteBuffer<float>(n * n);
        Mps = new MpsMatrixMultiply(device, n, GpuA, GpuB, GpuC);
    }

    public int N { get; }

    public float[] A { get; }

    public float[] B { get; }

    public GraphicsDevice Device { get; }

    public ReadOnlyBuffer<float> GpuA { get; }

    public ReadOnlyBuffer<float> GpuB { get; }

    public ReadWriteBuffer<float> GpuC { get; }

    public MpsMatrixMultiply Mps { get; }

    /// <summary>FP32 operations of one product: n³ multiplies and n³ adds.</summary>
    public double Flops => 2.0 * N * N * N;

    /// <summary>
    /// The product on the CPU, every element summed over k in ascending order in single precision:
    /// the order of the naive and the tiled kernels. (Parallel over rows; B is transposed first so the
    /// inner loop walks memory, which changes no sum.)
    /// </summary>
    public float[] CpuReference()
    {
        int n = N;
        float[] transposed = new float[n * n];
        float[] c = new float[n * n];

        Parallel.For(0, n, r =>
        {
            for (int k = 0; k < n; k++)
            {
                transposed[(k * n) + r] = B[(r * n) + k];
            }
        });

        Parallel.For(0, n, row =>
        {
            ReadOnlySpan<float> a = A.AsSpan(row * n, n);

            for (int col = 0; col < n; col++)
            {
                ReadOnlySpan<float> b = transposed.AsSpan(col * n, n);
                float sum = 0f;

                for (int k = 0; k < n; k++)
                {
                    sum += a[k] * b[k];
                }

                c[(row * n) + col] = sum;
            }
        });

        return c;
    }

    public void GeneratedNaive() => Device.For(N, N, new MatMulNaive(GpuA, GpuB, GpuC, N));

    public void GeneratedTiled() => Device.For(N, N, new MatMulTiled(GpuA, GpuB, GpuC, N));

    public void HandWrittenNaive() => Device.For(N, N, new MatMulNaiveHandWritten(GpuA, GpuB, GpuC, N));

    public void HandWrittenSimdgroup() =>
        Device.For(SgemmSimdgroupHandWritten.GridWidth(N), SgemmSimdgroupHandWritten.GridHeight(N), new SgemmSimdgroupHandWritten(GpuA, GpuB, GpuC, N));

    public void Dispose()
    {
        Mps.Dispose();
        GpuA.Dispose();
        GpuB.Dispose();
        GpuC.Dispose();
    }
}
