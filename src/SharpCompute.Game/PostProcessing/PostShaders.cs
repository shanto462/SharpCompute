namespace SharpCompute.Game;

/// <summary>A radial distortion ring as the shaders read it: displacement peaks at <see cref="Radius"/> from <see cref="Center"/>.</summary>
internal struct DistortionRing
{
    public Float2 Center;
    public float Radius;
    public float Width;
    public float Strength;
}

/// <summary>
/// Bloom, step 1 (HDR source): halves the resolution (a 2 × 2 average) and keeps only what is
/// brighter than <c>threshold</c>, with its hue (the excess over the threshold, not a hard cut).
/// </summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BloomPrefilterPass(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int sourceWidth, int destinationWidth, float threshold) : IComputeShader
{
    public void Execute()
    {
        int sx = ThreadIds.X * 2;
        int sy = ThreadIds.Y * 2;
        int i = (sy * sourceWidth) + sx;
        Float3 c = (source[i].XYZ + source[i + 1].XYZ + source[i + sourceWidth].XYZ + source[i + sourceWidth + 1].XYZ) * 0.25f;
        float peak = Gpu.Max(c.X, Gpu.Max(c.Y, c.Z));
        float keep = Gpu.Saturate((peak - threshold) / Gpu.Max(peak, 1e-4f));
        destination[(ThreadIds.Y * destinationWidth) + ThreadIds.X] = new Float4(c * keep, 1f);
    }
}

/// <summary>Bloom, step 1 for a packed RGBA8 source: the same prefilter, reading 8-bit pixels as 0..1.</summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BloomPrefilterPackedPass(ReadWriteBuffer<uint> source, ReadWriteBuffer<Float4> destination, int sourceWidth, int destinationWidth, float threshold) : IComputeShader
{
    public void Execute()
    {
        int sx = ThreadIds.X * 2;
        int sy = ThreadIds.Y * 2;
        int i = (sy * sourceWidth) + sx;
        Float3 c = (Unpack(source[i]) + Unpack(source[i + 1]) + Unpack(source[i + sourceWidth]) + Unpack(source[i + sourceWidth + 1])) * 0.25f;
        float peak = Gpu.Max(c.X, Gpu.Max(c.Y, c.Z));
        float keep = Gpu.Saturate((peak - threshold) / Gpu.Max(peak, 1e-4f));
        destination[(ThreadIds.Y * destinationWidth) + ThreadIds.X] = new Float4(c * keep, 1f);
    }

    private static Float3 Unpack(uint c) =>
        new((float)(c & 0xFFu) / 255f, (float)((c >> 8) & 0xFFu) / 255f, (float)((c >> 16) & 0xFFu) / 255f);
}

/// <summary>Bloom, the downsample between levels: a 2 × 2 average.</summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BloomDownsamplePass(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int sourceWidth, int destinationWidth) : IComputeShader
{
    public void Execute()
    {
        int i = (ThreadIds.Y * 2 * sourceWidth) + (ThreadIds.X * 2);
        destination[(ThreadIds.Y * destinationWidth) + ThreadIds.X] = (source[i] + source[i + 1] + source[i + sourceWidth] + source[i + sourceWidth + 1]) * 0.25f;
    }
}

/// <summary>
/// Bloom, the blur: one direction of a separable 9-tap Gaussian (σ ≈ 2 texels), so a horizontal
/// and a vertical pass cost 18 reads per pixel instead of 81. Edges clamp.
/// </summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BloomBlurPass(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int width, int height, int stepX, int stepY) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float4 sum = source[(y * width) + x] * 0.2270270f;
        sum += Tap(x, y, 1) * 0.1945946f;
        sum += Tap(x, y, 2) * 0.1216216f;
        sum += Tap(x, y, 3) * 0.0540540f;
        sum += Tap(x, y, 4) * 0.0162162f;
        destination[(y * width) + x] = sum;
    }

    /// <summary>The two samples <paramref name="k"/> texels either side, added.</summary>
    private Float4 Tap(int x, int y, int k)
    {
        int ax = Gpu.Clamp(x + (stepX * k), 0, width - 1);
        int ay = Gpu.Clamp(y + (stepY * k), 0, height - 1);
        int bx = Gpu.Clamp(x - (stepX * k), 0, width - 1);
        int by = Gpu.Clamp(y - (stepY * k), 0, height - 1);
        return source[(ay * width) + ax] + source[(by * width) + bx];
    }
}

/// <summary>Bloom onto a packed RGBA8 frame, in place: adds the two blurred levels (bilinearly upsampled) and clamps.</summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BloomAddPass(
    ReadWriteBuffer<uint> frame,
    ReadWriteBuffer<Float4> bloomHalf,
    ReadWriteBuffer<Float4> bloomQuarter,
    int width,
    int height,
    float strength) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float2 p = new((float)x + 0.5f, (float)y + 0.5f);
        uint c = frame[(y * width) + x];
        Float3 color = new((float)(c & 0xFFu) / 255f, (float)((c >> 8) & 0xFFu) / 255f, (float)((c >> 16) & 0xFFu) / 255f);
        Float3 glow = (SampleHalf(p * 0.5f) * 0.8f) + (SampleQuarter(p * 0.25f) * 1.1f);
        color = Gpu.Saturate(color + (glow * strength));
        uint red = (uint)((color.X * 255f) + 0.5f);
        uint green = (uint)((color.Y * 255f) + 0.5f);
        uint blue = (uint)((color.Z * 255f) + 0.5f);
        frame[(y * width) + x] = red | (green << 8) | (blue << 16) | (c & 0xFF000000u);
    }

    private Float3 SampleHalf(Float2 at)
    {
        int w = width / 2;
        Int4 i = Corners(at, w, height / 2);
        Float2 f = Weights(at);
        return Bilinear(bloomHalf[(i.Y * w) + i.X].XYZ, bloomHalf[(i.Y * w) + i.Z].XYZ, bloomHalf[(i.W * w) + i.X].XYZ, bloomHalf[(i.W * w) + i.Z].XYZ, f);
    }

    private Float3 SampleQuarter(Float2 at)
    {
        int w = width / 4;
        Int4 i = Corners(at, w, height / 4);
        Float2 f = Weights(at);
        return Bilinear(bloomQuarter[(i.Y * w) + i.X].XYZ, bloomQuarter[(i.Y * w) + i.Z].XYZ, bloomQuarter[(i.W * w) + i.X].XYZ, bloomQuarter[(i.W * w) + i.Z].XYZ, f);
    }

    /// <summary>The clamped texel columns (x, z) and rows (y, w) around a point whose texel centers are at +0.5.</summary>
    private static Int4 Corners(Float2 at, int w, int h)
    {
        int x0 = (int)Gpu.Floor(at.X - 0.5f);
        int y0 = (int)Gpu.Floor(at.Y - 0.5f);
        return new Int4(Gpu.Clamp(x0, 0, w - 1), Gpu.Clamp(y0, 0, h - 1), Gpu.Clamp(x0 + 1, 0, w - 1), Gpu.Clamp(y0 + 1, 0, h - 1));
    }

    private static Float2 Weights(Float2 at) => new(Gpu.Fract(at.X - 0.5f), Gpu.Fract(at.Y - 0.5f));

    private static Float3 Bilinear(Float3 a, Float3 b, Float3 c, Float3 d, Float2 f) =>
        Gpu.Lerp(Gpu.Lerp(a, b, f.X), Gpu.Lerp(c, d, f.X), f.Y);
}

/// <summary>
/// The final image of an HDR frame: shockwave distortion (each ring pushes the lookup radially,
/// with a slight color split), an offset for screen shake, the scene plus two bloom levels, a
/// flash, exponential tone mapping and a vignette, packed to RGBA8.
/// </summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct HdrCompositePass(
    ReadWriteBuffer<Float4> hdr,
    ReadWriteBuffer<Float4> bloomHalf,
    ReadWriteBuffer<Float4> bloomQuarter,
    ReadOnlyBuffer<DistortionRing> rings,
    int ringCount,
    ReadWriteBuffer<uint> output,
    int width,
    int height,
    float bloom,
    float exposure,
    float flashR,
    float flashG,
    float flashB,
    float shakeX,
    float shakeY,
    float colorSplit,
    float vignette) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float2 p = new((float)x + 0.5f, (float)y + 0.5f);

        Float2 offset = new(0f, 0f);

        for (int i = 0; i < ringCount; i++)
        {
            DistortionRing r = rings[i];
            Float2 v = p - r.Center;
            float d = Gpu.Length(v);
            float band = (d - r.Radius) / r.Width;
            float push = -band * Gpu.Exp(-band * band) * r.Strength;
            offset += v / Gpu.Max(d, 1f) * push;
        }

        Float2 s = p + offset + new Float2(shakeX, shakeY);
        Float2 split = offset * colorSplit;
        Float3 color = new(SampleHdr(s + split).X, SampleHdr(s).Y, SampleHdr(s - split).Z);
        Float3 glow = (SampleHalf(s * 0.5f) * 0.8f) + (SampleQuarter(s * 0.25f) * 1.1f);
        color += (glow * bloom) + new Float3(flashR, flashG, flashB);

        // Exponential tone mapping keeps saturated color from clipping to flat white too early.
        color = new Float3(1f, 1f, 1f) - new Float3(Gpu.Exp(-color.X * exposure), Gpu.Exp(-color.Y * exposure), Gpu.Exp(-color.Z * exposure));

        float u = ((float)x / (float)width) - 0.5f;
        float w = ((float)y / (float)height) - 0.5f;
        color *= 1f - (((u * u) + (w * w)) * vignette);

        color = Gpu.Saturate(color);
        uint red = (uint)((color.X * 255f) + 0.5f);
        uint green = (uint)((color.Y * 255f) + 0.5f);
        uint blue = (uint)((color.Z * 255f) + 0.5f);
        output[(y * width) + x] = red | (green << 8) | (blue << 16) | 0xFF000000u;
    }

    private Float3 SampleHdr(Float2 at)
    {
        Int4 i = Corners(at, width, height);
        Float2 f = Weights(at);
        return Bilinear(hdr[(i.Y * width) + i.X].XYZ, hdr[(i.Y * width) + i.Z].XYZ, hdr[(i.W * width) + i.X].XYZ, hdr[(i.W * width) + i.Z].XYZ, f);
    }

    private Float3 SampleHalf(Float2 at)
    {
        int w = width / 2;
        Int4 i = Corners(at, w, height / 2);
        Float2 f = Weights(at);
        return Bilinear(bloomHalf[(i.Y * w) + i.X].XYZ, bloomHalf[(i.Y * w) + i.Z].XYZ, bloomHalf[(i.W * w) + i.X].XYZ, bloomHalf[(i.W * w) + i.Z].XYZ, f);
    }

    private Float3 SampleQuarter(Float2 at)
    {
        int w = width / 4;
        Int4 i = Corners(at, w, height / 4);
        Float2 f = Weights(at);
        return Bilinear(bloomQuarter[(i.Y * w) + i.X].XYZ, bloomQuarter[(i.Y * w) + i.Z].XYZ, bloomQuarter[(i.W * w) + i.X].XYZ, bloomQuarter[(i.W * w) + i.Z].XYZ, f);
    }

    /// <summary>The clamped texel columns (x, z) and rows (y, w) around a point whose texel centers are at +0.5.</summary>
    private static Int4 Corners(Float2 at, int w, int h)
    {
        int x0 = (int)Gpu.Floor(at.X - 0.5f);
        int y0 = (int)Gpu.Floor(at.Y - 0.5f);
        return new Int4(Gpu.Clamp(x0, 0, w - 1), Gpu.Clamp(y0, 0, h - 1), Gpu.Clamp(x0 + 1, 0, w - 1), Gpu.Clamp(y0 + 1, 0, h - 1));
    }

    private static Float2 Weights(Float2 at) => new(Gpu.Fract(at.X - 0.5f), Gpu.Fract(at.Y - 0.5f));

    private static Float3 Bilinear(Float3 a, Float3 b, Float3 c, Float3 d, Float2 f) =>
        Gpu.Lerp(Gpu.Lerp(a, b, f.X), Gpu.Lerp(c, d, f.X), f.Y);
}

/// <summary>
/// Shockwave distortion of a packed RGBA8 frame into another: every pixel looks up the source a
/// little way along each ring's radius, with red and blue split apart slightly. Pixels no ring
/// reaches are copied exactly.
/// </summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct DistortPass(
    ReadWriteBuffer<uint> source,
    ReadWriteBuffer<uint> destination,
    ReadOnlyBuffer<DistortionRing> rings,
    int ringCount,
    int width,
    int height,
    float colorSplit) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float2 p = new((float)x + 0.5f, (float)y + 0.5f);
        Float2 offset = new(0f, 0f);

        for (int i = 0; i < ringCount; i++)
        {
            DistortionRing r = rings[i];
            Float2 v = p - r.Center;
            float d = Gpu.Length(v);
            float band = (d - r.Radius) / r.Width;
            float push = -band * Gpu.Exp(-band * band) * r.Strength;
            offset += v / Gpu.Max(d, 1f) * push;
        }

        uint original = source[(y * width) + x];

        // Out of every ring's reach: an exact copy, not a resample.
        if (Gpu.Abs(offset.X) + Gpu.Abs(offset.Y) < 0.004f)
        {
            destination[(y * width) + x] = original;
            return;
        }

        Float2 s = p + offset;
        Float2 split = offset * colorSplit;
        Float3 color = Gpu.Saturate(new Float3(Sample(s + split).X, Sample(s).Y, Sample(s - split).Z));
        uint red = (uint)((color.X * 255f) + 0.5f);
        uint green = (uint)((color.Y * 255f) + 0.5f);
        uint blue = (uint)((color.Z * 255f) + 0.5f);
        destination[(y * width) + x] = red | (green << 8) | (blue << 16) | (original & 0xFF000000u);
    }

    private Float3 Sample(Float2 at)
    {
        int x0 = (int)Gpu.Floor(at.X - 0.5f);
        int y0 = (int)Gpu.Floor(at.Y - 0.5f);
        int ax = Gpu.Clamp(x0, 0, width - 1);
        int ay = Gpu.Clamp(y0, 0, height - 1);
        int bx = Gpu.Clamp(x0 + 1, 0, width - 1);
        int by = Gpu.Clamp(y0 + 1, 0, height - 1);
        float fx = Gpu.Fract(at.X - 0.5f);
        float fy = Gpu.Fract(at.Y - 0.5f);
        return Gpu.Lerp(
            Gpu.Lerp(Unpack(source[(ay * width) + ax]), Unpack(source[(ay * width) + bx]), fx),
            Gpu.Lerp(Unpack(source[(by * width) + ax]), Unpack(source[(by * width) + bx]), fx),
            fy);
    }

    private static Float3 Unpack(uint c) =>
        new((float)(c & 0xFFu) / 255f, (float)((c >> 8) & 0xFFu) / 255f, (float)((c >> 16) & 0xFFu) / 255f);
}

/// <summary>A vignette on a packed RGBA8 frame, in place: darkens towards the corners by the squared distance from the center.</summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct VignettePass(ReadWriteBuffer<uint> frame, int width, int height, float strength) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        uint c = frame[(y * width) + x];
        float u = ((float)x / (float)width) - 0.5f;
        float v = ((float)y / (float)height) - 0.5f;
        float keep = Gpu.Saturate(1f - (((u * u) + (v * v)) * strength));
        uint red = (uint)(((float)(c & 0xFFu) * keep) + 0.5f);
        uint green = (uint)(((float)((c >> 8) & 0xFFu) * keep) + 0.5f);
        uint blue = (uint)(((float)((c >> 16) & 0xFFu) * keep) + 0.5f);
        frame[(y * width) + x] = red | (green << 8) | (blue << 16) | (c & 0xFF000000u);
    }
}

/// <summary>
/// Scales a native-resolution frame up by a whole number into a window's texture, with black
/// borders, so pixel art stays crisp (one source pixel becomes an exact <c>scale × scale</c> block).
/// </summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct UpscalePass(ReadWriteBuffer<uint> source, int sourceWidth, int sourceHeight, int scale, int originX, int originY) : IComputeShader<Float4>
{
    public Float4 Execute()
    {
        int x = ThreadIds.X - originX;
        int y = ThreadIds.Y - originY;

        if (x < 0 || y < 0)
        {
            return new Float4(0f, 0f, 0f, 1f);
        }

        int sx = x / scale;
        int sy = y / scale;

        if (sx >= sourceWidth || sy >= sourceHeight)
        {
            return new Float4(0f, 0f, 0f, 1f);
        }

        uint c = source[(sy * sourceWidth) + sx];
        return new Float4((float)(c & 0xFFu) / 255f, (float)((c >> 8) & 0xFFu) / 255f, (float)((c >> 16) & 0xFFu) / 255f, 1f);
    }
}
