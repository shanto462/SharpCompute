using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpCompute.Tests.Conformance.Semantics;

/// <summary>
/// Compares what the GPU and <c>ForCpu</c> wrote, for shaders in which every thread writes a fixed
/// number of slots. A failure names each differing slot, how many threads differ there, and the first
/// such thread with its inputs, so one run says which statement and which operands went wrong.
/// </summary>
internal static class SlotComparison
{
    /// <summary>
    /// Asserts that <paramref name="gpu"/> and <paramref name="cpu"/> are equal slot by slot.
    /// <paramref name="names"/> labels the slots (a slot without a label is shown by number), and
    /// <paramref name="describe"/> the inputs of a thread.
    /// </summary>
    public static void AssertEqual<T>(string title, T[] gpu, T[] cpu, int slots, IReadOnlyDictionary<int, string>? names = null, Func<int, string>? describe = null)
        where T : IEquatable<T>
    {
        string? report = Differences(gpu, cpu, slots, names, describe);
        Assert.True(report is null, $"{title}: the GPU and ForCpu differ.\n{report}");
    }

    /// <summary>A report of the differing slots, or null when the two arrays are equal.</summary>
    public static string? Differences<T>(T[] gpu, T[] cpu, int slots, IReadOnlyDictionary<int, string>? names = null, Func<int, string>? describe = null)
        where T : IEquatable<T>
    {
        Assert.Equal(gpu.Length, cpu.Length);
        Assert.True(gpu.Length % slots == 0, $"{gpu.Length} values are not a whole number of threads of {slots} slots.");

        int threads = gpu.Length / slots;
        StringBuilder report = new();
        int total = 0;

        for (int slot = 0; slot < slots; slot++)
        {
            int[] bad = Enumerable.Range(0, threads).Where(t => !gpu[(t * slots) + slot].Equals(cpu[(t * slots) + slot])).ToArray();

            if (bad.Length == 0)
            {
                continue;
            }

            total += bad.Length;
            int first = bad[0];
            string name = names is not null && names.TryGetValue(slot, out string? label) ? $"{slot} ({label})" : slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            report.AppendLine($"  slot {name}: {bad.Length} of {threads} threads; first thread {first}{(describe is null ? "" : " " + describe(first))}: GPU {gpu[(first * slots) + slot]}, ForCpu {cpu[(first * slots) + slot]}");
        }

        return total == 0 ? null : report.Append($"  {total} of {gpu.Length} values differ ({threads} threads, {slots} slots).").ToString();
    }
}
