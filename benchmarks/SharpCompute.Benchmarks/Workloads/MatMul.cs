using System;
using System.Numerics.Tensors;
using System.Threading.Tasks;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary>Naive <c>C = A · B</c>, n × n, row-major. The CPU variants use Bᵀ (transposed once at setup, not timed).</summary>
public sealed class MatMul : IDisposable
{
    public MatMul(int n, GraphicsDevice device)
    {
        N = n;
        Random random = new(3);
        A = new float[n * n];
        B = new float[n * n];

        for (int i = 0; i < A.Length; i++)
        {
            A[i] = random.NextSingle();
            B[i] = random.NextSingle();
        }

        BTransposed = new float[n * n];

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                BTransposed[(c * n) + r] = B[(r * n) + c];
            }
        }

        C = new float[n * n];
        Device = device;
        GpuA = device.AllocateReadOnlyBuffer<float>(A);
        GpuB = device.AllocateReadOnlyBuffer<float>(B);
        GpuC = device.AllocateReadWriteBuffer<float>(n * n);
    }

    public int N { get; }

    public float[] A { get; }

    public float[] B { get; }

    public float[] BTransposed { get; }

    public float[] C { get; }

    public GraphicsDevice Device { get; }

    public ReadOnlyBuffer<float> GpuA { get; }

    public ReadOnlyBuffer<float> GpuB { get; }

    public ReadWriteBuffer<float> GpuC { get; }

    /// <summary>Parallel.For over rows; scalar dot product over a row of A and a row of Bᵀ (same summation order as the GPU).</summary>
    public float CpuParallel()
    {
        Parallel.For(0, N, row =>
        {
            int n = N;
            ReadOnlySpan<float> a = A.AsSpan(row * n, n);
            Span<float> c = C.AsSpan(row * n, n);

            for (int col = 0; col < n; col++)
            {
                ReadOnlySpan<float> b = BTransposed.AsSpan(col * n, n);
                float sum = 0f;

                for (int k = 0; k < n; k++)
                {
                    sum += a[k] * b[k];
                }

                c[col] = sum;
            }
        });

        return C[C.Length / 2];
    }

    /// <summary>Parallel.For over rows; SIMD dot product (<see cref="TensorPrimitives.Dot(ReadOnlySpan{float}, ReadOnlySpan{float})"/>).</summary>
    public float CpuParallelSimd()
    {
        Parallel.For(0, N, row =>
        {
            int n = N;
            ReadOnlySpan<float> a = A.AsSpan(row * n, n);
            Span<float> c = C.AsSpan(row * n, n);

            for (int col = 0; col < n; col++)
            {
                c[col] = TensorPrimitives.Dot(a, BTransposed.AsSpan(col * n, n));
            }
        });

        return C[C.Length / 2];
    }

    public float Gpu()
    {
        Device.For(N, N, new MatMulNaive(GpuA, GpuB, GpuC, N));
        return GpuC[GpuC.Length / 2];
    }

    /// <summary>
    /// The same product on the GPU's matrix units (<see cref="MatMulWaveMatrix"/>): one threadgroup of
    /// 128 threads per 32 × 64 tile of C. N must be a multiple of 64.
    /// </summary>
    public float GpuWaveMatrix()
    {
        if (N % MatMulWaveMatrix.TileColumns != 0)
        {
            throw new InvalidOperationException($"The wave-matrix product needs a size that is a multiple of {MatMulWaveMatrix.TileColumns}, not {N}.");
        }

        Device.For(N / MatMulWaveMatrix.TileColumns * MatMulWaveMatrix.GroupThreads, N / MatMulWaveMatrix.TileRows, new MatMulWaveMatrix(GpuA, GpuB, GpuC, N));
        return GpuC[GpuC.Length / 2];
    }

    public void Dispose()
    {
        GpuA.Dispose();
        GpuB.Dispose();
        GpuC.Dispose();
    }
}
