// Original SharpCompute sample (MIT).
using System.Collections.Generic;
using SharpCompute.Samples.ComputeLab.Pages;

namespace SharpCompute.Samples.GroupShared.Pages;

// The pages are ComputeLab pages: they implement its ILabPage, draw into its LabCanvas and depend on
// nothing else of this sample, so each file can be hosted by ComputeLab itself as it is.

/// <summary>The pages, in ←/→ order.</summary>
public static class GroupSharedPages
{
    public static List<ILabPage> Create(GraphicsDevice device) =>
    [
        new ReductionPage(device),
        new PrefixSumPage(device),
        new MatMulPage(device),
        new BlurPage(device),
    ];
}

/// <summary>A page with a figure of merit beyond milliseconds, shown after the times and in the headless summary.</summary>
public interface IRatedPage
{
    /// <summary>The rate for the measured times, e.g. "GPU 900 GFLOP/s   CPU 38 GFLOP/s".</summary>
    string Rate(double gpuMilliseconds, double cpuMilliseconds);
}
