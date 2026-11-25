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
    /// <summary>"GPU 0.41 ms   CPU 2.75 ms   GPU is 6.7x faster", or the other way round.</summary>
    public override string ToString()
    {
        string ratio = GpuMilliseconds <= 0 || CpuMilliseconds <= 0
            ? string.Empty
            : GpuMilliseconds <= CpuMilliseconds
                ? Invariant($"   GPU is {CpuMilliseconds / GpuMilliseconds:F1}x faster")
                : Invariant($"   CPU is {GpuMilliseconds / CpuMilliseconds:F1}x faster");

        return Invariant($"GPU {GpuMilliseconds:F2} ms   CPU {CpuMilliseconds:F2} ms (all cores){ratio}");
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
    /// Draws <paramref name="pages"/>[<paramref name="index"/>] and the surrounding text into
    /// <paramref name="canvas"/>. <paramref name="check"/> is the latest comparison with the CPU (null
    /// before the first one); <paramref name="timing"/> is left out of pictures that must be reproducible.
    /// </summary>
    public static void Compose(LabCanvas canvas, IReadOnlyList<ILabPage> pages, int index, LabFrame frame, LabCheck? check, LabTiming? timing, bool paused = false)
    {
        ILabPage page = pages[index];

        canvas.ClearText();
        page.Draw(canvas, frame);

        canvas.Text(1, 0, Invariant($"ComputeLab  [{index + 1}/{pages.Count}]  {page.Name}: {page.Feature}  -  {page.Variants[page.Variant]}{(paused ? "  (paused)" : string.Empty)}"));

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
            canvas.Text(1, 2, times.ToString(), TextColor.Cool);
        }

        string own = page.Help.Length == 0 ? string.Empty : page.Help + "   ";
        canvas.Text(1, LabCanvas.Rows - 1, Invariant($"<- -> page   {own}Space pause   S save PNG   Esc quit"), TextColor.Dim);

        canvas.ComposeText();
    }
}
