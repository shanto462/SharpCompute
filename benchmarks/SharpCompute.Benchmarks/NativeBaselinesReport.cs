using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SharpCompute.Benchmarks.Shaders;
using static System.FormattableString;

namespace SharpCompute.Benchmarks;

/// <summary>Renders the "Native baselines and roofline" section of RESULTS.md from a <see cref="NativeBaselineData"/>.</summary>
public static class NativeBaselinesReport
{
    public const string Heading = "## Native baselines and roofline";

    /// <summary>
    /// What others publish for a chip's ceilings, to hold the measured ones against. Only for the
    /// machine RESULTS.md was produced on; on any other the section shows the measured ceilings alone.
    /// </summary>
    private static readonly (string Gpu, string Flops, string Bandwidth)[] Published =
    [
        (
            "Apple M4 Pro, 16 cores",
            "6.41 TFLOPS ([cpu-monkey](https://www.cpu-monkey.com/en/igpu-apple_m4_pro_16_core)) to 6.82 TFLOPS "
                + "([Wikipedia's Apple silicon table, as quoted by Low End Mac](https://lowendmac.com/2025/apple-silicon-m4-pro-chip-specs/)): "
                + "estimates of ALU count × clock, not Apple's figures",
            "273 GB/s ([Apple](https://www.apple.com/newsroom/2024/10/apple-introduces-m4-pro-and-m4-max/))"),
    ];

    public static void Append(StringBuilder md, NativeBaselineData data)
    {
        IReadOnlyList<NativeRow> rows = data.Rows;
        NativeRow? flopPeak = rows.Where(r => r.Flops > 0).MaxBy(r => r.FlopRate);
        NativeRow? bytePeak = rows.Where(r => r.Bytes > 0).MaxBy(r => r.ByteRate);

        if (flopPeak is null || bytePeak is null)
        {
            return;
        }

        Machine machine = data.Machine;

        md.AppendLine(Heading);
        md.AppendLine();
        md.AppendLine("The tables above compare the GPU with the CPU. This section compares the kernels SharpCompute generates with **native "
            + "controls on the same GPU**: the same kernel written by hand in MSL, and for single-precision matrix multiply (SGEMM) Apple's "
            + "`MPSMatrixMultiplication` and a hand-written kernel on Metal's matrix units (`simdgroup_matrix`), which a C# shader cannot use. "
            + "A generated kernel that matches its hand-written twin shows the generator costs nothing; a gap to MPS shows what is still out of reach from C#.");
        md.AppendLine();
        md.AppendLine(Invariant($"Measured by `scripts/run-benchmarks.sh --native-baselines` on {data.Before.Time:yyyy-MM-dd HH:mm zzz} ({data.ElapsedSeconds:F0} s) on ")
            + $"{machine.Model} (GPU: {machine.Gpu}; {machine.Memory}; {machine.Os}).");
        md.AppendLine();
        md.AppendLine("- **GPU time** is the dispatch's own time on the GPU: `GraphicsDevice.LastGpuTime`, the interval between Metal's `GPUStartTime` and "
            + "`GPUEndTime` of its command buffer. (For MPS, which SharpCompute does not dispatch, the same two properties of its command buffer.) It is the column to compare kernels by.");
        md.AppendLine("- **Wall time** is a stopwatch around the same synchronous call (`device.For`: encode, commit, wait), which is what a caller waits for. "
            + "The difference is the submission and wake-up of one command buffer.");
        md.AppendLine(Invariant($"- **Each number** is the median of {data.Passes} passes; a pass's value is the median of {data.Samples} dispatches after {data.Warmups} ")
            + "that are thrown away, with the kernels of a workload taking turns (in a new order every round) and newly allocated buffers in every pass. "
            + "**Passes** is the lowest and the highest pass value of the GPU time: how far the passes were apart.");
        md.AppendLine("- **Inputs** are seeded random numbers, the same buffers for every kernel of a workload, and `--verify` checks every kernel here against the CPU.");
        md.AppendLine("- All hand-written kernels are compiled at run time by the runtime with its own options (MSL 3.2, safe math mode, precise math functions) "
            + "and dispatched by the same `For` as a generated shader, so a row differs from its twin in the kernel's source alone.");
        md.AppendLine();
        AppendSnapshot(md, "Start", data.Before);
        AppendSnapshot(md, "End", data.After);
        md.AppendLine();

        md.AppendLine("### The machine's ceilings");
        md.AppendLine();
        md.AppendLine("A roofline holds a kernel against the two things that can limit it: how fast the GPU can do arithmetic and how fast it can move memory. "
            + "Both are measured here by probes, kernels that do nothing else.");
        md.AppendLine();

        (string Gpu, string Flops, string Bandwidth) published = Published.FirstOrDefault(p => machine.Gpu.StartsWith(p.Gpu, StringComparison.Ordinal));
        string header = published.Gpu is null ? "| Ceiling | Measured | How |" : "| Ceiling | Measured | How | Published for this chip |";
        md.AppendLine(header);
        md.AppendLine(published.Gpu is null ? "|---|---:|---|" : "|---|---:|---|---|");
        md.AppendLine(Invariant($"| FP32 arithmetic | **{flopPeak.FlopRate / 1e12:F2} TFLOP/s** | {Kernel(flopPeak)}, {flopPeak.Size} |")
            + (published.Gpu is null ? "" : $" {published.Flops} |"));
        md.AppendLine(Invariant($"| Memory bandwidth | **{bytePeak.ByteRate / 1e9:F0} GB/s** | {Kernel(bytePeak)}, {bytePeak.Size} |")
            + (published.Gpu is null ? "" : $" {published.Bandwidth} |"));
        md.AppendLine();
        md.AppendLine("The ceilings are the highest rates measured in this run, so no row can exceed 100 %. "
            + "The FP32 probe counts a fused multiply-add as two operations, as the published estimates do; it reaches its rate only with dozens of independent "
            + "chains per thread (a thread with few chains waits on its own results), so it is the rate of arithmetic that never waits for memory, not a typical one.");
        md.AppendLine();

        md.AppendLine("### Kernels under the ceilings");
        md.AppendLine();
        md.AppendLine("| Workload | Size | Kernel | Written as | GPU time | Passes | Wall time | Rate | % of FP32 peak | % of bandwidth peak | × fastest |");
        md.AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|");

        foreach (IGrouping<(string Workload, string Size), NativeRow> group in rows.GroupBy(r => (r.Workload, r.Size)))
        {
            double fastest = group.Min(r => r.GpuSeconds);

            foreach (NativeRow row in group)
            {
                string flopShare = row.Flops > 0 ? Percent(row.FlopRate / flopPeak.FlopRate) : "—";
                string byteShare = row.Bytes > 0 ? Percent(row.ByteRate / bytePeak.ByteRate) : "—";
                string relative = group.Count() > 1 && !row.IsProbe ? Invariant($"{row.GpuSeconds / fastest:F2}×") : "—";
                string name = row.Kind == NativeKinds.Mps ? $"**{row.Variant}**" : row.Variant;

                md.AppendLine(
                    $"| {row.Workload} | {row.Size} | {name} | {row.Kind} | {Time(row.GpuSeconds)} | {Range(row)} | {Time(row.WallSeconds)} | {Rate(row)} | {flopShare} | {byteShare} | {relative} |");
            }
        }

        md.AppendLine();
        md.AppendLine("How the rates are counted: SGEMM is 2n³ operations (n³ multiplies and n³ adds); an N-body step is "
            + Invariant($"{NativeBaselines.NBodyFlopsPerInteraction} operations per pair of bodies (each of its square root and divide counted as one); SAXPY is 2 operations and 12 bytes ")
            + "per element (x and y read, y written); a hash round is "
            + Invariant($"{HashRounds.OperationsPerRound} integer operations. \"—\" means the ceiling does not apply: the integer kernel does no floating point, and the data of SGEMM ")
            + "and N-body is small enough that how much of it moves through memory depends on the kernel's own blocking, which is what those rows compare.");
        md.AppendLine();

        AppendGaps(md, rows);
        AppendCaveats(md, data, flopPeak);
    }

    /// <summary>Each generated kernel against its hand-written twin, and the best generated SGEMM against the native ones.</summary>
    private static void AppendGaps(StringBuilder md, IReadOnlyList<NativeRow> rows)
    {
        md.AppendLine("### Generated against hand-written");
        md.AppendLine();
        md.AppendLine("The same kernel in C# and by hand in MSL (the two agree bit for bit in `--verify`), by GPU time. The ratio is taken pass by pass, "
            + "where both kernels ran on the same buffers in turn, and is the median of those, with the lowest and the highest pass next to it:");
        md.AppendLine();
        md.AppendLine("| Workload | Size | Kernel | Generated C# | Hand-written MSL | Generated / hand-written | Passes |");
        md.AppendLine("|---|---|---|---:|---:|---:|---:|");

        foreach (NativeRow generated in rows.Where(r => r.IsGenerated))
        {
            NativeRow? twin = rows.FirstOrDefault(r =>
                r.Kind == NativeKinds.HandWritten && r.Workload == generated.Workload && r.Size == generated.Size && r.Variant == generated.Variant);

            if (twin is not null && twin.GpuPasses.Count == generated.GpuPasses.Count)
            {
                double[] ratios = [.. generated.GpuPasses.Zip(twin.GpuPasses, (g, h) => g / h)];
                md.AppendLine(Invariant(
                    $"| {generated.Workload} | {generated.Size} | {generated.Variant} | {Time(generated.GpuSeconds)} | {Time(twin.GpuSeconds)} | **{RuntimeFeatures.MedianOf(ratios):F3}** | {ratios.Min():F3} – {ratios.Max():F3} |"));
            }
        }

        md.AppendLine();

        // How far the naive SGEMM is from its twin, for the sentence below: stated from the data, not assumed.
        double naiveGap = rows
            .Where(r => r.IsGenerated && r.Workload == "SGEMM")
            .Select(g => (Generated: g, Twin: rows.FirstOrDefault(r => r.Kind == NativeKinds.HandWritten && r.Workload == g.Workload && r.Size == g.Size && r.Variant == g.Variant)))
            .Where(p => p.Twin is not null)
            .Select(p => Math.Abs((p.Generated.GpuSeconds / p.Twin!.GpuSeconds) - 1))
            .DefaultIfEmpty(double.NaN)
            .Max();

        List<string> lines = [];

        foreach (IGrouping<string, NativeRow> size in rows.Where(r => r.Workload == "SGEMM").GroupBy(r => r.Size))
        {
            NativeRow? best = size.Where(r => r.IsGenerated).MinBy(r => r.GpuSeconds);
            NativeRow? mps = size.FirstOrDefault(r => r.Kind == NativeKinds.Mps);
            NativeRow? matrixUnits = size.Where(r => r.Kind == NativeKinds.HandWritten).MinBy(r => r.GpuSeconds);

            if (best is not null && mps is not null && matrixUnits is not null)
            {
                lines.Add(Invariant($"- {size.Key}: the fastest generated kernel ({best.Variant}, {best.FlopRate / 1e12:F2} TFLOP/s) takes **{best.GpuSeconds / mps.GpuSeconds:F1}×** the GPU time of ")
                    + Invariant($"`MPSMatrixMultiplication` ({mps.FlopRate / 1e12:F2} TFLOP/s) and {best.GpuSeconds / matrixUnits.GpuSeconds:F1}× that of the hand-written ")
                    + Invariant($"{matrixUnits.Variant} kernel ({matrixUnits.FlopRate / 1e12:F2} TFLOP/s), which itself is within {(matrixUnits.GpuSeconds / mps.GpuSeconds - 1) * 100:F0} % of MPS."));
            }
        }

        if (lines.Count > 0)
        {
            string twins = double.IsNaN(naiveGap) ? "" : Invariant($"The generated naive kernel is within {Math.Max(naiveGap * 100, 0.1):F1} % of its hand-written twin's GPU time, so the generated MSL is not what is slow. ");
            md.AppendLine("**SGEMM, the gap that is not the generator's.** " + twins
                + "What separates a C# shader from MPS is the matrix units: `simdgroup_matrix` multiplies 8 × 8 blocks with a whole SIMD group per operation, "
                + "and a C# shader has no way to say that yet.");
            md.AppendLine();

            foreach (string line in lines)
            {
                md.AppendLine(line);
            }

            md.AppendLine();
        }
    }

    private static void AppendCaveats(StringBuilder md, NativeBaselineData data, NativeRow flopPeak)
    {
        md.AppendLine("### Caveats");
        md.AppendLine();
        md.AppendLine("- **One machine, one OS.** `MPSMatrixMultiplication` is Apple's kernel and changes with macOS. The hand-written matrix-unit kernel was tuned on an "
            + "Apple M4 Pro: of the accumulator grids tried (2 × 2, 2 × 4 and 4 × 4 blocks per SIMD group, with and without staging in threadgroup memory) "
            + "2 × 4 without staging was the fastest, and another GPU may prefer another.");
        md.AppendLine("- **GPU time is elapsed time on the GPU**, not time that was only this dispatch's: work of other processes that the GPU runs in between lengthens it. "
            + "The machine was shared; the medians and the Passes column are the guard against that, and a row whose passes are far apart should be read with that in mind.");
        double[] overheads = [.. data.Rows.Select(r => r.WallSeconds - r.GpuSeconds)];
        md.AppendLine("- **Wall time has a floor.** Every synchronous dispatch pays for a command buffer's submission and for waking the waiting thread, whatever the kernel: "
            + Invariant($"the difference between the two time columns, {overheads.Min() * 1e6:F0} to {overheads.Max() * 1e6:F0} µs in this run. A `ComputeContext` shares it among the dispatches of a batch."));
        md.AppendLine(Invariant($"- **The FP32 ceiling is a measurement, not a specification**: {flopPeak.FlopRate / 1e12:F2} TFLOP/s is what the probe reached. ")
            + "Percentages are of that, so they are a little higher than they would be against the published estimates.");
        md.AppendLine("- **Operation counts are nominal.** A square root or a divide costs the GPU more than a multiply, so the N-body row sits further below the FP32 ceiling "
            + "than its arithmetic alone would put it; that is the count's doing, not a sign of slack in the kernel.");
        md.AppendLine("- **MPS and the matrix-unit kernel are free to sum in another order** than the naive kernel (they multiply 8 × 8 blocks), so `--verify` holds "
            + Invariant($"them to a relative error of {Verify.ReorderedSumTolerance:G3} against the CPU rather than to the bit-for-bit match a hand-written twin must reach; ")
            + "the verification table shows how close they came to the naive kernel's product.");
        md.AppendLine(Invariant($"- **Sizes are fixed** ({string.Join(" and ", NativeBaselines.SgemmSizes)} for SGEMM, both multiples of 64 as the matrix-unit kernel needs): nothing here says how a kernel scales to small ")
            + "matrices, where the wall-time floor dominates, or to ones too large for MPS's blocking to matter.");
        md.AppendLine();
    }

    private static void AppendSnapshot(StringBuilder md, string label, Machine.Snapshot snapshot) =>
        md.AppendLine(Invariant($"- **{label}** ({snapshot.Time:HH:mm}): `{snapshot.Uptime}`"));

    private static string Kernel(NativeRow row) => row.IsProbe ? row.Variant : $"{row.Variant} ({row.Kind})";

    private static string Time(double seconds) => seconds < 1e-3 ? Invariant($"{seconds * 1e6:F0} µs") : Invariant($"{seconds * 1e3:F3} ms");

    /// <summary>Both ends in the unit of the higher one, written once.</summary>
    private static string Range(NativeRow row) => row.GpuHighSeconds < 1e-3
        ? Invariant($"{row.GpuLowSeconds * 1e6:F0} – {row.GpuHighSeconds * 1e6:F0} µs")
        : Invariant($"{row.GpuLowSeconds * 1e3:F3} – {row.GpuHighSeconds * 1e3:F3} ms");

    private static string Percent(double share) => share >= 0.1 ? Invariant($"{share * 100:F0} %") : Invariant($"{share * 100:F1} %");

    private static string Rate(NativeRow row) =>
        row.Bytes > 0 ? Invariant($"{row.ByteRate / 1e9:F0} GB/s")
        : row.Flops > 0 ? Invariant($"{row.FlopRate / 1e9:F0} GFLOP/s")
        : Invariant($"{row.Rounds / row.GpuSeconds / 1e9:F0} G rounds/s");
}
