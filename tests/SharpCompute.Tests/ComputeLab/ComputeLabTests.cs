using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static System.FormattableString;
using SharpCompute.Presentation;
using SharpCompute.Samples.ComputeLab;
using SharpCompute.Samples.ComputeLab.Pages;
using SharpCompute.Samples.ComputeLab.Pages.SharedMemory;
using SharpCompute.Samples.ComputeLab.Pages.Simd;

// The atomics topic's compaction; the SIMD-group one is tested in SimdPageTests.
using CompactionPage = SharpCompute.Samples.ComputeLab.Pages.CompactionPage;
using Program = SharpCompute.Samples.ComputeLab.Program;

namespace SharpCompute.Tests.ComputeLab;

/// <summary>
/// The ComputeLab sample without its window: every page renders, is not blank, agrees with the CPU,
/// keeps its text off the frame's edges, and the headless pictures are the same on every run.
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
        Assert.Equal(43, LabCanvas.Rows);
        Assert.True(LabView.FirstPageRow < LabCanvas.Rows - 1);

        // A cell's height of margin above the first row and below the last, and rows on 16-pixel lines.
        Assert.Equal(LabCanvas.CellHeight, LabCanvas.TextTop);
        Assert.Equal(LabCanvas.Height, (2 * LabCanvas.TextTop) + (LabCanvas.Rows * LabCanvas.CellHeight));
        Assert.Equal(16, LabCanvas.RowY(0));
        Assert.Equal(LabCanvas.Height - LabCanvas.TextTop - LabCanvas.CellHeight, LabCanvas.RowY(LabCanvas.Rows - 1));
    }

    [MetalFact]
    public void Canvas_DrawsGlyphsAtTwiceTheirSize_OverADarkenedBacking()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using LabCanvas canvas = new(device);

        canvas.Pixels.AsSpan().Fill(0xFFFFFFFFu); // a white frame
        canvas.Text(0, 0, new string('W', LabCanvas.Columns), TextColor.White); // the whole first row
        canvas.Text(0, 2, "Z", TextColor.Bad); // the cell after the last of row 1 in memory
        canvas.Text(2, 1, "L!", TextColor.White);
        canvas.Text(LabCanvas.Columns - 1, LabCanvas.Rows - 1, "ZZ", TextColor.Good); // the second Z falls off the edge
        canvas.Text(0, LabCanvas.Rows, "off", TextColor.Bad); // below the last row: ignored
        canvas.ComposeText();

        Rgba32[] pixels = canvas.ToArray();
        Rgba32 At(int x, int y) => pixels[(y * LabCanvas.Width) + x];

        // Outside every used cell the frame is untouched: the margins above the first row and below the
        // last (text in both rows), the pixels right of the last column, and the unused cells.
        for (int x = 0; x < LabCanvas.Width; x++)
        {
            for (int y = 0; y < LabCanvas.TextTop; y++)
            {
                Assert.Equal(new Rgba32(255, 255, 255, 255), At(x, y));
                Assert.Equal(new Rgba32(255, 255, 255, 255), At(x, LabCanvas.Height - 1 - y));
            }
        }

        for (int y = LabCanvas.RowY(1); y < LabCanvas.RowY(2); y++)
        {
            for (int x = LabCanvas.Columns * LabCanvas.CellWidth; x < LabCanvas.Width; x++)
            {
                Assert.Equal(new Rgba32(255, 255, 255, 255), At(x, y));
            }
        }

        Assert.Equal(new Rgba32(255, 255, 255, 255), At((4 * LabCanvas.CellWidth) + 1, LabCanvas.RowY(1) + 1));
        Assert.Equal(new Rgba32(56, 56, 56, 255), At(LabCanvas.CellWidth - 1, LabCanvas.TextTop)); // the first row's backing

        // Inside: a set font pixel is the text color, anything else is the frame at 22 %.
        ulong mask = PixelFont.Mask(PixelFont.Index('L'));
        int originX = 2 * LabCanvas.CellWidth, originY = LabCanvas.RowY(1);

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
        Rgba32 green = At(((LabCanvas.Columns - 1) * LabCanvas.CellWidth) + 2, LabCanvas.RowY(LabCanvas.Rows - 1));
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

    [MetalFact]
    public void Label_PutsTextInTheCellUnderItsPixel_AndNothingInTheMargins()
    {
        using LabCanvas canvas = new(GraphicsDevice.GetDefault());

        canvas.Label(30, 100, "a");                // row (100 - 16) / 16 = 5, column 2
        canvas.Label(30, 5, "margin");              // above the grid: dropped
        canvas.Label(30, LabCanvas.Height - 5, "z"); // below it: dropped
        string[] lines = canvas.TextLines();

        Assert.Equal("A", lines[5]);
        Assert.Equal(96, LabCanvas.RowY(5));
        Assert.Equal(1, lines.Count(static line => line.Length > 0));
    }

    [Fact]
    public void PageNames_BecomeFileNamesAndCommandLineWords()
    {
        Assert.Equal("histogram", LabPages.Slug("Histogram"));
        Assert.Equal("histogram-privatised", LabPages.Slug("Histogram: privatised"));
        Assert.Equal("small-and-wide-integers", LabPages.Slug("Small and wide integers"));
        Assert.Equal("a-b-c9", LabPages.Slug("  A / b -- C9!?"));
        Assert.Equal(string.Empty, LabPages.Slug(": :"));
    }

    // ---- the pages --------------------------------------------------------------------------------

    private static readonly string[] s_names =
    [
        "Histogram", "Histogram: privatised", "Compaction",
        "Reduction", "PrefixSum", "MatMul", "Blur",
        "LocalArrays",
        "SIMD reduction", "SIMD compaction", "SIMD shuffle",
        "Indirect",
        "Bits", "FastMath", "Small and wide integers", "Half precision",
        "Shared code",
    ];

    private static readonly string[] s_pictures =
    [
        "1-histogram.png", "2-histogram-privatised.png", "3-compaction.png",
        "4-reduction.png", "5-prefixsum.png", "6-matmul.png", "7-blur.png",
        "8-localarrays.png",
        "9-simd-reduction.png", "10-simd-compaction.png", "11-simd-shuffle.png",
        "12-indirect.png",
        "13-bits.png", "14-fastmath.png", "15-small-and-wide-integers.png", "16-half-precision.png",
        "17-shared-code.png",
    ];

    [MetalFact]
    public void Pages_AreGroupedByTopic_EachWithAtLeastOneVariant()
    {
        List<ILabPage> pages = LabPages.Create(GraphicsDevice.GetDefault());

        try
        {
            // The privatised histogram sits next to the one it is compared with.
            Assert.Equal(s_names, pages.Select(static p => p.Name));
            Assert.Equal(
                [
                    "atomics", "threadgroup atomics", "atomics",
                    "tree sum", "Blelloch scan", "tiled product", "tile + apron",
                    "fixed-size local arrays",
                    "SIMD-group reductions", "ballots and prefix sums", "lane shuffles",
                    "indirect dispatch",
                    "bit and packing intrinsics", "per-shader compile options", "8-bit and 64-bit integers", "Half4 and bool vectors",
                    "static helpers and struct methods",
                ],
                pages.Select(static p => p.Feature));

            // Topic by topic, in the lab's order, every topic with at least one page.
            Assert.Equal(
                [.. Enumerable.Repeat(LabTopics.Atomics, 3), .. Enumerable.Repeat(LabTopics.SharedMemory, 4), LabTopics.LocalArrays, .. Enumerable.Repeat(LabTopics.Simd, 3), LabTopics.Dispatch, .. Enumerable.Repeat(LabTopics.Math, 4), LabTopics.CodeReuse],
                pages.Select(static p => p.Topic));
            Assert.Equal(LabTopics.All, pages.Select(static p => p.Topic).Distinct());
            Assert.Equal(pages.Select(static p => p.Topic), pages.Select(static p => p.Topic).OrderBy(static t => LabTopics.All.ToList().IndexOf(t)));

            Assert.All(pages, static p => Assert.NotEmpty(p.Variants));
            Assert.All(pages, static p => Assert.Equal(0, p.Variant));
            Assert.Equal(s_pictures, Enumerable.Range(0, pages.Count).Select(i => Headless.FileName(pages, i)));

            // Names, and so the pictures and --page, are unique.
            Assert.Equal(pages.Count, pages.Select(static p => LabPages.Slug(p.Name)).Distinct().Count());

            // Only the privatised page times a second dispatch, and only the matrix product has a rate.
            Assert.Equal(pages.Select(static p => p.Name is "Histogram: privatised" or "Half precision"), pages.Select(static p => p.Baseline is not null));
            Assert.Equal(pages.Select(static p => p.Name == "MatMul"), pages.Select(static p => p is IRatedPage));

            // The CPU side runs on all cores unless the page says otherwise, as its check line does too.
            Assert.Equal(
                ["PrefixSum", "SIMD reduction", "SIMD compaction", "SIMD shuffle", "Half precision", "Shared code"],
                pages.Where(static p => p.CpuNote != "all cores").Select(static p => p.Name));
            Assert.All(pages.Where(static p => p.CpuNote != "all cores" && p.Name is not ("Shared code" or "Half precision")), static p => Assert.Equal("one core", p.CpuNote));

            // A CPU time that is extrapolated from a sample says so.
            Assert.Equal("all cores, scaled from every 8th pixel", pages.Single(static p => p.Name == "Shared code").CpuNote);

            // And so does one that is not of the page's own kernel: the half-precision page times the float blur on the CPU.
            Assert.Equal("all cores, the float blur", pages.Single(static p => p.Name == "Half precision").CpuNote);

            // --page takes the name or the file-name form of it, in any case.
            Assert.Equal(1, LabPages.Find(pages, "Histogram: privatised"));
            Assert.Equal(1, LabPages.Find(pages, "histogram-privatised"));
            Assert.Equal(1, LabPages.Find(pages, "HISTOGRAM-PRIVATISED"));
            Assert.Equal(0, LabPages.Find(pages, "histogram"));
            Assert.Equal(3, LabPages.Find(pages, "reduction"));
            Assert.Equal(7, LabPages.Find(pages, "localarrays"));
            Assert.Equal(8, LabPages.Find(pages, "simd-reduction"));
            Assert.Equal(10, LabPages.Find(pages, "SIMD shuffle"));
            Assert.Equal(12, LabPages.Find(pages, "Bits"));
            Assert.Equal(13, LabPages.Find(pages, "fastmath"));
            Assert.Equal(14, LabPages.Find(pages, "small-and-wide-integers"));
            Assert.Equal(14, LabPages.Find(pages, "Small and wide integers"));
            Assert.Equal(15, LabPages.Find(pages, "half-precision"));
            Assert.Equal(15, LabPages.Find(pages, "Half precision"));
            Assert.Equal(16, LabPages.Find(pages, "shared-code"));
            Assert.Equal(-1, LabPages.Find(pages, "privatised"));
            Assert.Equal(-1, LabPages.Find(pages, "shuffle"));
        }
        finally
        {
            DisposeAll(pages);
        }
    }

    [MetalFact]
    public void TheTitleAndTheHelpLine_FitTheirRows_ForEveryPageAndVariant()
    {
        List<ILabPage> pages = LabPages.Create(GraphicsDevice.GetDefault());

        try
        {
            for (int index = 0; index < pages.Count; index++)
            {
                ILabPage page = pages[index];

                for (int variant = 0; variant < page.Variants.Count; variant++)
                {
                    page.Variant = variant;
                    (string Lab, string Topic, string Page) title = LabView.Title(pages, index);

                    // One column of margin on the left, where Compose starts, and at least one on the right.
                    Assert.True(1 + LabView.TitleLength(title) < LabCanvas.Columns, $"{title} is {LabView.TitleLength(title)} columns");
                    Assert.Equal(page.Topic, title.Topic);
                    Assert.Contains(page.Variants[variant], title.Page);
                }

                page.Variant = 0;
                Assert.True(1 + LabView.HelpLine(page, paused: true).Length < LabCanvas.Columns, LabView.HelpLine(page, paused: true));
                Assert.EndsWith("(paused)", LabView.HelpLine(page, paused: true));
                Assert.DoesNotContain("paused", LabView.HelpLine(page));
            }
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

                    // The check line fits its row: a column of margin, "CHECK PASSED: " and the summary.
                    Assert.True(1 + "CHECK PASSED: ".Length + check.Summary.Length <= LabCanvas.Columns, $"{page.Name}: the summary is {check.Summary.Length} characters: {check.Summary}");

                    // The page alone, before any text is composed over it...
                    canvas.ClearText();
                    page.Draw(canvas, Frame);
                    Rgba32[] bare = canvas.ToArray();

                    LabView.Compose(canvas, pages, index, Frame, check, timing: null);
                    Rgba32[] pixels = canvas.ToArray();

                    // ...is what the picture shows in the margins: no text reaches the top or bottom edge.
                    int margin = LabCanvas.TextTop * LabCanvas.Width;
                    Assert.True(bare.AsSpan(0, margin).SequenceEqual(pixels.AsSpan(0, margin)), $"{page.Name}: text in the top margin");
                    Assert.True(bare.AsSpan(pixels.Length - margin).SequenceEqual(pixels.AsSpan(pixels.Length - margin)), $"{page.Name}: text in the bottom margin");

                    // Opaque everywhere, more than a few colors, and a part of the frame is not background
                    // (the SIMD-group pages are flat-shaded text, bars and curves: from 13 colors, 5 % lit).
                    Assert.All(pixels, static p => Assert.Equal(255, p.A));
                    Assert.True(pixels.Distinct().Count() > 8, $"{page.Name}: only {pixels.Distinct().Count()} colors");
                    int lit = pixels.Count(static p => p.R + p.G + p.B > 150);
                    Assert.True(lit > pixels.Length / 40, $"{page.Name}: only {lit} of {pixels.Length} pixels are lit");

                    // The lab's own text is there: the check line is green where its glyphs are.
                    int greenText = 0;

                    for (int y = LabCanvas.RowY(1); y < LabCanvas.RowY(2); y++)
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
                // Every variant of every page: two histograms, compaction, reduction, prefix sum, product, blur, local arrays,
                // SIMD reduction (3), SIMD compaction, shuffle, indirect (3), bits, fast math, small and wide integers, half precision, shared code.
                int variants = 2 + 2 + 1 + 2 + 1 + 2 + 2 + 2 + 3 + 2 + 2 + 3 + 1 + 2 + 1 + 2 + 1;
                Assert.Equal(variants, summary.Split('\n').Count(static line => line.TrimStart().StartsWith("PASS", StringComparison.Ordinal)));
                Assert.Contains("256 bins", summary);
                Assert.Contains("4 bins (64x the contention)", summary);
                Assert.Contains("indirect: compact, dispatch the survivors", summary);
                Assert.Contains("compact, dispatch all 1,048,576 threads", summary);
                Assert.Contains("no compaction: every thread tests itself", summary);
                Assert.Contains("safe math (the default)", summary);
                Assert.Contains("[ShaderOptions(FastMath = true)]", summary);
                Assert.Contains("tile + apron in shared memory", summary);
                Assert.Contains("serial loop per thread, no shared memory", summary);
                Assert.Contains("naive: every thread reads its row and column", summary);
                Assert.Contains("SIMD + per-group partial", summary);
                Assert.Contains("median 3x3 (sorting network)", summary);
                Assert.Contains("gaussian 5x5 (weight table)", summary);
                Assert.Equal(variants, summary.Split('\n').Count(static line => line.Contains("GPU ", StringComparison.Ordinal) && line.Contains(" ms   CPU ", StringComparison.Ordinal)));
                Assert.Equal(2, summary.Split('\n').Count(static line => line.Contains("GFLOP/s   CPU ", StringComparison.Ordinal)));
                Assert.Equal(1 + 3 + 2 + 2, summary.Split('\n').Count(static line => line.Contains(" ms (one core)", StringComparison.Ordinal))); // the prefix sum, and the SIMD pages' variants
                Assert.Contains("[9/17] SIMD groups / SIMD reduction: SIMD-group reductions", summary);

                Assert.Contains("[15/17] Math / Small and wide integers: 8-bit and 64-bit integers", summary);
                Assert.Contains("1,048,576 bytes equalised in place, 1,048,576 64-bit hashes: GPU, ForCpu and plain C# agree", summary);

                // The privatised histogram is also timed against the global-atomics one, at both bin counts.
                Assert.Contains("[2/17] Atomics / Histogram: privatised: threadgroup atomics", summary);
                Assert.Equal(2, summary.Split('\n').Count(static line => line.TrimStart().StartsWith("global atomics: GPU ", StringComparison.Ordinal) && line.Contains("this page's kernel is ", StringComparison.Ordinal)));

                // And the half blur against the float blur, at both radii; its CPU time is the float blur's.
                Assert.Contains("[16/17] Math / Half precision: Half4 and bool vectors", summary);
                Assert.Contains("289 taps (radius 8)", summary);
                Assert.Contains("81 taps (radius 4)", summary);
                Assert.Equal(2, summary.Split('\n').Count(static line => line.TrimStart().StartsWith("the same blur in float: GPU ", StringComparison.Ordinal) && line.Contains("this page's kernel is ", StringComparison.Ordinal)));
                Assert.Equal(2, summary.Split('\n').Count(static line => line.Contains("bit for bit: float, half (96x96 tile), mask.", StringComparison.Ordinal)));
                Assert.Equal(2, summary.Split('\n').Count(static line => line.Contains(" ms (all cores, the float blur)", StringComparison.Ordinal)));

                string[] pictures = s_pictures;
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
            int upper = (DrawPrivatisedHistogram.PanelY - 40 - LabCanvas.TextTop) / LabCanvas.CellHeight, lower = (DrawPrivatisedHistogram.LowerChartY - 40 - LabCanvas.TextTop) / LabCanvas.CellHeight;

            // Over the charts only: the image on the left runs through the lower of these rows.
            int DistinctColors(int row)
            {
                Rgba32[] pixels = canvas.ToArray();
                var colors = new HashSet<Rgba32>();

                for (int y = LabCanvas.RowY(row); y < LabCanvas.RowY(row + 1); y++)
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

    [MetalFact]
    public void SharedCode_TracesTheScene_AndForCpuAgreesOnTheSample()
    {
        using SharedCodePage page = new(GraphicsDevice.GetDefault());

        page.Compute(Frame);
        LabCheck check = page.Check();

        Assert.True(check.Passed, check.Summary);
        Assert.Contains("sampled pixels within 2/255 of ForCpu", check.Summary);
        Assert.InRange(page.Agreeing, (int)(SharedCodePage.RequiredAgreement * SharedCodePage.SampleWidth * SharedCodePage.SampleHeight), SharedCodePage.SampleWidth * SharedCodePage.SampleHeight);
        Assert.True(page.GpuMilliseconds > 0 && check.CpuMilliseconds > 0);

        // The picture is the scene: blue sky along the top, the red sphere in the middle, lit ground below.
        ReadOnlySpan<Float3> light = page.Light;
        Float3 sky = light[(8 * SharedCodePage.ImageWidth) + (SharedCodePage.ImageWidth / 2)];
        Float3 sphere = light[(SharedCodePage.ImageHeight / 2 * SharedCodePage.ImageWidth) + (SharedCodePage.ImageWidth / 2)];
        Float3 ground = light[((SharedCodePage.ImageHeight - 8) * SharedCodePage.ImageWidth) + (SharedCodePage.ImageWidth / 2)];

        Assert.True(sky.Z > sky.X * 1.2f, $"sky {sky}");
        Assert.True(sphere.X > sphere.Z * 1.5f, $"sphere {sphere}");
        Assert.True(ground.X + ground.Y + ground.Z > 0.3f, $"ground {ground}");

        foreach (Float3 pixel in light)
        {
            Assert.True(float.IsFinite(pixel.X + pixel.Y + pixel.Z) && pixel.X >= 0 && pixel.Y >= 0 && pixel.Z >= 0, $"light {pixel}");
        }
    }

    [Fact]
    public void SharedCode_TheSharedStructsAndHelpers_AreOrdinaryCSharp()
    {
        // What the shaders call is what this test calls: the CPU needs no second implementation.
        Ray ray = new(new Float3(0f, 1f, -5f), new Float3(0f, 0f, 3f));
        Assert.Equal(new Float3(0f, 0f, 1f), ray.Direction);
        Assert.Equal(new Float3(0f, 1f, -3f), ray.At(2f));

        Sphere sphere = new(new Float3(0f, 1f, 0f), 1f, new Float3(1f, 0f, 0f), mirror: true);
        Assert.Equal(4f, sphere.Intersect(ray));
        Assert.Equal(-1f, sphere.Intersect(new Ray(new Float3(3f, 1f, -5f), new Float3(0f, 0f, 1f))));
        Assert.Equal(-1f, sphere.Intersect(new Ray(new Float3(0f, 1f, 5f), new Float3(0f, 0f, 1f))));
        Assert.Equal(new Float3(0f, 0f, -1f), sphere.NormalAt(ray.At(4f)));
        Assert.Equal(1, sphere.Mirror);
        Assert.Equal(32, System.Runtime.CompilerServices.Unsafe.SizeOf<Sphere>());

        Assert.False(default(Hit).Found);
        Assert.True(new Hit { Distance = 0.5f }.Found);

        Camera camera = new(new Float3(0f, 0f, -4f), new Float3(0f, 0f, 0f), verticalFov: MathF.PI / 2f, aspect: 2f);
        Assert.Equal(new Float3(0f, 0f, 1f), camera.RayThrough(new Float2(0f, 0f)).Direction);
        Assert.True(camera.RayThrough(new Float2(1f, 0f)).Direction.X > 0.8f);
        Assert.True(camera.RayThrough(new Float2(0f, 1f)).Direction.Y > 0.6f);

        // A stream is its seed: two with one seed give the same numbers, and the numbers are in 0..1.
        RandomStream a = new(42u), b = new(42u), c = new(43u);
        float first = a.Next();
        Assert.Equal(first, b.Next());
        Assert.NotEqual(first, c.Next());
        Assert.NotEqual(first, a.Next());

        for (int i = 0; i < 1000; i++)
        {
            Assert.InRange(a.Next(), 0f, 0.99999994f);
            Assert.InRange(Gpu.Length(a.NextDirection()), 0.9999f, 1.0001f);
        }

        Assert.Equal(1.5f, ShaderMath.Pick(true, 1.5f, 2.5f));
        Assert.Equal(new Float3(2f), ShaderMath.Pick(false, new Float3(1f), new Float3(2f)));
        Assert.Equal(new Float3(0f), ShaderMath.Tonemap(new Float3(0f)));
        Assert.True(ShaderMath.Tonemap(new Float3(100f)).X is > 0.99f and <= 1f);
        Assert.True(ShaderMath.Sky(new Float3(0f, 1f, 0f)).Z > ShaderMath.Sky(new Float3(0f, 1f, 0f)).X);
        Assert.InRange(Gpu.Length(ShaderMath.SunDirection), 0.9999f, 1.0001f);
    }

    [Fact]
    public void SharedCode_EachShaderHasTheSharedFunctionsItCalls_Once()
    {
        string trace = Msl<TraceScene>(), draw = Msl<DrawSharedCode>();

        // The tracer: the static class, the structs' members (by value, or by reference where they
        // modify the struct), a constructor, and a generic method at the one type it is used with.
        Assert.Equal(1, trace.Split("static float3 ShaderMath_Sky(float3 direction)\n{").Length - 1);
        Assert.Equal(1, trace.Split("static uint ShaderMath_Hash(uint x)\n{").Length - 1);
        Assert.Contains("static float3 ShaderMath_Pick_float3(bool condition, float3 whenTrue, float3 whenFalse)", trace);
        Assert.Contains("static Ray Ray_new(float3 origin, float3 direction)", trace);
        Assert.Contains("static float3 Ray_At(Ray __this, float distance)", trace);
        Assert.Contains("static float Sphere_Intersect(Sphere __this, Ray ray)", trace);
        Assert.Contains("static Ray Camera_RayThrough(Camera __this, float2 point)", trace);
        Assert.Contains("static float RandomStream_Next(thread RandomStream& __this)", trace);
        Assert.Contains("static float3 RandomStream_NextDirection(thread RandomStream& __this)", trace);
        Assert.Contains("static bool Hit_Found(Hit __this)", trace);
        Assert.Contains("constant float3 ShaderMath_SunDirection = float3(", trace);
        Assert.Contains("::Sphere_Intersect(spheres[i], ray)", trace);
        Assert.Contains("::Camera_RayThrough(__c.camera, point)", trace);

        // The drawing shader shares the class, and gets only what it calls. (Its copy of the function
        // has its parameter renamed: `light` is the name of a buffer that shader captures.)
        Assert.Equal(1, draw.Split("static float3 ShaderMath_Tonemap(float3 __local_light)\n{").Length - 1);
        Assert.DoesNotContain("ShaderMath_Sky", draw);
        Assert.DoesNotContain("ShaderMath_Tonemap", trace);

        // Other pages share code the same way: both fast-math variants call one method, and the two
        // generators of test data call the lab's hash.
        Assert.Contains("static float WaveMath_Value(int x, int y, int size, float time)", Msl<WaveField>());
        Assert.Contains("static float WaveMath_Value(int x, int y, int size, float time)", Msl<WaveFieldFast>());
        Assert.Contains("static uint ShaderMath_Hash(uint x)", Msl<SharpCompute.Samples.ComputeLab.Pages.GenerateImage>());
        Assert.Contains("static uint ShaderMath_Hash(uint x)", Msl<SharpCompute.Samples.ComputeLab.Pages.GenerateField>());

        static string Msl<T>()
            where T : struct, IComputeShaderDescriptor<T> => T.MslSource;
    }

    // ---- small and wide integers ------------------------------------------------------------------

    [Fact]
    public void Integers_ReferenceHash_IsFnv1aOverTheCoordinates_WithTheFinishingRound()
    {
        // The published FNV-1a 64 test vectors.
        Assert.Equal(0xCBF29CE484222325UL, IntegersPage.Fnv1a([]));
        Assert.Equal(0xAF63DC4C8601EC8CUL, IntegersPage.Fnv1a("a"u8));
        Assert.Equal(0x85944171F73967E8UL, IntegersPage.Fnv1a("foobar"u8));

        ulong fnv = IntegersPage.Fnv1a([3, 0, 0, 0, 7, 1, 0, 0, 0x78, 0x56, 0x34, 0x12]);
        fnv ^= fnv >> 32;
        fnv *= 0xD6E8FEB86659FD93UL;
        fnv ^= fnv >> 32;

        Assert.Equal(fnv, IntegersPage.ReferenceHash(3, 263, 0x12345678));
        Assert.NotEqual(IntegersPage.ReferenceHash(3, 263, 0), IntegersPage.ReferenceHash(263, 3, 0));

        // The key the pixels compete with keeps the top 44 bits of the hash and names the pixel.
        Assert.Equal(0xFFFFFFFFFFF00000UL | 0x12345, HashPixels.Key(ulong.MaxValue, 0x12345));
        Assert.Equal(0xABCDEF0123400000UL | 5, HashPixels.Key(0xABCDEF01234FFFFFUL, 5));
        Assert.Equal((1UL << 20) - 1, HashPixels.IndexMask);
    }

    [Fact]
    public void Integers_ReferenceLut_StretchesThePresentLevelsOverTheWholeRange()
    {
        // Four levels present, 100 pixels in all: 10 at 50, 40 at 51, 30 at 100, 20 at 200.
        var counts = new uint[256];
        counts[50] = 10;
        counts[51] = 40;
        counts[100] = 30;
        counts[200] = 20;

        var lut = new byte[256];
        IntegersPage.ReferenceLut(counts, lut);

        // (cdf - 10) / (100 - 10) * 255, rounded: the darkest level present becomes 0, the brightest 255.
        Assert.All(lut[..51], static level => Assert.Equal(0, level));
        Assert.Equal(113, lut[51]);            // 40 / 90 * 255 = 113.3
        Assert.All(lut[51..100], static level => Assert.Equal(113, level));
        Assert.Equal(198, lut[100]);           // 70 / 90 * 255 = 198.3
        Assert.Equal(255, lut[200]);
        Assert.Equal(255, lut[255]);
        Assert.True(lut.Zip(lut.Skip(1)).All(static pair => pair.First <= pair.Second), "the table never decreases");

        // One level only: there is nothing to stretch, and nothing divides by zero.
        Array.Clear(counts);
        counts[77] = 1000;
        IntegersPage.ReferenceLut(counts, lut);
        Assert.All(lut, static level => Assert.Equal(0, level));
    }

    [MetalFact]
    public void Integers_EqualisesTheByteImageInPlace_AndHashesEveryPixelIn64Bits()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using IntegersPage page = new(device);

        page.Compute(Frame);
        LabCheck check = page.Check();
        Assert.True(check.Passed, check.Summary);

        byte[] original = page.Original.ToArray(), equalised = page.Equalised.ToArray(), lut = page.Lut.ToArray();
        uint[] counts = page.Counts.ToArray();
        ulong[] hashes = page.Hashes.ToArray();

        // The generated picture is washed out: it uses well under half of the 256 levels.
        Assert.InRange(original.Min(), 40, 110);
        Assert.InRange(original.Max(), 130, 200);
        Assert.Equal((uint)(IntegersPage.Size * IntegersPage.Size), counts.Aggregate(0u, static (sum, count) => sum + count));

        // The histogram is that of the bytes, and the table is the one plain C# builds from it.
        var expectedCounts = new uint[256];

        foreach (byte level in original)
        {
            expectedCounts[level]++;
        }

        var expectedLut = new byte[256];
        IntegersPage.ReferenceLut(expectedCounts, expectedLut);

        Assert.Equal(expectedCounts, counts);
        Assert.Equal(expectedLut, lut);

        // In place: every byte is now its table entry, and the levels span the whole range.
        Assert.Equal(original.Select(level => lut[level]), equalised);
        Assert.Equal(0, equalised.Min());
        Assert.Equal(255, equalised.Max());

        // Every hash is the reference hash of its pixel, and they are all distinct.
        for (int i = 0; i < hashes.Length; i += 331)
        {
            Assert.Equal(IntegersPage.ReferenceHash(i % IntegersPage.Size, i / IntegersPage.Size, 0), hashes[i]);
        }

        Assert.Equal(hashes.Length, hashes.Distinct().Count());

        // The 64-bit atomic maximum holds the largest key, which names its pixel.
        ulong largest = hashes.Select(HashPixels.Key).Max();
        int winner = (int)(page.Largest & HashPixels.IndexMask);

        Assert.Equal(largest, page.Largest);
        Assert.Equal(hashes[winner] & ~HashPixels.IndexMask, page.Largest & ~HashPixels.IndexMask);

        // The bits are well mixed: each of the 64 is set in about half of the million hashes.
        for (int bit = 0; bit < 64; bit++)
        {
            int set = hashes.Count(hash => ((hash >> bit) & 1) != 0);
            Assert.InRange(set, (hashes.Length / 2) - 4000, (hashes.Length / 2) + 4000);
        }

        // Computing again starts from the original again: the result does not drift.
        page.Compute(Frame);
        Assert.Equal(equalised, page.Equalised.ToArray());
    }

    [MetalFact]
    public void Integers_PageShowsTheImageBeforeAndAfter_TheHashes_AndItsCaptions()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using IntegersPage page = new(device);
        using LabCanvas canvas = new(device);

        page.Compute(Frame);
        canvas.ClearText();
        page.Draw(canvas, Frame);

        ReadOnlySpan<uint> pixels = canvas.Pixels.AsReadOnlySpan();
        ReadOnlySpan<byte> original = page.Original, equalised = page.Equalised;
        ReadOnlySpan<ulong> hashes = page.Hashes;
        int winner = (int)(page.Largest & HashPixels.IndexMask);
        var random = new Random(9);

        static uint Gray(byte level) => 0xFF000000u | ((uint)level << 16) | ((uint)level << 8) | level;

        for (int n = 0; n < 2000; n++)
        {
            int u = random.Next(DrawIntegers.PanelSize), v = random.Next(DrawIntegers.PanelSize);
            int source = ((v * IntegersPage.Size / DrawIntegers.PanelSize) * IntegersPage.Size) + (u * IntegersPage.Size / DrawIntegers.PanelSize);
            int block = DrawIntegers.HashBlock;
            int hashSource = ((v / block * block * IntegersPage.Size / DrawIntegers.PanelSize) * IntegersPage.Size) + (u / block * block * IntegersPage.Size / DrawIntegers.PanelSize);
            int row = (DrawIntegers.PanelY + v) * LabCanvas.Width;

            // The first two panels are the bytes as gray; the third is three bytes of each hash.
            Assert.Equal(Gray(original[source]), pixels[row + DrawIntegers.PanelX + u]);
            Assert.Equal(Gray(equalised[source]), pixels[row + DrawIntegers.PanelX + DrawIntegers.PanelPitch + u]);

            int markerX = (winner % IntegersPage.Size) * DrawIntegers.PanelSize / IntegersPage.Size;
            int markerY = (winner / IntegersPage.Size) * DrawIntegers.PanelSize / IntegersPage.Size;

            if (Math.Abs(u - markerX) > 15 || Math.Abs(v - markerY) > 15)
            {
                // One hash per 4 x 4 block of screen pixels.
                ulong hash = hashes[hashSource];
                uint color = 0xFF000000u | ((uint)(hash & 255) << 16) | ((uint)((hash >> 28) & 255) << 8) | (uint)(hash >> 56);
                Assert.Equal(color, pixels[row + DrawIntegers.PanelX + (2 * DrawIntegers.PanelPitch) + u]);
            }
        }

        // Between the panels: background.
        Assert.Equal(pixels[0], pixels[((DrawIntegers.PanelY + 10) * LabCanvas.Width) + DrawIntegers.PanelX + DrawIntegers.PanelSize + 10]);

        string[] text = canvas.TextLines();
        Assert.Contains(text, static line => line.Contains("READWRITEBUFFER<BYTE> IMAGE", StringComparison.Ordinal));
        Assert.Contains(text, static line => line.Contains("EQUALISED IN PLACE BY A BYTE LUT", StringComparison.Ordinal));
        Assert.Contains(text, static line => line.Contains("64-BIT HASH PER PIXEL, 1 IN 16", StringComparison.Ordinal));

        // Nothing a page writes runs into the next column of panels, or off the frame.
        Assert.All(text, static line => Assert.True(line.Length <= LabCanvas.Columns - 2, line));
        Assert.Contains(text, static line => line.Contains("LEVELS 0..255 OF 0..255", StringComparison.Ordinal)); // after equalisation
        Assert.Contains(text, line => line.Contains(FormattableString.Invariant($"GPU.ATOMICMAX: PIXEL ({winner % IntegersPage.Size}, {winner / IntegersPage.Size})"), StringComparison.Ordinal));
    }

    [MetalFact]
    public void FastMathAndIndirect_SayWhereTheirKernelCameFrom()
    {
        // The lab's shaders are precompiled when it is built, and its library is copied here with it;
        // without the Metal toolchain there is none, and the pages say so instead.
        string library = Path.Combine(Path.GetDirectoryName(typeof(FastMathPage).Assembly.Location)!, "SharpCompute.Samples.ComputeLab.metallib");
        PipelineOrigin expected = File.Exists(library) ? PipelineOrigin.Precompiled : PipelineOrigin.CompiledAtRunTime;
        string text = expected == PipelineOrigin.Precompiled ? "kernel: precompiled" : "kernel: compiled at run time";

        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using LabCanvas canvas = new(device);
        using FastMathPage fastMath = new(device);
        using IndirectPage indirect = new(device);

        foreach (ILabPage page in new ILabPage[] { fastMath, indirect })
        {
            for (int variant = 0; variant < page.Variants.Count; variant++)
            {
                page.Variant = variant;
                page.Compute(Frame);

                Assert.Equal(expected, page.KernelOrigin);
                Assert.Equal(text, LabView.Describe(page.KernelOrigin));

                page.Draw(canvas, Frame);
                Assert.Contains(canvas.TextLines(), line => line.Contains(text, StringComparison.OrdinalIgnoreCase));
            }
        }

        // Fast math is an option of the precompiled kernel too: both variants came out of the library.
        Assert.Equal(expected, device.GetPipelineOrigin<WaveField>());
        Assert.Equal(expected, device.GetPipelineOrigin<WaveFieldFast>());

        // A page that does not report an origin shows nothing.
        using BitsPage bits = new(device);
        Assert.Equal(PipelineOrigin.None, ((ILabPage)bits).KernelOrigin);
        Assert.Equal(string.Empty, LabView.Describe(PipelineOrigin.None));
    }

    [MetalFact]
    public void HalfPrecision_BothBlursMatchForCpuBitForBit_AndTheHalfErrorIsWhatElevenBitsGive()
    {
        using HalfPrecisionPage page = new(GraphicsDevice.GetDefault());

        page.Compute(Frame);
        LabCheck wide = page.Check();
        Assert.True(wide.Passed, wide.Summary);
        Assert.Equal((8, 289), (page.Radius, page.Taps));
        Assert.StartsWith("bit for bit: float, half (96x96 tile), mask.", wide.Summary);
        Assert.NotNull(page.Baseline);
        Assert.Equal("the same blur in float", page.Baseline!.Value.Name);
        Assert.True(page.GpuMilliseconds > 0 && page.FloatGpuMilliseconds > 0);

        // The float blur is exact up to its one division; the half blur rounds every tap to 11 bits,
        // which is five orders of magnitude more and still under 1 % of the range.
        Assert.InRange(page.FloatMaxError, 0, HalfPrecisionPage.FloatErrorBound);
        Assert.InRange(page.HalfMaxError, 1e-4, HalfPrecisionPage.HalfErrorBound);
        Assert.InRange(page.HalfMeanError, 1e-5, page.HalfMaxError);
        Assert.True(page.HalfMaxError > 1000 * page.FloatMaxError);
        double wideError = page.HalfMaxError;

        // The alpha channel is the sum of the weights divided by the same sum, in the same order: exactly 1.
        Assert.All(new[] { 0, 12345, HalfPrecisionPage.Size * HalfPrecisionPage.Size - 1 }, i => Assert.Equal((Half)1f, page.HalfResult[i].W));
        Assert.Equal(1f, page.FloatResult[777].W);

        // A blur of values in 0..1 stays in 0..1, and the half result is the float result within the error.
        Half4 half = page.HalfResult[(500 * HalfPrecisionPage.Size) + 300];
        Float4 single = page.FloatResult[(500 * HalfPrecisionPage.Size) + 300];
        Assert.InRange((float)half.X, 0f, 1f);
        Assert.InRange(MathF.Abs((float)half.X - single.X), 0f, (float)page.HalfMaxError + 1e-6f);

        // Fewer taps, a smaller sum, a smaller error.
        Assert.True(page.OnKeyDown(Key.R));
        Assert.Equal(1, page.Variant);
        page.Compute(Frame);
        LabCheck narrow = page.Check();
        Assert.True(narrow.Passed, narrow.Summary);
        Assert.Equal((4, 81), (page.Radius, page.Taps));
        Assert.True(page.HalfMaxError < wideError, $"{page.HalfMaxError} with 81 taps, {wideError} with 289");
        Assert.False(page.OnKeyDown(Key.F));

        // Both blurs are compiled without contraction (which is what makes the half one bit-exact on the
        // CPU), and differ in their types and in nothing else.
        Assert.Equal(ShaderCompileOptions.NoFpContract, Options<TentBlurFloat>());
        Assert.Equal(ShaderCompileOptions.NoFpContract, Options<TentBlurHalf>());
        Assert.Equal(ShaderCompileOptions.None, Options<HighlightMask>());
        Assert.Equal(
            Kernel<TentBlurFloat>().Replace(nameof(TentBlurFloat), "X").Replace("packed_float4", "V").Replace("float4(0.0f)", "V0").Replace("float4", "V").Replace("0.0f", "0").Replace("float", "S"),
            Kernel<TentBlurHalf>().Replace(nameof(TentBlurHalf), "X").Replace("packed_half4", "V").Replace("half4(half(0.0f))", "V0").Replace("half4", "V").Replace("half(0.0f)", "0").Replace("half", "S"));
        Assert.Contains("sum += image[int(uint(row) + uint(dx))] * w;", Kernel<TentBlurHalf>());

        static ShaderCompileOptions Options<T>()
            where T : struct, IComputeShaderDescriptor<T> => T.CompileOptions;

        // The kernel function: what comes before it is the layout guard of the element type, which does differ.
        static string Kernel<T>()
            where T : struct, IComputeShaderDescriptor<T> => T.MslSource[T.MslSource.IndexOf("kernel void", StringComparison.Ordinal)..];
    }

    [MetalFact]
    public void HalfPrecision_MaskIsAComponentwiseComparison_AndFollowsTheThreshold()
    {
        using HalfPrecisionPage page = new(GraphicsDevice.GetDefault());

        page.Compute(Frame);
        Half3 threshold = page.Threshold;
        ReadOnlySpan<Half4> blurred = page.HalfResult;
        ReadOnlySpan<uint> highlights = page.Highlights;
        int none = 0, some = 0, all = 0;

        for (int i = 0; i < blurred.Length; i += 37)
        {
            // Restated with scalar C#: a channel is kept where it is above its threshold.
            Half4 color = blurred[i];
            bool r = color.X > threshold.X, g = color.Y > threshold.Y, b = color.Z > threshold.Z;
            Float4 expected = r && g && b
                ? new Float4(1f, 1f, 1f, 1f)
                : new Float4(r ? (float)color.X : 0f, g ? (float)color.Y : 0f, b ? (float)color.Z : 0f, r || g || b ? 1f : 0f);

            Assert.Equal(Gpu.PackUnorm4x8(expected), highlights[i]);
            (none, some, all) = (none + (!r && !g && !b ? 1 : 0), some + ((r || g || b) && !(r && g && b) ? 1 : 0), all + (r && g && b ? 1 : 0));
        }

        // The picture has all three kinds of pixel.
        Assert.True(none > 100 && some > 100 && all > 100, $"{none} with no channel, {some} with some, {all} with all");

        // Another time, another threshold, another mask.
        uint[] before = highlights.ToArray();
        page.Compute(new LabFrame(Headless.Time + 2f, Deterministic: true));
        Assert.NotEqual(threshold, page.Threshold);
        Assert.False(before.AsSpan().SequenceEqual(page.Highlights));
        Assert.True(page.Check().Passed);
    }

    [Fact]
    public void HalfPrecision_SourceIsExactInAHalf()
    {
        Float4[] source = HalfPrecisionPage.CreateSource();
        Assert.Equal(HalfPrecisionPage.Padded * HalfPrecisionPage.Padded, source.Length);

        for (int i = 0; i < source.Length; i += 7)
        {
            Float4 pixel = source[i];
            Assert.Equal(pixel.X, (float)(Half)pixel.X);
            Assert.Equal(pixel.Y, (float)(Half)pixel.Y);
            Assert.Equal(pixel.Z, (float)(Half)pixel.Z);
            Assert.Equal(1f, pixel.W);
            Assert.InRange(pixel.X, 0f, 1f);
            Assert.Equal(MathF.Round(pixel.Y * 256f), pixel.Y * 256f);
        }

        // Not a flat picture: there is something to blur.
        Assert.True(source.Select(static p => p.X).Distinct().Count() > 100);
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
            int bits = LabPages.Find(pages, "bits");
            LabCheck check = Headless.Measure(pages[bits], Frame, out LabTiming timing);
            Assert.True(check.Passed);
            Assert.True(timing.GpuMilliseconds > 0 && timing.CpuMilliseconds > 0);

            int TextPixelsInRow(int row)
            {
                Rgba32[] pixels = canvas.ToArray();
                int count = 0;

                for (int y = LabCanvas.RowY(row); y < LabCanvas.RowY(row + 1); y++)
                {
                    for (int x = 0; x < LabCanvas.Width; x++)
                    {
                        count += pixels[(y * LabCanvas.Width) + x].B > 200 ? 1 : 0;
                    }
                }

                return count;
            }

            LabView.Compose(canvas, pages, bits, Frame, check, timing: null);
            Assert.Equal(0, TextPixelsInRow(2));
            Assert.DoesNotContain("(PAUSED)", canvas.TextLines()[LabCanvas.Rows - 1]);

            LabView.Compose(canvas, pages, bits, Frame, check, timing);
            Assert.True(TextPixelsInRow(2) > 300);
            Assert.StartsWith("GPU ", canvas.TextLines()[2]);
            Assert.Contains(" MS (ALL CORES)", canvas.TextLines()[2]);

            // Before the first check there is a placeholder instead of a result; paused, the help line says so.
            LabView.Compose(canvas, pages, bits, Frame, check: null, timing: null, paused: true);
            Assert.True(TextPixelsInRow(0) > 300);
            Assert.Equal("CHECKING AGAINST THE CPU...", canvas.TextLines()[1]);
            Assert.EndsWith("(PAUSED)", canvas.TextLines()[LabCanvas.Rows - 1]);

            // The title: the lab and the page number, the topic, the page.
            Assert.Equal(Invariant($"COMPUTELAB [{bits + 1}/{pages.Count}]  MATH  BITS: BIT AND PACKING INTRINSICS  -  1280 X 720 PIXELS"), canvas.TextLines()[0]);
        }
        finally
        {
            DisposeAll(pages);
        }
    }

    [MetalFact]
    public void Record_WritesAPagesFramesOverTime_TheSameEveryRun()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string root = Path.Combine(Path.GetTempPath(), "sharpcompute-computelab-record-" + Guid.NewGuid().ToString("N"));
        List<ILabPage> pages = LabPages.Create(device);

        try
        {
            int histogram = LabPages.Find(pages, "histogram");
            Assert.Equal(3, Headless.Record(device, pages, histogram, Path.Combine(root, "a"), seconds: 0.5, fps: 6));
            Assert.Equal(3, Headless.Record(device, pages, histogram, Path.Combine(root, "b"), seconds: 0.5, fps: 6));

            string[] frames = ["histogram-0001.png", "histogram-0002.png", "histogram-0003.png"];
            Assert.Equal(frames, Directory.GetFiles(Path.Combine(root, "a")).Select(Path.GetFileName).Order());

            byte[][] first = frames.Select(name => File.ReadAllBytes(Path.Combine(root, "a", name))).ToArray();
            Assert.All(frames, name => Assert.True(File.ReadAllBytes(Path.Combine(root, "a", name)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "b", name))), $"{name} differs between two runs"));

            // The animation moves from frame to frame, and the first frame is the headless picture's moment.
            Assert.False(first[0].AsSpan().SequenceEqual(first[1]));
            Assert.False(first[1].AsSpan().SequenceEqual(first[2]));
        }
        finally
        {
            DisposeAll(pages);

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
    [InlineData(1, "--page", "shuffle", "--headless")]
    [InlineData(1, "--record", "out")]
    [InlineData(1, "--headless", "--record", "out")]
    [InlineData(1, "--page", "histogram", "--record", "out")]
    [InlineData(1, "--headless", "--page", "histogram", "--record", "out", "--seconds", "0")]
    [InlineData(1, "--headless", "--page", "histogram", "--record", "out", "--fps", "120")]
    public void Main_HandlesItsArguments_WithoutOpeningAWindow(int exitCode, params string[] arguments)
    {
        Assert.Equal(exitCode, Program.Main(arguments));
    }
}
