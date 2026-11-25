using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompute.Presentation;
using SharpCompute.Samples.ComputeLab;
using SharpCompute.Samples.ComputeLab.Pages;

namespace SharpCompute.Tests.ComputeLab;

/// <summary>
/// The ComputeLab sample without its window: every page renders, is not blank, agrees with the CPU,
/// and the headless pictures are the same on every run.
/// </summary>
public sealed class ComputeLabTests
{
    private static readonly LabFrame Frame = new(Headless.Time, Deterministic: true);

    private static void DisposeAll(IEnumerable<ILabPage> pages)
    {
        foreach (ILabPage page in pages)
        {
            page.Dispose();
        }
    }

    // ---- the font and the canvas ------------------------------------------------------------------

    [Fact]
    public void Font_CoversWhatThePagesWrite()
    {
        Assert.Equal(0ul, PixelFont.Mask(PixelFont.Index(' ')));
        Assert.Equal(PixelFont.Count * 2, PixelFont.Masks.Length);

        foreach (char c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ.,:;!?-+*/=<>()[]%#'_|&^~")
        {
            int glyph = PixelFont.Index(c);
            Assert.NotEqual(0, glyph);
            Assert.NotEqual(0ul, PixelFont.Mask(glyph));
            Assert.True(PixelFont.Mask(glyph) < 1ul << (PixelFont.GlyphWidth * PixelFont.GlyphHeight), $"'{c}' has bits outside 5 x 7");
        }

        Assert.Equal(PixelFont.Index('X'), PixelFont.Index('x'));
        Assert.Equal('X', PixelFont.Character(PixelFont.Index('x')));
        Assert.Equal(' ', PixelFont.Character(0));
        Assert.Equal(' ', PixelFont.Character(PixelFont.Count));
        Assert.Equal(' ', PixelFont.Character(-1));
        Assert.Equal(0, PixelFont.Index('§'));
        Assert.Equal(PixelFont.Count, Enumerable.Range(0, PixelFont.Count).Select(PixelFont.Mask).Distinct().Count());

        // Top-left pixel is bit 0: 'L' is a left column and a bottom row.
        ulong l = PixelFont.Mask(PixelFont.Index('L'));
        Assert.Equal(1ul, l & 0b11111);
        Assert.Equal(0b11111ul, l >> 30);
    }

    [Fact]
    public void Canvas_GridFillsTheFrame()
    {
        Assert.Equal(12, LabCanvas.CellWidth);
        Assert.Equal(16, LabCanvas.CellHeight);
        Assert.Equal(106, LabCanvas.Columns);
        Assert.Equal(45, LabCanvas.Rows);
        Assert.True(LabView.FirstPageRow < LabCanvas.Rows - 1);
    }

    [MetalFact]
    public void Canvas_DrawsGlyphsAtTwiceTheirSize_OverADarkenedBacking()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using LabCanvas canvas = new(device);

        canvas.Pixels.AsSpan().Fill(0xFFFFFFFFu); // a white frame
        canvas.Text(2, 1, "L!", TextColor.White);
        canvas.Text(LabCanvas.Columns - 1, LabCanvas.Rows - 1, "ZZ", TextColor.Good); // the second Z falls off the edge
        canvas.Text(0, LabCanvas.Rows, "off", TextColor.Bad); // below the last row: ignored
        canvas.ComposeText();

        Rgba32[] pixels = canvas.ToArray();
        Rgba32 At(int x, int y) => pixels[(y * LabCanvas.Width) + x];

        // Outside every used cell the frame is untouched.
        Assert.Equal(new Rgba32(255, 255, 255, 255), At(0, 0));
        Assert.Equal(new Rgba32(255, 255, 255, 255), At((4 * LabCanvas.CellWidth) + 1, LabCanvas.CellHeight + 1));

        // Inside: a set font pixel is the text color, anything else is the frame at 22 %.
        ulong mask = PixelFont.Mask(PixelFont.Index('L'));
        int originX = 2 * LabCanvas.CellWidth, originY = LabCanvas.CellHeight;

        for (int y = 0; y < LabCanvas.CellHeight; y++)
        {
            for (int x = 0; x < LabCanvas.CellWidth; x++)
            {
                int gx = x / LabCanvas.TextScale, gy = y / LabCanvas.TextScale;
                bool lit = gx < PixelFont.GlyphWidth && gy < PixelFont.GlyphHeight && ((mask >> ((gy * PixelFont.GlyphWidth) + gx)) & 1) != 0;
                Assert.Equal(lit ? new Rgba32(255, 255, 255, 255) : new Rgba32(56, 56, 56, 255), At(originX + x, originY + y));
            }
        }

        // The green text in the very last cell: its top row is all set for 'Z'.
        Rgba32 green = At(((LabCanvas.Columns - 1) * LabCanvas.CellWidth) + 2, (LabCanvas.Rows - 1) * LabCanvas.CellHeight);
        Assert.True(green.G > green.R && green.G > green.B, green.ToString());

        // The texture the window presents holds exactly the buffer's pixels.
        Assert.Equal(pixels, canvas.ToTexture().ToArray());

        canvas.ClearText();
        canvas.Pixels.AsSpan().Fill(0xFF000000u);
        canvas.ComposeText();
        Assert.All(canvas.ToArray(), static p => Assert.Equal(new Rgba32(0, 0, 0, 255), p));
    }

    [Fact]
    public void Timing_SaysWhichSideIsFaster()
    {
        Assert.Equal("GPU 0.50 ms   CPU 5.00 ms (all cores)   GPU is 10.0x faster", new LabTiming(0.5, 5).ToString());
        Assert.Equal("GPU 3.00 ms   CPU 1.50 ms (all cores)   CPU is 2.0x faster", new LabTiming(3, 1.5).ToString());
        Assert.Equal("GPU 0.00 ms   CPU 1.50 ms (all cores)", new LabTiming(0, 1.5).ToString());
    }

    [Fact]
    public void Timing_ComparesThePagesKernelWithItsBaseline_WhenThereIsOne()
    {
        Assert.Null(new LabTiming(0.5, 5).BaselineText);

        LabTiming faster = new(0.5, 5) { Baseline = new LabBaseline("global atomics", 2) };
        Assert.Equal("global atomics: GPU 2.00 ms   this page's kernel is 4.0x faster", faster.BaselineText);
        Assert.Equal("GPU 0.50 ms   CPU 5.00 ms (all cores)   GPU is 10.0x faster", faster.ToString()); // the timing line itself is unchanged

        LabTiming slower = new(3, 5) { Baseline = new LabBaseline("global atomics", 1.5) };
        Assert.Equal("global atomics: GPU 1.50 ms   this page's kernel is 2.0x slower", slower.BaselineText);

        Assert.Equal("other: GPU 0.00 ms", new LabTiming(3, 5) { Baseline = new LabBaseline("other", 0) }.BaselineText);
    }

    [Fact]
    public void PageNames_BecomeFileNamesAndCommandLineWords()
    {
        Assert.Equal("histogram", LabPages.Slug("Histogram"));
        Assert.Equal("histogram-privatised", LabPages.Slug("Histogram: privatised"));
        Assert.Equal("a-b-c9", LabPages.Slug("  A / b -- C9!?"));
        Assert.Equal(string.Empty, LabPages.Slug(": :"));
    }

    // ---- the pages --------------------------------------------------------------------------------

    [MetalFact]
    public void Pages_AreTheSevenOfThisLab_EachWithAtLeastOneVariant()
    {
        List<ILabPage> pages = LabPages.Create(GraphicsDevice.GetDefault());

        try
        {
            // The privatised histogram sits next to the one it is compared with.
            Assert.Equal(["Histogram", "Histogram: privatised", "Compaction", "Bits", "LocalArrays", "Indirect", "FastMath"], pages.Select(static p => p.Name));
            Assert.Equal(
                ["atomics", "threadgroup atomics", "atomics", "bit and packing intrinsics", "fixed-size local arrays", "indirect dispatch", "per-shader compile options"],
                pages.Select(static p => p.Feature));
            Assert.All(pages, static p => Assert.NotEmpty(p.Variants));
            Assert.All(pages, static p => Assert.Equal(0, p.Variant));
            Assert.Equal(
                ["1-histogram.png", "2-histogram-privatised.png", "3-compaction.png", "4-bits.png", "5-localarrays.png", "6-indirect.png", "7-fastmath.png"],
                Enumerable.Range(0, pages.Count).Select(i => Headless.FileName(pages, i)));

            // Only the privatised page times a second dispatch.
            Assert.Equal([false, true, false, false, false, false, false], pages.Select(static p => p.Baseline is not null));

            // --page takes the name or the file-name form of it, in any case.
            Assert.Equal(1, LabPages.Find(pages, "Histogram: privatised"));
            Assert.Equal(1, LabPages.Find(pages, "histogram-privatised"));
            Assert.Equal(1, LabPages.Find(pages, "HISTOGRAM-PRIVATISED"));
            Assert.Equal(0, LabPages.Find(pages, "histogram"));
            Assert.Equal(3, LabPages.Find(pages, "Bits"));
            Assert.Equal(4, LabPages.Find(pages, "localarrays"));
            Assert.Equal(6, LabPages.Find(pages, "fastmath"));
            Assert.Equal(-1, LabPages.Find(pages, "privatised"));
        }
        finally
        {
            DisposeAll(pages);
        }
    }

    [MetalFact]
    public void EveryPage_Renders_IsNotBlank_AndMatchesTheCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        List<ILabPage> pages = LabPages.Create(device);
        using LabCanvas canvas = new(device);

        try
        {
            for (int index = 0; index < pages.Count; index++)
            {
                ILabPage page = pages[index];

                for (int variant = page.Variants.Count - 1; variant >= 0; variant--)
                {
                    page.Variant = variant;
                    page.Compute(Frame);
                    LabCheck check = page.Check();

                    Assert.True(check.Passed, $"{page.Name} ({page.Variants[variant]}): {check.Summary}");
                    Assert.NotEmpty(check.Summary);
                    Assert.True(page.GpuMilliseconds > 0 && check.CpuMilliseconds > 0, $"{page.Name}: timings must be measured");

                    LabView.Compose(canvas, pages, index, Frame, check, timing: null);
                    Rgba32[] pixels = canvas.ToArray();

                    // Opaque everywhere, many colors, and a good part of the frame is not background.
                    Assert.All(pixels, static p => Assert.Equal(255, p.A));
                    Assert.True(pixels.Distinct().Count() > 100, $"{page.Name}: only {pixels.Distinct().Count()} colors");
                    int lit = pixels.Count(static p => p.R + p.G + p.B > 150);
                    Assert.True(lit > pixels.Length / 10, $"{page.Name}: only {lit} of {pixels.Length} pixels are lit");

                    // The lab's own text is there: the check line is green where its glyphs are.
                    int greenText = 0;

                    for (int y = LabCanvas.CellHeight; y < 2 * LabCanvas.CellHeight; y++)
                    {
                        for (int x = 0; x < LabCanvas.Width; x++)
                        {
                            Rgba32 p = pixels[(y * LabCanvas.Width) + x];
                            greenText += p.G > 200 && p.R < 150 && p.B < 180 ? 1 : 0;
                        }
                    }

                    Assert.True(greenText > 500, $"{page.Name}: the 'CHECK PASSED' line is missing ({greenText} green pixels)");
                }
            }
        }
        finally
        {
            DisposeAll(pages);
        }
    }

    [MetalFact]
    public void HeadlessPictures_AreTheSameOnEveryRun_AndTheSummaryReportsEveryCheck()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string root = Path.Combine(Path.GetTempPath(), "sharpcompute-computelab-" + Guid.NewGuid().ToString("N"));

        try
        {
            var files = new List<Dictionary<string, byte[]>>();

            for (int run = 0; run < 2; run++)
            {
                string directory = Path.Combine(root, run.ToString(System.Globalization.CultureInfo.InvariantCulture));
                List<ILabPage> pages = LabPages.Create(device);
                using StringWriter log = new();

                try
                {
                    Assert.True(Headless.Run(device, pages, directory, log), log.ToString());
                }
                finally
                {
                    DisposeAll(pages);
                }

                string summary = File.ReadAllText(Path.Combine(directory, "summary.txt"));
                Assert.Equal(summary, log.ToString());
                Assert.Contains("All GPU results match the CPU.", summary);
                Assert.DoesNotContain("FAIL", summary);
                Assert.Equal(13, summary.Split('\n').Count(static line => line.TrimStart().StartsWith("PASS", StringComparison.Ordinal))); // both histograms, local arrays and fast math twice, indirect three times
                Assert.Contains("256 bins", summary);
                Assert.Contains("4 bins (64x the contention)", summary);
                Assert.Contains("indirect: compact, dispatch the survivors", summary);
                Assert.Contains("compact, dispatch all 1,048,576 threads", summary);
                Assert.Contains("no compaction: every thread tests itself", summary);
                Assert.Contains("safe math (the default)", summary);
                Assert.Contains("[ShaderOptions(FastMath = true)]", summary);
                Assert.Contains("median 3x3 (sorting network)", summary);
                Assert.Contains("gaussian 5x5 (weight table)", summary);
                Assert.Equal(13, summary.Split('\n').Count(static line => line.Contains("GPU ", StringComparison.Ordinal) && line.Contains(" ms   CPU ", StringComparison.Ordinal)));

                // The privatised histogram is also timed against the global-atomics one, at both bin counts.
                Assert.Contains("[2/7] Histogram: privatised: threadgroup atomics", summary);
                Assert.Equal(2, summary.Split('\n').Count(static line => line.TrimStart().StartsWith("global atomics: GPU ", StringComparison.Ordinal) && line.Contains("this page's kernel is ", StringComparison.Ordinal)));

                string[] pictures = ["1-histogram.png", "2-histogram-privatised.png", "3-compaction.png", "4-bits.png", "5-localarrays.png", "6-indirect.png", "7-fastmath.png"];
                Assert.Equal(pictures.Append("summary.txt").Order(), Directory.GetFiles(directory).Select(Path.GetFileName).Order());
                Assert.All(pictures, name => Assert.Contains(name, summary));
                files.Add(pictures.ToDictionary(static name => name, name => File.ReadAllBytes(Path.Combine(directory, name))));
            }

            foreach ((string name, byte[] first) in files[0])
            {
                // A PNG, 1280 x 720, and byte for byte the one the second run wrote: no timing and no
                // thread-scheduling order ends up in a picture.
                Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], first[..8]);
                Assert.Equal([0, 0, 5, 0, 0, 0, 2, 0xD0], first[16..24]);
                Assert.True(first.Length > 20_000, $"{name} is suspiciously small ({first.Length} bytes)");
                Assert.True(first.AsSpan().SequenceEqual(files[1][name]), $"{name} differs between two runs");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [MetalFact]
    public void Histogram_FourBins_AreTheFoldOfThe256()
    {
        using HistogramPage page = new(GraphicsDevice.GetDefault());

        page.Compute(Frame);
        int[] fine = page.Counts.ToArray();
        Assert.Equal(256, fine.Length);
        Assert.Equal(HistogramPage.Size * HistogramPage.Size, fine.Sum());
        Assert.True(fine.Count(static c => c > 0) > 100, "the image should spread over most of the luminance range");

        // The contention toggle: same image, same million adds, 64 times fewer counters.
        Assert.True(page.OnKeyDown(Key.C));
        Assert.Equal(4, page.Bins);
        page.Compute(Frame);
        int[] coarse = page.Counts.ToArray();

        Assert.Equal(Enumerable.Range(0, 4).Select(b => fine.Skip(b * 64).Take(64).Sum()), coarse);
        Assert.True(page.Check().Passed);

        Assert.False(page.OnKeyDown(Key.X));
        Assert.True(page.OnKeyDown(Key.C));
        Assert.Equal(256, page.Bins);
    }

    [MetalFact]
    public void Histogram_FollowsTheAnimation()
    {
        using HistogramPage page = new(GraphicsDevice.GetDefault());

        page.Compute(new LabFrame(1f, Deterministic: false));
        int[] early = page.Counts.ToArray();
        page.Compute(new LabFrame(4f, Deterministic: false));

        Assert.NotEqual(early, page.Counts.ToArray());
        Assert.True(page.Check().Passed);
    }

    [MetalFact]
    public void PrivatisedHistogram_CountsWhatTheGlobalOneCounts_AtBothBinCounts()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using PrivatisedHistogramPage page = new(device);
        using HistogramPage global = new(device);

        page.Compute(Frame);
        global.Compute(Frame);
        int[] fine = page.Counts.ToArray();

        // The same image as the Histogram page, counted per threadgroup and merged: the same bins.
        Assert.Equal(256, fine.Length);
        Assert.Equal(PrivatisedHistogramPage.Size * PrivatisedHistogramPage.Size, fine.Sum());
        Assert.Equal(global.Counts.ToArray(), fine);
        Assert.Equal(fine, page.GlobalCounts.ToArray());

        // Both dispatches were timed, and the global one is the page's baseline.
        Assert.True(page.GpuMilliseconds > 0 && page.GlobalGpuMilliseconds > 0);
        Assert.Equal(new LabBaseline("global atomics", page.GlobalGpuMilliseconds), page.Baseline);

        LabCheck check = page.Check();
        Assert.True(check.Passed, check.Summary);
        Assert.Contains("256 bins: privatised = global atomics = CPU", check.Summary);
        Assert.True(check.CpuMilliseconds > 0);

        // The contention toggle: the same image and the same million adds, on 4 bins.
        Assert.True(page.OnKeyDown(Key.C));
        Assert.Equal(4, page.Bins);
        page.Compute(Frame);
        int[] coarse = page.Counts.ToArray();

        Assert.Equal(Enumerable.Range(0, 4).Select(b => fine.Skip(b * 64).Take(64).Sum()), coarse);
        Assert.Equal(coarse, page.GlobalCounts.ToArray());
        check = page.Check();
        Assert.True(check.Passed, check.Summary);
        Assert.Contains("4 bins: privatised", check.Summary);

        Assert.False(page.OnKeyDown(Key.X));
        Assert.True(page.OnKeyDown(Key.C));
        Assert.Equal(256, page.Bins);

        // It follows the animation like the page next to it.
        page.Compute(new LabFrame(4f, Deterministic: false));
        Assert.NotEqual(fine, page.Counts.ToArray());
        Assert.True(page.Check().Passed);
    }

    [MetalFact]
    public void PrivatisedHistogram_Kernel_HasAtomicsOnBothKindsOfMemory_SoItIsNotSplit()
    {
        static (bool Atomics, bool DeviceAtomics, bool Threadgroup) Read<T>()
            where T : struct, IComputeShaderDescriptor<T> => (T.UsesAtomics, T.UsesDeviceAtomics, T.UsesThreadgroupMemory);

        // The per-group counts are atomics on threadgroup memory; the merge is an atomic on the buffer.
        Assert.Equal((true, true, true), Read<PrivatisedLuminanceHistogram>());
        Assert.Equal((true, true, false), Read<LuminanceHistogram>());

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> image = device.AllocateReadWriteBuffer<uint>(4096);
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(256);

        Assert.Throws<NotSupportedException>(() => device.ForSplit(4096, new PrivatisedLuminanceHistogram(image, counts, 0), 0.5));
        Assert.All(counts.ToArray(), static count => Assert.Equal(0, count));

        // Every pixel is 0: luminance 0, so all 4,096 land in bin 0, on the GPU and on the CPU.
        device.For(4096, new PrivatisedLuminanceHistogram(image, counts, 0));
        device.ForCpu(4096, new PrivatisedLuminanceHistogram(image, counts, 6));
        Assert.Equal(8192, counts[0]);
        Assert.Equal(8192, counts.ToArray().Sum());
        Assert.Equal(0u, PrivatisedLuminanceHistogram.Luminance(0xFF000000u));
        Assert.Equal(255u, PrivatisedLuminanceHistogram.Luminance(0x00FFFFFFu));
    }

    [MetalFact]
    public void PrivatisedHistogram_ShowsItsTwoTimes_InTheWindowOnly()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        List<ILabPage> pages = LabPages.Create(device);
        using LabCanvas canvas = new(device);

        try
        {
            ILabPage page = pages[1];
            page.Compute(Frame);
            LabCheck check = page.Check();

            // The rows above the two descriptions hold the times: text only when the frame may vary.
            int upper = (DrawPrivatisedHistogram.PanelY - 40) / LabCanvas.CellHeight, lower = (DrawPrivatisedHistogram.LowerChartY - 40) / LabCanvas.CellHeight;

            // Over the charts only: the image on the left runs through the lower of these rows.
            int DistinctColors(int row)
            {
                Rgba32[] pixels = canvas.ToArray();
                var colors = new HashSet<Rgba32>();

                for (int y = row * LabCanvas.CellHeight; y < (row + 1) * LabCanvas.CellHeight; y++)
                {
                    for (int x = DrawPrivatisedHistogram.ChartX; x < DrawPrivatisedHistogram.ChartX + DrawPrivatisedHistogram.ChartWidth; x++)
                    {
                        colors.Add(pixels[(y * LabCanvas.Width) + x]);
                    }
                }

                return colors.Count;
            }

            LabView.Compose(canvas, pages, 1, Frame, check, timing: null);
            Assert.Equal((1, 1), (DistinctColors(upper), DistinctColors(lower)));
            Assert.True(DistinctColors(upper + 1) > 1 && DistinctColors(lower + 1) > 1, "the descriptions are always there");

            LabView.Compose(canvas, pages, 1, new LabFrame(Headless.Time, Deterministic: false), check, timing: null);
            Assert.True(DistinctColors(upper) > 1 && DistinctColors(lower) > 1, "the window shows both GPU times");
        }
        finally
        {
            DisposeAll(pages);
        }
    }

    [MetalFact]
    public void Compaction_KeepsExactlyThePassingElements_AndTheThresholdMovesTheCount()
    {
        using CompactionPage page = new(GraphicsDevice.GetDefault());

        page.Compute(Frame);
        int kept = page.Kept;
        Assert.InRange(kept, CompactionPage.Count / 10, CompactionPage.Count * 9 / 10);

        LabCheck check = page.Check();
        Assert.True(check.Passed, check.Summary);
        Assert.Contains(kept.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), check.Summary);

        // A million threads racing for slots do not arrive in index order.
        Assert.False(page.InIndexOrder);

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
        Assert.False(page.OnKeyDown(Key.C));

        for (int i = 0; i < 40; i++)
        {
            page.OnKeyDown(Key.Up);
        }

        Assert.Equal(60000u, page.Cutoff); // clamped
    }

    [MetalFact]
    public void Bits_TilesAreWhatTheDefinitionsSay()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using BitsPage page = new(device);
        using LabCanvas canvas = new(device);

        page.Compute(Frame);
        Assert.True(page.Check().Passed);
        canvas.ClearText();
        page.Draw(canvas, Frame);

        ReadOnlySpan<uint> pixels = canvas.Pixels.AsReadOnlySpan();
        uint At(ReadOnlySpan<uint> frame, int tile, int u, int v) =>
            frame[((BitPatterns.OriginY + ((tile / 4) * BitPatterns.PitchY) + v) * LabCanvas.Width) + BitPatterns.OriginX + ((tile % 4) * BitPatterns.PitchX) + u];

        var random = new Random(4);

        for (int i = 0; i < 2000; i++)
        {
            int u = random.Next(256), v = random.Next(256);

            // Tile 0: the popcount of x ^ y through the color ramp, restated with BitOperations.
            Assert.Equal(BitsPage.ReferencePopCountTile(u, v), At(pixels, 0, u, v));

            // Tile 1: two colors, by the parity of the shared bits.
            bool odd = (System.Numerics.BitOperations.PopCount((uint)(u & v)) & 1) != 0;
            Assert.Equal(odd, At(pixels, 1, u, v) == At(pixels, 1, 1, 1));

            // Tile 7: the float color (u, v, u + v) / 255 survives pack, unpack, pack unchanged.
            Assert.Equal(0xFF000000u | ((uint)((u + v) & 255) << 16) | ((uint)v << 8) | (uint)u, At(pixels, 7, u, v));
        }

        // Tile 6: the top band is the exponent 255: infinity where the mantissa is zero (white), NaN beside it.
        Assert.Equal(0xFFFFFFFFu, At(pixels, 6, 3, 2));
        Assert.NotEqual(0xFFFFFFFFu, At(pixels, 6, 40, 2));
        Assert.NotEqual(At(pixels, 6, 40, 2), At(pixels, 6, 40, 100));

        // Between the tiles: background.
        Assert.Equal(pixels[0], pixels[(BitPatterns.OriginY * LabCanvas.Width) + BitPatterns.OriginX + 270]);
    }

    [MetalFact]
    public void Indirect_DispatchesWholeThreadGroupsOverTheSurvivors_AndAllThreeWaysAgree()
    {
        using IndirectPage page = new(GraphicsDevice.GetDefault());
        Assert.Equal(3, page.Variants.Count);

        // Indirect: the GPU's own count, rounded up to whole groups of 256, is what ran.
        page.Compute(Frame);
        int kept = page.Kept;
        Assert.InRange(kept, IndirectPage.Count / 50, IndirectPage.Count / 2);
        Assert.Equal(DispatchArguments.FromCount(kept, ShadeSurvivors.GroupSize), page.Arguments);
        Assert.Equal((kept + 255) / 256 * 256, page.ThreadsDispatched);
        Assert.InRange(page.ThreadsDispatched - kept, 0, 255);

        // All three ways were run and timed, and the page's own time is the shown one's.
        Assert.Equal(3, page.Milliseconds.Count);
        Assert.All(page.Milliseconds, static ms => Assert.InRange(ms, double.Epsilon, 10_000));
        Assert.Equal(page.Milliseconds[0], page.GpuMilliseconds);

        LabCheck check = page.Check();
        Assert.True(check.Passed, check.Summary);
        Assert.Contains(kept.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), check.Summary);

        // Exactly the survivors were written, each with its own value.
        uint[] indirect = page.Shaded.ToArray();
        Assert.Equal(kept, indirect.Count(static value => value != 0u));

        // The other two ways run every thread of the array, and give the same result: first over the
        // compacted list with a bounds check, then with no compaction at all.
        for (int way = 1; way <= 2; way++)
        {
            Assert.True(page.OnKeyDown(Key.D));
            Assert.Equal(way, page.Variant);
            page.Compute(Frame);
            Assert.Equal(kept, page.Kept);
            Assert.Equal(DispatchArguments.FromCount(kept, ShadeSurvivors.GroupSize), page.Arguments);
            Assert.Equal(IndirectPage.Count, page.ThreadsDispatched);
            Assert.Equal(page.Milliseconds[way], page.GpuMilliseconds);
            Assert.Equal(indirect, page.Shaded.ToArray());

            LabCheck other = page.Check();
            Assert.True(other.Passed, other.Summary);
        }

        // D comes back round to the indirect way.
        Assert.True(page.OnKeyDown(Key.D));
        Assert.Equal(0, page.Variant);

        // A higher threshold leaves fewer survivors and fewer threadgroups; other keys are not the page's.
        uint cutoff = page.Cutoff;
        uint groups = page.Arguments.ThreadGroupsX;
        Assert.True(page.OnKeyDown(Key.Up));
        Assert.True(page.Cutoff > cutoff);
        page.Compute(Frame);
        Assert.True(page.Kept < kept);
        Assert.True(page.Arguments.ThreadGroupsX < groups);
        Assert.True(page.Check().Passed);
        Assert.False(page.OnKeyDown(Key.C));

        for (int i = 0; i < 40; i++)
        {
            page.OnKeyDown(Key.Up);
        }

        Assert.Equal(63000u, page.Cutoff); // clamped

        // Almost nothing survives at the top of the range; all three ways still agree, down to few groups.
        for (int way = 0; way < 3; way++)
        {
            page.Variant = way;
            page.Compute(new LabFrame(Headless.Time, Deterministic: way == 1));
            Assert.True(page.Kept < kept / 4);
            Assert.True(page.Check().Passed);
        }
    }

    [MetalFact]
    public void Indirect_PageSaysWhereTheSpeedUpComesFrom()
    {
        // The sentence is drawn on the page itself (the picture is what people look at), in both modes.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using IndirectPage page = new(device);
        using LabCanvas canvas = new(device);
        const string Sentence = "The speed-up comes from compaction; indirect dispatch's gain is no CPU read-back.";

        page.Compute(Frame);
        page.Draw(canvas, Frame);
        Assert.Contains(Sentence, canvas.TextLines(), StringComparer.OrdinalIgnoreCase);

        page.Draw(canvas, new LabFrame(Headless.Time, Deterministic: false));
        string[] live = canvas.TextLines();
        Assert.Contains(Sentence, live, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(live, static line => line.StartsWith("This frame: compact + indirect ", StringComparison.OrdinalIgnoreCase) && line.Contains("no compaction", StringComparison.OrdinalIgnoreCase));
    }

    [MetalFact]
    public void FastMath_BothVariantsStayWithinTheirBounds_AndFastMathIsTheLooserOne()
    {
        using FastMathPage page = new(GraphicsDevice.GetDefault());

        page.Compute(Frame);
        LabCheck safe = page.Check();
        Assert.True(safe.Passed, safe.Summary);
        Assert.Equal(FastMathPage.SafeBound, page.Bound);
        Assert.InRange(page.MaxError, 0f, FastMathPage.SafeBound);
        Assert.Contains("max error against the CPU", safe.Summary);

        Assert.True(page.OnKeyDown(Key.F));
        Assert.Equal(1, page.Variant);
        page.Compute(Frame);
        LabCheck fast = page.Check();
        Assert.True(fast.Passed, fast.Summary);
        Assert.Equal(FastMathPage.FastBound, page.Bound);
        Assert.True(FastMathPage.FastBound > FastMathPage.SafeBound);
        Assert.False(page.OnKeyDown(Key.C));

        // The two shaders differ in their compile options and in nothing else.
        Assert.Equal(ShaderCompileOptions.None, Options<WaveField>());
        Assert.Equal(ShaderCompileOptions.FastMath, Options<WaveFieldFast>());
        Assert.Equal(Source<WaveField>().Replace(nameof(WaveFieldFast), "X").Replace(nameof(WaveField), "X"), Source<WaveFieldFast>().Replace(nameof(WaveFieldFast), "X"));

        static ShaderCompileOptions Options<T>()
            where T : struct, IComputeShaderDescriptor<T> => T.CompileOptions;

        static string Source<T>()
            where T : struct, IComputeShaderDescriptor<T> => T.MslSource;
    }

    [Fact]
    public void Indirect_ShadeIsNeverZero_SoZeroMarksAnElementThatWasNotWritten()
    {
        for (uint i = 0; i < 10_000; i++)
        {
            Assert.NotEqual(0u, ShadeSurvivors.Shade(i * 7u, i));
        }

        Assert.NotEqual(ShadeSurvivors.Shade(1000u, 1u), ShadeSurvivors.Shade(1000u, 2u));
    }

    [MetalFact]
    public void LocalArrays_TheMedianRemovesTheNoise_TheGaussianSmearsIt_BothExactAgainstTheCpu()
    {
        using LocalArraysPage page = new(GraphicsDevice.GetDefault());
        Assert.Equal(["median 3x3 (sorting network)", "gaussian 5x5 (weight table)"], page.Variants);
        Assert.True(page.Noise);

        page.Compute(Frame);
        LabCheck median = page.Check();
        Assert.True(median.Passed, median.Summary);
        Assert.StartsWith("all 1,048,576 pixels equal ForCpu's", median.Summary);

        // About 6 % of the input are specks; the median leaves almost none of them.
        Assert.InRange(page.NoisyOff, 0.05, 0.08);
        Assert.True(page.FilteredOff < page.NoisyOff / 10, $"median: {page.FilteredOff:P2} of the pixels are still off");
        double medianOff = page.FilteredOff;

        Assert.True(page.OnKeyDown(Key.F));
        Assert.Equal(1, page.Variant);
        page.Compute(Frame);
        LabCheck gaussian = page.Check();
        Assert.True(gaussian.Passed, gaussian.Summary);
        Assert.True(page.FilteredOff > medianOff * 10, $"gaussian: only {page.FilteredOff:P2} of the pixels are off");

        // Without noise the input is the clean image, and nothing much is off after either filter.
        Assert.True(page.OnKeyDown(Key.N));
        Assert.False(page.Noise);
        page.Compute(Frame);
        Assert.True(page.Input.SequenceEqual(page.Clean));
        Assert.True(page.Check().Passed);
        Assert.Equal(0, page.NoisyOff);
        Assert.True(page.FilteredOff < 0.01, $"{page.FilteredOff:P2}");

        Assert.False(page.OnKeyDown(Key.C));
        Assert.True(page.OnKeyDown(Key.F));
        Assert.Equal(0, page.Variant);
    }

    [MetalFact]
    public void LocalArrays_Filters_AreTheTextbookOnes()
    {
        // A white speck on black: the median removes it, the Gaussian spreads 36/256 of it to the centre.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Size = 9;
        uint[] image = Enumerable.Repeat(0xFF000000u, Size * Size).ToArray();
        image[(4 * Size) + 4] = 0xFFFFFFFFu;

        using ReadWriteBuffer<uint> source = device.AllocateReadWriteBuffer<uint>(image);
        using ReadWriteBuffer<uint> median = device.AllocateReadWriteBuffer<uint>(Size * Size);
        using ReadWriteBuffer<uint> gaussian = device.AllocateReadWriteBuffer<uint>(Size * Size);

        device.For(Size, Size, new MedianFilter(source, median, Size));
        device.For(Size, Size, new GaussianFilter(source, gaussian, Size));

        Assert.All(median.ToArray(), static p => Assert.Equal(0xFF000000u, p));
        uint centre = gaussian[(4 * Size) + 4];
        Assert.Equal(0xFF000000u | (36u << 16) | (36u << 8) | 36u, centre); // round(255 * 36 / 256)
        Assert.Equal(0xFF000000u | (1u << 16) | (1u << 8) | 1u, gaussian[(2 * Size) + 2]); // round(255 / 256)
        Assert.Equal(0xFF000000u, gaussian[0]);
    }

    [MetalFact]
    public void TheWindowView_ShowsTimings_ThePictureDoesNot()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        List<ILabPage> pages = LabPages.Create(device);
        using LabCanvas canvas = new(device);

        try
        {
            LabCheck check = Headless.Measure(pages[3], Frame, out LabTiming timing);
            Assert.True(check.Passed);
            Assert.True(timing.GpuMilliseconds > 0 && timing.CpuMilliseconds > 0);

            int TextPixelsInRow(int row)
            {
                Rgba32[] pixels = canvas.ToArray();
                int count = 0;

                for (int y = row * LabCanvas.CellHeight; y < (row + 1) * LabCanvas.CellHeight; y++)
                {
                    for (int x = 0; x < LabCanvas.Width; x++)
                    {
                        count += pixels[(y * LabCanvas.Width) + x].B > 200 ? 1 : 0;
                    }
                }

                return count;
            }

            LabView.Compose(canvas, pages, 3, Frame, check, timing: null);
            Assert.Equal(0, TextPixelsInRow(2));

            LabView.Compose(canvas, pages, 3, Frame, check, timing);
            Assert.True(TextPixelsInRow(2) > 300);

            // Before the first check there is a placeholder instead of a result.
            LabView.Compose(canvas, pages, 3, Frame, check: null, timing: null, paused: true);
            Assert.True(TextPixelsInRow(0) > 300);
        }
        finally
        {
            DisposeAll(pages);
        }
    }
}
