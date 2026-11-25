// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// Half precision and bool vectors: one many-tap blur written twice, on <see cref="Float4"/> pixels
/// and on <see cref="Half4"/> pixels, timed side by side; what the 11 bits of a half cost in accuracy,
/// measured against the same blur in <c>double</c>; and a mask made of a componentwise comparison,
/// <c>Gpu.Select(color &gt; threshold, color, 0)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both blurs read the same numbers: every channel of the source is a multiple of 1/256, which a half
/// holds exactly. A half pixel is 8 bytes where a float pixel is 16, so the half blur moves half the
/// memory; its arithmetic rounds to 11 significant bits after every operation, and the running sum
/// of a 289-tap blur grows to several thousand, where halves are 4 apart. That is the error the third
/// panel shows. (Reading halves and summing them in floats would keep the bandwidth and lose the
/// error; here the whole kernel is half on purpose.)
/// </para>
/// <para>
/// The check: the float blur is repeated with <c>ForCpu</c> over the whole image and must match the
/// GPU bit for bit, and so must the half blur on a <see cref="Tile"/> × <see cref="Tile"/> tile. Both
/// kernels are compiled with <c>[ShaderOptions(FpContract = false)]</c>, so that the GPU rounds the
/// product and the sum of every tap separately, as C# does; half arithmetic is then the same on both
/// sides, bit for bit (measured on an Apple M4 Pro, which does IEEE half arithmetic). The half blur
/// is only repeated on a tile because the CPU has no half arithmetic of its own (<see cref="Half"/> computes in <c>float</c> and rounds), which makes it
/// some fifty times slower than the float blur there; the CPU time the page shows is the float blur.
/// The mask is repeated with <c>ForCpu</c> over the whole image and must match exactly as well.
/// </para>
/// </remarks>
public sealed class HalfPrecisionPage : ILabPage
{
    /// <summary>The image is <see cref="Size"/> × <see cref="Size"/> pixels.</summary>
    public const int Size = 1024;

    /// <summary>The largest blur radius; the source has a border this wide, so the kernels need no edge handling.</summary>
    public const int MaxRadius = 8;

    /// <summary>The width (and height) of the source with its border.</summary>
    public const int Padded = Size + (2 * MaxRadius);

    /// <summary>The side of the tile the half blur is repeated on with <c>ForCpu</c>, and where it starts.</summary>
    public const int Tile = 96;
    public const int TileOrigin = (Size - Tile) / 2;

    /// <summary>The largest error against the <c>double</c> blur the check accepts, per channel (values are 0..1).</summary>
    public const double HalfErrorBound = 2e-2;
    public const double FloatErrorBound = 1e-6;

    private const string BaselineName = "the same blur in float";

    // How many times Compute runs each blur; see there.
    private const int Repeats = 3;

    private static readonly string[] s_variants = ["289 taps (radius 8)", "81 taps (radius 4)"];

    private readonly GraphicsDevice _device;
    private readonly ReadOnlyBuffer<Float4> _source;
    private readonly ReadOnlyBuffer<Half4> _sourceHalf;
    private readonly ReadWriteBuffer<Float4> _float;
    private readonly ReadWriteBuffer<Half4> _half;
    private readonly ReadWriteBuffer<Float4> _cpuFloat;
    private readonly ReadWriteBuffer<Half4> _cpuTile;
    private readonly ReadWriteBuffer<uint> _highlights;
    private readonly ReadWriteBuffer<uint> _cpuHighlights;
    private readonly ReadWriteBuffer<float> _error;
    private readonly double[] _reference = new double[Size * Size * 3];
    private int _referenceRadius;
    private int _variant;
    private Half3 _threshold;

    // Smoothed times for the page's own labels: single dispatches jitter by tens of microseconds.
    private double _smoothedMs;
    private double _smoothedFloatMs;

    public HalfPrecisionPage(GraphicsDevice device)
    {
        _device = device;

        Float4[] source = CreateSource();
        Half4[] sourceHalf = new Half4[source.Length];

        for (int i = 0; i < source.Length; i++)
        {
            // Exact: every channel is a multiple of 1/256.
            sourceHalf[i] = (Half4)source[i];
        }

        _source = device.AllocateReadOnlyBuffer<Float4>(source);
        _sourceHalf = device.AllocateReadOnlyBuffer<Half4>(sourceHalf);
        _float = device.AllocateReadWriteBuffer<Float4>(Size * Size);
        _half = device.AllocateReadWriteBuffer<Half4>(Size * Size);
        _cpuFloat = device.AllocateReadWriteBuffer<Float4>(Size * Size);
        _cpuTile = device.AllocateReadWriteBuffer<Half4>(Tile * Tile);
        _highlights = device.AllocateReadWriteBuffer<uint>(Size * Size);
        _cpuHighlights = device.AllocateReadWriteBuffer<uint>(Size * Size);
        _error = device.AllocateReadWriteBuffer<float>(Size * Size);
        device.Prepare(ShaderReference.Of<TentBlurFloat>(), ShaderReference.Of<TentBlurHalf>(), ShaderReference.Of<HighlightMask>(), ShaderReference.Of<DrawHalfPrecision>());
    }

    public string Name => "Half precision";

    public string Feature => "Half4 and bool vectors";

    public string Topic => LabTopics.Math;

    public string Help => "R radius (8 / 4)";

    /// <summary>The CPU time is the float blur's: see the remarks of the class.</summary>
    public string CpuNote => "all cores, the float blur";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant
    {
        get => _variant;
        set
        {
            if (_variant != value)
            {
                // The smoothed times describe the other radius.
                (_variant, _smoothedMs, _smoothedFloatMs) = (value, 0, 0);
            }
        }
    }

    /// <summary>The half blur of the last <see cref="Compute"/>.</summary>
    public double GpuMilliseconds { get; private set; }

    /// <summary>The float blur of the last <see cref="Compute"/>, on the same image.</summary>
    public double FloatGpuMilliseconds { get; private set; }

    public LabBaseline? Baseline => new LabBaseline(BaselineName, FloatGpuMilliseconds);

    /// <summary>8, or 4 for the second variant; the blur has (2 × radius + 1)² taps.</summary>
    public int Radius => Variant == 0 ? MaxRadius : 4;

    public int Taps => ((2 * Radius) + 1) * ((2 * Radius) + 1);

    /// <summary>The largest and the mean error of the half blur against the <c>double</c> blur at the last <see cref="Check"/>, per channel.</summary>
    public double HalfMaxError { get; private set; }

    public double HalfMeanError { get; private set; }

    /// <summary>The same for the float blur.</summary>
    public double FloatMaxError { get; private set; }

    public double FloatMeanError { get; private set; }

    /// <summary>The thresholds of the mask of the last <see cref="Compute"/>, one per channel.</summary>
    public Half3 Threshold => _threshold;

    /// <summary>The half blur of the last <see cref="Compute"/>, row by row.</summary>
    public ReadOnlySpan<Half4> HalfResult => _half.AsReadOnlySpan();

    /// <summary>The float blur of the last <see cref="Compute"/>, row by row.</summary>
    public ReadOnlySpan<Float4> FloatResult => _float.AsReadOnlySpan();

    /// <summary>The mask example of the last <see cref="Compute"/>: packed RGBA, see <see cref="HighlightMask"/>.</summary>
    public ReadOnlySpan<uint> Highlights => _highlights.AsReadOnlySpan();

    /// <summary>
    /// The source with its border: soft color fields under hard-edged rings, squares and thin
    /// stripes, so that a blur has something to do everywhere. Every channel is a multiple of 1/256.
    /// </summary>
    public static Float4[] CreateSource()
    {
        Float4[] source = new Float4[Padded * Padded];

        for (int y = 0; y < Padded; y++)
        {
            for (int x = 0; x < Padded; x++)
            {
                float u = (x - MaxRadius) / (float)Size, v = (y - MaxRadius) / (float)Size;
                float r = 0.5f + (0.35f * MathF.Sin((u * 9f) + (v * 4f)));
                float g = 0.5f + (0.35f * MathF.Sin((v * 11f) - (u * 3f) + 1f));
                float b = 0.5f + (0.35f * MathF.Cos((u + v) * 7f));

                // Rings and a grid of squares: hard edges.
                float du = u - 0.5f, dv = v - 0.5f;
                float ring = MathF.Sqrt((du * du) + (dv * dv)) * 14f;

                if (ring - MathF.Floor(ring) < 0.5f)
                {
                    (r, g, b) = (b, r, g);
                }

                if (((x >> 6) + (y >> 6)) % 3 == 0 && (x & 63) > 12 && (y & 63) > 12)
                {
                    (r, g, b) = (1f - r, 1f - g, 0.15f + (b * 0.5f));
                }

                // Thin diagonal stripes: detail finer than the blur.
                float stripe = ((x + y) & 15) < 4 ? 0.18f : 0f;

                source[(y * Padded) + x] = new Float4(Quantize(r + stripe), Quantize(g + stripe), Quantize(b - stripe), 1f);
            }
        }

        return source;

        static float Quantize(float value) => MathF.Round(Math.Clamp(value, 0f, 1f) * 256f) / 256f;
    }

    public void Compute(LabFrame frame)
    {
        int radius = Radius;

        // A dispatch this short is timed by its fastest of a few runs: one sample says more about what
        // the GPU was doing just before than about the kernel.
        double fastestFloat = double.MaxValue, fastestHalf = double.MaxValue;

        for (int repeat = 0; repeat < Repeats; repeat++)
        {
            long start = Stopwatch.GetTimestamp();
            _device.For(Size, Size, new TentBlurFloat(_source, _float, Padded, radius, 0, 0, Size));
            fastestFloat = Math.Min(fastestFloat, Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            start = Stopwatch.GetTimestamp();
            _device.For(Size, Size, new TentBlurHalf(_sourceHalf, _half, Padded, radius, 0, 0, Size));
            fastestHalf = Math.Min(fastestHalf, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        FloatGpuMilliseconds = fastestFloat;
        GpuMilliseconds = fastestHalf;
        _smoothedMs = _smoothedMs == 0 ? fastestHalf : (_smoothedMs * 0.9) + (fastestHalf * 0.1);
        _smoothedFloatMs = _smoothedFloatMs == 0 ? fastestFloat : (_smoothedFloatMs * 0.9) + (fastestFloat * 0.1);

        // The mask: a threshold per channel that drifts with time.
        _threshold = new Half3(
            (Half)(0.5f + (0.2f * MathF.Sin(frame.Time * 0.7f))),
            (Half)(0.5f + (0.2f * MathF.Sin((frame.Time * 0.9f) + 2f))),
            (Half)(0.5f + (0.2f * MathF.Sin((frame.Time * 1.1f) + 4f))));
        _device.For(Size * Size, new HighlightMask(_half, _highlights, _threshold));
    }

    public LabCheck Check()
    {
        int radius = Radius;

        // The float blur on every core: the CPU time of the page, and the GPU's float result bit for bit.
        long start = Stopwatch.GetTimestamp();
        _device.ForCpu(Size, Size, new TentBlurFloat(_source, _cpuFloat, Padded, radius, 0, 0, Size));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        bool floatSame = MemoryMarshal.AsBytes(_float.AsReadOnlySpan()).SequenceEqual(MemoryMarshal.AsBytes(_cpuFloat.AsReadOnlySpan()));

        // The half blur on a tile (see the remarks of the class for why only a tile).
        _device.ForCpu(Tile, Tile, new TentBlurHalf(_sourceHalf, _cpuTile, Padded, radius, TileOrigin, TileOrigin, Tile));
        ReadOnlySpan<Half4> half = _half.AsReadOnlySpan(), tile = _cpuTile.AsReadOnlySpan();
        bool halfSame = true;

        for (int row = 0; row < Tile; row++)
        {
            ReadOnlySpan<Half4> gpuRow = half.Slice(((TileOrigin + row) * Size) + TileOrigin, Tile);
            halfSame &= MemoryMarshal.AsBytes(gpuRow).SequenceEqual(MemoryMarshal.AsBytes(tile.Slice(row * Tile, Tile)));
        }

        // The mask, from the GPU's half blur, on the whole image.
        _device.ForCpu(Size * Size, new HighlightMask(_half, _cpuHighlights, _threshold));
        bool maskSame = _highlights.AsReadOnlySpan().SequenceEqual(_cpuHighlights.AsReadOnlySpan());

        MeasureErrors(radius);

        bool passed = floatSame && halfSame && maskSame && HalfMaxError <= HalfErrorBound && FloatMaxError <= FloatErrorBound;
        string summary = passed
            ? Invariant($"bit for bit: float, half ({Tile}x{Tile} tile), mask. Max error: half {HalfMaxError:E1}, float {FloatMaxError:E1}")
            : !floatSame ? "the float blur differs from ForCpu"
            : !halfSame ? Invariant($"the half blur differs from ForCpu on the {Tile}x{Tile} tile")
            : !maskSame ? "the mask differs from ForCpu"
            : Invariant($"error against the double blur out of bounds: half {HalfMaxError:E1} (bound {HalfErrorBound:E0}), float {FloatMaxError:E1} (bound {FloatErrorBound:E0})");

        return new LabCheck(passed, summary, cpuMs);
    }

    /// <summary>
    /// Compares both GPU results with the same blur in <c>double</c>: the largest and the mean
    /// difference per channel, and for the error panel the largest of a pixel's three channels.
    /// </summary>
    private void MeasureErrors(int radius)
    {
        if (_referenceRadius != radius)
        {
            ComputeReference(radius);
            _referenceRadius = radius;
        }

        double[] reference = _reference;
        double[] halfMax = new double[Size], halfSum = new double[Size], floatMax = new double[Size], floatSum = new double[Size];

        Parallel.For(0, Size, y =>
        {
            ReadOnlySpan<Half4> half = _half.AsReadOnlySpan().Slice(y * Size, Size);
            ReadOnlySpan<Float4> single = _float.AsReadOnlySpan().Slice(y * Size, Size);
            Span<float> error = _error.AsSpan().Slice(y * Size, Size);
            double rowHalfMax = 0, rowHalfSum = 0, rowFloatMax = 0, rowFloatSum = 0;

            for (int x = 0; x < Size; x++)
            {
                int at = ((y * Size) + x) * 3;
                Half4 h = half[x];
                Float4 f = single[x];
                double hr = Math.Abs((double)h.X - reference[at]), hg = Math.Abs((double)h.Y - reference[at + 1]), hb = Math.Abs((double)h.Z - reference[at + 2]);
                double fr = Math.Abs(f.X - reference[at]), fg = Math.Abs(f.Y - reference[at + 1]), fb = Math.Abs(f.Z - reference[at + 2]);
                double pixelHalf = Math.Max(hr, Math.Max(hg, hb)), pixelFloat = Math.Max(fr, Math.Max(fg, fb));

                error[x] = (float)pixelHalf;
                rowHalfMax = Math.Max(rowHalfMax, pixelHalf);
                rowHalfSum += hr + hg + hb;
                rowFloatMax = Math.Max(rowFloatMax, pixelFloat);
                rowFloatSum += fr + fg + fb;
            }

            (halfMax[y], halfSum[y], floatMax[y], floatSum[y]) = (rowHalfMax, rowHalfSum, rowFloatMax, rowFloatSum);
        });

        double channels = (double)Size * Size * 3;
        (HalfMaxError, HalfMeanError, FloatMaxError, FloatMeanError) = (0, 0, 0, 0);

        for (int y = 0; y < Size; y++)
        {
            HalfMaxError = Math.Max(HalfMaxError, halfMax[y]);
            HalfMeanError += halfSum[y] / channels;
            FloatMaxError = Math.Max(FloatMaxError, floatMax[y]);
            FloatMeanError += floatSum[y] / channels;
        }
    }

    /// <summary>The blur of the kernels, tap for tap, in <c>double</c>: exact to about 1e-16, far below what it is compared with.</summary>
    private void ComputeReference(int radius)
    {
        ReadOnlyBuffer<Float4> source = _source;
        double[] reference = _reference;
        int first = MaxRadius - radius;

        Parallel.For(0, Size, y =>
        {
            ReadOnlySpan<Float4> pixels = source.AsReadOnlySpan();

            for (int x = 0; x < Size; x++)
            {
                double r = 0, g = 0, b = 0, total = 0;

                for (int dy = 0; dy <= 2 * radius; dy++)
                {
                    int row = ((y + first + dy) * Padded) + x + first;
                    double wy = radius + 1 - Math.Abs(dy - radius);

                    for (int dx = 0; dx <= 2 * radius; dx++)
                    {
                        double w = wy * (radius + 1 - Math.Abs(dx - radius));
                        Float4 pixel = pixels[row + dx];

                        r += pixel.X * w;
                        g += pixel.Y * w;
                        b += pixel.Z * w;
                        total += w;
                    }
                }

                int at = ((y * Size) + x) * 3;
                (reference[at], reference[at + 1], reference[at + 2]) = (r / total, g / total, b / total);
            }
        });
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        float errorScale = HalfMaxError > 0 ? (float)(1 / HalfMaxError) : 0f;
        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawHalfPrecision(canvas.Pixels, _source, _half, _error, _highlights, LabCanvas.Width, errorScale));

        int above = DrawHalfPrecision.PanelY - 24;
        int below = DrawHalfPrecision.PanelY + DrawHalfPrecision.PanelSize + 8;
        int x0 = DrawHalfPrecision.PanelX(0), x1 = DrawHalfPrecision.PanelX(1), x2 = DrawHalfPrecision.PanelX(2), x3 = DrawHalfPrecision.PanelX(3);

        canvas.Label(x0, above, "source, 1024 x 1024");
        canvas.Label(x1, above, Invariant($"blur in Half4, {Taps} taps"));
        canvas.Label(x2, above, "|half - double|");
        canvas.Label(x3, above, "Select(c > t, c, 0)");

        canvas.Label(x0, below, "8 bits a channel: exact", TextColor.Cool);
        canvas.Label(x0, below + 16, "in a half and a float", TextColor.Cool);
        canvas.Label(x1, below, "8 bytes a pixel,", TextColor.Cool);
        canvas.Label(x1, below + 16, "Float4 has 16", TextColor.Cool);

        if (HalfMaxError > 0)
        {
            canvas.Label(x2, below, Invariant($"white = {HalfMaxError:E1} (max)"), TextColor.Accent);
            canvas.Label(x2, below + 16, Invariant($"mean {HalfMeanError:E1}"), TextColor.Accent);
            canvas.Label(x2, below + 32, Invariant($"float: max {FloatMaxError:E1}"), TextColor.Dim);
        }

        canvas.Label(x3, below, Invariant($"t = {(float)_threshold.X:F2}, {(float)_threshold.Y:F2}, {(float)_threshold.Z:F2}"), TextColor.Accent);
        canvas.Label(x3, below + 16, "white: Gpu.All(c > t)", TextColor.Dim);

        int row = (below + 64) / LabCanvas.CellHeight;
        canvas.Text(2, row, "One kernel, written on Float4 and on Half4. A half has 11 significant bits and rounds", TextColor.Dim);
        canvas.Text(2, row + 1, Invariant($"after every operation; the running sum of {Taps} taps is where that shows. Half the bytes move."), TextColor.Dim);
        canvas.Text(2, row + 3, "color > t compares per channel and gives a Bool3; Gpu.Select picks per channel with it,", TextColor.Dim);
        canvas.Text(2, row + 4, "Gpu.Any and Gpu.All reduce it to one bool. The same C# runs on the CPU, bit for bit.", TextColor.Dim);

        if (!frame.Deterministic && _smoothedMs > 0 && _smoothedFloatMs > 0)
        {
            // Timings vary from run to run, so they stay out of the reproducible picture.
            canvas.Text(2, row + 6, Invariant($"This frame: half {_smoothedMs:F2} ms, float {_smoothedFloatMs:F2} ms on the GPU ({_smoothedFloatMs / _smoothedMs:F2}x)"), TextColor.Cool);
        }
    }

    public bool OnKeyDown(Key key)
    {
        if (key != Key.R)
        {
            return false;
        }

        Variant = 1 - Variant;
        return true;
    }

    public void Dispose()
    {
        _source.Dispose();
        _sourceHalf.Dispose();
        _float.Dispose();
        _half.Dispose();
        _cpuFloat.Dispose();
        _cpuTile.Dispose();
        _highlights.Dispose();
        _cpuHighlights.Dispose();
        _error.Dispose();
    }
}

/// <summary>
/// The blur on float pixels: (2 × radius + 1)² taps with tent weights, <c>(r + 1 - |dx|) × (r + 1 - |dy|)</c>.
/// Thread (x, y) blurs pixel (originX + x, originY + y) into <c>output[y × outputWidth + x]</c>, so the
/// same kernel serves the whole image and a tile of it. The source has a border of
/// <see cref="HalfPrecisionPage.MaxRadius"/> pixels, and <c>stride</c> is its width with that border.
/// </summary>
[ShaderOptions(FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct TentBlurFloat(
    ReadOnlyBuffer<Float4> image, ReadWriteBuffer<Float4> output, int stride, int radius, int originX, int originY, int outputWidth) : IComputeShader
{
    public void Execute()
    {
        int first = HalfPrecisionPage.MaxRadius - radius;
        int x = originX + ThreadIds.X + first;
        int y = originY + ThreadIds.Y + first;
        Float4 sum = new Float4(0f);
        float total = 0f;

        for (int dy = 0; dy <= 2 * radius; dy++)
        {
            int row = ((y + dy) * stride) + x;
            float wy = radius + 1 - Math.Abs(dy - radius);

            for (int dx = 0; dx <= 2 * radius; dx++)
            {
                float w = wy * (radius + 1 - Math.Abs(dx - radius));

                sum += image[row + dx] * w;
                total += w;
            }
        }

        output[(ThreadIds.Y * outputWidth) + ThreadIds.X] = sum / total;
    }
}

/// <summary>
/// <see cref="TentBlurFloat"/> on half pixels: the same statements with <see cref="Half4"/> and
/// <see cref="Half"/> for <see cref="Float4"/> and <c>float</c> (the two differ in their types, so
/// the body is written twice rather than shared). Every operation rounds to a half.
/// </summary>
[ShaderOptions(FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct TentBlurHalf(
    ReadOnlyBuffer<Half4> image, ReadWriteBuffer<Half4> output, int stride, int radius, int originX, int originY, int outputWidth) : IComputeShader
{
    public void Execute()
    {
        int first = HalfPrecisionPage.MaxRadius - radius;
        int x = originX + ThreadIds.X + first;
        int y = originY + ThreadIds.Y + first;
        Half4 sum = new Half4((Half)0f);
        Half total = (Half)0f;

        for (int dy = 0; dy <= 2 * radius; dy++)
        {
            int row = ((y + dy) * stride) + x;
            Half wy = (Half)(radius + 1 - Math.Abs(dy - radius));

            for (int dx = 0; dx <= 2 * radius; dx++)
            {
                Half w = wy * (Half)(radius + 1 - Math.Abs(dx - radius));

                sum += image[row + dx] * w;
                total += w;
            }
        }

        output[(ThreadIds.Y * outputWidth) + ThreadIds.X] = sum / total;
    }
}

/// <summary>
/// The bool-vector example: which channels of a blurred pixel are above their threshold. The
/// comparison gives a <see cref="Bool3"/>; <see cref="Gpu.Select(Bool3, Half3, Half3)"/> keeps those
/// channels and blackens the others, a pixel with all three becomes white, and the alpha says whether
/// any channel passed. Packed with <see cref="Gpu.PackUnorm4x8"/>, like the lab's frame.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HighlightMask(ReadWriteBuffer<Half4> blurred, ReadWriteBuffer<uint> highlights, Half3 threshold) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Half3 color = blurred[i].XYZ;
        Bool3 bright = color > threshold;
        Half3 kept = Gpu.Select(bright, color, new Half3((Half)0f));

        if (Gpu.All(bright))
        {
            kept = new Half3((Half)1f);
        }

        highlights[i] = Gpu.PackUnorm4x8(new Float4((Float3)kept, Gpu.Any(bright) ? 1f : 0f));
    }
}

/// <summary>Draws the four panels: the source, the half blur, its error against the double blur, and the mask.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawHalfPrecision(
    ReadWriteBuffer<uint> pixels, ReadOnlyBuffer<Float4> source, ReadWriteBuffer<Half4> half, ReadWriteBuffer<float> error, ReadWriteBuffer<uint> highlights,
    int width, float errorScale) : IComputeShader
{
    public const int PanelY = 128;
    public const int PanelSize = 296;
    public const int PanelGap = 16;
    public const int FirstPanelX = 24;

    /// <summary>How many shades the error panel has between black (no error) and white (the largest).</summary>
    public const float ErrorSteps = 8f;

    /// <summary>The left edge of panel 0..3.</summary>
    public static int PanelX(int panel) => FirstPanelX + (panel * (PanelSize + PanelGap));

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int py = y - PanelY;
        int column = x - FirstPanelX;

        if (py >= 0 && py < PanelSize && column >= 0)
        {
            int panel = column / (PanelSize + PanelGap);
            int px = column % (PanelSize + PanelGap);

            if (panel < 4 && px < PanelSize)
            {
                // The whole image in the panel: one pixel of every 1024 / 296.
                int ix = (px * HalfPrecisionPage.Size) / PanelSize;
                int iy = (py * HalfPrecisionPage.Size) / PanelSize;
                int i = (iy * HalfPrecisionPage.Size) + ix;

                if (panel == 0)
                {
                    color = source[((iy + HalfPrecisionPage.MaxRadius) * HalfPrecisionPage.Padded) + ix + HalfPrecisionPage.MaxRadius].XYZ;
                }
                else if (panel == 1)
                {
                    color = (Float3)half[i].XYZ;
                }
                else if (panel == 2)
                {
                    // Eight steps from no error to the largest: rounding error is noise, and steps read better than a haze.
                    float level = Gpu.Ceil(Gpu.Saturate(error[i] * errorScale) * ErrorSteps) / ErrorSteps;
                    color = Gpu.Lerp(new Float3(0.03f, 0.04f, 0.06f), new Float3(1f, 0.45f, 0.2f), Gpu.Sqrt(level)) + new Float3(level * level * 0.6f);
                }
                else
                {
                    Float4 kept = Gpu.UnpackUnorm4x8(highlights[i]);
                    color = Gpu.Lerp(new Float3(0.03f, 0.04f, 0.06f), kept.XYZ, kept.W);
                }
            }
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
