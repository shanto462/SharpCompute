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
/// Converts a loaded image (an <see cref="Rgba32"/> texture of straight-alpha sRGB bytes, as
/// <c>ImageLoader</c> decodes it) to the filters' premultiplied floats, on the GPU, so a decoded image
/// goes from the texture to the filters without a CPU pass. Same math as
/// <see cref="PixelConversion.FromStraightRgba32(ReadOnlySpan{Rgba32})"/>. 2D dispatch over the image.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct UnpackTexture(ReadOnlyTexture2D<Rgba32> image, ReadWriteBuffer<Float4> pixels) : IComputeShader
{
    public void Execute()
    {
        // A texture read gives byte / 255 per channel.
        Float4 p = image[ThreadIds.XY];
        pixels[(ThreadIds.Y * image.Width) + ThreadIds.X] = new Float4(p.XYZ * p.W, p.W);
    }
}

/// <summary>
/// Converts premultiplied floats back to straight alpha in an <see cref="Rgba32"/> texture, which
/// <c>ImageSaver</c> encodes in place; the texture write rounds each channel to the nearest byte.
/// Same math as <see cref="PixelConversion.ToStraight"/>. 2D dispatch over the image.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct PackTexture(ReadWriteBuffer<Float4> pixels, ReadWriteTexture2D<Rgba32> image) : IComputeShader
{
    public void Execute()
    {
        Float4 p = pixels[(ThreadIds.Y * image.Width) + ThreadIds.X];
        float a = Gpu.Saturate(p.W);

        // A pixel whose alpha rounds to 0 is stored as transparent black, as on the CPU.
        if (float.IsNaN(p.W) || a < 0.5f / 255f)
        {
            image[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        image[ThreadIds.XY] = new Float4(Gpu.Saturate(p.XYZ / a), a);
    }
}

/// <summary>
/// A lens: barrel (positive <c>strength</c>) or pincushion (negative) distortion with color fringing,
/// red and blue magnified slightly more and less than green by <c>fringe</c>. Each output pixel
/// samples the source three times with <see cref="Sampler.LinearClamp"/>, at coordinates that fall
/// between pixels, which is what texture sampling does in hardware. 2D dispatch over the image.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct LensDistortion(
    ReadOnlyTexture2D<Float4> source, ReadWriteBuffer<Float4> destination, int width, int height, float strength, float fringe) : IComputeShader
{
    public void Execute()
    {
        Float2 size = new(width, height);
        Float2 pixel = new Float2(ThreadIds.X, ThreadIds.Y) + 0.5f;
        Float4 red = source.Sample(Sampler.LinearClamp, SourceUv(pixel, size, strength, 1f + fringe));
        Float4 green = source.Sample(Sampler.LinearClamp, SourceUv(pixel, size, strength, 1f));
        Float4 blue = source.Sample(Sampler.LinearClamp, SourceUv(pixel, size, strength, 1f - fringe));

        destination[(ThreadIds.Y * width) + ThreadIds.X] = new Float4(Gpu.Min(red.X, green.W), green.Y, Gpu.Min(blue.Z, green.W), green.W);
    }

    /// <summary>
    /// The normalized source coordinate for the output pixel center <paramref name="pixel"/>: its
    /// offset from the image center scaled by <c>1 + strength · r²</c> (r = 1 at the corners), and
    /// by <paramref name="scale"/>. A barrel is divided by <c>1 + strength</c> so its corners stay at the corners.
    /// </summary>
    public static Float2 SourceUv(Float2 pixel, Float2 size, float strength, float scale)
    {
        Float2 center = size * 0.5f;
        Float2 offset = pixel - center;
        float r2 = Gpu.Dot(offset, offset) / Gpu.Dot(center, center);
        float factor = (1f + (strength * r2)) / (1f + Gpu.Max(strength, 0f)) * scale;
        return (center + (offset * factor)) / size;
    }
}
