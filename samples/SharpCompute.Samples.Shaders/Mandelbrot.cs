// Original SharpCompute sample (MIT), not a Shadertoy port.
using System;
using SharpCompute;

namespace SharpCompute.Samples.Shaders;

/// <summary>A smooth-colored Mandelbrot set written to a buffer of RGBA8 pixels packed into <c>uint</c>s (R in the low byte).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Mandelbrot(
    ReadWriteBuffer<uint> pixels, int width, int height, int maxIterations, Float2 center, float scale, float colorOffset = 0f) : IComputeShader
{
    public void Execute()
    {
        int px = ThreadIds.X;
        int py = ThreadIds.Y;

        Float2 c = center + (new Float2(px - (width * 0.5f), py - (height * 0.5f)) * (scale * 2f / height));
        Float2 z = new(0f, 0f);
        int n = 0;

        while (n < maxIterations && Gpu.Dot(z, z) < 256f)
        {
            z = new Float2((z.X * z.X) - (z.Y * z.Y), 2f * z.X * z.Y) + c;
            n++;
        }

        Float3 color = new(0f, 0f, 0f);

        if (n < maxIterations)
        {
            float smooth = n + 1f - Gpu.Log2(Gpu.Log2(Gpu.Length(z)));
            float t = (smooth * 0.03f) + colorOffset;
            color = new Float3(0.5f, 0.5f, 0.5f) + (new Float3(0.5f, 0.5f, 0.5f) * Gpu.Cos(6.28318f * (new Float3(t, t, t) + new Float3(0f, 0.1f, 0.2f))));
        }

        pixels[(py * width) + px] = PackRgba(color);
    }

    private static uint PackRgba(Float3 color)
    {
        UInt3 c = (UInt3)((Gpu.Saturate(color) * 255f) + new Float3(0.5f, 0.5f, 0.5f));
        return c.X | (c.Y << 8) | (c.Z << 16) | (255u << 24);
    }
}


/// <summary>
/// The gallery entry: an endless deep zoom into the Seahorse Valley, rendered with perturbation
/// theory (<see cref="MandelbrotPerturbation"/>) against a reference orbit computed on the CPU in
/// <see cref="FixedPoint"/>, so it keeps finding detail down to a view about 1e-30 high, where
/// plain float runs out at about 1e-6.
/// </summary>
/// <remarks>
/// The view halves every <see cref="HalvingSeconds"/> for <see cref="ZoomSeconds"/> (apart from a
/// faster pass past a minibrot, see <see cref="HalvingsAt"/>), then crossfades
/// over <see cref="FadeSeconds"/> to the start (the deep frame keeps zooming in while the next loop
/// fades in, zooming in from slightly further out), so <c>time mod Period</c> loops seamlessly and
/// any time is valid. Each frame is two dispatches: the iteration kernel, once per visible layer,
/// into a buffer of escape counts, then <see cref="MandelbrotPalette"/> into the texture.
/// </remarks>
public static class MandelbrotZoom
{
    /// <summary>Real part of the zoom center: the classic Seahorse Valley deep-zoom point.</summary>
    public const string CenterReal = "-0.743643887037158704752191506114774";

    /// <summary>Imaginary part of the zoom center.</summary>
    public const string CenterImaginary = "0.131825904205311970493132056385139";

    /// <summary>Half the view height at zoom time 0.</summary>
    public const double StartScale = 1.5;

    /// <summary>Seconds for the view to halve, outside the speed changes of <see cref="HalvingsAt"/>.</summary>
    public const double HalvingSeconds = 1.25;

    /// <summary>Length of the zoom before the loop's crossfade: <see cref="ScaleAt"/>(126) ≈ 8.6e-31.</summary>
    public const double ZoomSeconds = 126;

    /// <summary>Length of the crossfade back to the start.</summary>
    public const double FadeSeconds = 2;

    /// <summary>The loop period: frames at <c>t</c> and <c>t + Period</c> are identical.</summary>
    public const double Period = ZoomSeconds + FadeSeconds;

    /// <summary>The zoom center in float, for the shallow <see cref="Mandelbrot"/> sample.</summary>
    public static readonly Float2 Center = new(-0.743643887f, 0.131825904f);

    private static readonly object s_lock = new();
    private static readonly MandelbrotReferenceOrbit s_orbit = new(CenterReal, CenterImaginary);
    private static DeviceState? s_state;

    /// <summary>Half the view height after <paramref name="zoomTime"/> seconds of zoom (negative is further out).</summary>
    public static double ScaleAt(double zoomTime) => StartScale * Math.Pow(0.5, HalvingsAt(zoomTime));

    /// <summary>
    /// How many times the view has halved after <paramref name="zoomTime"/> seconds. Mostly one
    /// halving per <see cref="HalvingSeconds"/>, but around t = 28 the view fills up with the inside
    /// of a minibrot, so the zoom speeds up there (up to twice as fast over t = 23–33) and gives the
    /// time back by slowing down a little over t = 34–60, where there is plenty of detail. From
    /// t = 60 on the depth is the same as without the change, so the end of the zoom is too.
    /// </summary>
    public static double HalvingsAt(double zoomTime)
    {
        const double fastStart = 23, fastLength = 10, extraSpeed = 1;
        const double slowStart = 34, slowLength = 26;
        const double gained = extraSpeed * fastLength / 2;

        double seconds = zoomTime
            + (extraSpeed * RaisedCosineIntegral(zoomTime - fastStart, fastLength))
            - (2 * gained / slowLength * RaisedCosineIntegral(zoomTime - slowStart, slowLength));

        return seconds / HalvingSeconds;

        // ∫ (1 - cos(2πs/length)) / 2 ds from 0 to x, for x clamped to [0, length]: a bump in speed
        // that starts and ends smoothly, adding length / 2 in total.
        static double RaisedCosineIntegral(double x, double length)
        {
            x = Math.Clamp(x, 0, length);
            return (x / 2) - (length / (4 * Math.PI) * Math.Sin(2 * Math.PI * x / length));
        }
    }

    /// <summary>
    /// The iteration budget for a view of half-height <paramref name="scale"/>: <c>300 + 150·h + 3·h²</c>
    /// for <c>h</c> halvings of depth. Plain <c>300 + 150·h</c> is too small past h ≈ 60 here: the
    /// zoom approaches a minibrot of period ≈ 16000, and escape counts in view grow faster than
    /// linearly (median ≈ 24000 at h = 94, p99 ≈ 44000 at h = 102).
    /// </summary>
    public static int MaxIterationsAt(double scale)
    {
        double h = Math.Max(0, Math.Log2(StartScale / scale));
        return 300 + (int)((150 * h) + (3 * h * h));
    }

    /// <summary>
    /// The layers visible at <paramref name="time"/>: zoom times and the weight of the second.
    /// One layer (weight 0) during the zoom; two during the crossfade.
    /// </summary>
    public static (double ZoomTimeA, double ZoomTimeB, float Blend) LayersAt(double time)
    {
        double t = time % Period;

        if (t < 0)
        {
            t += Period;
        }

        if (t < ZoomSeconds)
        {
            return (t, t, 0f);
        }

        // Smoothstep from the deep frame (still zooming in) to the next loop, which reaches zoom
        // time 0 exactly at the wrap.
        double x = (t - ZoomSeconds) / FadeSeconds;
        return (t, t - Period, (float)(x * x * (3 - (2 * x))));
    }

    /// <summary>Renders one frame at <paramref name="time"/> seconds into <paramref name="texture"/>.</summary>
    public static void Render(GraphicsDevice device, ReadWriteTexture2D<Rgba32> texture, float time)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(texture);

        (double zoomA, double zoomB, float blend) = LayersAt(time);

        lock (s_lock)
        {
            DeviceState state = GetState(device, texture.Width * texture.Height);

            Iterate(device, state.LayerA, texture.Width, texture.Height, zoomA);

            if (blend > 0f)
            {
                Iterate(device, state.LayerB, texture.Width, texture.Height, zoomB);
            }

            (float phaseA, float densityA) = PaletteAt(zoomA);
            (float phaseB, float densityB) = PaletteAt(zoomB);

            device.ForEach(texture, new MandelbrotPalette(state.LayerA, blend > 0f ? state.LayerB : state.LayerA, blend, phaseA, densityA, phaseB, densityB));
        }
    }

    /// <summary>
    /// Runs the iteration kernel for a <paramref name="width"/> × <paramref name="height"/> view at
    /// <paramref name="zoomTime"/> into <paramref name="output"/>, in <see cref="MandelbrotPerturbation"/>'s format: <c>x</c> is
    /// the escape iteration, or -1 for a pixel that didn't escape. For tests and tools; <see cref="Render"/> uses the same path.
    /// </summary>
    /// <param name="useSeriesApproximation">Start from a <see cref="MandelbrotSeries"/> approximation, as <see cref="Render"/> does; false iterates every pixel from 0.</param>
    /// <returns>The number of iterations the series approximation skipped.</returns>
    public static int ComputeIterations(GraphicsDevice device, ReadWriteBuffer<Float4> output, int width, int height, double zoomTime, bool useSeriesApproximation = true)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(output);

        if (output.Length < width * height)
        {
            throw new ArgumentException($"The output holds {output.Length} elements; {width}x{height} needs {width * height}.", nameof(output));
        }

        lock (s_lock)
        {
            GetState(device, 0);
            return Iterate(device, output, width, height, zoomTime, useSeriesApproximation);
        }
    }

    /// <summary>Distance between neighbouring pixels for a view <paramref name="height"/> pixels high at <paramref name="zoomTime"/>.</summary>
    public static double PixelScaleAt(double zoomTime, int height) => 2 * ScaleAt(zoomTime) / height;

    // Caller holds s_lock.
    private static int Iterate(GraphicsDevice device, ReadWriteBuffer<Float4> output, int width, int height, double zoomTime, bool useSeriesApproximation = true)
    {
        double scale = ScaleAt(zoomTime);
        int maxIterations = MaxIterationsAt(scale);
        ReadOnlyBuffer<Float2> reference = EnsureReference(device, maxIterations + 1);
        double pixelScale = PixelScaleAt(zoomTime, height);

        // Every pixel lies within the half-diagonal of the center.
        double radius = 0.5 * Math.Sqrt(((double)(width - 1) * (width - 1)) + ((double)(height - 1) * (height - 1)));
        MandelbrotSeries series = useSeriesApproximation
            ? MandelbrotSeries.Compute(s_orbit.Values, pixelScale, radius, maxIterations - 1)
            : MandelbrotSeries.None;

        device.For(width, height, new MandelbrotPerturbation(
            reference, output, width, height, s_orbit.Length, maxIterations, (float)pixelScale,
            series.Skip, series.A, series.B, series.C));

        return series.Skip;
    }

    /// <summary>
    /// Palette phase and density. Deeper views have higher escape counts, so the density follows
    /// <c>1/sqrt(depth in halvings)</c> to keep a similar number of bands on screen, and the phase
    /// drifts with the log of the depth so the colors keep changing as the zoom goes on.
    /// </summary>
    private static (float Phase, float Density) PaletteAt(double zoomTime)
    {
        double halvings = Math.Max(0, HalvingsAt(zoomTime));
        return ((float)(0.04 * halvings), (float)(0.35 / Math.Sqrt(1 + (halvings / 8))));
    }

    // Caller holds s_lock.
    private static ReadOnlyBuffer<Float2> EnsureReference(GraphicsDevice device, int length)
    {
        DeviceState state = s_state!;
        s_orbit.EnsureLength(length);

        if (state.Reference is null || state.Reference.Length < s_orbit.Length)
        {
            // Grow in large steps so a zoom reallocates only a few times.
            int capacity = (s_orbit.Length + 4095) / 4096 * 4096;
            state.Reference?.Dispose();
            Float2[] padded = new Float2[capacity];
            s_orbit.Values.CopyTo(padded);
            state.Reference = device.AllocateReadOnlyBuffer<Float2>(padded);
            state.ReferenceLength = s_orbit.Length;
        }
        else if (state.ReferenceLength != s_orbit.Length)
        {
            state.Reference.CopyFrom(s_orbit.Values);
            state.ReferenceLength = s_orbit.Length;
        }

        return state.Reference;
    }

    // Caller holds s_lock. pixelCount 0 keeps the layer buffers as they are.
    private static DeviceState GetState(GraphicsDevice device, int pixelCount)
    {
        if (s_state is null || !ReferenceEquals(s_state.Device, device))
        {
            s_state?.Dispose();
            s_state = new DeviceState(device);
        }

        if (pixelCount > 0 && (s_state.LayerA is null || s_state.LayerA.Length != pixelCount))
        {
            s_state.LayerA?.Dispose();
            s_state.LayerB?.Dispose();
            s_state.LayerA = device.AllocateReadWriteBuffer<Float4>(pixelCount);
            s_state.LayerB = device.AllocateReadWriteBuffer<Float4>(pixelCount);
        }

        return s_state;
    }

    private sealed class DeviceState(GraphicsDevice device) : IDisposable
    {
        public GraphicsDevice Device { get; } = device;

        public ReadOnlyBuffer<Float2>? Reference { get; set; }

        public int ReferenceLength { get; set; }

        public ReadWriteBuffer<Float4> LayerA { get; set; } = null!;

        public ReadWriteBuffer<Float4> LayerB { get; set; } = null!;

        public void Dispose()
        {
            Reference?.Dispose();
            LayerA?.Dispose();
            LayerB?.Dispose();
        }
    }
}
