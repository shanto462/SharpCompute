// Original SharpCompute sample (MIT), not a Shadertoy port.
namespace SharpCompute.Samples.Shaders;

/// <summary>
/// The deep-zoom Mandelbrot iteration, by perturbation theory. Each pixel's <c>c = C + δc</c> is a
/// tiny offset from the reference point <c>C</c>, whose orbit <c>Z_n</c> was computed on the CPU in
/// high precision. Only the difference <c>δ_n = z_n - Z_n</c> is iterated, in float:
/// <c>δ_{n+1} = 2·Z_n·δ_n + δ_n² + δc</c>. That stays accurate at depths where <c>c</c> itself
/// can't be represented in float (or double), down to where <c>δc</c> nears float's smallest
/// normal value (Apple GPUs flush subnormals), about 1e-37.
/// </summary>
/// <remarks>
/// <para>
/// Glitches are avoided with Zhuoran's rebasing: when <c>|z| &lt; |δ|</c>, the pixel's orbit has
/// come closer to 0 than to the reference, so the reference is restarted from <c>Z_0 = 0</c> with
/// <c>δ = z</c>. The same happens when the pixel outlives the stored reference.
/// </para>
/// <para>
/// Writes <c>(n, smooth, 0, 0)</c> for a pixel that escapes: the escape iteration and the
/// continuous iteration count <c>n + 1 - log2(log2|z|)</c> at bailout <c>|z|² &gt; 256</c>. A pixel
/// that doesn't escape within <paramref name="maxIterations"/> (inside the set, or too close to it
/// for the budget) gets <c>(-1, log2 min|z|, trap, angle)</c> instead, for interior shading: the
/// smallest <c>|z|</c> its orbit reached (an orbit trap at 0), the iteration where that happened
/// (an atom-domain period), and the angle of <c>z</c> there in turns, in [-0.5, 0.5]. Near a
/// component's nucleus <c>z</c> at the trap is roughly proportional to the offset from it, so
/// <c>log2 min|z|</c> and the angle are a log-polar coordinate around the nucleus. Both stay
/// accurate at any depth: at the trap <c>z</c> is small, so <c>δ</c> is not lost against <c>Z</c>. The minimum is over
/// the iterations the kernel runs, from <paramref name="skip"/> on. Pixel <c>(x, y)</c> maps to
/// <c>δc = (x - (width-1)/2, (height-1)/2 - y) · pixelScale</c>, so +imaginary is up.
/// </para>
/// </remarks>
/// <param name="reference">The reference orbit <c>Z_0 … Z_{referenceLength-1}</c>, <c>Z_0 = 0</c>.</param>
/// <param name="iterations">Output, <c>width × height</c> row-major.</param>
/// <param name="width">Image width in pixels.</param>
/// <param name="height">Image height in pixels.</param>
/// <param name="referenceLength">Number of valid entries in <paramref name="reference"/> (at least 2).</param>
/// <param name="maxIterations">Iteration budget.</param>
/// <param name="pixelScale">Distance in the complex plane between neighbouring pixels.</param>
/// <param name="skip">Iterations skipped by the series approximation (0 for none); see <see cref="MandelbrotSeries"/>.</param>
/// <param name="seriesA">Series coefficient of <c>u</c>, where <c>u</c> is the pixel offset in pixels.</param>
/// <param name="seriesB">Series coefficient of <c>u²</c>.</param>
/// <param name="seriesC">Series coefficient of <c>u³</c>.</param>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MandelbrotPerturbation(
    ReadOnlyBuffer<Float2> reference,
    ReadWriteBuffer<Float4> iterations,
    int width,
    int height,
    int referenceLength,
    int maxIterations,
    float pixelScale,
    int skip,
    Float2 seriesA,
    Float2 seriesB,
    Float2 seriesC) : IComputeShader
{
    /// <summary>Squared escape radius (|z| &gt; 16): large enough for smooth coloring to be smooth.</summary>
    public const float Bailout = 256f;

    /// <inheritdoc/>
    public void Execute()
    {
        int px = ThreadIds.X;
        int py = ThreadIds.Y;

        Float2 u = new(px - ((width - 1) * 0.5f), ((height - 1) * 0.5f) - py);
        Float2 dc = u * pixelScale;

        // Series approximation: δ_skip ≈ ((c·u + b)·u + a)·u, valid for every pixel in view.
        Float2 dz = ComplexMul(ComplexMul(ComplexMul(seriesC, u) + seriesB, u) + seriesA, u);
        int referenceIndex = skip;
        int n = skip;
        float zz = 0f;
        float trapZz = Bailout;
        int trapN = n;
        Float2 trapZ = new(0f, 0f);

        while (n < maxIterations)
        {
            // δ' = 2·Z·δ + δ² + δc = (2Z + δ)·δ + δc
            Float2 t = (2f * reference[referenceIndex]) + dz;
            dz = ComplexMul(t, dz) + dc;
            referenceIndex++;
            n++;

            Float2 z = reference[referenceIndex] + dz;
            zz = Gpu.Dot(z, z);

            if (zz > Bailout)
            {
                break;
            }

            if (zz < trapZz)
            {
                trapZz = zz;
                trapN = n;
                trapZ = z;
            }

            // Zhuoran's rebasing: continue from the start of the reference with δ = z.
            if (zz < Gpu.Dot(dz, dz) || referenceIndex == referenceLength - 1)
            {
                dz = z;
                referenceIndex = 0;
            }
        }

        // log2|z| = log2(|z|²) / 2. The trap is clamped so an orbit that hits 0 exactly stays finite.
        Float4 result = new(
            -1f,
            0.5f * Gpu.Log2(Gpu.Max(trapZz, 1e-37f)),
            trapN,
            Gpu.Atan2(trapZ.Y, trapZ.X) * 0.15915494f);

        if (zz > Bailout)
        {
            result = new Float4(n, n + 1f - Gpu.Log2(0.5f * Gpu.Log2(zz)), 0f, 0f);
        }

        iterations[(py * width) + px] = result;
    }

    private static Float2 ComplexMul(Float2 a, Float2 b) => new((a.X * b.X) - (a.Y * b.Y), (a.X * b.Y) + (a.Y * b.X));
}

/// <summary>
/// Colors the deep-zoom Mandelbrot from <see cref="MandelbrotPerturbation"/>'s output, optionally
/// crossfading between two frames (<paramref name="blend"/> = 0 shows only
/// <paramref name="layerA"/>). Escaped pixels use a cosine palette over
/// <c>sqrt(smooth) · density + phase</c>. Pixels that didn't escape get a dark, desaturated
/// version of the same palette, driven by their orbit trap, atom domain and the angle of z at the trap, so the
/// inside of the set reads as calmer than the outside but is never one flat color.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MandelbrotPalette(
    ReadWriteBuffer<Float4> layerA,
    ReadWriteBuffer<Float4> layerB,
    float blend,
    float phaseA,
    float densityA,
    float phaseB,
    float densityB) : IComputeShader<Float4>
{
    /// <inheritdoc/>
    public Float4 Execute()
    {
        int index = (ThreadIds.Y * DispatchSize.X) + ThreadIds.X;
        Float3 color = Shade(layerA[index], phaseA, densityA);

        if (blend > 0f)
        {
            color = Gpu.Lerp(color, Shade(layerB[index], phaseB, densityB), blend);
        }

        return new Float4(color, 1f);
    }

    private static Float3 Shade(Float4 iteration, float phase, float density)
    {
        if (iteration.X < 0f)
        {
            return Interior(iteration, phase);
        }

        return Palette((Gpu.Sqrt(Gpu.Max(iteration.Y, 0f)) * density) + phase);
    }

    private static Float3 Interior(Float4 iteration, float phase)
    {
        // A log-polar grid around each component's nucleus: r = log2 of the trapped |z|, the hue
        // turns once around the nucleus, and each atom domain gets its own hue offset.
        float r = iteration.Y;
        float t = iteration.W + (0.5f * r) + (0.61803399f * Gpu.Fract(iteration.Z * 0.61803399f)) + phase;
        Float3 color = Palette(t);
        float luma = Gpu.Dot(color, new Float3(0.299f, 0.587f, 0.114f));
        color = Gpu.Lerp(new Float3(luma, luma, luma), color, 0.5f);

        // Soft rings, four per halving of |z|. Dark and desaturated so the inside stays calmer than
        // the outside, with a floor so even its darkest hue is clearly not black.
        float rings = 0.5f + (0.5f * Gpu.Cos(6.2831853f * 4f * r));
        return (new Float3(0.4f, 0.4f, 0.4f) + color) * (0.15f + (0.1f * rings));
    }

    private static Float3 Palette(float t) =>
        new Float3(0.5f, 0.5f, 0.5f) + (new Float3(0.5f, 0.5f, 0.5f) * Gpu.Cos(6.2831853f * (new Float3(t, t, t) + new Float3(0f, 0.15f, 0.3f))));
}
