// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpCompute.Presentation;
using SharpCompute.Samples.ComputeLab;
using SharpCompute.Samples.ComputeLab.Pages;
using static System.FormattableString;

namespace SharpCompute.Samples.SimdGroups.Pages;

/// <summary>
/// A reduction race: the sum of 16.7 million floats, three ways, all on the GPU. Two of them fold the
/// 32 lanes of a SIMD group with <c>Gpu.WaveActiveSum</c> and let the group's first lane
/// (<c>Gpu.WaveIsFirstLane</c>) store the result, which needs no shared memory, no barrier and no
/// atomics; the third is the baseline without any SIMD-group operation.
/// </summary>
/// <remarks>
/// <para>
/// "SIMD only" launches one thread per float, so every add of the first pass is a lane add.
/// "SIMD + per-group partial" launches 128 times fewer threads: each adds a strip of 128 floats in a
/// loop first, and the lanes then fold their strips. Both end with the same second pass, a single SIMD
/// group of 32 threads that adds the partials. "Thread loops" is one thread per strip of 4,096 floats
/// and one more thread for the 4,096 partials.
/// </para>
/// <para>
/// A float sum rounds at every add, and the three methods add in different orders, so they do not
/// agree to the bit with each other or with a C# loop. The check is therefore against the sum in
/// double precision, within <see cref="Tolerance"/>; the page shows each method's error next to its
/// time.
/// </para>
/// </remarks>
public sealed class ReductionPage : ILabPage
{
    /// <summary>The number of floats summed: 2^24.</summary>
    public const int Count = 1 << 24;

    /// <summary>The lanes of a SIMD group.</summary>
    public const int Lanes = 32;

    /// <summary>"SIMD + per-group partial": the floats one thread adds in a loop before the lanes are folded.</summary>
    public const int LaneStrip = 128;

    /// <summary>"Thread loops": the floats one thread adds in a loop.</summary>
    public const int LoopStrip = 4096;

    /// <summary>
    /// The largest relative difference from the double-precision sum that passes the check. A sum of
    /// 2^24 floats of about 0.5 is near 2^23, where a float's last bit is worth 0.5 to 1, so even a
    /// perfectly ordered float sum is about 1e-7 off; the order of the adds costs up to a few 1e-6 more.
    /// </summary>
    public const double Tolerance = 1e-5;

    private const int Methods = 3;

    // The bar of the CPU reference comes after the two bars (time, error) of each method.
    private const int CpuBar = Methods * 2;

    private static readonly string[] s_variants = ["SIMD only", "SIMD + per-group partial", "thread loops"];

    // The threads of both dispatches of each method.
    private static readonly int[] s_threads = [Count + Lanes, (Count / LaneStrip) + Lanes, (Count / LoopStrip) + 1];

    private static readonly string[] s_descriptions =
    [
        "One thread per float: WaveActiveSum adds the 32 lanes -> 524,288 partials -> one SIMD group folds them",
        "A thread adds 128 floats in a loop, WaveActiveSum adds the 32 lanes -> 4,096 partials -> one SIMD group",
        "A thread adds 4,096 floats in a loop -> 4,096 partials -> one thread adds them. No SIMD-group operation",
    ];

    private readonly GraphicsDevice _device;
    private readonly ComputeContext _context;
    private readonly ReadOnlyBuffer<float> _input;
    private readonly ReadWriteBuffer<float> _partials;
    private readonly ReadWriteBuffer<float> _totals;
    private readonly ReadWriteBuffer<float> _bars;

    private readonly float[] _sums = new float[Methods];
    private readonly double[] _errors = new double[Methods];
    private readonly double[] _milliseconds = new double[Methods];
    private readonly double[] _smoothedMilliseconds = new double[Methods];
    private readonly bool[] _hasRun = new bool[Methods];
    private readonly float[] _shown = new float[CpuBar + 1];
    private double _cpuMilliseconds;
    private bool _hasReference;

    public ReductionPage(GraphicsDevice device)
    {
        _device = device;
        _context = device.CreateComputeContext();
        _input = device.AllocateReadOnlyBuffer<float>(Count);
        _partials = device.AllocateReadWriteBuffer<float>(Count / Lanes);
        _totals = device.AllocateReadWriteBuffer<float>(Methods);
        _bars = device.AllocateReadWriteBuffer<float>(CpuBar + 1);

        // The input never changes: a hash of the index, as 24 random bits in [0, 1).
        Span<float> values = _input.AsSpan();

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (Hash((uint)i) >> 8) * (1f / 16777216f);
        }
    }

    public string Name => "Reduction";

    public string Feature => "SIMD-group reductions";

    public string Help => "V method";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>The GPU's sum by the active variant, from the last <see cref="Compute"/>.</summary>
    public float Total => _sums[Variant];

    /// <summary>The GPU's sum by each variant that has run (0 for one that has not).</summary>
    public IReadOnlyList<float> Totals => _sums;

    /// <summary>The double-precision sum of the last <see cref="Check"/>.</summary>
    public double Reference { get; private set; }

    public void Compute(LabFrame frame)
    {
        for (int method = 0; method < Methods; method++)
        {
            // In the window every method runs every frame, so all the bars are live. A reproducible
            // frame only pays for the active one, and once for the others so that the page can show
            // their results.
            if (method == Variant || !frame.Deterministic || !_hasRun[method])
            {
                Run(method);

                _smoothedMilliseconds[method] = _smoothedMilliseconds[method] == 0
                    ? _milliseconds[method]
                    : (_smoothedMilliseconds[method] * 0.9) + (_milliseconds[method] * 0.1);
            }
        }

        GpuMilliseconds = _milliseconds[Variant];
    }

    public LabCheck Check()
    {
        ReadOnlySpan<float> values = _input.AsReadOnlySpan();

        long start = Stopwatch.GetTimestamp();
        double reference = CpuReduction.Double(values);
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        Reference = reference;
        _hasReference = true;
        _cpuMilliseconds = _cpuMilliseconds == 0 ? cpuMs : (_cpuMilliseconds * 0.7) + (cpuMs * 0.3);

        for (int method = 0; method < Methods; method++)
        {
            _errors[method] = _hasRun[method] ? _sums[method] - reference : 0;
        }

        double error = _errors[Variant];
        double relative = Math.Abs(error) / Math.Max(1, Math.Abs(reference));
        bool passed = _hasRun[Variant] && relative <= Tolerance;

        string summary = Invariant(
            $"{Total:F1} is {error:+0.00;-0.00} off the double sum {reference:F2}: {relative * 1e6:F3} ppm, limit {Tolerance * 1e6:F0} (CPU: one core)");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        bool live = !frame.Deterministic;
        Span<float> target = stackalloc float[CpuBar + 1];

        // The first bar of a method is its GPU time; in a reproducible picture, which must not show a
        // timing, it is the number of threads launched instead (on a log scale: they span 1 to 4,096).
        double slowest = Math.Max(_cpuMilliseconds, Math.Max(_smoothedMilliseconds[0], Math.Max(_smoothedMilliseconds[1], _smoothedMilliseconds[2])));
        double largestError = Math.Max(Math.Abs(_errors[0]), Math.Max(Math.Abs(_errors[1]), Math.Abs(_errors[2])));

        for (int method = 0; method < Methods; method++)
        {
            target[method * 2] = live
                ? (float)(_smoothedMilliseconds[method] / Math.Max(slowest, 1e-9))
                : MathF.Log2(s_threads[method]) / MathF.Log2(s_threads[0]);

            target[(method * 2) + 1] = !_hasReference ? DrawReduction.NoBar : largestError == 0 ? 0f : (float)(Math.Abs(_errors[method]) / largestError);
        }

        target[CpuBar] = live && _cpuMilliseconds > 0 ? (float)(_cpuMilliseconds / Math.Max(slowest, 1e-9)) : DrawReduction.NoBar;

        Span<float> bars = _bars.AsSpan();

        for (int bar = 0; bar < target.Length; bar++)
        {
            // A live bar eases toward its value; a reproducible one is at it.
            bool ease = live && target[bar] >= 0 && _shown[bar] >= 0;
            _shown[bar] = ease ? _shown[bar] + ((target[bar] - _shown[bar]) * 0.15f) : target[bar];
            bars[bar] = _shown[bar];
        }

        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawReduction(canvas.Pixels, _bars, LabCanvas.Width, Variant, frame.Time));

        canvas.Text(2, 4, "The sum of 16,777,216 floats in 0..1, three ways, all on the GPU", TextColor.White);
        canvas.Text(2, 5, "OPS: WAVEACTIVESUM, WAVEISFIRSTLANE", TextColor.Accent);

        for (int method = 0; method < Methods; method++)
        {
            int row = DrawReduction.FirstRow + (method * DrawReduction.RowsPerMethod);
            bool active = method == Variant;

            canvas.Text(2, row, Invariant($"{method + 1}  {s_variants[method]}"), active ? TextColor.White : TextColor.Dim);
            canvas.Text(34, row, Invariant($"{s_threads[method],10:N0} threads"), active ? TextColor.White : TextColor.Dim);
            canvas.Text(2, row + 1, s_descriptions[method], active ? TextColor.Cool : TextColor.Dim);

            if (_hasRun[method])
            {
                canvas.Text(58, row, Invariant($"sum {_sums[method]:F1}"), active ? TextColor.White : TextColor.Dim);
            }

            if (live)
            {
                canvas.Text(2, row + 2, "GPU time", TextColor.Dim);
                canvas.Text(2, row + 3, Invariant($"{_smoothedMilliseconds[method]:F2} ms"), active ? TextColor.Accent : TextColor.Cool);
            }
            else
            {
                canvas.Text(2, row + 2, "threads", TextColor.Dim);
                canvas.Text(2, row + 3, Invariant($"{s_threads[method]:N0}"), active ? TextColor.Accent : TextColor.Cool);
            }

            if (_hasReference && _hasRun[method])
            {
                canvas.Text(2, row + 4, "error", TextColor.Dim);
                canvas.Text(2, row + 5, Invariant($"{_errors[method]:+0.00;-0.00}"), TextColor.Bad);
            }
        }

        int cpuRow = DrawReduction.FirstRow + (Methods * DrawReduction.RowsPerMethod);
        canvas.Text(2, cpuRow, "CPU reference", TextColor.Dim);
        canvas.Text(34, cpuRow, "one core, in double precision", TextColor.Dim);

        if (_hasReference)
        {
            canvas.Text(66, cpuRow, Invariant($"sum {Reference:F2}"), TextColor.Dim);
        }

        if (live)
        {
            canvas.Text(2, cpuRow + 2, "CPU time", TextColor.Dim);
            canvas.Text(2, cpuRow + 3, _cpuMilliseconds > 0 ? Invariant($"{_cpuMilliseconds:F2} ms") : "...", TextColor.Dim);
        }
        else
        {
            canvas.Text(2, cpuRow + 2, "Timings in summary.txt. A bar here is the threads a method launches (log scale) and its error.", TextColor.Dim);
        }

        int notes = cpuRow + 5;
        canvas.Text(2, notes, "float sum = Gpu.WaveActiveSum(input[i]);", TextColor.Cool);
        canvas.Text(56, notes, "the sum of the group's 32 lanes, in every lane", TextColor.Dim);
        canvas.Text(2, notes + 1, "if (Gpu.WaveIsFirstLane()) partials[i / 32] = sum;", TextColor.Cool);
        canvas.Text(56, notes + 1, "and one lane stores it", TextColor.Dim);
        canvas.Text(2, notes + 3, "Second pass: one SIMD group of 32 threads. Lane L adds partials[L], partials[L + 32], ... in a loop,", TextColor.Dim);
        canvas.Text(2, notes + 4, "WaveActiveSum folds the 32 lanes, and the first lane stores the total.", TextColor.Dim);
        canvas.Text(2, notes + 6, "No shared memory, no barrier, no atomics: the lanes of a SIMD group combine their values in one step.", TextColor.Accent);
        canvas.Text(2, notes + 7, "Every float add rounds; the three methods add in different orders, so their errors differ.", TextColor.Dim);
    }

    public bool OnKeyDown(Key key)
    {
        if (key != Key.V)
        {
            return false;
        }

        Variant = (Variant + 1) % Methods;
        return true;
    }

    public void Dispose()
    {
        _context.Dispose();
        _input.Dispose();
        _partials.Dispose();
        _totals.Dispose();
        _bars.Dispose();
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    /// <summary>Runs both dispatches of a method in one command buffer and times them, commit to completion.</summary>
    private void Run(int method)
    {
        long start = Stopwatch.GetTimestamp();

        switch (method)
        {
            case 0:
                _context.For(Count, new SumLanes(_input, _partials));
                _context.For(Lanes, new FoldInOneGroup(_partials, _totals, Count / Lanes, method));
                break;

            case 1:
                _context.For(Count / LaneStrip, new SumStripsAndLanes(_input, _partials, LaneStrip));
                _context.For(Lanes, new FoldInOneGroup(_partials, _totals, Count / LaneStrip / Lanes, method));
                break;

            default:
                _context.For(Count / LoopStrip, new SumStrips(_input, _partials, LoopStrip));
                _context.For(1, new SumSerial(_partials, _totals, Count / LoopStrip, method));
                break;
        }

        _context.Execute();
        _milliseconds[method] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _sums[method] = _totals[method];
        _hasRun[method] = true;
    }
}

/// <summary>The reduction in plain C#: the reference the GPU's sums are checked against.</summary>
public static class CpuReduction
{
    /// <summary>
    /// The sum in double precision on one core. Four accumulators, so that the adds do not wait for
    /// each other; they take the elements in a fixed pattern, so the result is the same on every run.
    /// </summary>
    public static double Double(ReadOnlySpan<float> values)
    {
        double s0 = 0, s1 = 0, s2 = 0, s3 = 0;
        int i = 0;

        for (; i + 4 <= values.Length; i += 4)
        {
            s0 += values[i];
            s1 += values[i + 1];
            s2 += values[i + 2];
            s3 += values[i + 3];
        }

        for (; i < values.Length; i++)
        {
            s0 += values[i];
        }

        return (s0 + s1) + (s2 + s3);
    }
}

/// <summary>
/// "SIMD only", first pass: one thread per float. <c>WaveActiveSum</c> returns, in every lane, the sum
/// of the values of the 32 lanes of its SIMD group, and the group's first lane stores it.
/// </summary>
/// <remarks>
/// Dispatched over a multiple of 32 threads with a 1D threadgroup that is one too (the default, 256),
/// so SIMD group <c>g</c> of the dispatch is the threads <c>32g .. 32g + 31</c> and
/// <c>partials[i / 32]</c> is written exactly once.
/// </remarks>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SumLanes(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> partials) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        float sum = Gpu.WaveActiveSum(input[i]);

        if (Gpu.WaveIsFirstLane())
        {
            partials[i / ReductionPage.Lanes] = sum;
        }
    }
}

/// <summary>
/// "SIMD + per-group partial", first pass: every thread adds its own strip of <paramref name="strip"/>
/// floats in a loop, then the 32 lanes of a SIMD group fold their strips and the first lane stores
/// one partial for the group.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SumStripsAndLanes(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> partials, int strip) : IComputeShader
{
    public void Execute()
    {
        int thread = ThreadIds.X;
        int first = thread * strip;
        float local = 0f;

        for (int k = 0; k < strip; k++)
        {
            local += input[first + k];
        }

        float sum = Gpu.WaveActiveSum(local);

        if (Gpu.WaveIsFirstLane())
        {
            partials[thread / ReductionPage.Lanes] = sum;
        }
    }
}

/// <summary>
/// The second pass of both SIMD methods: one SIMD group of 32 threads adds all the partials. Lane
/// <c>L</c> adds every 32nd partial starting at <c>L</c> in a loop, <c>WaveActiveSum</c> folds the
/// 32 lanes, and the first lane stores the total.
/// </summary>
/// <remarks>Dispatched over exactly 32 threads; the threadgroup size makes them one SIMD group.</remarks>
[ThreadGroupSize(ReductionPage.Lanes)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct FoldInOneGroup(ReadWriteBuffer<float> partials, ReadWriteBuffer<float> totals, int count, int slot) : IComputeShader
{
    public void Execute()
    {
        float local = 0f;

        for (int i = ThreadIds.X; i < count; i += ReductionPage.Lanes)
        {
            local += partials[i];
        }

        float total = Gpu.WaveActiveSum(local);

        if (Gpu.WaveIsFirstLane())
        {
            totals[slot] = total;
        }
    }
}

/// <summary>"Thread loops", first pass: one thread per strip of <paramref name="strip"/> floats, added in a loop.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SumStrips(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> partials, int strip) : IComputeShader
{
    public void Execute()
    {
        int first = ThreadIds.X * strip;
        float sum = 0f;

        for (int k = 0; k < strip; k++)
        {
            sum += input[first + k];
        }

        partials[ThreadIds.X] = sum;
    }
}

/// <summary>"Thread loops", second pass: one thread adds the <paramref name="count"/> partials.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SumSerial(ReadWriteBuffer<float> partials, ReadWriteBuffer<float> totals, int count, int slot) : IComputeShader
{
    public void Execute()
    {
        float sum = 0f;

        for (int i = 0; i < count; i++)
        {
            sum += partials[i];
        }

        totals[slot] = sum;
    }
}

/// <summary>
/// Draws the page's bars: two per method (its time, or its thread count in a reproducible picture,
/// and its error) and one for the CPU reference. <paramref name="bars"/> holds each bar's length as a
/// fraction of the track, or <see cref="NoBar"/>.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawReduction(ReadWriteBuffer<uint> pixels, ReadWriteBuffer<float> bars, int width, int active, float time) : IComputeShader
{
    /// <summary>The text row of the first method's heading; a method takes <see cref="RowsPerMethod"/> rows.</summary>
    public const int FirstRow = 7;

    /// <summary>A heading, a description, two bars of two rows each and a blank row.</summary>
    public const int RowsPerMethod = 7;

    public const int BarX = 168;
    public const int BarWidth = 1080;

    /// <summary>The number of bars: two per method and the CPU's.</summary>
    public const int Bars = 7;

    /// <summary>A bar length that means "draw neither the bar nor its track".</summary>
    public const float NoBar = -1f;

    private static uint Pack(Float3 color)
    {
        Float3 c = Gpu.Round(Gpu.Saturate(color) * 255f);
        return (uint)c.X | ((uint)c.Y << 8) | ((uint)c.Z << 16) | 0xFF000000u;
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int row = (y / LabCanvas.CellHeight) - FirstRow;

        if (row >= 0)
        {
            int block = row / RowsPerMethod;
            int line = row % RowsPerMethod;

            // A marker in the margin, next to the method that is the active variant.
            if (block == active && line < 6 && x >= 8 && x < 12)
            {
                color = new Float3(1f, 0.8f, 0.35f);
            }

            int bar = (block * 2) + ((line - 2) / 2);

            if (line >= 2 && line < 6 && bar < Bars && x >= BarX && x < BarX + BarWidth)
            {
                // A bar is two text rows high, less a margin.
                int v = y - ((FirstRow + (block * RowsPerMethod) + 2 + (((line - 2) / 2) * 2)) * LabCanvas.CellHeight);
                float length = bars[bar];

                if (length >= 0f && v >= 3 && v < 29)
                {
                    int u = x - BarX;
                    int filled = Gpu.Max(3, (int)(Gpu.Saturate(length) * BarWidth));

                    if (u < filled)
                    {
                        bool isError = (bar & 1) == 1;
                        Float3 fill = new Float3(0.3f, 0.62f, 0.9f);

                        if (isError)
                        {
                            fill = new Float3(0.95f, 0.42f, 0.36f);
                        }
                        else if (block == active)
                        {
                            fill = new Float3(1f, 0.8f, 0.35f);
                        }
                        else if (bar == Bars - 1)
                        {
                            fill = new Float3(0.62f, 0.66f, 0.72f);
                        }

                        // Rounded top to bottom, and a slow ripple along the bars that are not errors.
                        float gloss = 0.78f + (0.22f * (1f - Gpu.Abs((v - 16f) / 13f)));
                        float ripple = isError ? 1f : 0.93f + (0.07f * Gpu.Sin((u * 0.05f) - (time * 3f)));
                        color = fill * gloss * ripple;
                    }
                    else
                    {
                        // The empty track, with a tick every tenth.
                        color = u % (BarWidth / 10) == 0 ? new Float3(0.09f, 0.1f, 0.13f) : new Float3(0.03f, 0.04f, 0.06f);
                    }
                }
            }
        }

        pixels[(y * width) + x] = Pack(color);
    }
}
