// Original SharpCompute sample (MIT).
using System.Collections.Generic;
using SharpCompute.Samples.ComputeLab;
using SharpCompute.Samples.ComputeLab.Pages;
using static System.FormattableString;

namespace SharpCompute.Samples.SimdGroups;

/// <summary>
/// Puts one page on the canvas with the sample's own text around it: the title, the check result, the
/// timings and the help line. Shared by the window and the headless mode, so both show the same thing.
/// </summary>
/// <remarks>
/// ComputeLab's own <see cref="LabView"/> is not used because it names ComputeLab in the title and says
/// "all cores" after the CPU time, and the reduction's reference here runs on one core.
/// </remarks>
public static class SimdGroupsView
{
    /// <summary>
    /// "GPU 0.41 ms   CPU 2.75 ms   GPU is 6.7x faster", or the other way round. What the CPU ran (one
    /// core or all) is in the page's check line.
    /// </summary>
    public static string Describe(LabTiming timing)
    {
        (double gpu, double cpu) = (timing.GpuMilliseconds, timing.CpuMilliseconds);
        string ratio = gpu <= 0 || cpu <= 0
            ? string.Empty
            : gpu <= cpu ? Invariant($"   GPU is {cpu / gpu:F1}x faster") : Invariant($"   CPU is {gpu / cpu:F1}x faster");

        return Invariant($"GPU {gpu:F2} ms   CPU {cpu:F2} ms{ratio}");
    }

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

        canvas.Text(1, 0, Invariant($"SimdGroups  [{index + 1}/{pages.Count}]  {page.Name}: {page.Feature}  -  {page.Variants[page.Variant]}{(paused ? "  (paused)" : string.Empty)}"));

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
            canvas.Text(1, 2, Describe(times), TextColor.Cool);
        }

        string own = page.Help.Length == 0 ? string.Empty : page.Help + "   ";
        canvas.Text(1, LabCanvas.Rows - 1, Invariant($"<- -> page   {own}Space pause   S save PNG   Esc quit"), TextColor.Dim);

        canvas.ComposeText();
    }
}
