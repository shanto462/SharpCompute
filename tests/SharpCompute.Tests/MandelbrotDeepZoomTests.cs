using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using SharpCompute.Samples.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// The deep-zoom Mandelbrot gallery entry: the <see cref="FixedPoint"/> arithmetic behind its
/// reference orbit, the orbit itself, the loop schedule, and GPU renders at shallow and deep zoom.
/// </summary>
public class MandelbrotDeepZoomTests
{
    // ---- FixedPoint ---------------------------------------------------------------------------

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1.25", "-0.5")]
    [InlineData("-3.14159265", "2.71828182")]
    [InlineData("0.00000001", "12345.6789")]
    [InlineData("-0.74364388", "0.13182590")]
    [InlineData("99999.99999999", "-0.00000001")]
    public void FixedPoint_ArithmeticMatchesDecimal(string left, string right)
    {
        decimal a = decimal.Parse(left, CultureInfo.InvariantCulture);
        decimal b = decimal.Parse(right, CultureInfo.InvariantCulture);
        FixedPoint x = FixedPoint.Parse(left);
        FixedPoint y = FixedPoint.Parse(right);

        // With at most 8 decimals per operand the exact results have at most 16, and the fixed-point
        // error (≈ 2^-192) is far below the 20th digit, so 20 rounded digits must match exactly.
        AssertDecimal(a + b, x + y);
        AssertDecimal(a - b, x - y);
        AssertDecimal(a * b, x * y);
        AssertDecimal(a * a, x.Square());
        AssertDecimal(-a, -x);
        AssertDecimal(a * 8, x.ScaleB(3));
        AssertDecimal(a / 4, x.ScaleB(-2));

        Assert.Equal(a.CompareTo(b), x.CompareTo(y));
        Assert.Equal(a < b, x < y);
        Assert.Equal(a == b, x == y);
        Assert.Equal((double)a, x.ToDouble(), 15);

        static void AssertDecimal(decimal expected, FixedPoint actual) =>
            Assert.Equal(expected, decimal.Parse(actual.ToString(20), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FixedPoint_MatchesDoubleOnRandomValues()
    {
        Random random = new(1234);

        for (int i = 0; i < 1000; i++)
        {
            double a = (random.NextDouble() - 0.5) * 8;
            double b = (random.NextDouble() - 0.5) * 8;
            FixedPoint x = FixedPoint.FromDouble(a);
            FixedPoint y = FixedPoint.FromDouble(b);

            // Doubles convert exactly, so round trips are exact and sums are correctly rounded.
            Assert.Equal(a, x.ToDouble());
            Assert.Equal(a + b, (x + y).ToDouble(), 1e-15);
            Assert.Equal(a - b, (x - y).ToDouble(), 1e-15);
            Assert.Equal(a * b, (x * y).ToDouble(), 1e-14);
        }
    }

    [Fact]
    public void FixedPoint_ParsesAndFormatsWithFullPrecision()
    {
        Assert.Equal(FixedPoint.One, FixedPoint.Parse("1"));
        Assert.Equal(FixedPoint.FromInt(-42), FixedPoint.Parse("-4.2e1"));
        Assert.Equal(FixedPoint.Parse("0.000125"), FixedPoint.Parse("1.25E-4"));
        Assert.Equal("-0.500", FixedPoint.Parse("-0.5").ToString(3));
        Assert.Equal("3", FixedPoint.Parse("2.5").ToString(0));

        // The zoom center keeps all 33 decimals (double keeps about 16).
        FixedPoint center = FixedPoint.Parse(MandelbrotZoom.CenterReal);
        Assert.Equal(MandelbrotZoom.CenterReal, center.ToString(33));
        Assert.NotEqual(MandelbrotZoom.CenterReal, FixedPoint.FromDouble(center.ToDouble()).ToString(33));

        // 0.1 isn't representable; ten of them are within a few units in the last place of 1.
        FixedPoint tenth = FixedPoint.Parse("0.1");
        FixedPoint sum = FixedPoint.Zero;

        for (int i = 0; i < 10; i++)
        {
            sum += tenth;
        }

        Assert.True(BigInteger.Abs((sum - FixedPoint.One).Raw) <= 10);
        Assert.True(FixedPoint.Epsilon > FixedPoint.Zero);
        Assert.Throws<FormatException>(() => FixedPoint.Parse("1.2.3"));
        Assert.Throws<FormatException>(() => FixedPoint.Parse("-"));
    }

    // ---- Reference orbit and schedule --------------------------------------------------------

    [Fact]
    public void ReferenceOrbit_StaysBoundedForTheWholeZoom()
    {
        int deepest = MandelbrotZoom.MaxIterationsAt(MandelbrotZoom.ScaleAt(MandelbrotZoom.Period));
        MandelbrotReferenceOrbit orbit = new(MandelbrotZoom.CenterReal, MandelbrotZoom.CenterImaginary);

        Assert.True(orbit.EnsureLength(deepest + 1));
        Assert.False(orbit.Escaped);
        Assert.Equal(deepest + 1, orbit.Length);
        Assert.All(orbit.Values.ToArray(), static z => Assert.True((z.X * z.X) + (z.Y * z.Y) <= 4f));

        // Growing again only appends.
        Float2[] before = orbit.Values.ToArray();
        Assert.False(orbit.EnsureLength(deepest));
        Assert.True(orbit.EnsureLength(deepest + 100));
        Assert.Equal(before, orbit.Values[..before.Length].ToArray());

        // The first iterations agree with double precision, before rounding errors grow.
        double cx = double.Parse(MandelbrotZoom.CenterReal, CultureInfo.InvariantCulture);
        double cy = double.Parse(MandelbrotZoom.CenterImaginary, CultureInfo.InvariantCulture);
        double x = 0, y = 0;

        for (int n = 0; n < 30; n++)
        {
            Assert.Equal((float)x, orbit.Values[n].X, 1e-6f);
            Assert.Equal((float)y, orbit.Values[n].Y, 1e-6f);
            (x, y) = ((x * x) - (y * y) + cx, (2 * x * y) + cy);
        }
    }

    [Fact]
    public void ReferenceOrbit_StopsAtEscape()
    {
        MandelbrotReferenceOrbit orbit = new("0.5", "0.5");

        orbit.EnsureLength(1000);

        Assert.True(orbit.Escaped);
        Assert.InRange(orbit.Length, 2, 20);
        Assert.False(orbit.EnsureLength(2000));
    }

    [Fact]
    public void Schedule_LoopsSeamlessly()
    {
        Assert.Equal(1e-30, MandelbrotZoom.ScaleAt(125.3), 1e-31);
        Assert.True(MandelbrotZoom.ScaleAt(MandelbrotZoom.Period) * 2 / 144 > 1e-35, "the deepest pixel spacing must stay a normal float");

        Assert.Equal((0d, 0d, 0f), MandelbrotZoom.LayersAt(0));
        Assert.Equal((60d, 60d, 0f), MandelbrotZoom.LayersAt(60));
        Assert.Equal(MandelbrotZoom.LayersAt(10), MandelbrotZoom.LayersAt(10 + MandelbrotZoom.Period));
        Assert.Equal(MandelbrotZoom.LayersAt(10), MandelbrotZoom.LayersAt(10 - MandelbrotZoom.Period));

        // Mid-fade: both layers, weighted equally; at the end of the period the second layer has
        // full weight and is at zoom time 0 again, which is where the next loop starts.
        (double a, double b, float blend) = MandelbrotZoom.LayersAt(MandelbrotZoom.ZoomSeconds + (MandelbrotZoom.FadeSeconds / 2));
        Assert.Equal(MandelbrotZoom.ZoomSeconds + 1, a, 1e-9);
        Assert.Equal(-1, b, 1e-9);
        Assert.Equal(0.5f, blend, 1e-6f);

        (_, b, blend) = MandelbrotZoom.LayersAt(MandelbrotZoom.Period - 1e-9);
        Assert.Equal(0, b, 1e-6);
        Assert.Equal(1f, blend, 1e-6f);

        // The faster pass past the minibrot near t = 28 is given back by t = 60: from there on the
        // depth is one halving per HalvingSeconds again, and it only ever increases.
        Assert.Equal(60 / MandelbrotZoom.HalvingSeconds, MandelbrotZoom.HalvingsAt(60), 1e-9);
        Assert.Equal(126 / MandelbrotZoom.HalvingSeconds, MandelbrotZoom.HalvingsAt(126), 1e-9);
        Assert.Equal(32, MandelbrotZoom.HalvingsAt(35), 0.01);

        for (double t = -2; t < MandelbrotZoom.Period; t += 0.05)
        {
            Assert.True(MandelbrotZoom.HalvingsAt(t + 0.05) > MandelbrotZoom.HalvingsAt(t), $"the zoom reverses at t = {t}");
        }

        // Iterations grow with depth.
        Assert.Equal(300, MandelbrotZoom.MaxIterationsAt(MandelbrotZoom.ScaleAt(-1)));
        Assert.True(MandelbrotZoom.MaxIterationsAt(MandelbrotZoom.ScaleAt(100)) > MandelbrotZoom.MaxIterationsAt(MandelbrotZoom.ScaleAt(50)));
    }

    // ---- GPU -----------------------------------------------------------------------------------

    /// <summary>
    /// At t = 5 (a view 0.09 high) double precision is exact enough to be the ground truth: per-pixel
    /// escape counts from the perturbation kernel match a direct double-precision Mandelbrot.
    /// </summary>
    [MetalFact]
    public void ShallowZoom_MatchesDoublePrecision()
    {
        const int width = 160, height = 90;
        const double zoomTime = 5;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> output = device.AllocateReadWriteBuffer<Float4>(width * height);
        MandelbrotZoom.ComputeIterations(device, output, width, height, zoomTime);
        Float4[] gpu = output.ToArray();

        int[] cpu = DoublePrecisionEscapeCounts(width, height, zoomTime);
        (int compared, int matched) = CountMatches(gpu, cpu, width, height);

        Assert.True(compared > width * height / 2, $"only {compared} non-boundary pixels");
        Assert.True(matched >= 0.99 * compared, $"{matched} of {compared} non-boundary pixels ({100.0 * matched / compared:F2}%) match within ±1");
    }

    /// <summary>The series approximation skips iterations at depth without changing the picture.</summary>
    [MetalFact]
    public void SeriesApproximation_SkipsIterationsAndAgreesWithTheFullIteration()
    {
        const int width = 160, height = 90;

        // t = 35, 32 halvings deep: still checkable against double, and the approximation already
        // skips hundreds of iterations.
        const double zoomTime = 35;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> full = device.AllocateReadWriteBuffer<Float4>(width * height);
        using ReadWriteBuffer<Float4> skipped = device.AllocateReadWriteBuffer<Float4>(width * height);

        Assert.Equal(0, MandelbrotZoom.ComputeIterations(device, full, width, height, zoomTime, useSeriesApproximation: false));
        int skip = MandelbrotZoom.ComputeIterations(device, skipped, width, height, zoomTime);

        Assert.InRange(skip, 100, MandelbrotZoom.MaxIterationsAt(MandelbrotZoom.ScaleAt(zoomTime)));

        int[] cpu = DoublePrecisionEscapeCounts(width, height, zoomTime);
        (int compared, int matchedFull) = CountMatches(full.ToArray(), cpu, width, height);
        (_, int matchedSkipped) = CountMatches(skipped.ToArray(), cpu, width, height);

        // Pixel-level chaos makes float renders differ from double on a few percent of pixels at
        // this depth either way; the approximation must not make that noticeably worse.
        Assert.True(matchedFull >= 0.9 * compared, $"full: {matchedFull} of {compared}");
        Assert.True(matchedSkipped >= matchedFull - (0.01 * compared), $"skipped: {matchedSkipped}, full: {matchedFull} of {compared}");

        // Deep in the zoom the skip is thousands of iterations.
        Assert.True(MandelbrotZoom.ComputeIterations(device, skipped, width, height, 100) > 1000);
    }

    /// <summary>
    /// At t = 60 the view is about 5e-15 high, past where plain float (≈1e-6) and even double
    /// (≈1e-14) break down into flat blocks, and the render still shows fractal detail.
    /// </summary>
    [MetalFact]
    public void DeepZoom_RendersDetailNotBlocks()
    {
        const int width = 256, height = 144, block = 8;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        GalleryShader shader = Gallery.All.Single(static s => s.Name == "Mandelbrot");

        shader.Render(device, texture, 60f);
        Rgba32[] pixels = texture.ToArray();

        int colors = pixels.Distinct().Count();
        Assert.True(colors >= 64, $"only {colors} distinct colors");

        int uniformBlocks = 0, blocks = 0;

        for (int by = 0; by + block <= height; by += block)
        {
            for (int bx = 0; bx + block <= width; bx += block)
            {
                blocks++;
                Rgba32 first = pixels[(by * width) + bx];
                bool uniform = true;

                for (int y = by; y < by + block && uniform; y++)
                {
                    for (int x = bx; x < bx + block && uniform; x++)
                    {
                        uniform = pixels[(y * width) + x].Equals(first);
                    }
                }

                uniformBlocks += uniform ? 1 : 0;
            }
        }

        Assert.True(uniformBlocks * 2 <= blocks, $"{uniformBlocks} of {blocks} 8×8 blocks are a single color");
    }

    /// <summary>
    /// At t = 4 the view is mostly the main cardioid and at t = 28 it passes a minibrot: most pixels
    /// don't escape, and the interior shading must still keep any one color from taking over the frame.
    /// </summary>
    [MetalTheory]
    [InlineData(4f)]
    [InlineData(28f)]
    public void Interior_IsShadedNotFlat(float time)
    {
        const int width = 256, height = 144;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> iterations = device.AllocateReadWriteBuffer<Float4>(width * height);
        MandelbrotZoom.ComputeIterations(device, iterations, width, height, MandelbrotZoom.LayersAt(time).ZoomTimeA);
        int interior = iterations.ToArray().Count(static p => p.X < 0);
        Assert.True(interior >= width * height / 3, $"only {interior} interior pixels; the test needs a mostly-interior view");

        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        MandelbrotZoom.Render(device, texture, time);
        Rgba32[] pixels = texture.ToArray();

        int largest = pixels.GroupBy(static p => p).Max(static g => g.Count());
        Assert.True(largest <= 0.4 * pixels.Length, $"one color covers {100.0 * largest / pixels.Length:F1}% of the frame");
    }

    [MetalFact]
    public void Render_IsPeriodic()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(64, 36);

        MandelbrotZoom.Render(device, texture, 3f);
        Rgba32[] first = texture.ToArray();
        MandelbrotZoom.Render(device, texture, 3f + (float)MandelbrotZoom.Period);
        Rgba32[] second = texture.ToArray();

        Assert.Equal(first, second);
    }

    /// <summary>Direct iteration in double, with the kernel's pixel mapping and bailout; -1 inside.</summary>
    private static int[] DoublePrecisionEscapeCounts(int width, int height, double zoomTime)
    {
        double cx = double.Parse(MandelbrotZoom.CenterReal, CultureInfo.InvariantCulture);
        double cy = double.Parse(MandelbrotZoom.CenterImaginary, CultureInfo.InvariantCulture);
        double pixelScale = MandelbrotZoom.PixelScaleAt(zoomTime, height);
        int maxIterations = MandelbrotZoom.MaxIterationsAt(MandelbrotZoom.ScaleAt(zoomTime));
        int[] counts = new int[width * height];

        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                double ccx = cx + ((px - ((width - 1) * 0.5)) * pixelScale);
                double ccy = cy + ((((height - 1) * 0.5) - py) * pixelScale);
                double x = 0, y = 0;
                int n = 0;
                bool escaped = false;

                while (n < maxIterations)
                {
                    (x, y) = ((x * x) - (y * y) + ccx, (2 * x * y) + ccy);
                    n++;

                    if ((x * x) + (y * y) > MandelbrotPerturbation.Bailout)
                    {
                        escaped = true;
                        break;
                    }
                }

                counts[(py * width) + px] = escaped ? n : -1;
            }
        }

        return counts;
    }

    /// <summary>
    /// Compares escape counts within ±1, skipping boundary pixels: those whose 3×3 neighbourhood in
    /// the reference has both escaped and interior pixels, where any rounding can flip the result.
    /// </summary>
    private static (int Compared, int Matched) CountMatches(Float4[] gpu, int[] cpu, int width, int height)
    {
        int compared = 0, matched = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                HashSet<bool> kinds = [];

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = Math.Clamp(x + dx, 0, width - 1), ny = Math.Clamp(y + dy, 0, height - 1);
                        kinds.Add(cpu[(ny * width) + nx] < 0);
                    }
                }

                if (kinds.Count > 1)
                {
                    continue;
                }

                int i = (y * width) + x;
                compared++;
                matched += Math.Abs((int)gpu[i].X - cpu[i]) <= 1 ? 1 : 0;
            }
        }

        return (compared, matched);
    }
}
