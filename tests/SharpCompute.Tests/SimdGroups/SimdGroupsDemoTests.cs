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
