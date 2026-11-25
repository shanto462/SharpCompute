using System;
using SharpCompute;

namespace SharpCompute.Benchmarks.Shaders;

// The shaders of the --half-precision measurements: each kernel once on Float4 and once on Half4,
// statement for statement, so that the element type is the only difference.

/// <summary>A streaming pass, bound by memory bandwidth: <c>y = x × 0.5 + y</c> over two large buffers.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct StreamFloat4(ReadOnlyBuffer<Float4> x, ReadWriteBuffer<Float4> y) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        y[i] = (x[i] * 0.5f) + y[i];
    }
}

/// <summary><see cref="StreamFloat4"/> on half vectors: half the bytes per element.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct StreamHalf4(ReadOnlyBuffer<Half4> x, ReadWriteBuffer<Half4> y) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        y[i] = (x[i] * (Half)0.5f) + y[i];
    }
}

/// <summary>
/// A blur of (2 × radius + 1)² taps with tent weights. The image has a border of <c>radius</c> pixels
/// (<c>stride</c> is its width with the border), so the kernel has no edge handling.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct TapBlurFloat4(ReadOnlyBuffer<Float4> image, ReadWriteBuffer<Float4> output, int size, int stride, int radius) : IComputeShader
{
    public void Execute()
    {
        Float4 sum = new Float4(0f);
        float total = 0f;

        for (int dy = 0; dy <= 2 * radius; dy++)
        {
            int row = ((ThreadIds.Y + dy) * stride) + ThreadIds.X;
            float wy = radius + 1 - Math.Abs(dy - radius);

            for (int dx = 0; dx <= 2 * radius; dx++)
            {
                float w = wy * (radius + 1 - Math.Abs(dx - radius));

                sum += image[row + dx] * w;
                total += w;
            }
        }

        output[(ThreadIds.Y * size) + ThreadIds.X] = sum / total;
    }
}

/// <summary><see cref="TapBlurFloat4"/> on half pixels, with half arithmetic throughout.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct TapBlurHalf4(ReadOnlyBuffer<Half4> image, ReadWriteBuffer<Half4> output, int size, int stride, int radius) : IComputeShader
{
    public void Execute()
    {
        Half4 sum = new Half4((Half)0f);
        Half total = (Half)0f;

        for (int dy = 0; dy <= 2 * radius; dy++)
        {
            int row = ((ThreadIds.Y + dy) * stride) + ThreadIds.X;
            Half wy = (Half)(radius + 1 - Math.Abs(dy - radius));

            for (int dx = 0; dx <= 2 * radius; dx++)
            {
                Half w = wy * (Half)(radius + 1 - Math.Abs(dx - radius));

                sum += image[row + dx] * w;
                total += w;
            }
        }

        output[(ThreadIds.Y * size) + ThreadIds.X] = sum / total;
    }
}

/// <summary>
/// The blur on half pixels with float arithmetic: halves in memory, floats in the registers. It
/// moves the bytes of <see cref="TapBlurHalf4"/> and has the accuracy of <see cref="TapBlurFloat4"/>
/// until the result is stored.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct TapBlurHalfStorage(ReadOnlyBuffer<Half4> image, ReadWriteBuffer<Half4> output, int size, int stride, int radius) : IComputeShader
{
    public void Execute()
    {
        Float4 sum = new Float4(0f);
        float total = 0f;

        for (int dy = 0; dy <= 2 * radius; dy++)
        {
            int row = ((ThreadIds.Y + dy) * stride) + ThreadIds.X;
            float wy = radius + 1 - Math.Abs(dy - radius);

            for (int dx = 0; dx <= 2 * radius; dx++)
            {
                float w = wy * (radius + 1 - Math.Abs(dx - radius));

                sum += (Float4)image[row + dx] * w;
                total += w;
            }
        }

        output[(ThreadIds.Y * size) + ThreadIds.X] = (Half4)(sum / total);
    }
}

/// <summary>Arithmetic only: 64 rounds of a folding map on one vector per thread, nothing read, one vector written.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct FoldFloat4(ReadWriteBuffer<Float4> output, int size, int rounds) : IComputeShader
{
    public void Execute()
    {
        Float4 p = new Float4(ThreadIds.X / (float)size, ThreadIds.Y / (float)size, 0.5f, 0.25f);

        for (int i = 0; i < rounds; i++)
        {
            p = (Gpu.Abs(Gpu.Fract((p * 1.7f) + (p.YZWX * 0.3f)) - 0.5f) * 1.9f) + (p.ZWXY * 0.05f);
        }

        output[(ThreadIds.Y * size) + ThreadIds.X] = p;
    }
}

/// <summary><see cref="FoldFloat4"/> in half arithmetic.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct FoldHalf4(ReadWriteBuffer<Half4> output, int size, int rounds) : IComputeShader
{
    public void Execute()
    {
        Half4 p = (Half4)new Float4(ThreadIds.X / (float)size, ThreadIds.Y / (float)size, 0.5f, 0.25f);

        for (int i = 0; i < rounds; i++)
        {
            p = (Gpu.Abs(Gpu.Fract((p * (Half)1.7f) + (p.YZWX * (Half)0.3f)) - (Half)0.5f) * (Half)1.9f) + (p.ZWXY * (Half)0.05f);
        }

        output[(ThreadIds.Y * size) + ThreadIds.X] = p;
    }
}
