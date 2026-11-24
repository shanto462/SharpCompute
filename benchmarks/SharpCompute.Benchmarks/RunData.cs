using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SharpCompute.Benchmarks;

/// <summary>One benchmark result: a workload, a problem size and a variant.</summary>
public sealed record BenchmarkRow(string Workload, string Size, long SizeKey, string Variant, bool Baseline, double Mean, double Error, double StdDev)
{
    public bool IsGpu => Variant.StartsWith("GPU", StringComparison.Ordinal);

    public bool IsGeneratedGpu => Variant.StartsWith("GPU generated", StringComparison.Ordinal);
}

/// <summary>A later run of some workloads only, merged into an earlier full run with <c>--run --merge</c>.</summary>
public sealed record Rerun(IReadOnlyList<string> Workloads, Machine.Snapshot Before, Machine.Snapshot After, bool Quick, double ElapsedMinutes);

/// <summary>
/// Everything RESULTS.md is rendered from. --run saves it next to the BenchmarkDotNet artifacts, so
/// the report can be re-rendered with --render without re-running the benchmarks.
/// </summary>
public sealed record RunData(
    Machine Machine,
    Machine.Snapshot Before,
    Machine.Snapshot After,
    IReadOnlyList<Check> Checks,
    IReadOnlyList<FirstDispatch> FirstDispatches,
    IReadOnlyList<BenchmarkRow> Rows,
    bool Quick,
    double ElapsedMinutes,
    IReadOnlyList<Rerun>? Reruns = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    public static RunData Load(string path) =>
        JsonSerializer.Deserialize<RunData>(File.ReadAllText(path), Options) ?? throw new InvalidDataException($"No run data in {path}.");
}
