using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompute.Presentation;
using SharpCompute.Samples.ComputeLab;
using SharpCompute.Samples.ComputeLab.Pages;
using SharpCompute.Samples.ComputeLab.Pages.SharedMemory;

namespace SharpCompute.Tests.ComputeLab;

/// <summary>
/// ComputeLab's shared-memory pages: the reusable pieces (the sum, the scan, the tiled product, the
/// tiled blur) are right at sizes that are not multiples of a threadgroup, and each page shows and
/// checks what it claims. Rendering, the headless pictures and the timing line of every page are
/// covered by <see cref="ComputeLabTests"/>.
/// </summary>
public sealed class SharedMemoryPageTests
{
    private static readonly LabFrame Frame = new(Headless.Time, Deterministic: true);

    [MetalFact]
    public void TimingLine_SaysWhereTheCpuRan_AndAddsTheMatrixProductsRate()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReductionPage reduction = new(device);
        using PrefixSumPage scan = new(device);
        using MatMulPage product = new(device);

        Assert.Equal("GPU 0.50 ms   CPU 5.00 ms (all cores)   GPU is 10.0x faster", LabView.TimingLine(reduction, new LabTiming(0.5, 5)));
        Assert.Equal("GPU 3.00 ms   CPU 1.50 ms (one core)   CPU is 2.0x faster", LabView.TimingLine(scan, new LabTiming(3, 1.5)));
        Assert.Equal("GPU 1.00 ms   CPU 10.00 ms (all cores)   GPU is 10.0x faster   GPU 2147 GFLOP/s   CPU 215 GFLOP/s", LabView.TimingLine(product, new LabTiming(1, 10)));
    }

    // ---- reduction --------------------------------------------------------------------------------

    private static float[] Floats(int count, int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => (float)random.NextDouble()).ToArray();
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(65_537)]
    [InlineData(100_003)]
    public void Sum_IsTheKernelsAdditionsInC_AtAnyCount(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] values = Floats(count, seed: count);
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer(values);
        using GpuSum sum = new(device, count);

        float tree = sum.Run(buffer, count, shared: true);
        float serial = sum.Run(buffer, count, shared: false);
        double reference = CpuSum.Double(values);

        // Floats, added in a fixed order on both sides: equal to the bit.
        Assert.Equal(CpuSum.Tree(values), tree);
        Assert.Equal(CpuSum.SerialBlocks(values), serial);
        Assert.Equal(reference, tree, Math.Max(1e-3, reference * 1e-6));
        Assert.Equal(reference, serial, Math.Max(1e-3, reference * 1e-6));
        Assert.Equal(values.Sum(static v => (double)v), reference, 1e-6);
    }

    [MetalFact]
    public void Sum_OfFewerValuesThanItsCapacity_IgnoresTheRest()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] values = Floats(5000, seed: 1);
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer(values);
        using GpuSum sum = new(device, 5000);

        Assert.Equal(2, sum.Passes);
        Assert.Equal(CpuSum.Tree(values[..300]), sum.Run(buffer, 300, shared: true));
        Assert.Equal(CpuSum.SerialBlocks(values[..300]), sum.Run(buffer, 300, shared: false));
        Assert.Equal(0f, sum.Run(buffer, 0, shared: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => sum.Run(buffer, 5001, shared: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuSum(device, 0));
    }

    [MetalFact]
    public void Reduction_TreeIsCloserToTheDoubleSumThanARunningFloatTotal()
    {
        using ReductionPage page = new(GraphicsDevice.GetDefault());

        page.Compute(Frame);
        LabCheck check = page.Check();

        Assert.True(check.Passed, check.Summary);
        Assert.InRange(page.Reference, ReductionPage.Count * 0.3, ReductionPage.Count * 0.7);

        double treeError = Math.Abs(page.Total - page.Reference);
        double runningError = Math.Abs(page.RunningFloatTotal - page.Reference);
        Assert.True(treeError < 16, $"tree error {treeError}");
        Assert.True(runningError > 100 * Math.Max(1, treeError), $"running error {runningError}, tree error {treeError}");

        // V switches to the serial kernel, which must still agree with its own additions in C#.
        Assert.True(page.OnKeyDown(Key.V));
        Assert.Equal(1, page.Variant);
        page.Compute(Frame);
        Assert.True(page.Check().Passed);
        Assert.False(page.OnKeyDown(Key.Up));
        Assert.True(page.OnKeyDown(Key.V));
        Assert.Equal(0, page.Variant);
    }

    [MetalFact]
    public void Reduction_FollowsTheAnimation_AndTimesBothVariantsInTheWindow()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReductionPage page = new(device);
        using LabCanvas canvas = new(device);

        page.Compute(new LabFrame(1f, Deterministic: false));
        float early = page.Total;
        page.Compute(new LabFrame(4f, Deterministic: false));

        Assert.NotEqual(early, page.Total);
        Assert.True(page.Check().Passed);

        // The window's picture carries the two times; the deterministic one does not.
        canvas.ClearText();
        page.Draw(canvas, new LabFrame(4f, Deterministic: false));
        canvas.ComposeText();
        Rgba32[] live = canvas.ToArray();

        canvas.ClearText();
        page.Draw(canvas, new LabFrame(4f, Deterministic: true));
        canvas.ComposeText();

        Assert.NotEqual(live, canvas.ToArray());
    }

    // ---- prefix sum -------------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(65_536)]
    [InlineData(65_537)]
    [InlineData(100_003)]
    public void Scan_IsTheExclusivePrefixSum_AtAnyCount(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(count);
        int[] values = Enumerable.Range(0, count).Select(_ => random.Next(-50, 50)).ToArray();
        using ReadWriteBuffer<int> input = device.AllocateReadWriteBuffer(values);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(count);
        using GpuScan scan = new(device, count);

        int total = scan.Run(input, output, count);

        int[] expected = new int[count];
        Assert.Equal(CpuScan.Exclusive(values, expected), total);
        Assert.Equal(expected, output.ToArray());
        Assert.Equal(values, input.ToArray());
    }

    [MetalFact]
    public void Scan_OfFewerValuesThanItsCapacity_LeavesTheRestAlone()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] values = Enumerable.Range(1, 70_000).ToArray();
        using ReadWriteBuffer<int> input = device.AllocateReadWriteBuffer(values);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(values.Length);
        using GpuScan scan = new(device, values.Length);

        output.AsSpan().Fill(-1);
        Assert.Equal(300 * 301 / 2, scan.Run(input, output, 300));

        int[] result = output.ToArray();
        Assert.Equal(Enumerable.Range(0, 300).Select(static i => i * (i + 1) / 2), result[..300]);
        Assert.All(result[300..], static v => Assert.Equal(-1, v));
        Assert.Equal(0, scan.Run(input, output, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => scan.Run(input, output, values.Length + 1));
    }

    [MetalFact]
    public void PrefixSum_KeepsExactlyThePassingElements_InOrder_AndTheThresholdMovesTheCount()
    {
        using PrefixSumPage page = new(GraphicsDevice.GetDefault());

        page.Compute(Frame);
        int kept = page.Kept;
        Assert.InRange(kept, PrefixSumPage.Count / 10, PrefixSumPage.Count * 9 / 10);

        LabCheck check = page.Check();
        Assert.True(check.Passed, check.Summary);
        Assert.Contains(kept.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), check.Summary);
        Assert.Contains("in index order", check.Summary);

        // A higher threshold keeps fewer elements, a lower one more; other keys are not the page's.
        uint cutoff = page.Cutoff;
        Assert.True(page.OnKeyDown(Key.Up));
        Assert.True(page.Cutoff > cutoff);
        page.Compute(Frame);
        Assert.True(page.Kept < kept);
        Assert.True(page.Check().Passed);

        Assert.True(page.OnKeyDown(Key.Down));
        Assert.True(page.OnKeyDown(Key.Down));
        page.Compute(Frame);
        Assert.True(page.Kept > kept);
        Assert.True(page.Check().Passed);
        Assert.False(page.OnKeyDown(Key.V));

        for (int i = 0; i < 40; i++)
        {
            page.OnKeyDown(Key.Up);
        }

        Assert.Equal(60000u, page.Cutoff); // clamped
    }

    // ---- matrix product ---------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(50)]
    [InlineData(129)]
    public void MatMul_TiledAndNaive_AgreeWithC_AtAnySize(int n)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(n);
        float[] a = Enumerable.Range(0, n * n).Select(_ => (float)((random.NextDouble() * 2) - 1)).ToArray();
        float[] b = Enumerable.Range(0, n * n).Select(_ => (float)((random.NextDouble() * 2) - 1)).ToArray();
        using ReadWriteBuffer<float> bufferA = device.AllocateReadWriteBuffer(a);
        using ReadWriteBuffer<float> bufferB = device.AllocateReadWriteBuffer(b);
        using ReadWriteBuffer<float> tiled = device.AllocateReadWriteBuffer<float>(n * n);
        using ReadWriteBuffer<float> naive = device.AllocateReadWriteBuffer<float>(n * n);

        int size = (n + MatMulTiled.Tile - 1) / MatMulTiled.Tile * MatMulTiled.Tile;
        device.For(size, size, new MatMulTiled(bufferA, bufferB, tiled, n));
        device.For(n, n, new MatMulNaive(bufferA, bufferB, naive, n));

        float[] gpuTiled = tiled.ToArray(), gpuNaive = naive.ToArray();
        float[] cpu = new float[n];
        double[] exact = new double[n], magnitude = new double[n];

        for (int row = 0; row < n; row++)
        {
            CpuMatMul.Row(a, b, n, row, cpu);
            CpuMatMul.RowExact(a, b, n, row, exact, magnitude);

            for (int column = 0; column < n; column++)
            {
                double tolerance = (1e-4 * magnitude[column]) + 1e-5;
                Assert.True(Math.Abs(gpuTiled[(row * n) + column] - exact[column]) <= tolerance, $"tiled ({row}, {column})");
                Assert.True(Math.Abs(gpuNaive[(row * n) + column] - exact[column]) <= tolerance, $"naive ({row}, {column})");
                Assert.True(Math.Abs(cpu[column] - exact[column]) <= tolerance, $"C# floats ({row}, {column})");
            }
        }
    }

    [MetalFact]
    public void MatMul_BothVariantsMatchTheCpu_AndTheProductIsOnlyRecomputedWhenTheMatricesMove()
    {
        using MatMulPage page = new(GraphicsDevice.GetDefault());

        page.Compute(Frame);
        LabCheck tiled = page.Check();
        Assert.True(tiled.Passed, tiled.Summary);
        Assert.True(page.MaxError < 0.05, $"max error {page.MaxError}");
        Assert.True(page.OnKeyDown(Key.V));
        page.Compute(Frame);
        Assert.True(page.Check().Passed);
        Assert.False(page.OnKeyDown(Key.Up));

        // In the window the matrices move four times a second; in between, a frame costs nothing.
        page.Compute(new LabFrame(1.01f, Deterministic: false));
        double first = page.GpuMilliseconds;
        page.Compute(new LabFrame(1.2f, Deterministic: false));
        Assert.Equal(first, page.GpuMilliseconds);
        page.Compute(new LabFrame(1.3f, Deterministic: false));
        Assert.NotEqual(first, page.GpuMilliseconds);
        Assert.True(page.Check().Passed);
    }

    // ---- blur -------------------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(128, 64, 8)]
    [InlineData(100, 70, 1)]
    [InlineData(129, 127, 16)]
    [InlineData(300, 5, 64)]
    [InlineData(1, 1, 3)]
    [InlineData(50, 200, 64)]
    public void Blur_TiledAndNaive_AgreeWithC_AtAnySizeAndRadius(int width, int height, int radius)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(width + height + radius);
        float[] image = Enumerable.Range(0, width * height).Select(_ => (float)random.NextDouble()).ToArray();
        using ReadWriteBuffer<float> source = device.AllocateReadWriteBuffer(image);
        using ReadWriteBuffer<float> rows = device.AllocateReadWriteBuffer<float>(image.Length);
        using ReadWriteBuffer<float> tiled = device.AllocateReadWriteBuffer<float>(image.Length);
        using ReadWriteBuffer<float> naive = device.AllocateReadWriteBuffer<float>(image.Length);

        int group = BlurRowsShared.Group;
        device.For((width + group - 1) / group * group, height, new BlurRowsShared(source, rows, width, height, radius));
        device.For(width, (height + group - 1) / group * group, new BlurColumnsShared(rows, tiled, width, height, radius));
        device.For(width, height, new BlurNaive(source, rows, width, height, radius, 1, 0));
        device.For(width, height, new BlurNaive(rows, naive, width, height, radius, 0, 1));

        float[] cpuRows = new float[image.Length], cpu = new float[image.Length];
        CpuBlur.Pass(image, cpuRows, width, height, radius, horizontal: true);
        CpuBlur.Pass(cpuRows, cpu, width, height, radius, horizontal: false);

        float[] gpuTiled = tiled.ToArray(), gpuNaive = naive.ToArray();

        for (int i = 0; i < cpu.Length; i++)
        {
            Assert.True(Math.Abs(gpuTiled[i] - cpu[i]) <= 1e-4f, $"tiled pixel {i}: {gpuTiled[i]} vs {cpu[i]}");
            Assert.True(Math.Abs(gpuNaive[i] - cpu[i]) <= 1e-4f, $"naive pixel {i}: {gpuNaive[i]} vs {cpu[i]}");
        }

        // A blur of values in 0..1 stays in 0..1 and, away from flat input, changes it.
        Assert.All(cpu, static v => Assert.InRange(v, 0f, 1.0001f));
    }

    [MetalFact]
    public void Blur_BothVariantsMatchTheCpu_AndTheRadiusKeysStayWithinTheTile()
    {
        using BlurPage page = new(GraphicsDevice.GetDefault());

        Assert.Equal(64, BlurPage.MaxRadius);
        Assert.Equal(16, page.Radius);
        page.Compute(Frame);
        LabCheck check = page.Check();
        Assert.True(check.Passed, check.Summary);
        Assert.Contains("radius 16", check.Summary);

        Assert.True(page.OnKeyDown(Key.V));
        page.Compute(Frame);
        Assert.True(page.Check().Passed);

        for (int i = 0; i < 4; i++)
        {
            Assert.True(page.OnKeyDown(Key.Up));
        }

        Assert.Equal(BlurPage.MaxRadius, page.Radius); // clamped: the apron must fit the tile
        page.Compute(Frame);
        Assert.True(page.Check().Passed);
        Assert.True(page.OnKeyDown(Key.V));
        page.Compute(Frame);
        Assert.True(page.Check().Passed);

        for (int i = 0; i < 9; i++)
        {
            Assert.True(page.OnKeyDown(Key.Down));
        }

        Assert.Equal(1, page.Radius);
        page.Compute(Frame);
        Assert.True(page.Check().Passed);
        Assert.False(page.OnKeyDown(Key.Left));
    }

    [Fact]
    public void GigaFlops_AreTwoOperationsPerTermOverTheTime()
    {
        Assert.Equal(2.0 * 1024 * 1024 * 1024, MatMulPage.Flops);
        Assert.Equal(MatMulPage.Flops / 1e9, MatMulPage.GigaFlops(1000), 9);
        Assert.Equal(MatMulPage.Flops / 1e7, MatMulPage.GigaFlops(10), 9);
        Assert.Equal(0, MatMulPage.GigaFlops(0));
    }
}
