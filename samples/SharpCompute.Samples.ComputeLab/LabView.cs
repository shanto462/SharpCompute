// Original SharpCompute sample (MIT).
using System.Collections.Generic;
using SharpCompute.Samples.ComputeLab.Pages;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab;

/// <summary>GPU and CPU times of a page's feature kernel, for the timing line; null when there is none to show.</summary>
/// <param name="GpuMilliseconds">The GPU dispatch, commit to completion.</param>
/// <param name="CpuMilliseconds">The same work on the CPU.</param>
public readonly record struct LabTiming(double GpuMilliseconds, double CpuMilliseconds)
{
    /// <summary>What the CPU ran on (<see cref="ILabPage.CpuNote"/>), shown in brackets after its time; empty for nothing.</summary>
    public string CpuNote { get; init; } = "all cores";

    /// <summary>The dispatch the page compares its feature kernel with (<see cref="ILabPage.Baseline"/>), timed the same way, or null.</summary>
    public LabBaseline? Baseline { get; init; }

    /// <summary>"global atomics: GPU 1.90 ms   this page's kernel is 4.6x faster", or slower; null without a <see cref="Baseline"/>.</summary>
    public string? BaselineText
    {
        get
        {
            if (Baseline is not { } baseline)
            {
                return null;
            }

            string ratio = GpuMilliseconds <= 0 || baseline.GpuMilliseconds <= 0
                ? string.Empty
                : GpuMilliseconds <= baseline.GpuMilliseconds
                    ? Invariant($"   this page's kernel is {baseline.GpuMilliseconds / GpuMilliseconds:F1}x faster")
                    : Invariant($"   this page's kernel is {GpuMilliseconds / baseline.GpuMilliseconds:F1}x slower");

            return Invariant($"{baseline.Name}: GPU {baseline.GpuMilliseconds:F2} ms{ratio}");
        }
    }

    /// <summary>"GPU 0.41 ms   CPU 2.75 ms (all cores)   GPU is 6.7x faster", or the other way round.</summary>
    public override string ToString()
    {
        string note = string.IsNullOrEmpty(CpuNote) ? string.Empty : $" ({CpuNote})";
        string ratio = GpuMilliseconds <= 0 || CpuMilliseconds <= 0
            ? string.Empty
            : GpuMilliseconds <= CpuMilliseconds
                ? Invariant($"   GPU is {CpuMilliseconds / GpuMilliseconds:F1}x faster")
                : Invariant($"   CPU is {GpuMilliseconds / CpuMilliseconds:F1}x faster");

        return Invariant($"GPU {GpuMilliseconds:F2} ms   CPU {CpuMilliseconds:F2} ms{note}{ratio}");
    }
}

/// <summary>
/// Puts one page on the canvas with the lab's own text around it: the title, the check result, the
/// timings and the help line. Shared by the window and the headless mode, so both show the same thing.
/// </summary>
public static class LabView
{
    /// <summary>The first text row a page may use; rows 0..2 are the title, the check and the timings.</summary>
    public const int FirstPageRow = 3;

    /// <summary>
    /// The page's times for the timing line and the headless summary: <paramref name="timing"/> with
    /// the page's <see cref="ILabPage.CpuNote"/>, then the page's rate if it is an <see cref="IRatedPage"/>.
    /// </summary>
    public static string TimingLine(ILabPage page, LabTiming timing)
    {
        string text = (timing with { CpuNote = page.CpuNote }).ToString();
        return page is IRatedPage rated ? text + "   " + rated.Rate(timing.GpuMilliseconds, timing.CpuMilliseconds) : text;
    }

    /// <summary>
    /// The title row, in three parts: the lab and the page number, the page's topic, and the page with
    /// its feature and active variant. <see cref="TitleLength"/> of it fits the row for every page.
    /// </summary>
    public static (string Lab, string Topic, string Page) Title(IReadOnlyList<ILabPage> pages, int index)
    {
        ILabPage page = pages[index];
        return (Invariant($"ComputeLab [{index + 1}/{pages.Count}]"), page.Topic, Invariant($"{page.Name}: {page.Feature}  -  {page.Variants[page.Variant]}"));
    }

    /// <summary>The columns the title takes, from its first character to its last, as <see cref="Compose"/> lays it out.</summary>
    public static int TitleLength((string Lab, string Topic, string Page) title) => title.Lab.Length + 2 + title.Topic.Length + 2 + title.Page.Length;

    /// <summary>The help line at the bottom; while paused it says so, at its end, where there is room.</summary>
    public static string HelpLine(ILabPage page, bool paused = false)
    {
        string own = page.Help.Length == 0 ? string.Empty : page.Help + "   ";
        return Invariant($"<- -> page   {own}Space {(paused ? "resume" : "pause")}   S save PNG   Esc quit{(paused ? "      (paused)" : string.Empty)}");
    }

    /// <summary>
    /// A page's <see cref="ILabPage.KernelOrigin"/> for a label or the summary: "kernel: precompiled"
    /// (when the lab was built), "kernel: compiled at run time", or an empty string for a page that does not say.
    /// </summary>
    public static string Describe(PipelineOrigin origin) => origin switch
    {
        PipelineOrigin.Precompiled => "kernel: precompiled",
        PipelineOrigin.CompiledAtRunTime => "kernel: compiled at run time",
        _ => string.Empty,
    };

    /// <summary>
    /// Draws <paramref name="pages"/>[<paramref name="index"/>] and the surrounding text into
    /// <paramref name="canvas"/>. <paramref name="check"/> is the latest comparison with the CPU (null
    /// before the first one); <paramref name="timing"/> is left out of pictures that must be reproducible.
    /// </summary>
    public static void Compose(LabCanvas canvas, IReadOnlyList<ILabPage> pages, int index, LabFrame frame, LabCheck? check, LabTiming? timing, bool paused = false)
    {
        ILabPage page = pages[index];

        canvas.ClearText();
        page.Draw(canvas, frame);

        (string lab, string topic, string title) = Title(pages, index);
        int column = 1;
        canvas.Text(column, 0, lab);
        column += lab.Length + 2;
        canvas.Text(column, 0, topic, TextColor.Accent);
        column += topic.Length + 2;
        canvas.Text(column, 0, title);

        if (check is { } result)
        {
            canvas.Text(1, 1, (result.Passed ? "CHECK PASSED: " : "CHECK FAILED: ") + result.Summary, result.Passed ? TextColor.Good : TextColor.Bad);
        }
        else
        {
            canvas.Text(1, 1, "Checking against the CPU...", TextColor.Dim);
        }

        if (timing is { } times)
        {
            canvas.Text(1, 2, TimingLine(page, times), TextColor.Cool);
        }

        canvas.Text(1, LabCanvas.Rows - 1, HelpLine(page, paused), paused ? TextColor.Accent : TextColor.Dim);

        canvas.ComposeText();
    }
}
