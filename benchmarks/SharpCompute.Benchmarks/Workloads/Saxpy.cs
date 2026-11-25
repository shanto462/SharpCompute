using System;
using System.Numerics.Tensors;
using System.Threading.Tasks;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary><c>y = a * x + y</c> in place, on the CPU (managed arrays) and the GPU (shared buffers).</summary>
public sealed class Saxpy : IDisposable
{
    public const float A = 0.75f;

    private readonly float[] _y0;

    public Saxpy(int length, GraphicsDevice device)
    {
        Length = length;
        Random random = new(42);
        X = new float[length];
        _y0 = new float[length];

        for (int i = 0; i < length; i++)
        {
            X[i] = random.NextSingle();
            _y0[i] = random.NextSingle();
        }

        Y = (float[])_y0.Clone();
        Device = device;
        GpuX = device.AllocateReadOnlyBuffer<float>(X);
        GpuY = device.AllocateReadWriteBuffer<float>(_y0);
    }

    public int Length { get; }

    public float[] X { get; }

    public float[] Y { get; }

    public GraphicsDevice Device { get; }

    public ReadOnlyBuffer<float> GpuX { get; }

    public ReadWriteBuffer<float> GpuY { get; }

    /// <summary>Restores the initial <c>y</c> on both sides (only --verify needs this; the benchmarks just keep accumulating).</summary>
    public void Reset()
    {
        _y0.CopyTo(Y, 0);
        GpuY.CopyFrom(_y0);
    }

    /// <summary>Scalar, single thread. RyuJIT does not contract <c>a * x + y</c>, so this is the unfused form.</summary>
    public float CpuScalar()
    {
        float[] x = X, y = Y;

        for (int i = 0; i < y.Length; i++)
        {
            y[i] = (A * x[i]) + y[i];
        }

        return y[^1];
    }

    /// <summary>The fused form <c>fma(a, x, y)</c>, which Metal's default FP contraction may produce. Reference only.</summary>
    public static void CpuFused(float[] x, float[] y)
    {
        for (int i = 0; i < y.Length; i++)
        {
            y[i] = MathF.FusedMultiplyAdd(A, x[i], y[i]);
        }
    }

    /// <summary>SIMD, single thread: <see cref="TensorPrimitives.MultiplyAdd(ReadOnlySpan{float}, float, ReadOnlySpan{float}, Span{float})"/>.</summary>
    public float CpuSimd()
    {
        TensorPrimitives.MultiplyAdd(X, A, Y, Y);
        return Y[^1];
    }

    /// <summary>Parallel.For over fixed-size chunks, each chunk SIMD through TensorPrimitives.</summary>
    public float CpuParallelSimd()
    {
        const int Chunk = 1 << 16;
        float[] x = X, y = Y;
        int chunks = (y.Length + Chunk - 1) / Chunk;

        Parallel.For(0, chunks, c =>
        {
            int start = c * Chunk;
            int count = Math.Min(Chunk, y.Length - start);
            TensorPrimitives.MultiplyAdd(x.AsSpan(start, count), A, y.AsSpan(start, count), y.AsSpan(start, count));
        });

        return y[^1];
    }

    public float GpuGenerated()
    {
        Device.For(Length, new SaxpyGenerated(GpuX, GpuY, A));
        return GpuY[Length - 1];
    }

    public float GpuHandWritten()
    {
        Device.For(Length, new SaxpyHandWritten(GpuX, GpuY, A));
        return GpuY[Length - 1];
    }

    /// <summary>Four elements per thread (<see cref="SaxpyFloat4HandWritten"/>); the length must be a multiple of 4.</summary>
    public void GpuHandWrittenFloat4() => Device.For(Length / 4, new SaxpyFloat4HandWritten(GpuX, GpuY, A));

    public void Dispose()
    {
        GpuX.Dispose();
        GpuY.Dispose();
    }
}
