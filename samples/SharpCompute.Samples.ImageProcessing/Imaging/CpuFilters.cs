// Original SharpCompute sample (MIT).
using System;
using System.Threading.Tasks;

namespace SharpCompute.Samples.Imaging;

/// <summary>
/// The CPU reference for every <see cref="FilterSpec"/> filter: the same math as the kernels in
/// <c>ImageKernels.cs</c> (it calls their static helpers where they have one), in the same
/// summation order, one image row per <see cref="Parallel.For(int, int, Action{int})"/> iteration.
/// Results differ from the GPU only by float rounding: the GPU fuses multiply-adds.
/// </summary>
public static class CpuFilters
{
    /// <summary>The linear gain for an exposure change of <paramref name="stops"/>.</summary>
    public static float ExposureGain(float stops) => MathF.Pow(2f, stops);

    /// <summary>Filters <paramref name="source"/> into <paramref name="destination"/> (different arrays of <c>width * height</c> pixels).</summary>
    public static void Apply(FilterSpec filter, Float4[] source, Float4[] destination, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (source.Length != width * height || destination.Length != width * height || ReferenceEquals(source, destination))
        {
            throw new ArgumentException($"Expected two different arrays of {width}×{height} pixels.");
        }

        switch (filter.Kind)
        {
            case FilterKind.Blur:
                Blur(source, destination, width, height, (int)filter[0]);
                break;

            case FilterKind.Bokeh:
                Bokeh(source, destination, width, height, filter[0], filter[1]);
                break;

            case FilterKind.Sharpen:
                Float4[] blurred = new Float4[source.Length];
                Blur(source, blurred, width, height, FilterSpec.SharpenBlurRadius);
                float amount = filter[0];
                Rows(height, y =>
                {
                    for (int i = y * width, end = i + width; i < end; i++)
                    {
                        destination[i] = UnsharpMask.Sharpen(source[i], blurred[i], amount);
                    }
                });
                break;

            case FilterKind.Edges:
                Sobel(source, destination, width, height);
                break;

            case FilterKind.Grade:
                float gain = ExposureGain(filter[0]);
                float contrast = filter[1];
                float saturation = filter[2];
                Rows(height, y =>
                {
                    for (int i = y * width, end = i + width; i < end; i++)
                    {
                        destination[i] = ColorGrade.Grade(source[i], gain, contrast, saturation);
                    }
                });
                break;

            case FilterKind.Pixelate:
                Pixelate(source, destination, width, height, (int)filter[0]);
                break;

            case FilterKind.Invert:
            case FilterKind.Posterize:
            case FilterKind.Grayscale:
                int mode = filter.Kind == FilterKind.Invert ? 0 : filter.Kind == FilterKind.Posterize ? 1 : 2;
                float levels = filter.Kind == FilterKind.Posterize ? filter[0] : 0f;
                Rows(height, y =>
                {
                    for (int i = y * width, end = i + width; i < end; i++)
                    {
                        destination[i] = PointFilter.Apply(source[i], mode, levels);
                    }
                });
                break;

            case FilterKind.Lens:
                Lens(source, destination, width, height, filter[0], filter[1]);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(filter), filter.Kind, "Unknown filter.");
        }
    }

    private static void Rows(int height, Action<int> row) => Parallel.For(0, height, row);

    private static void Blur(Float4[] source, Float4[] destination, int width, int height, int radius)
    {
        float[] weights = FilterSpec.GaussianWeights(radius);
        Float4[] horizontal = new Float4[source.Length];
        BlurPass(source, horizontal, weights, width, height, radius, 1, 0);
        BlurPass(horizontal, destination, weights, width, height, radius, 0, 1);
    }

    private static void BlurPass(Float4[] source, Float4[] destination, float[] weights, int width, int height, int radius, int stepX, int stepY) =>
        Rows(height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                Float4 sum = new(0f, 0f, 0f, 0f);

                for (int k = -radius; k <= radius; k++)
                {
                    int sx = Math.Clamp(x + (k * stepX), 0, width - 1);
                    int sy = Math.Clamp(y + (k * stepY), 0, height - 1);
                    sum += source[(sy * width) + sx] * weights[k + radius];
                }

                destination[(y * width) + x] = sum;
            }
        });

    private static void Bokeh(Float4[] source, Float4[] destination, int width, int height, float radius, float highlightGain)
    {
        int reach = (int)MathF.Ceiling(radius);

        // The disc weights depend only on the offset; precomputing them changes no value.
        float[] disc = new float[((2 * reach) + 1) * ((2 * reach) + 1)];

        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                disc[((dy + reach) * ((2 * reach) + 1)) + dx + reach] = BokehBlur.DiscWeight(dx, dy, radius);
            }
        }

        Rows(height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                Float4 sum = new(0f, 0f, 0f, 0f);
                float total = 0f;

                for (int dy = -reach; dy <= reach; dy++)
                {
                    int sy = Math.Clamp(y + dy, 0, height - 1);
                    int row = (dy + reach) * ((2 * reach) + 1);

                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        float weight = disc[row + dx + reach];

                        if (weight > 0f)
                        {
                            Float4 sample = source[(sy * width) + Math.Clamp(x + dx, 0, width - 1)];
                            weight *= BokehBlur.HighlightWeight(sample, highlightGain);
                            sum += sample * weight;
                            total += weight;
                        }
                    }
                }

                destination[(y * width) + x] = sum / total;
            }
        });
    }

    private static void Sobel(Float4[] source, Float4[] destination, int width, int height) =>
        Rows(height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float gx = 0f;
                float gy = 0f;

                for (int j = -1; j <= 1; j++)
                {
                    int sy = Math.Clamp(y + j, 0, height - 1);

                    for (int i = -1; i <= 1; i++)
                    {
                        float luma = SobelEdges.Luma(source[(sy * width) + Math.Clamp(x + i, 0, width - 1)]);
                        gx += luma * (i * (j == 0 ? 2 : 1));
                        gy += luma * (j * (i == 0 ? 2 : 1));
                    }
                }

                float magnitude = Gpu.Saturate(MathF.Sqrt((gx * gx) + (gy * gy)));
                destination[(y * width) + x] = new Float4(magnitude, magnitude, magnitude, 1f);
            }
        });

    private static void Pixelate(Float4[] source, Float4[] destination, int width, int height, int size) =>
        Rows(height, y =>
        {
            int y0 = y / size * size;
            int y1 = Math.Min(y0 + size, height);

            for (int x = 0; x < width; x++)
            {
                int x0 = x / size * size;
                int x1 = Math.Min(x0 + size, width);
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
        });

    private static void Lens(Float4[] source, Float4[] destination, int width, int height, float strength, float fringe) =>
        Rows(height, y =>
        {
            Float2 size = new(width, height);

            for (int x = 0; x < width; x++)
            {
                Float2 pixel = new(x + 0.5f, y + 0.5f);
                Float4 red = SampleLinearClamp(source, width, height, LensDistortion.SourceUv(pixel, size, strength, 1f + fringe));
                Float4 green = SampleLinearClamp(source, width, height, LensDistortion.SourceUv(pixel, size, strength, 1f));
                Float4 blue = SampleLinearClamp(source, width, height, LensDistortion.SourceUv(pixel, size, strength, 1f - fringe));
                destination[(y * width) + x] = new Float4(MathF.Min(red.X, green.W), green.Y, MathF.Min(blue.Z, green.W), green.W);
            }
        });

    /// <summary>
    /// <c>Sample(Sampler.LinearClamp, uv)</c> over a row-major image, with the texture's own math:
    /// texel centers at <c>(i + 0.5) / size</c>, indices clamped, <c>a + (b - a) * t</c>. The GPU rounds
    /// the interpolation weights to 1/256, so its samples differ from these by up to 1/512 of the
    /// difference between neighbouring pixels.
    /// </summary>
    public static Float4 SampleLinearClamp(Float4[] image, int width, int height, Float2 uv)
    {
        float fx = (uv.X * width) - 0.5f;
        float fy = (uv.Y * height) - 0.5f;
        float floorX = MathF.Floor(fx);
        float floorY = MathF.Floor(fy);
        float tx = fx - floorX;
        float ty = fy - floorY;
        int x0 = Math.Clamp((int)floorX, 0, width - 1);
        int x1 = Math.Clamp((int)floorX + 1, 0, width - 1);
        int y0 = Math.Clamp((int)floorY, 0, height - 1);
        int y1 = Math.Clamp((int)floorY + 1, 0, height - 1);

        Float4 upper = Lerp(image[(y0 * width) + x0], image[(y0 * width) + x1], tx);
        Float4 lower = Lerp(image[(y1 * width) + x0], image[(y1 * width) + x1], tx);
        return Lerp(upper, lower, ty);
    }

    private static Float4 Lerp(Float4 a, Float4 b, float t) => a + ((b - a) * t);
}
