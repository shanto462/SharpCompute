using System;
using System.Runtime.Intrinsics;
using System.Threading.Tasks;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary>Escape-time Mandelbrot into packed RGBA (<see cref="MandelbrotIterations"/> documents the packing).</summary>
public sealed class Mandelbrot : IDisposable
{
    public const int MaxIterations = 512;

    /// <summary>The full set, framed 3 units tall whatever the resolution.</summary>
    public static readonly Float2 Center = new(-0.6f, 0f);

    public Mandelbrot(int width, int height, GraphicsDevice device)
    {
        Width = width;
        Height = height;
        Step = 3f / height;
        Pixels = new uint[width * height];
        Device = device;
        GpuPixels = device.AllocateReadWriteBuffer<uint>(width * height);
    }

    public int Width { get; }

    public int Height { get; }

    public float Step { get; }

    public uint[] Pixels { get; }

    public GraphicsDevice Device { get; }

    public ReadWriteBuffer<uint> GpuPixels { get; }

    public static int Iterations(uint pixel) => (int)(pixel & 0xFFFFu);

    /// <summary>Scalar, single thread.</summary>
    public uint CpuScalar()
    {
        for (int py = 0; py < Height; py++)
        {
            Row(py);
        }

        return Checksum();
    }

    /// <summary>Parallel.For over rows, scalar per pixel.</summary>
    public uint CpuParallel()
    {
        Parallel.For(0, Height, Row);
        return Checksum();
    }

    /// <summary>
    /// Parallel.For over rows, four pixels at a time with <see cref="Vector128{T}"/> (128-bit NEON).
    /// Same unfused arithmetic as the scalar loop, so the output is identical to it.
    /// </summary>
    public uint CpuParallelSimd()
    {
        Parallel.For(0, Height, RowSimd);
        return Checksum();
    }

    public uint Gpu()
    {
        Device.For(Width, Height, Shader());
        return GpuPixels[(Height / 2 * Width) + (Width / 2)];
    }

    /// <summary>
    /// Iteration counts computed the way Metal's default FP contraction compiles the kernel: the
    /// pixel-to-plane mapping and <c>2xy + cy</c> become fused multiply-adds. With it the GPU counts
    /// match exactly. Without it, boundary pixels (where the orbit is chaotic) can drift far apart
    /// after a single differently rounded step. Reference only, not benchmarked.
    /// </summary>
    public int[] CpuFusedIterations()
    {
        int[] counts = new int[Width * Height];

        for (int py = 0; py < Height; py++)
        {
            float cy = MathF.FusedMultiplyAdd(py - (Height * 0.5f), Step, Center.Y);

            for (int px = 0; px < Width; px++)
            {
                float cx = MathF.FusedMultiplyAdd(px - (Width * 0.5f), Step, Center.X);
                float x = 0f, y = 0f, x2 = 0f, y2 = 0f;
                int n = 0;

                while (n < MaxIterations && x2 + y2 <= 4f)
                {
                    y = MathF.FusedMultiplyAdd(2f * x, y, cy);
                    x = x2 - y2 + cx;
                    x2 = x * x;
                    y2 = y * y;
                    n++;
                }

                counts[(py * Width) + px] = n;
            }
        }

        return counts;
    }

    public MandelbrotIterations Shader() => new(GpuPixels, Width, Height, MaxIterations, Center, Step);

    public void Dispose() => GpuPixels.Dispose();

    // Pixels are written by every variant; returning one keeps the work observable.
    private uint Checksum() => Pixels[(Height / 2 * Width) + (Width / 2)];

    private void Row(int py)
    {
        int width = Width;
        float cy = Center.Y + ((py - (Height * 0.5f)) * Step);
        Span<uint> row = Pixels.AsSpan(py * width, width);

        for (int px = 0; px < width; px++)
        {
            float cx = Center.X + ((px - (width * 0.5f)) * Step);
            float x = 0f, y = 0f, x2 = 0f, y2 = 0f;
            int n = 0;

            while (n < MaxIterations && x2 + y2 <= 4f)
            {
                y = (2f * x * y) + cy;
                x = x2 - y2 + cx;
                x2 = x * x;
                y2 = y * y;
                n++;
            }

            row[px] = Pack(n);
        }
    }

    private void RowSimd(int py)
    {
        int width = Width;
        float step = Step, half = width * 0.5f;
        float cyScalar = Center.Y + ((py - (Height * 0.5f)) * step);
        Vector128<float> cy = Vector128.Create(cyScalar), four = Vector128.Create(4f), two = Vector128.Create(2f);
        Span<uint> row = Pixels.AsSpan(py * width, width);
        int px = 0;

        for (; px <= width - 4; px += 4)
        {
            Vector128<float> cx = Vector128.Create(
                Center.X + ((px - half) * step),
                Center.X + ((px + 1 - half) * step),
                Center.X + ((px + 2 - half) * step),
                Center.X + ((px + 3 - half) * step));
            Vector128<float> x = Vector128<float>.Zero, y = x, x2 = x, y2 = x;
            Vector128<int> n = Vector128<int>.Zero;

            for (int i = 0; i < MaxIterations; i++)
            {
                // All-ones in the lanes still iterating. An escaped lane keeps its x and y (so it stays
                // escaped) and its counter stops, which is exactly where the scalar loop exits.
                Vector128<float> active = Vector128.LessThanOrEqual(x2 + y2, four);

                if (active == Vector128<float>.Zero)
                {
                    break;
                }

                Vector128<float> nextY = (two * x * y) + cy;
                Vector128<float> nextX = x2 - y2 + cx;
                x = Vector128.ConditionalSelect(active, nextX, x);
                y = Vector128.ConditionalSelect(active, nextY, y);
                x2 = x * x;
                y2 = y * y;
                n -= active.AsInt32();
            }

            row[px] = Pack(n.GetElement(0));
            row[px + 1] = Pack(n.GetElement(1));
            row[px + 2] = Pack(n.GetElement(2));
            row[px + 3] = Pack(n.GetElement(3));
        }

        for (; px < width; px++)
        {
            float cx = Center.X + ((px - half) * step);
            float x = 0f, y = 0f, x2 = 0f, y2 = 0f;
            int n = 0;

            while (n < MaxIterations && x2 + y2 <= 4f)
            {
                y = (2f * x * y) + cyScalar;
                x = x2 - y2 + cx;
                x2 = x * x;
                y2 = y * y;
                n++;
            }

            row[px] = Pack(n);
        }
    }

    private static uint Pack(int n)
    {
        uint count = (uint)n;
        uint shade = n < MaxIterations ? 255u - ((count * 8u) & 255u) : 0u;
        return (count & 0xFFFFu) | (shade << 16) | (255u << 24);
    }
}
