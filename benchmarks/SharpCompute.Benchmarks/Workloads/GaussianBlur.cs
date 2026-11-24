using System;
using System.Numerics.Tensors;
using System.Threading.Tasks;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary>
/// Separable Gaussian blur of a single-channel float image: horizontal pass into a temp buffer, then
/// vertical pass into the output, clamping at the edges. Every variant sums the taps of a pixel in
/// the same order (k = −r … r), so they differ only by rounding (and fma contraction on the GPU).
/// </summary>
public sealed class GaussianBlur : IDisposable
{
    public GaussianBlur(int width, int height, int radius, GraphicsDevice device)
    {
        Width = width;
        Height = height;
        Radius = radius;
        Weights = MakeWeights(radius);

        Random random = new(7);
        Source = new float[width * height];

        for (int i = 0; i < Source.Length; i++)
        {
            Source[i] = random.NextSingle();
        }

        Temp = new float[width * height];
        Output = new float[width * height];

        Device = device;
        GpuSource = device.AllocateReadOnlyBuffer<float>(Source);
        GpuWeights = device.AllocateReadOnlyBuffer<float>(Weights);
        GpuTemp = device.AllocateReadWriteBuffer<float>(width * height);
        GpuOutput = device.AllocateReadWriteBuffer<float>(width * height);
    }

    public int Width { get; }

    public int Height { get; }

    public int Radius { get; }

    public float[] Weights { get; }

    public float[] Source { get; }

    public float[] Temp { get; }

    public float[] Output { get; }

    public GraphicsDevice Device { get; }

    public ReadOnlyBuffer<float> GpuSource { get; }

    public ReadOnlyBuffer<float> GpuWeights { get; }

    public ReadWriteBuffer<float> GpuTemp { get; }

    public ReadWriteBuffer<float> GpuOutput { get; }

    /// <summary>Normalized weights for σ = radius / 2.</summary>
    public static float[] MakeWeights(int radius)
    {
        float sigma = radius / 2f;
        float[] weights = new float[(2 * radius) + 1];
        float total = 0f;

        for (int k = -radius; k <= radius; k++)
        {
            total += weights[k + radius] = MathF.Exp(-(k * k) / (2f * sigma * sigma));
        }

        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] /= total;
        }

        return weights;
    }

    /// <summary>Scalar, single thread, both passes.</summary>
    public float CpuScalar()
    {
        for (int y = 0; y < Height; y++)
        {
            HorizontalRowScalar(y);
        }

        for (int y = 0; y < Height; y++)
        {
            VerticalRowScalar(y);
        }

        return Output[Output.Length / 2];
    }

    /// <summary>Parallel.For per row for each pass, scalar per pixel.</summary>
    public float CpuParallel()
    {
        Parallel.For(0, Height, HorizontalRowScalar);
        Parallel.For(0, Height, VerticalRowScalar);
        return Output[Output.Length / 2];
    }

    /// <summary>Parallel.For per row for each pass, each tap applied to a whole row with TensorPrimitives (SIMD).</summary>
    public float CpuParallelSimd()
    {
        Parallel.For(0, Height, HorizontalRowSimd);
        Parallel.For(0, Height, VerticalRowSimd);
        return Output[Output.Length / 2];
    }

    /// <summary>Two dispatches (each its own command buffer + wait), horizontal then vertical.</summary>
    public float Gpu()
    {
        Device.For(Width, Height, new BlurHorizontal(GpuSource, GpuTemp, GpuWeights, Width, Radius));
        Device.For(Width, Height, new BlurVertical(GpuTemp, GpuOutput, GpuWeights, Width, Height, Radius));
        return GpuOutput[GpuOutput.Length / 2];
    }

    public void Dispose()
    {
        GpuSource.Dispose();
        GpuWeights.Dispose();
        GpuTemp.Dispose();
        GpuOutput.Dispose();
    }

    private void HorizontalRowScalar(int y)
    {
        int width = Width, r = Radius;
        ReadOnlySpan<float> src = Source.AsSpan(y * width, width);
        Span<float> dst = Temp.AsSpan(y * width, width);
        float[] w = Weights;

        for (int x = 0; x < width; x++)
        {
            float sum = 0f;

            for (int k = -r; k <= r; k++)
            {
                sum += w[k + r] * src[Math.Clamp(x + k, 0, width - 1)];
            }

            dst[x] = sum;
        }
    }

    private void VerticalRowScalar(int y)
    {
        int width = Width, r = Radius;
        Span<float> dst = Output.AsSpan(y * width, width);
        float[] w = Weights, src = Temp;

        for (int x = 0; x < width; x++)
        {
            float sum = 0f;

            for (int k = -r; k <= r; k++)
            {
                sum += w[k + r] * src[(Math.Clamp(y + k, 0, Height - 1) * width) + x];
            }

            dst[x] = sum;
        }
    }

    private void HorizontalRowSimd(int y)
    {
        int width = Width, r = Radius;
        ReadOnlySpan<float> src = Source.AsSpan(y * width, width);
        Span<float> dst = Temp.AsSpan(y * width, width);
        float[] w = Weights;

        // Interior pixels [r, width − r): tap k reads src[x + k − r], a shifted slice of the row.
        int interior = width - (2 * r);
        Span<float> inner = dst.Slice(r, interior);
        inner.Clear();

        for (int k = 0; k <= 2 * r; k++)
        {
            TensorPrimitives.MultiplyAdd(src.Slice(k, interior), w[k], inner, inner);
        }

        // The clamped borders, scalar.
        for (int x = 0; x < width; x = x == r - 1 ? width - r : x + 1)
        {
            float sum = 0f;

            for (int k = -r; k <= r; k++)
            {
                sum += w[k + r] * src[Math.Clamp(x + k, 0, width - 1)];
            }

            dst[x] = sum;
        }
    }

    private void VerticalRowSimd(int y)
    {
        int width = Width, r = Radius;
        Span<float> dst = Output.AsSpan(y * width, width);
        dst.Clear();

        for (int k = -r; k <= r; k++)
        {
            int sy = Math.Clamp(y + k, 0, Height - 1);
            TensorPrimitives.MultiplyAdd(Temp.AsSpan(sy * width, width), Weights[k + r], dst, dst);
        }
    }
}
