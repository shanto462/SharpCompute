// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// Fixed-size local arrays: two classic per-pixel filters over the animated image, each built on a
/// small array of its own thread. The 3 × 3 median gathers the nine neighbours into a
/// <c>Span&lt;Float3&gt;</c> and runs a 19-step sorting network over it; the 5 × 5 Gaussian reads its
/// 25 weights from a <c>ReadOnlySpan&lt;int&gt;</c> table. Salt-and-pepper noise shows what the median
/// is for: it removes the specks, which the Gaussian only smears.
/// </summary>
/// <remarks>
/// Both filters are exact: the median only moves values around, and the Gaussian is integer
/// arithmetic on the unorm8 channels, so the check compares every pixel of the GPU's result with
/// <c>ForCpu</c> of the same shader for equality. It also counts how many pixels end up far from the
/// clean image, which is the effect the page is about.
/// </remarks>
public sealed class LocalArraysPage : ILabPage
{
    /// <summary>The image is <see cref="Size"/> × <see cref="Size"/> pixels.</summary>
    public const int Size = 1024;

    /// <summary>The share of the pixels the noise replaces by black or white.</summary>
    public const float NoiseDensity = 0.06f;

    /// <summary>A pixel is "off" when a channel differs from the clean image by more than this many unorm8 steps.</summary>
    public const int OffThreshold = 24;

    private static readonly string[] s_variants = ["median 3x3 (sorting network)", "gaussian 5x5 (weight table)"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<uint> _clean;
    private readonly ReadWriteBuffer<uint> _noisy;
    private readonly ReadWriteBuffer<uint> _median;
    private readonly ReadWriteBuffer<uint> _gaussian;
    private readonly ReadWriteBuffer<uint> _cpu;

    public LocalArraysPage(GraphicsDevice device)
    {
        _device = device;
        _clean = device.AllocateReadWriteBuffer<uint>(Size * Size);
        _noisy = device.AllocateReadWriteBuffer<uint>(Size * Size);
        _median = device.AllocateReadWriteBuffer<uint>(Size * Size);
        _gaussian = device.AllocateReadWriteBuffer<uint>(Size * Size);
        _cpu = device.AllocateReadWriteBuffer<uint>(Size * Size);
        device.Prepare(ShaderReference.Of<GenerateImage>(), ShaderReference.Of<SaltAndPepper>(), ShaderReference.Of<MedianFilter>(), ShaderReference.Of<GaussianFilter>(), ShaderReference.Of<DrawLocalArrays>());
    }

    public string Name => "LocalArrays";

    public string Feature => "fixed-size local arrays";

    public string Topic => LabTopics.LocalArrays;

    public string Help => "F median / gaussian   N noise on/off";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>True while salt-and-pepper noise is added to the image (the default).</summary>
    public bool Noise { get; set; } = true;

    /// <summary>The share of the filter's input pixels that are off the clean image, at the last <see cref="Check"/>.</summary>
    public double NoisyOff { get; private set; }

    /// <summary>The share of the active filter's output pixels that are off the clean image, at the last <see cref="Check"/>.</summary>
    public double FilteredOff { get; private set; }

    /// <summary>The GPU's result of the active filter at the last <see cref="Compute"/>.</summary>
    public ReadOnlySpan<uint> Filtered => (Variant == 0 ? _median : _gaussian).AsReadOnlySpan();

    /// <summary>The filter's input at the last <see cref="Compute"/>.</summary>
    public ReadOnlySpan<uint> Input => _noisy.AsReadOnlySpan();

    /// <summary>The image before the noise at the last <see cref="Compute"/>.</summary>
    public ReadOnlySpan<uint> Clean => _clean.AsReadOnlySpan();

    public void Compute(LabFrame frame)
    {
        _device.For(Size, Size, new GenerateImage(_clean, Size, frame.Time));

        // New specks ten times a second; the same ones for the same time, so headless runs repeat.
        uint seed = (uint)(int)(frame.Time * 10f);
        _device.For(Size, Size, new SaltAndPepper(_clean, _noisy, Size, seed, Noise ? NoiseDensity : 0f));

        // Both filters run (the page shows both); the active one is timed.
        RunFilter(Variant, _median, _gaussian, onCpu: false);
        GpuMilliseconds = _device.LastGpuTime.TotalMilliseconds;
        RunFilter(1 - Variant, _median, _gaussian, onCpu: false);
    }

    public LabCheck Check()
    {
        long start = Stopwatch.GetTimestamp();
        RunFilter(Variant, _cpu, _cpu, onCpu: true);
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        ReadOnlySpan<uint> gpu = Filtered, cpu = _cpu.AsReadOnlySpan();
        int mismatches = 0;

        for (int i = 0; i < gpu.Length; i++)
        {
            mismatches += gpu[i] == cpu[i] ? 0 : 1;
        }

        NoisyOff = OffShare(_noisy.AsReadOnlySpan(), _clean.AsReadOnlySpan());
        FilteredOff = OffShare(gpu, _clean.AsReadOnlySpan());

        bool passed = mismatches == 0;
        string summary = passed
            ? Invariant($"all {gpu.Length:N0} pixels equal ForCpu's; off the clean image: {NoisyOff * 100:F1}% in, {FilteredOff * 100:F1}% out")
            : Invariant($"{mismatches:N0} of {gpu.Length:N0} GPU pixels differ from ForCpu's");

        return new LabCheck(passed, summary, cpuMs);
    }

    /// <summary>The share of <paramref name="image"/>'s pixels with a channel more than <see cref="OffThreshold"/> steps from <paramref name="clean"/>.</summary>
    public static double OffShare(ReadOnlySpan<uint> image, ReadOnlySpan<uint> clean)
    {
        int off = 0;

        for (int i = 0; i < image.Length; i++)
        {
            uint a = image[i], b = clean[i];

            for (int shift = 0; shift < 24; shift += 8)
            {
                if (Math.Abs((int)((a >> shift) & 255u) - (int)((b >> shift) & 255u)) > OffThreshold)
                {
                    off++;
                    break;
                }
            }
        }

        return (double)off / image.Length;
    }

    private void RunFilter(int variant, ReadWriteBuffer<uint> median, ReadWriteBuffer<uint> gaussian, bool onCpu)
    {
        if (variant == 0)
        {
            var filter = new MedianFilter(_noisy, median, Size);

            if (onCpu)
            {
                _device.ForCpu(Size, Size, filter);
            }
            else
            {
                _device.For(Size, Size, filter);
            }
        }
        else
        {
            var filter = new GaussianFilter(_noisy, gaussian, Size);

            if (onCpu)
            {
                _device.ForCpu(Size, Size, filter);
            }
            else
            {
                _device.For(Size, Size, filter);
            }
        }
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawLocalArrays(canvas.Pixels, _noisy, _median, _gaussian, LabCanvas.Width, Size, Variant));

        int labelY = DrawLocalArrays.PanelY - 24;
        int below = DrawLocalArrays.PanelY + DrawLocalArrays.PanelSize + 8;
        canvas.Label(DrawLocalArrays.InputX, labelY, Noise ? Invariant($"input + {NoiseDensity * 100:F0}% noise, 2x zoom") : "input, no noise, 2x zoom");
        canvas.Label(DrawLocalArrays.MedianX, labelY, "median 3x3", Variant == 0 ? TextColor.Accent : TextColor.Dim);
        canvas.Label(DrawLocalArrays.GaussianX, labelY, "gaussian 5x5", Variant == 1 ? TextColor.Accent : TextColor.Dim);
        canvas.Label(DrawLocalArrays.MedianX, below, "removes the specks", TextColor.Dim);
        canvas.Label(DrawLocalArrays.GaussianX, below, "smears them", TextColor.Dim);

        // The C# of the two filters, and what the generator makes of it.
        const int CSharp = 3, Msl = 62;
        int row = ((below - LabCanvas.TextTop) / LabCanvas.CellHeight) + 3;
        canvas.Text(CSharp, row, "C#", TextColor.Dim);
        canvas.Text(Msl, row, "MSL", TextColor.Dim);
        canvas.Text(CSharp, row + 1, "Span<Float3> window = stackalloc Float3[9];", TextColor.Cool);
        canvas.Text(Msl, row + 1, "float3 window[9];", TextColor.Cool);
        canvas.Text(CSharp, row + 2, "static void MedianOfNine(Span<Float3> p)", TextColor.Cool);
        canvas.Text(Msl, row + 2, "MedianOfNine(thread float3* p, int __len_p)", TextColor.Cool);
        canvas.Text(CSharp, row + 3, "    Order(ref p[1], ref p[2]); ...  // 19 steps", TextColor.Cool);
        canvas.Text(Msl, row + 3, "Order(p[1], p[2]); ...", TextColor.Cool);
        canvas.Text(CSharp, row + 4, "ReadOnlySpan<int> weights = [1, 4, 6, 4, 1, ...];", TextColor.Cool);
        canvas.Text(Msl, row + 4, "const int weights[25] = ...;", TextColor.Cool);
        canvas.Text(CSharp, row + 6, "No bounds checks on the GPU; ForCpu runs the C#, where an index out of range throws.", TextColor.Dim);
    }

    public bool OnKeyDown(Key key)
    {
        switch (key)
        {
            case Key.F:
                Variant = 1 - Variant;
                return true;
            case Key.N:
                Noise = !Noise;
                return true;
            default:
                return false;
        }
    }

    public void Dispose()
    {
        _clean.Dispose();
        _noisy.Dispose();
        _median.Dispose();
        _gaussian.Dispose();
        _cpu.Dispose();
    }
}

/// <summary>Replaces a share of the pixels by black or white, chosen by a hash of the pixel and the seed.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SaltAndPepper(ReadWriteBuffer<uint> clean, ReadWriteBuffer<uint> noisy, int size, uint seed, float density) : IComputeShader
{
    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    public void Execute()
    {
        int i = (ThreadIds.Y * size) + ThreadIds.X;
        uint h = Hash((uint)i ^ (seed * 0x9E3779B9u));

        // The low 24 bits pick whether the pixel is hit, the top bit black or white.
        bool hit = (h & 0xFFFFFFu) < (uint)(density * 16777216f);
        noisy[i] = hit ? ((h >> 31) == 0u ? 0xFF000000u : 0xFFFFFFFFu) : clean[i];
    }
}

/// <summary>
/// The 3 × 3 median: the nine neighbours (clamped at the edges) go into a nine-element local array,
/// and Paeth's median-of-nine network leaves the median in the middle. Each step orders two elements
/// with a component-wise min and max, which runs the same network on the three channels at once.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MedianFilter(ReadWriteBuffer<uint> source, ReadWriteBuffer<uint> destination, int size) : IComputeShader
{
    private static void Order(ref Float3 a, ref Float3 b)
    {
        Float3 low = Gpu.Min(a, b);
        b = Gpu.Max(a, b);
        a = low;
    }

    /// <summary>Leaves the median of the nine values in <c>p[4]</c> (19 compare-and-swaps).</summary>
    private static void MedianOfNine(Span<Float3> p)
    {
        Order(ref p[1], ref p[2]); Order(ref p[4], ref p[5]); Order(ref p[7], ref p[8]);
        Order(ref p[0], ref p[1]); Order(ref p[3], ref p[4]); Order(ref p[6], ref p[7]);
        Order(ref p[1], ref p[2]); Order(ref p[4], ref p[5]); Order(ref p[7], ref p[8]);
        Order(ref p[0], ref p[3]); Order(ref p[5], ref p[8]); Order(ref p[4], ref p[7]);
        Order(ref p[3], ref p[6]); Order(ref p[1], ref p[4]); Order(ref p[2], ref p[5]);
        Order(ref p[4], ref p[7]); Order(ref p[4], ref p[2]); Order(ref p[6], ref p[4]);
        Order(ref p[4], ref p[2]);
    }

    public void Execute()
    {
        int x = ThreadIds.X, y = ThreadIds.Y;
        Span<Float3> window = stackalloc Float3[9];
        int n = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int i = (Gpu.Clamp(y + dy, 0, size - 1) * size) + Gpu.Clamp(x + dx, 0, size - 1);
                window[n] = Gpu.UnpackUnorm4x8(source[i]).XYZ;
                n++;
            }
        }

        MedianOfNine(window);
        destination[(y * size) + x] = Gpu.PackUnorm4x8(new Float4(window[4], 1f));
    }
}

/// <summary>
/// The 5 × 5 Gaussian: the binomial weights (1 4 6 4 1)² / 256 from a constant table, applied to the
/// unorm8 channels in integer arithmetic, so the GPU and the CPU round identically.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GaussianFilter(ReadWriteBuffer<uint> source, ReadWriteBuffer<uint> destination, int size) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        ReadOnlySpan<int> weights =
        [
            1, 4, 6, 4, 1,
            4, 16, 24, 16, 4,
            6, 24, 36, 24, 6,
            4, 16, 24, 16, 4,
            1, 4, 6, 4, 1,
        ];

        int r = 0, g = 0, b = 0;

        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int sx = Gpu.Clamp(x + dx, 0, size - 1);
                int sy = Gpu.Clamp(y + dy, 0, size - 1);
                uint pixel = source[(sy * size) + sx];
                int w = weights[((dy + 2) * 5) + dx + 2];

                r += w * (int)(pixel & 255u);
                g += w * (int)((pixel >> 8) & 255u);
                b += w * (int)((pixel >> 16) & 255u);
            }
        }

        // The weights sum to 256: round to the nearest step.
        uint red = (uint)((r + 128) >> 8);
        uint green = (uint)((g + 128) >> 8);
        uint blue = (uint)((b + 128) >> 8);
        destination[(y * size) + x] = 0xFF000000u | (blue << 16) | (green << 8) | red;
    }
}

/// <summary>Draws the page: the same crop of the input, the median and the Gaussian, magnified twice.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawLocalArrays(
    ReadWriteBuffer<uint> pixels, ReadWriteBuffer<uint> input, ReadWriteBuffer<uint> median, ReadWriteBuffer<uint> gaussian, int width, int size, int active) : IComputeShader
{
    public const int PanelY = 104;
    public const int PanelSize = 384;
    public const int InputX = 32;
    public const int MedianX = 448;
    public const int GaussianX = 864;

    /// <summary>The top-left pixel of the crop: a 192 × 192 square around the middle of the image.</summary>
    public const int CropX = 400;
    public const int CropY = 360;

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int py = y - PanelY;

        if (py >= -3 && py < PanelSize + 3)
        {
            int panel = x >= GaussianX - 3 ? 2 : (x >= MedianX - 3 ? 1 : 0);
            int left = panel == 0 ? InputX : (panel == 1 ? MedianX : GaussianX);
            int px = x - left;

            if (px >= 0 && px < PanelSize && py >= 0 && py < PanelSize)
            {
                int i = ((CropY + (py / 2)) * size) + CropX + (px / 2);
                uint pixel = panel == 0 ? input[i] : (panel == 1 ? median[i] : gaussian[i]);
                color = Gpu.UnpackUnorm4x8(pixel).XYZ;
            }
            else if (px >= -3 && px < PanelSize + 3 && panel == active + 1)
            {
                // A frame around the filter that is timed and checked.
                color = new Float3(1f, 0.8f, 0.35f);
            }
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
