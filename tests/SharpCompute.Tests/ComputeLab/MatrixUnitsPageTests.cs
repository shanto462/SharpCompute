using System;
using System.Linq;
using SharpCompute.Presentation;
using SharpCompute.Samples.ComputeLab;
using SharpCompute.Samples.ComputeLab.Pages;
using SharpCompute.Samples.ComputeLab.Pages.Simd;

namespace SharpCompute.Tests.ComputeLab;

/// <summary>
/// ComputeLab's "Matrix units" page: its matrix product on wave matrices against a double-precision
/// reference on random data (whole tiles and, padded by the caller, sizes that are not), the same
/// product under <c>ForCpu</c>, and what the page shows. Rendering, the headless pictures and the
/// timing line of every page are covered by <see cref="ComputeLabTests"/>.
/// </summary>
public class MatrixUnitsPageTests
{
    private static string Msl<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.MslSource;

    private static bool UsesSimdGroups<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.UsesSimdGroups;

    private static float[] Random(int count, int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => (random.NextSingle() * 2f) - 1f).ToArray();
    }

    /// <summary><c>a × b</c> for row-major n × n matrices in double precision, and per element the sum of the magnitudes of its terms.</summary>
    private static (double[] Product, double[] Magnitude) Reference(float[] a, float[] b, int n)
    {
        double[] product = new double[n * n], magnitude = new double[n * n];

        for (int row = 0; row < n; row++)
        {
            for (int k = 0; k < n; k++)
            {
                double scale = a[(row * n) + k];

                for (int column = 0; column < n; column++)
                {
                    double term = scale * b[(k * n) + column];
                    product[(row * n) + column] += term;
                    magnitude[(row * n) + column] += Math.Abs(term);
                }
            }
        }

        return (product, magnitude);
    }

    /// <summary>Every element within what a float sum of n products can be off by, whatever order they are added in.</summary>
    private static void AssertMatches((double[] Product, double[] Magnitude) reference, float[] actual, string what)
    {
        Assert.Equal(reference.Product.Length, actual.Length);

        for (int i = 0; i < actual.Length; i++)
        {
            double allowed = (1e-5 * reference.Magnitude[i]) + 1e-6;
            Assert.True(Math.Abs(actual[i] - reference.Product[i]) <= allowed, $"{what}, element {i}: {actual[i]:R} for {reference.Product[i]:R} (allowed {allowed:E1})");
        }
    }

    /// <summary>The product of two n × n matrices by the page's kernel, padded as <see cref="WaveMatMul"/> says, on the GPU or the CPU.</summary>
    private static float[] Multiply(float[] a, float[] b, int n, bool cpu = false)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int size = WaveMatMul.PaddedSize(n);
        using ReadWriteBuffer<float> left = device.AllocateReadWriteBuffer<float>(WaveMatMul.Pad(a, n));
        using ReadWriteBuffer<float> right = device.AllocateReadWriteBuffer<float>(WaveMatMul.Pad(b, n));
        using ReadWriteBuffer<float> product = device.AllocateReadWriteBuffer<float>(size * size);
        product.AsSpan().Fill(float.NaN);

        if (cpu)
        {
            WaveMatMul.MultiplyOnCpu(device, left, right, product, size);
        }
        else
        {
            WaveMatMul.Multiply(device, left, right, product, size);
        }

        // Every element of the padded product was written, the padding with zeros.
        float[] padded = product.ToArray();
        Assert.DoesNotContain(padded, float.IsNaN);

        for (int row = 0; row < size; row++)
        {
            for (int column = row < n ? n : 0; column < size; column++)
            {
                Assert.Equal(0f, padded[(row * size) + column]);
            }
        }

        return WaveMatMul.Unpad(padded, n);
    }

    // ---- the kernel -------------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(64)] // one column of threadgroups, two rows
    [InlineData(128)]
    [InlineData(320)] // 5 x 10 threadgroups
    public void TheKernel_OnRandomData_MatchesADoubleReference(int n)
    {
        float[] a = Random(n * n, seed: n), b = Random(n * n, seed: n + 1);

        AssertMatches(Reference(a, b, n), Multiply(a, b, n), $"n = {n}");
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(65)]
    [InlineData(100)]
    public void PartialTiles_AreTheCallersJob_PaddingHandlesThem(int n)
    {
        // n is not a multiple of 64 (nor of 8): the matrices are padded with zeros to the next one.
        float[] a = Random(n * n, seed: 100 + n), b = Random(n * n, seed: 200 + n);

        AssertMatches(Reference(a, b, n), Multiply(a, b, n), $"n = {n}");
    }

    [Fact]
    public void PaddedSize_IsTheNextMultipleOf64_AndPadAndUnpadAreInverses()
    {
        Assert.Equal(64, WaveMatMul.PaddedSize(1));
        Assert.Equal(64, WaveMatMul.PaddedSize(64));
        Assert.Equal(128, WaveMatMul.PaddedSize(65));
        Assert.Equal(128, WaveMatMul.PaddedSize(100));
        Assert.Equal(1024, WaveMatMul.PaddedSize(1024));
        Assert.Equal(2048, WaveMatMul.PaddedSize(2000));

        float[] matrix = Random(100 * 100, seed: 9);
        float[] padded = WaveMatMul.Pad(matrix, 100);

        Assert.Equal(128 * 128, padded.Length);
        Assert.Equal(matrix[(3 * 100) + 99], padded[(3 * 128) + 99]);
        Assert.Equal(0f, padded[(3 * 128) + 100]);
        Assert.Equal(0f, padded[^1]);
        Assert.Equal(matrix, WaveMatMul.Unpad(padded, 100));
    }

    [MetalFact]
    public void ASizeThatIsNotPadded_IsRefused()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(128 * 128);
        using ComputeContext context = device.CreateComputeContext();

        foreach (int size in new[] { 0, -64, 100, 96, 32 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WaveMatMul.Multiply(device, buffer, buffer, buffer, size));
            Assert.Throws<ArgumentOutOfRangeException>(() => WaveMatMul.Multiply(context, buffer, buffer, buffer, size));
            Assert.Throws<ArgumentOutOfRangeException>(() => WaveMatMul.MultiplyOnCpu(device, buffer, buffer, buffer, size));
        }

        Assert.Equal(0, context.PendingCount);
    }

    [MetalTheory]
    [InlineData(64)]
    [InlineData(40)] // padded to 64
    public void ForCpu_MatchesTheGpu_WithinRounding(int n)
    {
        // Small: on the CPU every lane is a thread and every matrix operation a meeting of 32 of them.
        float[] a = Random(n * n, seed: 300 + n), b = Random(n * n, seed: 400 + n);
        (double[] Product, double[] Magnitude) reference = Reference(a, b, n);

        float[] gpu = Multiply(a, b, n);
        float[] cpu = Multiply(a, b, n, cpu: true);

        AssertMatches(reference, cpu, "CPU");
        AssertMatches(reference, gpu, "GPU");
    }

    [Fact]
    public void TheKernel_IsWhatThePageSaysItIs()
    {
        string msl = Msl<MatMulWaveMatrix>();

        Assert.True(UsesSimdGroups<MatMulWaveMatrix>());
        Assert.Equal(128, MatMulWaveMatrix.GroupThreads);
        Assert.Equal((32, 64), (MatMulWaveMatrix.TileRows, MatMulWaveMatrix.TileColumns));
        Assert.StartsWith("#include <metal_stdlib>\n#include <metal_simdgroup_matrix>\n", msl.ReplaceLineEndings("\n"));

        // Eight accumulators, which start as zero matrices; two loads of a and four of b per step, twice.
        Assert.Equal(8, msl.Split("= metal::make_filled_simdgroup_matrix<float, 8, 8>(0.0f);").Length - 1);
        Assert.Equal(4, msl.Split("__ms_wave_matrix_load(a + ").Length - 1);
        Assert.Equal(8, msl.Split("__ms_wave_matrix_load(b + ").Length - 1);
        Assert.Equal(16, msl.Split("= __ms_wave_matrix_multiply_accumulate<float>(").Length - 1);
        Assert.Equal(8, msl.Split("metal::simdgroup_store(c").Length - 1);

        // The offsets are added to the buffer one term at a time, which is where the speed comes from.
        Assert.Contains("__ms_wave_matrix_load(a + row * __c.stride + k, ulong(__c.stride), false)", msl);
        Assert.Contains("__ms_wave_matrix_load(b + (k + 8) * __c.stride + column + 24, ulong(__c.stride), false)", msl);
        Assert.Contains("metal::simdgroup_store(c13, c + (row + 8) * __c.stride + column + 24, ulong(__c.stride), ulong2(0), false);", msl);
    }

    // ---- the page ---------------------------------------------------------------------------------

    [MetalFact]
    public void EveryVariant_MatchesTheCpuReference()
    {
        using MatrixUnitsPage page = new(GraphicsDevice.GetDefault());

        Assert.Equal("Matrix units", page.Name);
        Assert.Equal(LabTopics.Simd, page.Topic);
        Assert.Equal("all cores", ((ILabPage)page).CpuNote);
        Assert.Null(((ILabPage)page).Baseline);
        Assert.Equal(
            [
                "1024 x 1024: wave matrices", "1024 x 1024: tiles in shared memory", "1024 x 1024: naive",
                "2048 x 2048: wave matrices", "2048 x 2048: tiles in shared memory", "2048 x 2048: naive",
            ],
            page.Variants);

        for (int variant = 0; variant < page.Variants.Count; variant++)
        {
            page.Variant = variant;
            Assert.Equal(variant < 3 ? 1024 : 2048, page.N);
            Assert.Equal(variant % 3, page.Kernel);

            page.Compute(new LabFrame(Headless.Time, Deterministic: true));
            LabCheck check = page.Check();

            Assert.True(check.Passed, $"{page.Variants[variant]}: {check.Summary}");
            Assert.StartsWith($"32 of {page.N} rows match C# doubles", check.Summary);
            Assert.True(page.GpuMilliseconds > 0 && check.CpuMilliseconds > 0);

            // Random values in -1..1: a sum of n products is off by far less than one of them.
            Assert.InRange(page.MaxError, 0, 1e-3);
        }
    }

    [MetalFact]
    public void TheMatrices_AreRandom_AndChangeWithTime()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int n = 256;
        using ReadWriteBuffer<float> a = device.AllocateReadWriteBuffer<float>(n * n), b = device.AllocateReadWriteBuffer<float>(n * n);
        using ReadWriteBuffer<float> later = device.AllocateReadWriteBuffer<float>(n * n), other = device.AllocateReadWriteBuffer<float>(n * n);

        device.For(n, n, new GenerateRandomMatrices(a, b, n, seed: 1));
        device.For(n, n, new GenerateRandomMatrices(later, other, n, seed: 2));
        float[] values = a.ToArray();

        // In -1..1, centred, spread like a uniform distribution (variance 1/3), and nothing like b or the next seed.
        Assert.All(values, static value => Assert.InRange(value, -1f, 1f));
        Assert.InRange(values.Average(), -0.02, 0.02);
        Assert.InRange(values.Average(static value => (double)value * value), 0.32, 0.345);
        Assert.InRange(values.Zip(b.ToArray(), static (x, y) => (double)x * y).Average(), -0.01, 0.01);
        Assert.InRange(values.Zip(later.ToArray(), static (x, y) => (double)x * y).Average(), -0.01, 0.01);
        Assert.True(values.Distinct().Count() > n * n * 0.99);
    }

    [Fact]
    public void GigaFlops_CountAMultiplyAndAnAddPerTerm()
    {
        // 2 x 1024^3 operations in a millisecond: 2,147 GFLOP/s.
        Assert.Equal(2147.483648, MatrixUnitsPage.GigaFlops(1024, 1.0), 9);
        Assert.Equal(8 * 2147.483648, MatrixUnitsPage.GigaFlops(2048, 1.0), 9);
        Assert.Equal(0, MatrixUnitsPage.GigaFlops(1024, 0));

        // The recorded rates: the matrix units ahead of the tiles ahead of the naive kernel, at both sizes.
        Assert.Equal(2, MatrixUnitsPage.RecordedGigaFlops.Length);
        Assert.All(MatrixUnitsPage.RecordedGigaFlops, static rates =>
        {
            Assert.Equal(MatrixUnitsPage.Kernels, rates.Length);
            Assert.True(rates[MatrixUnitsPage.Wave] > 4 * rates[MatrixUnitsPage.Tiled]);
            Assert.True(rates[MatrixUnitsPage.Tiled] > rates[MatrixUnitsPage.Naive]);
        });
    }

    [MetalFact]
    public void TheKeys_SwitchTheKernelAndTheSize()
    {
        using MatrixUnitsPage page = new(GraphicsDevice.GetDefault());

        Assert.Equal("V kernel   N size", page.Help);
        Assert.True(page.OnKeyDown(Key.V));
        Assert.Equal("1024 x 1024: tiles in shared memory", page.Variants[page.Variant]);
        Assert.True(page.OnKeyDown(Key.N));
        Assert.Equal("2048 x 2048: tiles in shared memory", page.Variants[page.Variant]);
        Assert.True(page.OnKeyDown(Key.V));
        Assert.True(page.OnKeyDown(Key.V));
        Assert.Equal("2048 x 2048: wave matrices", page.Variants[page.Variant]);
        Assert.True(page.OnKeyDown(Key.N));
        Assert.Equal(0, page.Variant);
        Assert.False(page.OnKeyDown(Key.C));
        Assert.Equal(0, page.Variant);

        // The rate is that of the active size.
        Assert.Equal("GPU 2147 GFLOP/s   CPU 215 GFLOP/s", page.Rate(1.0, 10.0));
        page.Variant = 3;
        Assert.Equal("GPU 17180 GFLOP/s   CPU 1718 GFLOP/s", page.Rate(1.0, 10.0));
    }

    [MetalFact]
    public void ThePicture_NamesTheOperations_AndShowsRecordedRates_TheWindowThisMachines()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using MatrixUnitsPage page = new(device);
        using LabCanvas canvas = new(device);

        // A reproducible frame: the recorded rates, said to be recorded, and no time anywhere.
        LabFrame still = new(Headless.Time, Deterministic: true);
        page.Compute(still);
        canvas.ClearText();
        page.Draw(canvas, still);
        string picture = string.Join("\n", canvas.TextLines());

        Assert.Contains("C = A X B FOR 1024 X 1024 RANDOM FLOATS", picture);
        Assert.Contains("OPS: WAVEMATRIX8X8.LOAD, WAVEMATRIX8X8.MULTIPLYACCUMULATE, STORE", picture);
        Assert.Contains("C00 = WAVEMATRIX8X8.MULTIPLYACCUMULATE(A0, B0, C00);", picture);
        Assert.Contains("A0 = WAVEMATRIX8X8.LOAD(A, ROW * N + K, N);", picture);
        Assert.Contains("C00.STORE(C, ROW * N + COLUMN, N);", picture);
        Assert.Contains("GFLOP/S RECORDED ON APPLE M4 PRO", picture);
        Assert.Contains(FormattableString.Invariant($"{MatrixUnitsPage.RecordedGigaFlops[0][MatrixUnitsPage.Wave]:N0} GFLOP/S"), picture);
        Assert.Contains(FormattableString.Invariant($"{MatrixUnitsPage.RecordedGigaFlops[0][MatrixUnitsPage.Naive]:N0} GFLOP/S"), picture);
        Assert.Contains("16 SPILL: 7X SLOWER", picture);
        Assert.DoesNotContain(" MS", picture);
        Assert.DoesNotContain("THIS MACHINE,", picture);

        // The bars: the matrix units' is full, the others in proportion to their recorded rates.
        Rgba32[] pixels = canvas.ToArray();

        int Filled(int kernel)
        {
            int y = DrawMatrixUnits.BarY(kernel) + (DrawMatrixUnits.BarHeight / 2), count = 0;

            for (int x = DrawMatrixUnits.BarX; x < DrawMatrixUnits.BarX + DrawMatrixUnits.BarWidth; x++)
            {
                count += pixels[(y * LabCanvas.Width) + x].B > 200 || pixels[(y * LabCanvas.Width) + x].R > 200 ? 1 : 0;
            }

            return count;
        }

        Assert.Equal(DrawMatrixUnits.BarWidth, Filled(MatrixUnitsPage.Wave));
        Assert.InRange(
            Filled(MatrixUnitsPage.Tiled),
            (DrawMatrixUnits.BarWidth * MatrixUnitsPage.RecordedGigaFlops[0][MatrixUnitsPage.Tiled] / MatrixUnitsPage.RecordedGigaFlops[0][MatrixUnitsPage.Wave]) - 2,
            (DrawMatrixUnits.BarWidth * MatrixUnitsPage.RecordedGigaFlops[0][MatrixUnitsPage.Tiled] / MatrixUnitsPage.RecordedGigaFlops[0][MatrixUnitsPage.Wave]) + 2);
        Assert.True(Filled(MatrixUnitsPage.Naive) < Filled(MatrixUnitsPage.Tiled));

        // The window: all three kernels timed here, each bar with its rate and its time.
        LabFrame live = new(Headless.Time, Deterministic: false);
        page.Compute(live);
        canvas.ClearText();
        page.Draw(canvas, live);
        string window = string.Join("\n", canvas.TextLines());

        Assert.Contains("GFLOP/S ON THIS MACHINE, COMMIT TO COMPLETION", window);
        Assert.DoesNotContain("RECORDED", window);
        Assert.Equal(3, window.Split('\n').Count(static line => line.Contains(" GFLOP/S  ", StringComparison.Ordinal) && line.EndsWith(" MS", StringComparison.Ordinal)));
    }
}
