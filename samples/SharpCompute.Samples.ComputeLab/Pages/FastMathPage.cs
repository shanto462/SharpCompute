// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// Per-shader compile options: the same transcendental-heavy kernel compiled twice, once the way
/// every SharpCompute shader is (IEEE-safe math, precise <c>sin</c>/<c>exp</c>/<c>pow</c>) and once
/// with <c>[ShaderOptions(FastMath = true)]</c>. The page shows what the option buys in time and
/// what it costs in accuracy, measured against the CPU running the same C#.
/// </summary>
/// <remarks>
/// The check has a bound per variant rather than "equal": math functions differ from the CPU's by a
/// few units in the last place even in safe mode, and fast math is allowed to be coarser. The error
/// panel is the difference at the last check, scaled so that the largest error is white.
/// </remarks>
public sealed class FastMathPage : ILabPage
{
    /// <summary>The field is <see cref="Size"/> × <see cref="Size"/> values.</summary>
    public const int Size = 1024;

    /// <summary>How many octaves of sin, cos, exp and pow each value sums.</summary>
    public const int Octaves = 12;

    /// <summary>The largest difference from the CPU the check accepts: safe math, then fast math.</summary>
    public const float SafeBound = 2e-5f;
    public const float FastBound = 2e-3f;

    // How many times Compute runs the kernel; see there.
    private const int Repeats = 3;

    private static readonly string[] s_variants = ["safe math (the default)", "[ShaderOptions(FastMath = true)]"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<float> _field;
    private readonly ReadWriteBuffer<float> _cpuField;
    private readonly ReadWriteBuffer<float> _error;
    private float _time;

    public FastMathPage(GraphicsDevice device)
    {
        _device = device;
        _field = device.AllocateReadWriteBuffer<float>(Size * Size);
        _cpuField = device.AllocateReadWriteBuffer<float>(Size * Size);
        _error = device.AllocateReadWriteBuffer<float>(Size * Size);
        device.Prepare(ShaderReference.Of<WaveField>(), ShaderReference.Of<WaveFieldFast>(), ShaderReference.Of<DrawFastMath>());
    }

    public string Name => "FastMath";

    public string Feature => "per-shader compile options";


    public string Topic => LabTopics.Math;

    public string Help => "F fast math on/off";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>The largest absolute difference between the GPU and the CPU at the last <see cref="Check"/>.</summary>
    public float MaxError { get; private set; }

    /// <summary>The bound the active variant is checked against.</summary>
    public float Bound => Variant == 0 ? SafeBound : FastBound;

    /// <summary>
    /// The two variants are two kernels in the precompiled library, each compiled at build time with
    /// its own options, exactly as the runtime would compile it.
    /// </summary>
    public PipelineOrigin KernelOrigin => Variant == 0 ? _device.GetPipelineOrigin<WaveField>() : _device.GetPipelineOrigin<WaveFieldFast>();

    public void Compute(LabFrame frame)
    {
        _time = frame.Time;

        // A dispatch this short is timed by its fastest of a few runs: one sample says more about what
        // the GPU was doing just before than about the kernel.
        double fastest = double.MaxValue;

        for (int repeat = 0; repeat < Repeats; repeat++)
        {
            if (Variant == 0)
            {
                _device.For(Size, Size, new WaveField(_field, Size, _time));
            }
            else
            {
                _device.For(Size, Size, new WaveFieldFast(_field, Size, _time));
            }

            fastest = Math.Min(fastest, _device.LastGpuTime.TotalMilliseconds);
        }

        GpuMilliseconds = fastest;
    }

    public LabCheck Check()
    {
        // The options are GPU compile options: on the CPU both shaders are the same C#.
        long start = Stopwatch.GetTimestamp();
        _device.ForCpu(Size, Size, new WaveField(_cpuField, Size, _time));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        ReadOnlySpan<float> gpu = _field.AsReadOnlySpan(), cpu = _cpuField.AsReadOnlySpan();
        Span<float> error = _error.AsSpan();
        float max = 0f;
        bool finite = true;

        for (int i = 0; i < gpu.Length; i++)
        {
            float difference = MathF.Abs(gpu[i] - cpu[i]);
            finite &= float.IsFinite(difference);
            error[i] = difference;
            max = MathF.Max(max, difference);
        }

        MaxError = max;
        bool passed = finite && max <= Bound;
        string summary = passed
            ? Invariant($"max error against the CPU {max:E1} over {gpu.Length:N0} values (bound {Bound:E0}, values are 0..4)")
            : Invariant($"max error against the CPU {max:E1} exceeds the bound {Bound:E0}");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawFastMath(canvas.Pixels, _field, _error, LabCanvas.Width, Size, MaxError > 0 ? 1f / MaxError : 0f));

        int labelY = DrawFastMath.PanelY - 24;
        int below = DrawFastMath.PanelY + DrawFastMath.PanelSize + 8;
        canvas.Label(DrawFastMath.FieldX, labelY, Invariant($"1M values: {Octaves} octaves of sin/cos/exp/pow"));
        canvas.Label(DrawFastMath.ErrorX, labelY, "|GPU - CPU| at the last check");
        canvas.Label(DrawFastMath.FieldX, below, Variant == 0 ? "default: IEEE-safe, precise functions" : "[ShaderOptions(FastMath = true)]", TextColor.Cool);
        canvas.Label(DrawFastMath.ErrorX, below, Invariant($"white = {MaxError:E1}, the largest error"), TextColor.Accent);
        canvas.Label(DrawFastMath.FieldX, below + 16, LabView.Describe(KernelOrigin), KernelOrigin == PipelineOrigin.Precompiled ? TextColor.Good : TextColor.Dim);
    }

    public bool OnKeyDown(Key key)
    {
        if (key != Key.F)
        {
            return false;
        }

        Variant = 1 - Variant;
        return true;
    }

    public void Dispose()
    {
        _field.Dispose();
        _cpuField.Dispose();
        _error.Dispose();
    }
}

/// <summary>
/// What both variants of the feature kernel compute. A static method of a class of its own, so that
/// two shaders can call it: each gets the function in its own MSL, compiled with its own options.
/// </summary>
public static class WaveMath
{
    /// <summary>
    /// The value at (<paramref name="x"/>, <paramref name="y"/>) of a <paramref name="size"/> ×
    /// <paramref name="size"/> field: a sum of <see cref="FastMathPage.Octaves"/> octaves of
    /// trigonometric, exponential and power terms (about 0..4).
    /// </summary>
    public static float Value(int x, int y, int size, float time)
    {
        float u = (x * 6f) / size;
        float v = (y * 6f) / size;
        float sum = 2f;

        for (int k = 1; k <= FastMathPage.Octaves; k++)
        {
            float f = k * 1.7f;
            float wave = Gpu.Sin((u * f) + time) * Gpu.Cos((v * f) - (time * 0.7f));

            sum += wave / k;
            sum += Gpu.Exp(-Gpu.Abs(Gpu.Sin((u - v) * f))) * 0.08f;
            sum += Gpu.Pow(Gpu.Abs(Gpu.Cos(f * (u + v))) + 0.001f, 1.5f) * 0.05f;
        }

        return sum;
    }
}

/// <summary>The feature kernel with the default compile options.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveField(ReadWriteBuffer<float> field, int size, float time) : IComputeShader
{
    public void Execute() => field[(ThreadIds.Y * size) + ThreadIds.X] = WaveMath.Value(ThreadIds.X, ThreadIds.Y, size, time);
}

/// <summary>
/// <see cref="WaveField"/> compiled with fast math: the same call of <see cref="WaveMath.Value"/>, so
/// only the Metal compile options differ.
/// </summary>
[ShaderOptions(FastMath = true)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveFieldFast(ReadWriteBuffer<float> field, int size, float time) : IComputeShader
{
    public void Execute() => field[(ThreadIds.Y * size) + ThreadIds.X] = WaveMath.Value(ThreadIds.X, ThreadIds.Y, size, time);
}

/// <summary>Draws the field on the left and the error map on the right.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawFastMath(
    ReadWriteBuffer<uint> pixels, ReadWriteBuffer<float> field, ReadWriteBuffer<float> error, int width, int size, float errorScale) : IComputeShader
{
    public const int PanelY = 104;
    public const int PanelSize = 512;
    public const int FieldX = 80;
    public const int ErrorX = 688;

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int py = y - PanelY;

        if (py >= 0 && py < PanelSize)
        {
            int fx = x - FieldX;
            int ex = x - ErrorX;

            if (fx >= 0 && fx < PanelSize)
            {
                // Every second value of the 1024 x 1024 field, as a blue-to-gold ramp with contour lines.
                float value = Gpu.Saturate(field[(py * 2 * size) + (fx * 2)] * 0.25f);
                float line = Gpu.Smoothstep(0.42f, 0.5f, Gpu.Abs(Gpu.Fract(value * 12f) - 0.5f));
                color = Gpu.Lerp(new Float3(0.05f, 0.12f, 0.35f), new Float3(1f, 0.8f, 0.35f), value) * (1f - (0.35f * line));
            }
            else if (ex >= 0 && ex < PanelSize)
            {
                float level = Gpu.Saturate(error[(py * 2 * size) + (ex * 2)] * errorScale);
                color = Gpu.Lerp(new Float3(0.03f, 0.04f, 0.06f), new Float3(1f, 0.45f, 0.2f), Gpu.Sqrt(level)) + new Float3(level * level * 0.6f);
            }
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
