// Original SharpCompute sample (MIT).
using System.Collections.Generic;
using SharpCompute.Samples.ComputeLab.Pages;

namespace SharpCompute.Samples.SimdGroups.Pages;

// The pages are ComputeLab pages: they implement its ILabPage, draw into its LabCanvas and depend on
// nothing else of this sample, so each file can be hosted by ComputeLab itself as it is. ComputeLab
// has a CompactionPage and a DrawCompaction of its own (compaction by an atomic counter); inside this
// namespace the names mean this sample's, and code that imports both namespaces has to say which.

/// <summary>The pages, in ←/→ order.</summary>
public static class SimdGroupsPages
{
    public static List<ILabPage> Create(GraphicsDevice device) =>
    [
        new ReductionPage(device),
        new CompactionPage(device),
        new ShufflePage(device),
    ];
}
