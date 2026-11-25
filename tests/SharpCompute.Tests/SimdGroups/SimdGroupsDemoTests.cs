using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompute.Samples.ComputeLab;
using SharpCompute.Samples.ComputeLab.Pages;
using SharpCompute.Samples.SimdGroups;
using SharpCompute.Samples.SimdGroups.Pages;

// ComputeLab has a CompactionPage (compaction by an atomic counter) and a Program of its own; these tests mean this sample's.
using CompactionPage = SharpCompute.Samples.SimdGroups.Pages.CompactionPage;
using Program = SharpCompute.Samples.SimdGroups.Program;

namespace SharpCompute.Tests.SimdGroups;

/// <summary>
/// The SimdGroups demo: every page's GPU result against its CPU reference for every variant, the
/// properties that make each page's picture what it claims to be, and the headless mode. The pages
/// are ComputeLab pages; its canvas and font are covered by the ComputeLab tests.
/// </summary>
public class SimdGroupsDemoTests
{
    private static LabFrame Frame(float time) => new(time, Deterministic: true);

    private static void Dispose(IEnumerable<ILabPage> pages)
    {
        foreach (ILabPage page in pages)
        {
            page.Dispose();
        }
    }

    [Fact]
    public void TimingLine_SaysWhichSideIsFaster_AndLeavesTheCoresToTheCheckLine()
    {
        Assert.Equal("GPU 0.50 ms   CPU 5.00 ms   GPU is 10.0x faster", SimdGroupsView.Describe(new LabTiming(0.5, 5)));
        Assert.Equal("GPU 3.00 ms   CPU 1.50 ms   CPU is 2.0x faster", SimdGroupsView.Describe(new LabTiming(3, 1.5)));
        Assert.Equal("GPU 0.00 ms   CPU 1.50 ms", SimdGroupsView.Describe(new LabTiming(0, 1.5)));
    }

    [MetalFact]
    public void EveryPage_EveryVariant_MatchesItsCpuReference()
    {
        List<ILabPage> pages = SimdGroupsPages.Create(GraphicsDevice.GetDefault());

        try
        {
            Assert.Equal(["Reduction", "Compaction", "Shuffle"], pages.Select(static p => p.Name));

            foreach (ILabPage page in pages)
            {
                Assert.NotEmpty(page.Variants);
                Assert.NotEmpty(page.Feature);

                for (int variant = 0; variant < page.Variants.Count; variant++)
                {
                    page.Variant = variant;

                    foreach (float time in new[] { 0f, SimdGroupsHeadless.Time, 7.25f })
                    {
                        page.Compute(Frame(time));
                        LabCheck check = page.Check();

                        Assert.True(check.Passed, $"{page.Name}, {page.Variants[variant]}, t = {time}: {check.Summary}");

                        // Every check also runs the page's kernels with ForCpu, on a sample of its input.
                        Assert.EndsWith("; ForCpu too", check.Summary);
                        Assert.True(page.GpuMilliseconds > 0 && check.CpuMilliseconds >= 0);
                    }
                }
            }
        }
        finally
        {
            Dispose(pages);
        }
    }

    // ---- the pages' kernels under ForCpu: the GPU and the CPU, kernel for kernel ----------------------

    [MetalFact]
    public void ReductionKernels_OnTheCpu_SumWhatTheGpuSums()
    {
        const int count = 4096;
        Random random = new(4);
        float[] input = Enumerable.Range(0, count).Select(_ => random.NextSingle()).ToArray();
        double reference = CpuReduction.Double(input);

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<float> partials = device.AllocateReadWriteBuffer<float>(count / 32);
        using ReadWriteBuffer<float> totals = device.AllocateReadWriteBuffer<float>(6);

        // Slots 0..2 on the GPU, 3..5 on the CPU: SIMD only, SIMD + per-group partial, thread loops.
        device.For(count, new SumLanes(inputBuffer, partials));
        device.For(32, new FoldInOneGroup(partials, totals, count / 32, 0));
        device.For(count / 128, new SumStripsAndLanes(inputBuffer, partials, 128));
        device.For(32, new FoldInOneGroup(partials, totals, 1, 1));
        device.For(4, new SumStrips(inputBuffer, partials, 1024));
        device.For(1, new SumSerial(partials, totals, 4, 2));

        device.ForCpu(count, new SumLanes(inputBuffer, partials));
        float[] cpuPartials = partials.ToArray();
        device.ForCpu(32, new FoldInOneGroup(partials, totals, count / 32, 3));
        device.ForCpu(count / 128, new SumStripsAndLanes(inputBuffer, partials, 128));
        device.ForCpu(32, new FoldInOneGroup(partials, totals, 1, 4));
        device.ForCpu(4, new SumStrips(inputBuffer, partials, 1024));
        device.ForCpu(1, new SumSerial(partials, totals, 4, 5));

        Assert.All(totals.ToArray(), total => Assert.InRange(total, reference * (1 - ReductionPage.Tolerance), reference * (1 + ReductionPage.Tolerance)));

        // On the CPU the lanes are added in lane order, so a partial is the plain float sum of its 32 floats.
        for (int group = 0; group < count / 32; group++)
        {
            Assert.Equal(input.AsSpan(group * 32, 32).ToArray().Aggregate(0f, static (a, b) => a + b), cpuPartials[group]);
        }
    }

    [MetalTheory]
    [InlineData(1024, 3000, 3000, 2500)]
    [InlineData(2048, 8000, 8000, 6000)]
    [InlineData(1024, 0, 0, 1)]         // nothing is kept
    [InlineData(1024, 8000, 8000, 30000)] // everything is
    public void CompactionKernels_OnTheCpu_PackTheParticlesTheGpuPacks(int count, int centerX, int centerY, int radius)
    {
        Random random = new(count + radius);
        Int2[] particles = Enumerable.Range(0, count).Select(_ => new Int2(random.Next(CompactionPage.Units), random.Next(CompactionPage.Units))).ToArray();
        int groups = count / 32;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<Int2> particleBuffer = device.AllocateReadOnlyBuffer<Int2>(particles);

        (uint[] Ballots, int Kept, Int2[] Compacted) Run(bool cpu)
        {
            using ReadWriteBuffer<uint> ballots = device.AllocateReadWriteBuffer<uint>(groups);
            using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(groups);
            using ReadWriteBuffer<int> offsets = device.AllocateReadWriteBuffer<int>(groups);
            using ReadWriteBuffer<int> blockTotals = device.AllocateReadWriteBuffer<int>(32);
            using ReadWriteBuffer<int> blockOffsets = device.AllocateReadWriteBuffer<int>(32);
            using ReadWriteBuffer<int> kept = device.AllocateReadWriteBuffer<int>(1);
            using ReadWriteBuffer<Int2> compacted = device.AllocateReadWriteBuffer<Int2>(count);

            VoteInGroups vote = new(particleBuffer, ballots, counts, centerX, centerY, radius * radius);
            ScanCounts scanGroups = new(counts, offsets, blockTotals);
            ScanCounts scanBlocks = new(blockTotals, blockOffsets, kept);
            ScatterKept scatter = new(particleBuffer, ballots, offsets, blockOffsets, compacted);

            if (cpu)
            {
                device.ForCpu(count, vote);
                device.ForCpu(groups, scanGroups);
                device.ForCpu((groups + 31) / 32, scanBlocks);
                device.ForCpu(count, scatter);
            }
            else
            {
                device.For(count, vote);
                device.For(groups, scanGroups);
                device.For((groups + 31) / 32, scanBlocks);
                device.For(count, scatter);
            }

            return (ballots.ToArray(), kept[0], compacted.AsReadOnlySpan()[..kept[0]].ToArray());
        }

        (uint[] Ballots, int Kept, Int2[] Compacted) gpu = Run(cpu: false), onCpu = Run(cpu: true);
        Int2[] expected = particles.Where(p => (long)(p.X - centerX) * (p.X - centerX) + (long)(p.Y - centerY) * (p.Y - centerY) < (long)radius * radius).ToArray();

        Assert.Equal(expected.Length, onCpu.Kept);
        Assert.Equal(expected, onCpu.Compacted);
        Assert.Equal(gpu.Kept, onCpu.Kept);
        Assert.Equal(gpu.Ballots, onCpu.Ballots);
        Assert.Equal(gpu.Compacted, onCpu.Compacted);
    }

    [MetalTheory]
    [InlineData(3, 100)]
    [InlineData(5, 100)]
    [InlineData(3, 33)]
    [InlineData(5, 34)]
    public void ShuffleKernels_OnTheCpu_FilterLikeTheGpu_AndExactlyLikeTheCSharpFilter(int taps, int count)
    {
        const int passes = 6;
        Random random = new(count);
        float[] signal = Enumerable.Range(0, count).Select(_ => random.NextSingle()).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();

        float[] Run(bool cpu)
        {
            ReadWriteBuffer<float> source = device.AllocateReadWriteBuffer<float>(signal), target = device.AllocateReadWriteBuffer<float>(count);

            try
            {
                for (int pass = 0; pass < passes; pass++)
                {
                    switch (taps, cpu)
                    {
                        case (3, false): device.For(count, new SmoothInGroups3(source, target)); break;
                        case (3, true): device.ForCpu(count, new SmoothInGroups3(source, target)); break;
                        case (_, false): device.For(count, new SmoothInGroups5(source, target)); break;
                        default: device.ForCpu(count, new SmoothInGroups5(source, target)); break;
                    }

                    (source, target) = (target, source);
                }

                return source.ToArray();
            }
            finally
            {
                source.Dispose();
                target.Dispose();
            }
        }

        float[] gpu = Run(cpu: false), onCpu = Run(cpu: true);
        float[] expected = new float[count], scratch = new float[count];
        CpuSmoothing.Run(signal, expected, scratch, passes, taps, ShufflePage.Lanes);

        // The kernel's C# is the filter's C#: the same floats. The GPU may fuse a multiply and an add.
        Assert.Equal(expected, onCpu);

        for (int i = 0; i < count; i++)
        {
            Assert.InRange(onCpu[i], gpu[i] - ShufflePage.Tolerance, gpu[i] + ShufflePage.Tolerance);
        }
    }

    [MetalFact]
    public void Reduction_AllThreeMethods_AgreeWithTheDoubleSum_AndTheSimdOnesAreCloser()
    {
        using ReductionPage page = new(GraphicsDevice.GetDefault());

        // A window frame runs all three methods.
        page.Compute(new LabFrame(0f, Deterministic: false));
        Assert.True(page.Check().Passed);

        Assert.Equal(3, page.Totals.Count);
        Assert.InRange(page.Reference, ReductionPage.Count * 0.49, ReductionPage.Count * 0.51);

        double[] errors = page.Totals.Select(total => Math.Abs(total - page.Reference)).ToArray();
        Assert.All(errors, error => Assert.True(error <= page.Reference * ReductionPage.Tolerance, $"off by {error}"));

        // 16M floats summed in serial strips of 4096 round far more than sums that fold 32 lanes at a
        // time: the page's point about accuracy. The last variant is the loops-only baseline.
        Assert.Equal("thread loops", page.Variants[2]);
        Assert.True(errors[0] < errors[2] && errors[1] < errors[2], $"errors: {string.Join(", ", errors)}");
    }

    [MetalTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Compaction_KeepsSomeButNotAllParticles_AndFollowsTheCircle(int variant)
    {
        using CompactionPage page = new(GraphicsDevice.GetDefault()) { Variant = variant };

        page.Compute(Frame(1f));
        Assert.True(page.Check().Passed);
        int kept = page.Kept;
        Int2[] first = page.Compacted.AsReadOnlySpan()[..kept].ToArray();

        Assert.InRange(kept, 1, CompactionPage.Count - 1);

        // The circle moves with time, so another set of particles is kept.
        page.Compute(Frame(5f));
        Assert.True(page.Check().Passed);
        Assert.False(first.AsSpan().SequenceEqual(page.Compacted.AsReadOnlySpan()[..Math.Min(kept, page.Kept)]) && kept == page.Kept);

        // The larger circle keeps more.
        page.Variant = 1 - variant;
        page.Compute(Frame(1f));
        Assert.True(variant == 0 ? page.Kept > kept : page.Kept < kept);
    }

    [MetalTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Shuffle_SmoothsInsideEachSimdGroup_SoAGroupKeepsItsMean(int variant)
    {
        using ShufflePage page = new(GraphicsDevice.GetDefault()) { Variant = variant };

        Assert.NotEqual(0, ShufflePage.Count % ShufflePage.Lanes); // the last group is partial, on purpose
        Assert.Equal(1, ShufflePage.PassesAt(0f));

        page.Compute(Frame(0f));
        Assert.True(page.Check().Passed);
        float[] onePass = page.Result.ToArray();

        page.Compute(Frame(SimdGroupsHeadless.Time));
        Assert.True(page.Check().Passed);
        Assert.True(page.Passes > 100);
        float[] manyPasses = page.Result.ToArray();

        Assert.False(onePass.AsSpan().SequenceEqual(manyPasses));

        // The filter clamps at the edges of a SIMD group, so nothing flows between groups: however many
        // passes run, each group's samples keep their sum. A filter that crossed the edges would not.
        double roughBefore = 0, roughAfter = 0;

        for (int start = 0; start < ShufflePage.Count; start += ShufflePage.Lanes)
        {
            int length = Math.Min(ShufflePage.Lanes, ShufflePage.Count - start);
            double before = onePass.AsSpan(start, length).ToArray().Sum(static v => (double)v);
            double after = manyPasses.AsSpan(start, length).ToArray().Sum(static v => (double)v);

            Assert.True(Math.Abs(before - after) < 5e-3, $"group at {start} ({length} lanes): {before} became {after}");

            for (int i = start + 1; i < start + length; i++)
            {
                roughBefore += Math.Abs(onePass[i] - onePass[i - 1]);
                roughAfter += Math.Abs(manyPasses[i] - manyPasses[i - 1]);
            }
        }

        // And inside a group it does smooth.
        Assert.True(roughAfter < roughBefore * 0.5, $"roughness {roughBefore} -> {roughAfter}");
    }

    [MetalFact]
    public void Headless_WritesAPngPerPageAndASummary_TheSameEveryRun()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpcompute-simdgroups-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "a"), second = Path.Combine(root, "b");
        GraphicsDevice device = GraphicsDevice.GetDefault();
        List<ILabPage> pages = SimdGroupsPages.Create(device);

        try
        {
            using StringWriter log = new();

            Assert.True(SimdGroupsHeadless.Run(device, pages, first, log));
            Assert.True(SimdGroupsHeadless.Run(device, pages, second, TextWriter.Null));

            string[] pictures = ["1-reduction.png", "2-compaction.png", "3-shuffle.png"];
            Assert.Equal(pictures, Enumerable.Range(0, pages.Count).Select(i => SimdGroupsHeadless.FileName(pages, i)));
            Assert.Equal([.. pictures, "summary.txt"], Directory.GetFiles(first).Select(Path.GetFileName).Order());

            foreach (string name in pictures)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(first, name));

                // A real picture, not a flat frame: a blank 1280 x 720 PNG compresses to a few kilobytes.
                Assert.True(bytes.Length > 20_000, $"{name} is only {bytes.Length} bytes.");
                Assert.True(bytes.AsSpan(1, 3).SequenceEqual("PNG"u8));
                Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(second, name))), $"{name} differs between two runs.");
            }

            // Every variant of every page is checked and timed in the summary.
            string summary = File.ReadAllText(Path.Combine(first, "summary.txt"));
            Assert.Equal(summary, log.ToString());
            Assert.Contains("All GPU results match the CPU.", summary);
            Assert.DoesNotContain("FAIL", summary);
            Assert.Equal(pages.Sum(static p => p.Variants.Count), summary.Split('\n').Count(static line => line.TrimStart().StartsWith("PASS", StringComparison.Ordinal)));
            Assert.All(pages, page => Assert.All(page.Variants, variant => Assert.Contains(variant, summary)));
        }
        finally
        {
            Dispose(pages);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [MetalTheory]
    [InlineData(0, "--help")]
    [InlineData(0, "--list")]
    [InlineData(1, "--bogus")]
    [InlineData(1, "--frames", "0")]
    [InlineData(1, "--page")]
    [InlineData(1, "--page", "nope", "--headless")]
    public void Main_HandlesItsArguments_WithoutOpeningAWindow(int exitCode, params string[] arguments)
    {
        Assert.Equal(exitCode, Program.Main(arguments));
    }
}
