// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.SimdGroups.Pages;

/// <summary>
/// A 1D signal smoothed by its neighbours, where a thread gets its neighbours' samples from the lanes
/// next to it (<c>Gpu.WaveShuffleUp</c>, <c>Gpu.WaveShuffleDown</c>) instead of from the buffer. A
/// lane can only reach the lanes of its own SIMD group, so every group of 32 samples smooths on its
/// own: pass after pass each group flattens toward its own mean, and the group edges show as steps.
/// </summary>
/// <remarks>
/// <para>
/// The signal has 1,000 samples, which is not a multiple of 32: the last SIMD group has 8 lanes.
/// Shuffling up is safe everywhere (the lowest lanes get their own value back), but shuffling down
/// from the top lanes of that group would read lanes that do not exist, which is undefined; the kernels
/// ask <c>Gpu.WaveActiveCountBits(true)</c> how many lanes there are and keep their own value instead.
/// </para>
/// <para>
/// The page also runs the same passes on the CPU with no group edges, for contrast, and the check
/// repeats the per-group filter in C# in the same order of operations.
/// </para>
/// </remarks>
public sealed class ShufflePage : ILabPage
{
    /// <summary>The number of samples: 31 SIMD groups of 32 and one of 8, in four threadgroups of up to 256.</summary>
    public const int Count = 1000;

    /// <summary>The lanes of a SIMD group.</summary>
    public const int Lanes = 32;

    /// <summary>The threads of a threadgroup of the smoothing kernels: the default for a 1D dispatch.</summary>
    public const int ThreadGroup = 256;

    /// <summary>The most passes the animation reaches.</summary>
    public const int MaxPasses = 200;

    /// <summary>The largest difference from the C# filter that passes the check: both compute in floats, but the GPU may fuse a multiply and an add.</summary>
    public const float Tolerance = 1e-5f;

    private static readonly string[] s_variants = ["3-tap", "5-tap"];

    private readonly GraphicsDevice _device;
    private readonly ComputeContext _context;
    private readonly ReadWriteBuffer<float> _a;
    private readonly ReadWriteBuffer<float> _b;
    private readonly ReadWriteBuffer<float> _plot;
    private readonly float[] _signal = new float[Count];
    private readonly float[] _means = new float[Count];
    private readonly float[] _global = new float[Count];
    private readonly float[] _expected = new float[Count];
    private readonly float[] _scratch = new float[Count];
    private ReadWriteBuffer<float> _result;

    public ShufflePage(GraphicsDevice device)
    {
        _device = device;
        _context = device.CreateComputeContext();
        _a = device.AllocateReadWriteBuffer<float>(Count);
        _b = device.AllocateReadWriteBuffer<float>(Count);
        _plot = device.AllocateReadWriteBuffer<float>(DrawShuffle.Curves * Count);
        _result = _a;

        // A slow wave, a ripple, grain and a step that starts and ends in the middle of a SIMD group.
        for (int i = 0; i < Count; i++)
        {
            float grain = ((Hash((uint)i) >> 8) * (1f / 16777216f)) - 0.5f;
            float step = i >= 432 && i < 624 ? 0.14f : 0f;
            _signal[i] = 0.44f + (0.22f * MathF.Sin((i * (MathF.Tau / 310f)) + 0.6f)) + (0.05f * MathF.Sin(i * (MathF.Tau / 23f))) + (0.12f * grain) + step;
        }

        // Each sample's SIMD-group mean: what the group's samples are heading for (the filters keep it).
        for (int first = 0; first < Count; first += Lanes)
        {
            Span<float> group = _signal.AsSpan(first, Math.Min(Lanes, Count - first));
            float sum = 0f;

            foreach (float value in group)
            {
                sum += value;
            }

            _means.AsSpan(first, group.Length).Fill(sum / group.Length);
        }
    }

    public string Name => "Shuffle";

    public string Feature => "lane shuffles";

    public string Help => "V taps";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>The number of smoothing passes of the last <see cref="Compute"/>.</summary>
    public int Passes { get; private set; }

    /// <summary>The GPU's result of the last <see cref="Compute"/>.</summary>
    public ReadWriteBuffer<float> Result => _result;

    /// <summary>The number of passes at an animation time: it swings between 1 and <see cref="MaxPasses"/>.</summary>
    public static int PassesAt(float time) => 1 + (int)MathF.Round((MaxPasses - 1) * (0.5f - (0.5f * MathF.Cos(time * 0.7f))));

    public void Compute(LabFrame frame)
    {
        Passes = PassesAt(frame.Time);
        _a.CopyFrom(_signal);

        ReadWriteBuffer<float> source = _a;
        ReadWriteBuffer<float> target = _b;

        long start = Stopwatch.GetTimestamp();

        // All the passes go into one command buffer; each reads what the one before wrote.
        for (int pass = 0; pass < Passes; pass++)
        {
            if (Variant == 0)
            {
                _context.For(Count, new SmoothInGroups3(source, target));
            }
            else
            {
                _context.For(Count, new SmoothInGroups5(source, target));
            }

            (source, target) = (target, source);
        }

        _context.Execute();
        GpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _result = source;

        // For the picture: the same passes with no group edges.
        CpuSmoothing.Run(_signal, _global, _scratch, Passes, Taps, groupSize: Count);
    }

    public LabCheck Check()
    {
        long start = Stopwatch.GetTimestamp();
        CpuSmoothing.Run(_signal, _expected, _scratch, Passes, Taps, Lanes);
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        ReadOnlySpan<float> result = _result.AsReadOnlySpan();
        float largest = 0f;

        for (int i = 0; i < Count; i++)
        {
            float difference = MathF.Abs(result[i] - _expected[i]);

            // Written so that a NaN counts as a difference.
            if (!(difference <= largest))
            {
                largest = difference;
            }
        }

        bool passed = largest <= Tolerance;
        string summary = Invariant(
            $"{Passes} {PassWord}: equal to the per-group filter in C# within {largest:0.0E+0} (limit {Tolerance:0E+0}; CPU: one core)");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        Span<float> plot = _plot.AsSpan();
        _signal.CopyTo(plot.Slice(DrawShuffle.InputCurve * Count, Count));
        _result.AsReadOnlySpan().CopyTo(plot.Slice(DrawShuffle.GroupsCurve * Count, Count));
        _global.CopyTo(plot.Slice(DrawShuffle.GlobalCurve * Count, Count));
        _means.CopyTo(plot.Slice(DrawShuffle.MeansCurve * Count, Count));

        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawShuffle(canvas.Pixels, _plot, LabCanvas.Width, Count));

        canvas.Text(2, 5, "OPS: WAVESHUFFLEUP, WAVESHUFFLEDOWN, WAVEACTIVECOUNTBITS, WAVEGETLANEINDEX", TextColor.Accent);
        canvas.Text(2, 4, Invariant($"{Passes} {PassWord} of a {Taps}-tap filter over {Count:N0} samples; a lane reads its neighbours' samples by a lane shuffle"), TextColor.White);

        // The threadgroups, above their first sample.
        for (int first = 0; first < Count; first += ThreadGroup)
        {
            int x = DrawShuffle.PlotX + (first * DrawShuffle.PlotWidth / Count);
            canvas.Label(x + LabCanvas.CellWidth - 1, DrawShuffle.PlotY - LabCanvas.CellHeight, Invariant($"threadgroup {first / ThreadGroup}: {first}..{Math.Min(first + ThreadGroup, Count) - 1}"));
        }

        int row = DrawShuffle.LegendRow;
        canvas.Text(7, row, "GPU: every SIMD group smooths its own 32 samples, so the group edges show as steps", TextColor.Accent);
        canvas.Text(7, row + 1, "CPU, for contrast: the same passes with no group edges", TextColor.Cool);
        canvas.Text(7, row + 2, "the input", TextColor.Dim);
        canvas.Text(27, row + 2, "each group's mean, where its samples are heading", TextColor.Dim);

        if (Variant == 0)
        {
            canvas.Text(2, row + 4, "float left  = Gpu.WaveShuffleUp(v, 1);", TextColor.Cool);
            canvas.Text(48, row + 4, "lane i reads lane i - 1; lane 0 gets its own value back", TextColor.Dim);
            canvas.Text(2, row + 5, "float down  = Gpu.WaveShuffleDown(v, 1);", TextColor.Cool);
            canvas.Text(48, row + 5, "lane i reads lane i + 1; lane 31 gets its own back", TextColor.Dim);
            canvas.Text(2, row + 6, "float right = lane + 1 < active ? down : v;", TextColor.Cool);
            canvas.Text(48, row + 6, "active = WaveActiveCountBits(true): 8 in the last group", TextColor.Dim);
            canvas.Text(2, row + 7, "target[i] = (left + 2 * v + right) * 0.25;", TextColor.Cool);
        }
        else
        {
            canvas.Text(2, row + 4, "left1 = WaveShuffleUp(v, 1);    left2 = WaveShuffleUp(v, 2);", TextColor.Cool);
            canvas.Text(65, row + 4, "the lowest lanes get their own back", TextColor.Dim);
            canvas.Text(2, row + 5, "down1 = WaveShuffleDown(v, 1);  down2 = WaveShuffleDown(v, 2);", TextColor.Cool);
            canvas.Text(65, row + 5, "active = WaveActiveCountBits(true)", TextColor.Dim);
            canvas.Text(2, row + 6, "right1 = lane + 1 < active ? down1 : v;  right2 = lane + 2 < active ? down2 : v;", TextColor.Cool);
            canvas.Text(2, row + 7, "target[i] = (left2 + 4 * left1 + 6 * v + 4 * right1 + right2) / 16;", TextColor.Cool);
        }

        canvas.Text(2, row + 9, "1,000 = 31 SIMD groups of 32 lanes + one of 8. Reading a lane that is not there is undefined: guarded.", TextColor.Dim);
        canvas.Text(2, row + 10, "Faint vertical lines: SIMD-group edges, every 32 samples. Bright ones: threadgroup edges, every 256.", TextColor.Dim);
    }

    public bool OnKeyDown(Key key)
    {
        if (key != Key.V)
        {
            return false;
        }

        Variant = 1 - Variant;
        return true;
    }

    public void Dispose()
    {
        _context.Dispose();
        _a.Dispose();
        _b.Dispose();
        _plot.Dispose();
    }

    private int Taps => Variant == 0 ? 3 : 5;

    private string PassWord => Passes == 1 ? "pass" : "passes";

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }
}

/// <summary>The smoothing in plain C#, with the kernels' arithmetic in the kernels' order.</summary>
public static class CpuSmoothing
{
    /// <summary>
    /// Runs <paramref name="passes"/> passes of the <paramref name="taps"/>-tap filter (3 or 5) over
    /// <paramref name="signal"/> into <paramref name="output"/>. A sample only sees the samples of its
    /// own group of <paramref name="groupSize"/>; where a neighbour is outside the group it uses its
    /// own value, like the kernels. A group size of the whole signal is the filter with no group edges.
    /// </summary>
    public static void Run(ReadOnlySpan<float> signal, Span<float> output, Span<float> scratch, int passes, int taps, int groupSize)
    {
        signal.CopyTo(output);

        Span<float> source = output;
        Span<float> target = scratch;

        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < source.Length; i++)
            {
                int lane = i % groupSize;
                int active = Math.Min(groupSize, source.Length - (i - lane));
                float v = source[i];
                float left1 = lane >= 1 ? source[i - 1] : v;
                float right1 = lane + 1 < active ? source[i + 1] : v;

                if (taps == 3)
                {
                    target[i] = (left1 + (2f * v) + right1) * 0.25f;
                }
                else
                {
                    float left2 = lane >= 2 ? source[i - 2] : v;
                    float right2 = lane + 2 < active ? source[i + 2] : v;
                    target[i] = (left2 + (4f * left1) + (6f * v) + (4f * right1) + right2) * 0.0625f;
                }
            }

            Span<float> swap = source;
            source = target;
            target = swap;
        }

        // After an odd number of passes the result is in the scratch buffer.
        if ((passes & 1) == 1)
        {
            source.CopyTo(output);
        }
    }
}

/// <summary>
/// One 3-tap pass, (left + 2 × own + right) / 4, with the neighbours read from the neighbouring
/// lanes. Lane 0 has no lane below and lane 31 none above: the shuffles give them their own value
/// back, which clamps the filter at the edges of a SIMD group.
/// </summary>
/// <remarks>
/// The shuffles are on lines of their own and not inside a branch: every lane has to execute them for
/// the others to be able to read its value. In the last SIMD group of a grid that is not a multiple of
/// 32, the lanes above the last thread do not exist and reading them is undefined, so the top lane
/// that does exist keeps its own value instead.
/// </remarks>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SmoothInGroups3(ReadWriteBuffer<float> source, ReadWriteBuffer<float> target) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        float v = source[i];
        int lane = Gpu.WaveGetLaneIndex();
        int active = Gpu.WaveActiveCountBits(true);

        float left = Gpu.WaveShuffleUp(v, 1);
        float down = Gpu.WaveShuffleDown(v, 1);
        float right = lane + 1 < active ? down : v;

        target[i] = (left + (2f * v) + right) * 0.25f;
    }
}

/// <summary>One 5-tap pass, (1, 4, 6, 4, 1) / 16: shuffles by one and by two lanes, guarded the same way.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SmoothInGroups5(ReadWriteBuffer<float> source, ReadWriteBuffer<float> target) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        float v = source[i];
        int lane = Gpu.WaveGetLaneIndex();
        int active = Gpu.WaveActiveCountBits(true);

        // One shuffle per line; the lowest one (two) lanes get their own value back.
        float left1 = Gpu.WaveShuffleUp(v, 1);
        float left2 = Gpu.WaveShuffleUp(v, 2);
        float down1 = Gpu.WaveShuffleDown(v, 1);
        float down2 = Gpu.WaveShuffleDown(v, 2);
        float right1 = lane + 1 < active ? down1 : v;
        float right2 = lane + 2 < active ? down2 : v;

        target[i] = (left2 + (4f * left1) + (6f * v) + (4f * right1) + right2) * 0.0625f;
    }
}

/// <summary>
/// Draws the plot: the curves of <paramref name="plot"/> (<paramref name="count"/> samples each, one
/// after the other), a line at every SIMD-group edge, a brighter one at every threadgroup edge, and
/// the legend's swatches.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawShuffle(ReadWriteBuffer<uint> pixels, ReadWriteBuffer<float> plot, int width, int count) : IComputeShader
{
    /// <summary>The curves of the plot buffer, in this order.</summary>
    public const int InputCurve = 0;
    public const int GlobalCurve = 1;
    public const int GroupsCurve = 2;
    public const int MeansCurve = 3;
    public const int Curves = 4;

    public const int PlotX = 40;
    public const int PlotY = 132;
    public const int PlotWidth = 1200;
    public const int PlotHeight = 380;

    /// <summary>The first text row of the legend; its swatches are drawn in front of rows 0, 1 and 2.</summary>
    public const int LegendRow = 33;

    private static uint Pack(Float3 color)
    {
        Float3 c = Gpu.Round(Gpu.Saturate(color) * 255f);
        return (uint)c.X | ((uint)c.Y << 8) | ((uint)c.Z << 16) | 0xFF000000u;
    }

    private static Float3 CurveColor(int curve)
    {
        if (curve == GroupsCurve)
        {
            return new Float3(1f, 0.8f, 0.35f);
        }

        if (curve == GlobalCurve)
        {
            return new Float3(0.45f, 0.85f, 1f);
        }

        if (curve == MeansCurve)
        {
            return new Float3(0.8f, 0.84f, 0.9f);
        }

        return new Float3(0.42f, 0.47f, 0.56f);
    }

    // The plot's y of a value: 0 at the bottom, 1 at the top.
    private static float ToY(float value) => PlotY + (PlotHeight * (1f - value));

    // The curve at a fractional sample position, clamped to the ends.
    private float Value(int curve, float position)
    {
        float p = Gpu.Clamp(position, 0f, count - 1f);
        int i = (int)p;
        int j = Gpu.Min(i + 1, count - 1);
        return Gpu.Lerp(plot[(curve * count) + i], plot[(curve * count) + j], p - i);
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int px = x - PlotX;
        int py = y - PlotY;

        if (px >= 0 && px < PlotWidth && py >= 0 && py < PlotHeight)
        {
            // The sample under this pixel column and under the one before: an edge is where their group changes.
            int sample = px * count / PlotWidth;
            int before = px == 0 ? 0 : (px - 1) * count / PlotWidth;
            bool groupEdge = sample / ShufflePage.Lanes != before / ShufflePage.Lanes;
            bool threadGroupEdge = px == 0 || sample / ShufflePage.ThreadGroup != before / ShufflePage.ThreadGroup;

            color = ((sample / ShufflePage.Lanes) & 1) == 0 ? new Float3(0.028f, 0.036f, 0.055f) : new Float3(0.04f, 0.05f, 0.072f);

            if (threadGroupEdge)
            {
                color = new Float3(0.4f, 0.45f, 0.55f);
            }
            else if (groupEdge)
            {
                color = new Float3(0.13f, 0.155f, 0.2f);
            }

            // Each group's mean, as dots.
            float mean = ToY(plot[(MeansCurve * count) + sample]);

            if (Gpu.Abs(y + 0.5f - mean) < 1f && (px / 3) % 2 == 0)
            {
                color = CurveColor(MeansCurve);
            }

            // The curves, back to front. A pixel is on a curve if the curve crosses its column at its height.
            float samplesPerPixel = count / (float)PlotWidth;
            float from = (px * samplesPerPixel) - 0.5f;
            float to = ((px + 1) * samplesPerPixel) - 0.5f;

            for (int curve = InputCurve; curve <= GroupsCurve; curve++)
            {
                float y0 = ToY(Value(curve, from));
                float y1 = ToY(Value(curve, to));
                float half = curve == InputCurve ? 0.8f : 1.2f;

                if (y + 0.5f >= Gpu.Min(y0, y1) - half && y + 0.5f <= Gpu.Max(y0, y1) + half)
                {
                    color = CurveColor(curve);
                }
            }
        }

        // The legend: a swatch in front of each of its lines.
        int legendY = y - (LegendRow * LabCanvas.CellHeight);
        int line = legendY / LabCanvas.CellHeight;
        int v = legendY % LabCanvas.CellHeight;

        if (legendY >= 0 && line < 3 && v >= 6 && v < 9)
        {
            if (x >= 24 && x < 72)
            {
                color = CurveColor(line == 0 ? GroupsCurve : line == 1 ? GlobalCurve : InputCurve);
            }
            else if (line == 2 && x >= 264 && x < 312 && ((x - 264) / 3) % 2 == 0)
            {
                color = CurveColor(MeansCurve);
            }
        }

        pixels[(y * width) + x] = Pack(color);
    }
}
