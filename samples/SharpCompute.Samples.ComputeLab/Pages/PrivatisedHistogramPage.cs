// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// Atomics on threadgroup memory: the luminance histogram of <see cref="HistogramPage"/>, privatised.
/// Each threadgroup of 256 threads counts its pixels in a histogram of its own in
/// <see cref="GroupSharedAttribute"/> memory, with <see cref="Gpu.AtomicAdd(ref int, int)"/> on elements
/// only those 256 threads can touch, and then adds its bins to the global histogram: one atomic on the
/// buffer per bin per group instead of one per pixel.
/// </summary>
/// <remarks>
/// <para>
/// The page runs both versions on the same image every frame and times each: the global one (a million
/// atomic adds on the buffer, as on the Histogram page) and the privatised one. The contention toggle
/// folds the 256 bins into 4. That makes the global version collide far more, while a privatised
/// group still only competes with itself, and then makes four adds on the buffer per group.
/// </para>
/// <para>
/// The check compares both GPU histograms with the CPU's count of the same image (<c>ForCpu</c> of the
/// global shader, <c>Interlocked</c> adds on every core), bin for bin. It also runs the privatised
/// shader itself with <c>ForCpu</c> on the first <see cref="CpuCheckPixels"/> pixels and compares that
/// with a plain loop: the CPU runs the threads of a threadgroup concurrently, one OS thread each, so
/// its atomics on group-shared memory are real too, but far too slow for the whole image.
/// </para>
/// </remarks>
public sealed class PrivatisedHistogramPage : ILabPage
{
    /// <summary>The image is <see cref="Size"/> × <see cref="Size"/> pixels, as on the Histogram page: 2^20 threads, 4,096 threadgroups.</summary>
    public const int Size = HistogramPage.Size;

    /// <summary>The pixels the privatised shader is also run on with <c>ForCpu</c>: 16 threadgroups.</summary>
    public const int CpuCheckPixels = 16 * PrivatisedLuminanceHistogram.GroupThreads;

    private const string BaselineName = "global atomics";

    private static readonly string[] s_variants = ["256 bins", "4 bins (64x the contention)"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<uint> _image;
    private readonly ReadWriteBuffer<int> _counts;
    private readonly ReadWriteBuffer<int> _globalCounts;
    private readonly ReadWriteBuffer<int> _cpuCounts;
    private readonly ReadWriteBuffer<int> _cpuGroupCounts;
    private readonly int[] _plainCounts = new int[256];
    private int _variant;

    // Smoothed times for the page's own labels: single dispatches jitter by tens of microseconds.
    private double _smoothedMs;
    private double _smoothedGlobalMs;

    public PrivatisedHistogramPage(GraphicsDevice device)
    {
        _device = device;
        _image = device.AllocateReadWriteBuffer<uint>(Size * Size);
        _counts = device.AllocateReadWriteBuffer<int>(256);
        _globalCounts = device.AllocateReadWriteBuffer<int>(256);
        _cpuCounts = device.AllocateReadWriteBuffer<int>(256);
        _cpuGroupCounts = device.AllocateReadWriteBuffer<int>(256);
    }

    public string Name => "Histogram: privatised";

    public string Feature => "threadgroup atomics";

    public string Help => "C contention (256 / 4 bins)";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant
    {
        get => _variant;
        set
        {
            if (_variant != value)
            {
                // The smoothed times describe the other bin count.
                (_variant, _smoothedMs, _smoothedGlobalMs) = (value, 0, 0);
            }
        }
    }

    /// <summary>The privatised dispatch of the last <see cref="Compute"/>.</summary>
    public double GpuMilliseconds { get; private set; }

    /// <summary>The global-atomics dispatch of the last <see cref="Compute"/>, on the same image.</summary>
    public double GlobalGpuMilliseconds { get; private set; }

    public LabBaseline? Baseline => new LabBaseline(BaselineName, GlobalGpuMilliseconds);

    /// <summary>256, or 4 with the contention toggle on.</summary>
    public int Bins => Variant == 0 ? 256 : 4;

    /// <summary>The privatised counts of the last <see cref="Compute"/>; only the first <see cref="Bins"/> are used.</summary>
    public ReadOnlySpan<int> Counts => _counts.AsReadOnlySpan()[..Bins];

    /// <summary>The global-atomics counts of the last <see cref="Compute"/>.</summary>
    public ReadOnlySpan<int> GlobalCounts => _globalCounts.AsReadOnlySpan()[..Bins];

    // Luminance is 0..255; shifting it right by 6 leaves the four bins 0..3.
    private int Shift => Variant == 0 ? 0 : 6;

    public void Compute(LabFrame frame)
    {
        _device.For(Size, Size, new GenerateImage(_image, Size, frame.Time));
        _counts.AsSpan().Clear();
        _globalCounts.AsSpan().Clear();

        long start = Stopwatch.GetTimestamp();
        _device.For(Size * Size, new LuminanceHistogram(_image, _globalCounts, Shift));
        GlobalGpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        start = Stopwatch.GetTimestamp();
        _device.For(Size * Size, new PrivatisedLuminanceHistogram(_image, _counts, Shift));
        GpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        _smoothedMs = _smoothedMs == 0 ? GpuMilliseconds : (_smoothedMs * 0.9) + (GpuMilliseconds * 0.1);
        _smoothedGlobalMs = _smoothedGlobalMs == 0 ? GlobalGpuMilliseconds : (_smoothedGlobalMs * 0.9) + (GlobalGpuMilliseconds * 0.1);
    }

    public LabCheck Check()
    {
        _cpuCounts.AsSpan().Clear();

        long start = Stopwatch.GetTimestamp();
        _device.ForCpu(Size * Size, new LuminanceHistogram(_image, _cpuCounts, Shift));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // The privatised shader itself on the CPU, where its barriers and its atomics on group-shared
        // memory run on one OS thread per thread of a threadgroup, against a plain loop over the same pixels.
        _cpuGroupCounts.AsSpan().Clear();
        _device.ForCpu(CpuCheckPixels, new PrivatisedLuminanceHistogram(_image, _cpuGroupCounts, Shift));

        Span<int> plain = _plainCounts;
        plain.Clear();

        foreach (uint pixel in _image.AsReadOnlySpan()[..CpuCheckPixels])
        {
            plain[(int)(PrivatisedLuminanceHistogram.Luminance(pixel) >> Shift)]++;
        }

        ReadOnlySpan<int> privatised = _counts.AsReadOnlySpan(), global = _globalCounts.AsReadOnlySpan(), cpu = _cpuCounts.AsReadOnlySpan();
        long total = 0;

        foreach (int count in privatised)
        {
            total += count;
        }

        // Every pixel landed in exactly one bin, and every bin is the same three ways.
        bool gpuAgrees = privatised.SequenceEqual(cpu) && global.SequenceEqual(cpu) && total == Size * Size;
        bool cpuGroupsAgree = _cpuGroupCounts.AsReadOnlySpan().SequenceEqual(plain);
        string summary = gpuAgrees && cpuGroupsAgree
            ? Invariant($"{Bins} bins: privatised = global atomics = CPU; its ForCpu = a plain loop ({CpuCheckPixels:N0} px)")
            : !gpuAgrees
                ? Invariant($"privatised, global and CPU counts differ ({total:N0} of {Size * Size:N0} pixels in the privatised bins)")
                : Invariant($"ForCpu of the privatised shader differs from a plain loop over {CpuCheckPixels:N0} pixels");

        return new LabCheck(gpuAgrees && cpuGroupsAgree, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        int max = 1;

        foreach (int count in Counts)
        {
            max = Math.Max(max, count);
        }

        _device.For(
            LabCanvas.Width,
            LabCanvas.Height,
            new DrawPrivatisedHistogram(canvas.Pixels, _image, _globalCounts, _counts, LabCanvas.Width, Size, Bins, max));

        int groups = Size * Size / PrivatisedLuminanceHistogram.GroupThreads;
        int upper = DrawPrivatisedHistogram.PanelY, lower = DrawPrivatisedHistogram.LowerChartY;
        int below = DrawPrivatisedHistogram.PanelY + DrawPrivatisedHistogram.PanelSize + 8;

        canvas.Label(DrawPrivatisedHistogram.ImageX, upper - 24, "Image: 1024 x 1024, generated on the GPU");
        canvas.Label(DrawPrivatisedHistogram.ChartX, upper - 24, Invariant($"Global: 1,048,576 atomic adds on {Bins} buffer bins"));
        canvas.Label(DrawPrivatisedHistogram.ChartX, lower - 24, Invariant($"Privatised: [GroupShared] bins, {groups:N0} groups merge"));

        // Timings are not reproducible, so the headless picture leaves them to summary.txt.
        if (!frame.Deterministic && _smoothedMs > 0 && _smoothedGlobalMs > 0)
        {
            string ratio = _smoothedMs <= _smoothedGlobalMs
                ? Invariant($"{_smoothedGlobalMs / _smoothedMs:F1}x faster than global atomics")
                : Invariant($"{_smoothedMs / _smoothedGlobalMs:F1}x slower than global atomics");

            canvas.Label(DrawPrivatisedHistogram.ChartX, upper - 40, Invariant($"GPU {_smoothedGlobalMs:F2} ms"), TextColor.Accent);
            canvas.Label(DrawPrivatisedHistogram.ChartX, lower - 40, Invariant($"GPU {_smoothedMs:F2} ms: {ratio}"), TextColor.Cool);
        }

        // The whole image is checked against the CPU's count; the privatised shader itself runs on the CPU for a sample.
        canvas.Label(DrawPrivatisedHistogram.ImageX, below, Invariant($"CPU check of this shader: a {CpuCheckPixels:N0}-pixel sample"), TextColor.Dim);
        canvas.Label(DrawPrivatisedHistogram.ChartX, below, "dark", TextColor.Dim);
        canvas.Label(DrawPrivatisedHistogram.ChartX + DrawPrivatisedHistogram.ChartWidth - (6 * LabCanvas.CellWidth), below, "bright", TextColor.Dim);
        canvas.Label(DrawPrivatisedHistogram.ChartX + 140, below, "a bin that differs would be red", TextColor.Dim);
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
        _globalCounts.Dispose();
        _cpuCounts.Dispose();
        _cpuGroupCounts.Dispose();
    }
}

/// <summary>
/// The feature kernel: the luminance histogram, privatised per threadgroup. Dispatched over a multiple
/// of <see cref="GroupThreads"/> pixels, so every group is whole and thread <c>n</c> of a group owns
/// bin <c>n</c> of its local histogram for clearing and merging.
/// </summary>
/// <remarks>
/// Threadgroup memory starts out undefined, and a plain read or write of an element must not overlap
/// the atomics on it. Hence the three phases with a barrier between them: clear, count (atomics only),
/// merge (plain reads, and one atomic on the buffer per non-empty bin).
/// </remarks>
[ThreadGroupSize(GroupThreads)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct PrivatisedLuminanceHistogram(ReadWriteBuffer<uint> image, ReadWriteBuffer<int> counts, int shift) : IComputeShader
{
    public const int GroupThreads = 256;

    [GroupShared]
    private static readonly int[] local = new int[256];

    /// <summary>The bin of a pixel before the contention shift: Rec. 709 weights scaled to 256, in integers, as in <see cref="LuminanceHistogram"/>.</summary>
    public static uint Luminance(uint pixel) =>
        (((pixel & 255u) * 54u) + (((pixel >> 8) & 255u) * 183u) + (((pixel >> 16) & 255u) * 19u)) >> 8;

    public void Execute()
    {
        int bin = GroupIds.Index;
        bool owner = bin < (256 >> shift);

        if (owner)
        {
            local[bin] = 0;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        // Contended by the 256 threads of this group only.
        Gpu.AtomicAdd(ref local[(int)(Luminance(image[ThreadIds.X]) >> shift)], 1);

        Gpu.GroupMemoryBarrierWithGroupSync();

        if (owner)
        {
            int count = local[bin];

            if (count != 0)
            {
                // Contended by every group, but once per bin per group instead of once per pixel.
                Gpu.AtomicAdd(ref counts[bin], count);
            }
        }
    }
}

/// <summary>Draws the page: the image on the left, the two histograms as bars on the right, the global one above the privatised one.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawPrivatisedHistogram(
    ReadWriteBuffer<uint> pixels,
    ReadWriteBuffer<uint> image,
    ReadWriteBuffer<int> globalCounts,
    ReadWriteBuffer<int> counts,
    int width,
    int size,
    int bins,
    int max) : IComputeShader
{
    public const int PanelY = 104;
    public const int PanelSize = 512;
    public const int ImageX = 48;
    public const int ChartX = 624;
    public const int ChartWidth = 608;
    public const int ChartHeight = 224;

    /// <summary>The top of the privatised chart; the global one starts at <see cref="PanelY"/>.</summary>
    public const int LowerChartY = PanelY + PanelSize - ChartHeight;

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
            bool lower = y >= LowerChartY;
            int fromBottom = lower ? PanelY + PanelSize - y : PanelY + ChartHeight - y;

            if (ix >= 0 && ix < PanelSize)
            {
                // Every second pixel of the 1024 x 1024 image.
                color = Gpu.UnpackUnorm4x8(image[(py * 2 * size) + (ix * 2)]).XYZ;
            }
            else if (cx >= 0 && cx < ChartWidth && fromBottom > 0 && fromBottom <= ChartHeight)
            {
                int scaled = cx * bins;
                int bin = scaled / ChartWidth;
                int inside = (scaled % ChartWidth) / bins;
                int count = lower ? counts[bin] : globalCounts[bin];
                float height = count * (float)ChartHeight / max;
                float level = (bin + 0.5f) / bins;

                color = new Float3(0.085f, 0.095f, 0.12f);

                // Wide bars (the 4-bin mode) get a gap; 256 bars of two pixels are drawn solid.
                if (fromBottom <= height && (bins > 64 || inside >= 6))
                {
                    color = lower
                        ? Gpu.Lerp(new Float3(0.2f, 0.45f, 0.95f), new Float3(0.45f, 0.95f, 0.8f), level)
                        : Gpu.Lerp(new Float3(0.95f, 0.4f, 0.2f), new Float3(1f, 0.85f, 0.4f), level);

                    // The two versions count the same pixels: a bin they disagree on would stand out.
                    if (counts[bin] != globalCounts[bin])
                    {
                        color = new Float3(1f, 0.1f, 0.1f);
                    }
                }
            }
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
