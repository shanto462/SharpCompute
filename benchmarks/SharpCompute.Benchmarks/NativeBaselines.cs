using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using SharpCompute.Benchmarks.Shaders;
using SharpCompute.Benchmarks.Workloads;
using static System.FormattableString;

namespace SharpCompute.Benchmarks;

/// <summary>One kernel of one workload, as the native baselines measured it. Times are seconds per dispatch.</summary>
/// <param name="Kind">One of <see cref="NativeKinds"/>: who wrote the kernel.</param>
/// <param name="GpuPasses">
/// The GPU execution time of the dispatch's command buffer (<c>LastGpuTime</c>) in each pass: the
/// median of the pass's dispatches. In pass order, so two kernels of a workload can be compared pass by pass.
/// </param>
/// <param name="WallSeconds">Wall time of the synchronous dispatch (encode, commit and wait): the median of the passes' medians.</param>
/// <param name="Flops">FP32 operations of one dispatch, or 0 for a kernel without floating point.</param>
/// <param name="Bytes">Bytes one dispatch must move through memory, or 0 when its data stays in the caches.</param>
/// <param name="Rounds">Hash rounds of one dispatch, for the integer workload; otherwise 0.</param>
public sealed record NativeRow(
    string Workload, string Size, string Variant, string Kind,
    IReadOnlyList<double> GpuPasses, double WallSeconds, double Flops, double Bytes, double Rounds)
{
    /// <summary>The row's GPU time: the median of <see cref="GpuPasses"/>.</summary>
    [JsonIgnore]
    public double GpuSeconds => RuntimeFeatures.MedianOf([.. GpuPasses]);

    [JsonIgnore]
    public double GpuLowSeconds => GpuPasses.Min();

    [JsonIgnore]
    public double GpuHighSeconds => GpuPasses.Max();

    [JsonIgnore]
    public bool IsProbe => Kind == NativeKinds.Probe;

    [JsonIgnore]
    public bool IsGenerated => Kind == NativeKinds.Generated;

    /// <summary>FP32 operations per second of GPU time, or 0.</summary>
    [JsonIgnore]
    public double FlopRate => Flops / GpuSeconds;

    /// <summary>Bytes per second of GPU time, or 0.</summary>
    [JsonIgnore]
    public double ByteRate => Bytes / GpuSeconds;
}

/// <summary>The values of <see cref="NativeRow.Kind"/>.</summary>
public static class NativeKinds
{
    public const string Generated = "generated C#";
    public const string HandWritten = "hand-written MSL";
    public const string Mps = "MPS";
    public const string Probe = "probe (hand-written MSL)";
}

/// <summary>A run of the native baselines: what the section of RESULTS.md about them is rendered from.</summary>
public sealed record NativeBaselineData(
    Machine Machine, Machine.Snapshot Before, Machine.Snapshot After, int Passes, int Warmups, int Samples, double ElapsedSeconds, IReadOnlyList<NativeRow> Rows);

/// <summary>
/// The native baselines (<c>--native-baselines</c>): each workload's generated kernel next to the
/// same kernel written by hand in MSL and, for SGEMM, next to Apple's
/// <c>MPSMatrixMultiplication</c> and a hand-written kernel on Metal's matrix units, timed by the GPU's
/// own clock, plus probes that measure the machine's FP32 and memory-bandwidth ceilings so that each
/// row can be placed under them (a roofline).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not BenchmarkDotNet.</b> What separates two kernels is the GPU's execution time
/// (<see cref="GraphicsDevice.LastGpuTime"/>), which a wall-clock harness cannot see: on the wall
/// clock a dispatch of a few hundred microseconds is a third submission and wake-up. This harness
/// records both for the same dispatches.
/// </para>
/// <para>
/// <b>A sample</b> is one synchronous dispatch in its own command buffer, as <c>device.For</c> makes
/// one: its GPU time is <see cref="GraphicsDevice.LastGpuTime"/> and its wall time a stopwatch around
/// the call. The variants of a workload take turns, in a new order every round, so that drift (heat,
/// other load) reaches them alike and the GPU never idles between samples. After <see cref="Warmups"/> rounds that are thrown
/// away (the first compiles the pipelines), <see cref="Samples"/> rounds are kept and their median is
/// the pass's value. The whole set runs <see cref="DefaultPasses"/> times, each pass with newly
/// allocated buffers, and a row is the median of its pass values; the lowest and highest pass values
/// are kept to show how far apart the passes were.
/// </para>
/// </remarks>
public static class NativeBaselines
{
    public const int DefaultPasses = 5;
    public const int Warmups = 5;
    public const int Samples = 25;

    public const int SaxpyLength = 1 << 24;
    public const int NBodyCount = 16384;
    public const int HashLength = 1 << 22;
    public const int HashRoundCount = 64;
    public const int FmaThreads = 1 << 20;
    public const int FmaIterations = 256;
    public static readonly int[] SgemmSizes = [1024, 2048];

    /// <summary>
    /// FP32 operations of one body-body interaction of <see cref="NBodyStep"/>, each counted as one:
    /// 3 subtracts, 5 for the dot product, the softening add, the square root, the divide, 3
    /// multiplies for the scale, and 3 multiplies and 3 adds to accumulate. The integration at the
    /// end of a thread is not counted.
    /// </summary>
    public const int NBodyFlopsPerInteraction = 20;

    private sealed record Variant(string Name, string Kind, Func<(double Gpu, double Wall)> Sample, double Flops = 0, double Bytes = 0, double Rounds = 0);

    /// <summary>One variant's median times in one pass.</summary>
    private sealed record PassValue(string Workload, string Size, Variant Variant, double Gpu, double Wall);

    public static NativeBaselineData Run(GraphicsDevice device, int passes, Action<string> log)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(passes);
        log(Invariant($"native baselines on {device.Name}: {passes} passes of {Warmups} warm-up + {Samples} measured dispatches per kernel; GPU time is LastGpuTime"));

        Machine machine = Machine.Describe();
        Machine.Snapshot before = Machine.TakeSnapshot();
        long start = Stopwatch.GetTimestamp();
        List<PassValue> values = [];

        for (int pass = 0; pass < passes; pass++)
        {
            Probes(device, values);

            foreach (int n in SgemmSizes)
            {
                Sgemm(device, n, values);
            }

            NBody(device, values);
            Saxpy(device, values);
            Hash(device, values);
            log(Invariant($"pass {pass + 1} of {passes} done"));
        }

        // Grouped in the order the first pass measured them.
        List<NativeRow> rows = values
            .GroupBy(v => (v.Workload, v.Size, v.Variant.Name, v.Variant.Kind))
            .Select(g =>
            {
                Variant variant = g.First().Variant;
                return new NativeRow(
                    g.Key.Workload, g.Key.Size, variant.Name, variant.Kind,
                    [.. g.Select(v => v.Gpu)], RuntimeFeatures.MedianOf([.. g.Select(v => v.Wall)]),
                    variant.Flops, variant.Bytes, variant.Rounds);
            })
            .ToList();

        foreach (NativeRow row in rows)
        {
            log(Describe(row));
        }

        return new NativeBaselineData(machine, before, Machine.TakeSnapshot(), passes, Warmups, Samples, Stopwatch.GetElapsedTime(start).TotalSeconds, rows);
    }

    private static void Probes(GraphicsDevice device, List<PassValue> values)
    {
        float[] seeds = new float[FmaThreads];
        Random random = new(5);

        for (int i = 0; i < seeds.Length; i++)
        {
            seeds[i] = random.NextSingle();
        }

        using ReadOnlyBuffer<float> gpuSeeds = device.AllocateReadOnlyBuffer<float>(seeds);
        using ReadWriteBuffer<float> sums = device.AllocateReadWriteBuffer<float>(seeds.Length);

        Measure("FP32 probe", Invariant($"{FmaThreads:N0} threads × {FmaIterations} steps"), values,
            new Variant(
                Invariant($"{FmaChainsHandWritten.Chains} fma chains per thread, registers only"), NativeKinds.Probe,
                Dispatch(device, () => device.For(FmaThreads, new FmaChainsHandWritten(gpuSeeds, sums, FmaIterations))),
                Flops: (double)FmaThreads * FmaIterations * FmaChainsHandWritten.FlopsPerIteration));

        using Saxpy saxpy = new(SaxpyLength, device);

        Measure("Bandwidth probe", SaxpySize, values,
            new Variant(
                "float4 fill (writes only)", NativeKinds.Probe,
                Dispatch(device, () => device.For(SaxpyLength / 4, new FillFloat4HandWritten(saxpy.GpuY))),
                Bytes: SaxpyLength * 4.0),
            new Variant(
                "float4 copy (reads x, writes y)", NativeKinds.Probe,
                Dispatch(device, () => device.For(SaxpyLength / 4, new CopyFloat4HandWritten(saxpy.GpuX, saxpy.GpuY))),
                Bytes: SaxpyLength * 8.0));
    }

    private static void Sgemm(GraphicsDevice device, int n, List<PassValue> values)
    {
        using Sgemm sgemm = new(n, device);
        double flops = sgemm.Flops;

        Measure("SGEMM", Invariant($"{n}×{n}"), values,
            new Variant("MPSMatrixMultiplication", NativeKinds.Mps, sgemm.Mps.Run, flops),
            new Variant("simdgroup_matrix, 2×4 accumulators", NativeKinds.HandWritten, Dispatch(device, sgemm.HandWrittenSimdgroup), flops),
            new Variant("threadgroup-tiled 16×16", NativeKinds.Generated, Dispatch(device, sgemm.GeneratedTiled), flops),
            new Variant("naive", NativeKinds.Generated, Dispatch(device, sgemm.GeneratedNaive), flops),
            new Variant("naive", NativeKinds.HandWritten, Dispatch(device, sgemm.HandWrittenNaive), flops));
    }

    private static void NBody(GraphicsDevice device, List<PassValue> values)
    {
        using NBody nbody = new(NBodyCount, device);
        double flops = (double)NBodyCount * NBodyCount * NBodyFlopsPerInteraction;

        Measure("N-body", Invariant($"{NBodyCount:N0} bodies"), values,
            new Variant("one step", NativeKinds.Generated, Dispatch(device, () => nbody.Gpu()), flops),
            new Variant("one step", NativeKinds.HandWritten, Dispatch(device, nbody.GpuHandWritten), flops));
    }

    private static void Saxpy(GraphicsDevice device, List<PassValue> values)
    {
        using Saxpy saxpy = new(SaxpyLength, device);

        // Per element: x and y are read and y is written (12 bytes), for one multiply and one add.
        double flops = SaxpyLength * 2.0, bytes = SaxpyLength * 12.0;

        Measure("SAXPY", SaxpySize, values,
            new Variant("scalar", NativeKinds.Generated, Dispatch(device, () => device.For(SaxpyLength, new SaxpyGenerated(saxpy.GpuX, saxpy.GpuY, Workloads.Saxpy.A))), flops, bytes),
            new Variant("scalar", NativeKinds.HandWritten, Dispatch(device, () => device.For(SaxpyLength, new SaxpyHandWritten(saxpy.GpuX, saxpy.GpuY, Workloads.Saxpy.A))), flops, bytes),
            new Variant("float4", NativeKinds.HandWritten, Dispatch(device, saxpy.GpuHandWrittenFloat4), flops, bytes));
    }

    private static void Hash(GraphicsDevice device, List<PassValue> values)
    {
        using Hash hash = new(HashLength, HashRoundCount, device);

        Measure("Integer hash", Invariant($"{HashLength:N0} × {HashRoundCount} rounds"), values,
            new Variant("mix rounds", NativeKinds.Generated, Dispatch(device, hash.Generated), Rounds: hash.RoundsPerDispatch),
            new Variant("mix rounds", NativeKinds.HandWritten, Dispatch(device, hash.HandWritten), Rounds: hash.RoundsPerDispatch));
    }

    private static string SaxpySize => Invariant($"{SaxpyLength:N0} floats");

    /// <summary>
    /// A sample of a SharpCompute kernel: one synchronous dispatch on this thread, its GPU time read
    /// from the public <see cref="GraphicsDevice.LastGpuTime"/> right after it.
    /// </summary>
    private static Func<(double Gpu, double Wall)> Dispatch(GraphicsDevice device, Action dispatch) =>
        () =>
        {
            long start = Stopwatch.GetTimestamp();
            dispatch();
            double wall = Stopwatch.GetElapsedTime(start).TotalSeconds;
            return (device.LastGpuTime.TotalSeconds, wall);
        };

    private static void Measure(string workload, string size, List<PassValue> values, params Variant[] variants)
    {
        double[][] gpu = new double[variants.Length][], wall = new double[variants.Length][];

        for (int v = 0; v < variants.Length; v++)
        {
            (gpu[v], wall[v]) = (new double[Samples], new double[Samples]);
        }

        // A new order every round. In a fixed order a round has a fixed length, and a disturbance with
        // a period close to it (the display's refresh is one) would keep landing on the same variant.
        int[] order = [.. Enumerable.Range(0, variants.Length)];
        Random shuffle = new(variants.Length);

        for (int round = -Warmups; round < Samples; round++)
        {
            shuffle.Shuffle(order);

            foreach (int v in order)
            {
                (double g, double w) = variants[v].Sample();

                if (round >= 0)
                {
                    (gpu[v][round], wall[v][round]) = (g, w);
                }
            }
        }

        for (int v = 0; v < variants.Length; v++)
        {
            values.Add(new PassValue(workload, size, variants[v], RuntimeFeatures.MedianOf(gpu[v]), RuntimeFeatures.MedianOf(wall[v])));
        }
    }

    private static string Describe(NativeRow row)
    {
        string rate = row.Bytes > 0 ? Invariant($"{row.ByteRate / 1e9,7:F0} GB/s")
            : row.Flops > 0 ? Invariant($"{row.FlopRate / 1e9,7:F0} GFLOP/s")
            : Invariant($"{row.Rounds / row.GpuSeconds / 1e9,7:F1} G rounds/s");
        return Invariant(
            $"{row.Workload,-16} {row.Size,-28} {row.Kind,-25} {row.Variant,-42} GPU {row.GpuSeconds * 1e3,7:F3} ms ({row.GpuLowSeconds * 1e3:F3}..{row.GpuHighSeconds * 1e3:F3})  wall {row.WallSeconds * 1e3,7:F3} ms  {rate}");
    }
}
