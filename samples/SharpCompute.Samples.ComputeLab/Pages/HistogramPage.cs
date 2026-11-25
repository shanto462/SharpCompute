// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// Atomics, part one: a luminance histogram. A million GPU threads each read one pixel of an
/// animated image and add 1 to that pixel's bin with <see cref="Gpu.AtomicAdd(ref int, int)"/>.
/// Without atomics two threads that hit the same bin at once would lose a count.
/// </summary>
/// <remarks>
/// The contention toggle folds the 256 bins into 4: the same million adds on a 64th of the counters,
/// so far more threads collide on each one. The counts must still be exact; only the time changes.
/// The check runs the same shader with <c>ForCpu</c>, where the adds are <c>Interlocked</c> operations
/// on every core, and the luminance is integer arithmetic, so the two must agree in every bin.
/// </remarks>
public sealed class HistogramPage : ILabPage
{
    /// <summary>The image is <see cref="Size"/> × <see cref="Size"/> pixels: 2^20 threads for the histogram.</summary>
    public const int Size = 1024;

    private static readonly string[] s_variants = ["256 bins", "4 bins (64x the contention)"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<uint> _image;
    private readonly ReadWriteBuffer<int> _counts;
    private readonly ReadWriteBuffer<int> _cpuCounts;

    public HistogramPage(GraphicsDevice device)
    {
        _device = device;
        _image = device.AllocateReadWriteBuffer<uint>(Size * Size);
        _counts = device.AllocateReadWriteBuffer<int>(256);
        _cpuCounts = device.AllocateReadWriteBuffer<int>(256);
    }

    public string Name => "Histogram";

    public string Feature => "atomics";


    public string Topic => LabTopics.Atomics;

    public string Help => "C contention (256 / 4 bins)";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>256, or 4 with the contention toggle on.</summary>
    public int Bins => Variant == 0 ? 256 : 4;

    /// <summary>The GPU's counts of the last <see cref="Compute"/>; only the first <see cref="Bins"/> are used.</summary>
    public ReadOnlySpan<int> Counts => _counts.AsReadOnlySpan()[..Bins];

    // Luminance is 0..255; shifting it right by 6 leaves the four bins 0..3.
    private int Shift => Variant == 0 ? 0 : 6;

    public void Compute(LabFrame frame)
    {
        _device.For(Size, Size, new GenerateImage(_image, Size, frame.Time));
        _counts.AsSpan().Clear();

        long start = Stopwatch.GetTimestamp();
        _device.For(Size * Size, new LuminanceHistogram(_image, _counts, Shift));
        GpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public LabCheck Check()
    {
        _cpuCounts.AsSpan().Clear();

        long start = Stopwatch.GetTimestamp();
        _device.ForCpu(Size * Size, new LuminanceHistogram(_image, _cpuCounts, Shift));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        ReadOnlySpan<int> gpu = _counts.AsReadOnlySpan(), cpu = _cpuCounts.AsReadOnlySpan();
        long total = 0;

        foreach (int count in gpu)
        {
            total += count;
        }

        // Every pixel landed in exactly one bin, and every bin matches the CPU's.
        bool passed = gpu.SequenceEqual(cpu) && total == Size * Size;
        string summary = passed
            ? Invariant($"{total:N0} pixels in {Bins} bins: every GPU count equals the CPU's (ForCpu, same shader)")
            : Invariant($"GPU and CPU counts differ ({total:N0} of {Size * Size:N0} pixels counted on the GPU)");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        int max = 1;

        foreach (int count in Counts)
        {
            max = Math.Max(max, count);
        }

        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawHistogram(canvas.Pixels, _image, _counts, LabCanvas.Width, Size, Bins, max));

        canvas.Label(DrawHistogram.ImageX, DrawHistogram.PanelY - 24, "Image: 1024 x 1024, generated on the GPU");
        canvas.Label(DrawHistogram.ChartX, DrawHistogram.PanelY - 24, Invariant($"Luminance histogram: {Bins} bins, Gpu.AtomicAdd"));
        canvas.Label(DrawHistogram.ChartX, DrawHistogram.PanelY + DrawHistogram.PanelSize + 8, "dark", TextColor.Dim);
        canvas.Label(DrawHistogram.ChartX + DrawHistogram.ChartWidth - (6 * LabCanvas.CellWidth), DrawHistogram.PanelY + DrawHistogram.PanelSize + 8, "bright", TextColor.Dim);
        canvas.Label(DrawHistogram.ChartX + 200, DrawHistogram.PanelY + DrawHistogram.PanelSize + 8, Invariant($"tallest bin: {max:N0} pixels"), TextColor.Dim);
    }

    public bool OnKeyDown(Key key)
    {
        if (key != Key.C)
        {
            return false;
        }

        Variant = 1 - Variant;
        return true;
    }

    public void Dispose()
    {
        _image.Dispose();
        _counts.Dispose();
        _cpuCounts.Dispose();
    }
}

/// <summary>
/// The histogram's input: drifting color gradients, a soft moving highlight and per-pixel noise,
/// stored as packed RGBA (one <c>uint</c> per pixel, since buffers cannot hold bytes).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GenerateImage(ReadWriteBuffer<uint> image, int size, float time) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float2 uv = new Float2(x, y) / size;

        // Two sets of bands sliding across each other, and a highlight on a slow orbit.
        float a = 0.5f + (0.5f * Gpu.Sin(6.2831853f * ((uv.X * 1.5f) + (uv.Y * 0.4f) + (time * 0.11f))));
        float b = 0.5f + (0.5f * Gpu.Sin(6.2831853f * ((uv.Y * 2.2f) - (uv.X * 0.7f) - (time * 0.07f))));
        Float2 center = new Float2(0.5f + (0.3f * Gpu.Cos(time * 0.5f)), 0.5f + (0.3f * Gpu.Sin(time * 0.37f)));
        float glow = Gpu.Smoothstep(0.42f, 0f, Gpu.Length(uv - center));

        // Noise that changes ten times a second, so the histogram's fine structure flickers.
        uint tick = (uint)(int)(time * 10f);
        float noise = (ShaderMath.Hash((uint)x + (ShaderMath.Hash((uint)y + (tick * 7919u)) * 31u)) >> 8) * (1f / 16777216f);

        // Kept inside 0.06..0.96, so the noise spreads the dark and bright ends instead of clipping them.
        Float3 color = (new Float3(0.78f, 0.3f, 0.12f) * a) + (new Float3(0.08f, 0.3f, 0.7f) * b * b);
        color = new Float3(0.12f) + (Gpu.Lerp(color, new Float3(0.84f, 0.8f, 0.68f), glow * glow) * 0.92f) + ((noise - 0.5f) * 0.12f);

        image[(y * size) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}

/// <summary>
/// The feature kernel: one thread per pixel adds 1 to the bin of the pixel's luminance. The
/// luminance is integer arithmetic on the unorm8 channels (Rec. 709 weights scaled to 256), so the
/// GPU and the CPU compute the same bin for every pixel.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct LuminanceHistogram(ReadWriteBuffer<uint> image, ReadWriteBuffer<int> counts, int shift) : IComputeShader
{
    public void Execute()
    {
        uint pixel = image[ThreadIds.X];
        uint luminance = (((pixel & 255u) * 54u) + (((pixel >> 8) & 255u) * 183u) + (((pixel >> 16) & 255u) * 19u)) >> 8;

        Gpu.AtomicAdd(ref counts[(int)(luminance >> shift)], 1);
    }
}

/// <summary>Draws the page: the image on the left, the bins as bars on the right.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawHistogram(
    ReadWriteBuffer<uint> pixels, ReadWriteBuffer<uint> image, ReadWriteBuffer<int> counts, int width, int size, int bins, int max) : IComputeShader
{
    public const int PanelY = 104;
    public const int PanelSize = 512;
    public const int ImageX = 48;
    public const int ChartX = 624;
    public const int ChartWidth = 608;

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int py = y - PanelY;

        if (py >= 0 && py < PanelSize)
        {
            int ix = x - ImageX;
            int cx = x - ChartX;

            if (ix >= 0 && ix < PanelSize)
            {
                // Every second pixel of the 1024 x 1024 image.
                color = Gpu.UnpackUnorm4x8(image[(py * 2 * size) + (ix * 2)]).XYZ;
            }
            else if (cx >= 0 && cx < ChartWidth)
            {
                int scaled = cx * bins;
                int bin = scaled / ChartWidth;
                int inside = (scaled % ChartWidth) / bins;
                float height = counts[bin] * (float)PanelSize / max;
                float level = (bin + 0.5f) / bins;

                color = new Float3(0.085f, 0.095f, 0.12f);

                // Wide bars (the 4-bin mode) get a gap; 256 bars of two pixels are drawn solid.
                if (PanelSize - py <= height && (bins > 64 || inside >= 6))
                {
                    color = Gpu.Lerp(new Float3(0.2f, 0.45f, 0.95f), new Float3(1f, 0.85f, 0.4f), level);
                }
            }
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
