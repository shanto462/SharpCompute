// Original SharpCompute sample (MIT).
//
// GPU image kernels shared by the ImageProcessing CLI and the Paint app (Paint links this folder).
// Every image is a row-major buffer of premultiplied RGBA in Float4 (0..1 per channel). Reads past
// an edge clamp to the nearest pixel. Per-pixel math that doesn't touch a buffer lives in public
// static helpers, which the CPU reference (CpuFilters) calls too, so both sides run the same code.
namespace SharpCompute.Samples.Imaging;

/// <summary>Copies one buffer into another (1D dispatch over the pixel count).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct CopyPixels(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination) : IComputeShader
{
    public void Execute() => destination[ThreadIds.X] = source[ThreadIds.X];
}

/// <summary>Fills a buffer with one color (1D dispatch over the pixel count).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct FillPixels(ReadWriteBuffer<Float4> destination, Float4 color) : IComputeShader
{
    public void Execute() => destination[ThreadIds.X] = color;
}

/// <summary>
/// One direction of a separable Gaussian blur: <c>(stepX, stepY)</c> is (1, 0) for the horizontal
/// pass and (0, 1) for the vertical one. <c>weights</c> holds <c>2 * radius + 1</c> normalized taps.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GaussianBlurPass(
    ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, ReadOnlyBuffer<float> weights,
    int width, int height, int radius, int stepX, int stepY) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float4 sum = new(0f, 0f, 0f, 0f);

        for (int k = -radius; k <= radius; k++)
        {
            int sx = Gpu.Clamp(x + (k * stepX), 0, width - 1);
            int sy = Gpu.Clamp(y + (k * stepY), 0, height - 1);
            sum += source[(sy * width) + sx] * weights[k + radius];
        }

        destination[(y * width) + x] = sum;
    }
}

/// <summary>
/// A lens ("bokeh") blur: the average over a disc of <c>radius</c> pixels with an anti-aliased rim,
/// evaluated brute force (π r² reads per pixel). Bright samples get extra weight, so highlights
/// bloom into the bright discs a real lens produces.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BokehBlur(
    ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int width, int height, float radius, float highlightGain) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int reach = (int)Gpu.Ceil(radius);
        Float4 sum = new(0f, 0f, 0f, 0f);
        float total = 0f;

        for (int dy = -reach; dy <= reach; dy++)
        {
            int sy = Gpu.Clamp(y + dy, 0, height - 1);

            for (int dx = -reach; dx <= reach; dx++)
            {
                float weight = DiscWeight(dx, dy, radius);

                if (weight > 0f)
                {
                    Float4 sample = source[(sy * width) + Gpu.Clamp(x + dx, 0, width - 1)];
                    weight *= HighlightWeight(sample, highlightGain);
                    sum += sample * weight;
                    total += weight;
                }
            }
        }

        destination[(y * width) + x] = sum / total;
    }

    /// <summary>Coverage of the pixel at offset (dx, dy) by a disc of <paramref name="radius"/>, with a one-pixel soft rim.</summary>
    public static float DiscWeight(int dx, int dy, float radius) => Gpu.Saturate(radius + 0.5f - Gpu.Sqrt((float)((dx * dx) + (dy * dy))));

    /// <summary>1 for ordinary samples, rising to <c>1 + gain</c> for the brightest ones (luma above 0.75).</summary>
    public static float HighlightWeight(Float4 sample, float gain)
    {
        float luma = (0.2126f * sample.X) + (0.7152f * sample.Y) + (0.0722f * sample.Z);
        float over = Gpu.Max(luma - 0.75f, 0f) * 4f;
        return 1f + (gain * over * over);
    }
}

/// <summary>Unsharp mask: <c>source + (source - blurred) * amount</c>, with color clamped to [0, alpha].</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct UnsharpMask(
    ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> blurred, ReadWriteBuffer<Float4> destination, float amount) : IComputeShader
{
    public void Execute() => destination[ThreadIds.X] = Sharpen(source[ThreadIds.X], blurred[ThreadIds.X], amount);

    public static Float4 Sharpen(Float4 source, Float4 blurred, float amount)
    {
        Float3 color = source.XYZ + ((source.XYZ - blurred.XYZ) * amount);
        return new Float4(Gpu.Clamp(color, 0f, source.W), source.W);
    }
}

/// <summary>Sobel edge magnitude of the luma, written as opaque gray.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SobelEdges(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int width, int height) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        float gx = 0f;
        float gy = 0f;

        for (int j = -1; j <= 1; j++)
        {
            int sy = Gpu.Clamp(y + j, 0, height - 1);

            for (int i = -1; i <= 1; i++)
            {
                float luma = Luma(source[(sy * width) + Gpu.Clamp(x + i, 0, width - 1)]);
                gx += luma * (i * (j == 0 ? 2 : 1));
                gy += luma * (j * (i == 0 ? 2 : 1));
            }
        }

        float magnitude = Gpu.Saturate(Gpu.Sqrt((gx * gx) + (gy * gy)));
        destination[(y * width) + x] = new Float4(magnitude, magnitude, magnitude, 1f);
    }

    public static float Luma(Float4 color) => (0.2126f * color.X) + (0.7152f * color.Y) + (0.0722f * color.Z);
}

/// <summary>
/// Exposure (a linear gain, <c>2^stops</c> computed on the CPU), contrast around mid-gray and
/// saturation around the luma, applied to unpremultiplied color.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ColorGrade(
    ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, float gain, float contrast, float saturation) : IComputeShader
{
    public void Execute() => destination[ThreadIds.X] = Grade(source[ThreadIds.X], gain, contrast, saturation);

    public static Float4 Grade(Float4 color, float gain, float contrast, float saturation)
    {
        if (color.W <= 0f)
        {
            return color;
        }

        Float3 rgb = (color.XYZ / color.W) * gain;
        rgb = ((rgb - 0.5f) * contrast) + 0.5f;
        float luma = (0.2126f * rgb.X) + (0.7152f * rgb.Y) + (0.0722f * rgb.Z);
        rgb = luma + ((rgb - luma) * saturation);
        return new Float4(Gpu.Saturate(rgb) * color.W, color.W);
    }
}

/// <summary>Replaces every <c>size</c> × <c>size</c> block with its average.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Pixelate(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int width, int height, int size) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int x0 = (x / size) * size;
        int y0 = (y / size) * size;
        int x1 = Gpu.Min(x0 + size, width);
        int y1 = Gpu.Min(y0 + size, height);
        Float4 sum = new(0f, 0f, 0f, 0f);

        for (int sy = y0; sy < y1; sy++)
        {
            for (int sx = x0; sx < x1; sx++)
            {
                sum += source[(sy * width) + sx];
            }
        }

        destination[(y * width) + x] = sum / (float)((x1 - x0) * (y1 - y0));
    }
}

/// <summary>
/// The per-pixel color filters: 0 = invert, 1 = posterize to <c>levels</c> levels per channel,
/// 2 = grayscale (Rec. 709 luma).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct PointFilter(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int mode, float levels) : IComputeShader
{
    public void Execute() => destination[ThreadIds.X] = Apply(source[ThreadIds.X], mode, levels);

    public static Float4 Apply(Float4 color, int mode, float levels)
    {
        if (mode == 0)
        {
            return new Float4(color.W - color.X, color.W - color.Y, color.W - color.Z, color.W);
        }

        if (mode == 1)
        {
            if (color.W <= 0f)
            {
                return color;
            }

            float steps = levels - 1f;
            Float3 rgb = Gpu.Round((color.XYZ / color.W) * steps) / steps;
            return new Float4(rgb * color.W, color.W);
        }

        float luma = (0.2126f * color.X) + (0.7152f * color.Y) + (0.0722f * color.Z);
        return new Float4(luma, luma, luma, color.W);
    }
}

/// <summary>
/// Converts premultiplied RGBA8 pixels (bytes R, G, B, A in memory, as CoreGraphics' premultiplied-last
/// bitmap contexts write them; one little-endian <c>uint</c> per pixel) to premultiplied floats, on
/// the GPU, so a decoded image goes from the decoder's memory to the filters without a CPU pass.
/// Same math as <see cref="PixelConversion.FromPremultipliedRgba8"/>. 1D dispatch over the pixel count.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct UnpackRgba8(ReadWriteBuffer<uint> packed, ReadWriteBuffer<Float4> pixels) : IComputeShader
{
    public void Execute()
    {
        uint p = packed[ThreadIds.X];
        pixels[ThreadIds.X] = new Float4(
            (p & 0xFFu) / 255f, ((p >> 8) & 0xFFu) / 255f, ((p >> 16) & 0xFFu) / 255f, (p >> 24) / 255f);
    }
}

/// <summary>
/// Converts premultiplied floats back to 8-bit RGBA on the GPU, rounding to nearest, either
/// premultiplied (for CoreGraphics/ImageIO; same math as <see cref="PixelConversion.ToPremultipliedRgba8"/>)
/// or straight alpha (for PNG; same math as <see cref="PixelConversion.ToStraight"/>). 1D dispatch over the pixel count.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct PackRgba8(ReadWriteBuffer<Float4> pixels, ReadWriteBuffer<uint> packed, int straightAlpha) : IComputeShader
{
    public void Execute()
    {
        Float4 p = pixels[ThreadIds.X];
        float a = Gpu.Saturate(p.W);
        uint alpha = Quantize(a);
        uint r, g, b;

        if (straightAlpha != 0)
        {
            if (alpha == 0u)
            {
                packed[ThreadIds.X] = 0u;
                return;
            }

            r = Quantize(p.X / a);
            g = Quantize(p.Y / a);
            b = Quantize(p.Z / a);
        }
        else
        {
            // A NaN channel stays 0 rather than becoming min(NaN, a) = a, matching Math.Min on the CPU.
            r = float.IsNaN(p.X) ? 0u : Quantize(Gpu.Min(p.X, a));
            g = float.IsNaN(p.Y) ? 0u : Quantize(Gpu.Min(p.Y, a));
            b = float.IsNaN(p.Z) ? 0u : Quantize(Gpu.Min(p.Z, a));
        }

        packed[ThreadIds.X] = r | (g << 8) | (b << 16) | (alpha << 24);
    }

    /// <summary>Same as <see cref="PixelConversion.Quantize"/>: clamp to 0..1, scale to 0..255, round to nearest; NaN → 0.</summary>
    private static uint Quantize(float value) => float.IsNaN(value) ? 0u : (uint)((Gpu.Saturate(value) * 255f) + 0.5f);
}
