using System;
using System.Globalization;
using SharpCompute.Benchmarks.Shaders;
using SharpCompute.Benchmarks.Workloads;

namespace SharpCompute.Benchmarks;

// The checks of the native baselines (--native-baselines): every kernel that mode times, at the
// sizes it times them, against the CPU, and each hand-written kernel against its generated twin.
public static partial class Verify
{
    /// <summary>
    /// The tolerance for a matrix product that is free to take its sums in another order than the CPU
    /// reference's ascending k: <c>MPSMatrixMultiplication</c> and the <c>simdgroup_matrix</c> kernel
    /// multiply 8 × 8 blocks, and nothing promises the order inside one. Reordering changes a sum of n
    /// positive products by up to about n · 2⁻²⁴ of its value (1.2e-4 at n = 2048) in the worst case
    /// and far less in practice; an indexing mistake is off by 1e-2 or more.
    /// </summary>
    public const double ReorderedSumTolerance = 1e-4;

    private static void VerifyNativeBaselines(GraphicsDevice device, Action<Check> add)
    {
        foreach (int n in NativeBaselines.SgemmSizes)
        {
            VerifySgemm(device, n, add);
        }

        VerifyNBodyHandWritten(device, add);
        VerifySaxpyFloat4(device, add);
        VerifyHash(device, add);
        VerifyProbes(device, add);
    }

    private static void VerifySgemm(GraphicsDevice device, int n, Action<Check> add)
    {
        const string cpu = "CPU Parallel.For";
        using Sgemm sgemm = new(n, device);
        string size = $"{n}x{n}";
        float[] reference = sgemm.CpuReference();

        sgemm.GeneratedNaive();
        float[] generated = sgemm.GpuC.ToArray();
        add(Compare("SGEMM", size, "GPU generated naive", cpu, generated, reference));

        Clear(sgemm);
        sgemm.HandWrittenNaive();
        add(Compare("SGEMM", size, "GPU hand-written naive", cpu, sgemm.GpuC.AsSpan(), reference));
        add(Compare("SGEMM", size, "GPU hand-written naive", "GPU generated naive", sgemm.GpuC.AsSpan(), generated) with { Tolerance = 0 });

        Clear(sgemm);
        sgemm.GeneratedTiled();
        add(Compare("SGEMM", size, "GPU generated tiled", cpu, sgemm.GpuC.AsSpan(), reference));

        Clear(sgemm);
        sgemm.HandWrittenSimdgroup();
        add(Reordered("GPU hand-written simdgroup"));

        Clear(sgemm);
        sgemm.Mps.Run();
        add(Reordered("GPU MPSMatrixMultiplication"));

        // Held to the CPU with the looser tolerance; how close it came to the naive kernel is a detail worth seeing.
        Check Reordered(string variant)
        {
            Check check = Compare("SGEMM", size, variant, cpu, sgemm.GpuC.AsSpan(), reference);
            return check with { Tolerance = ReorderedSumTolerance, Detail = $"{check.Detail}; {Compare("", "", "", "", sgemm.GpuC.AsSpan(), generated).Detail} vs GPU generated naive" };
        }

        // Every kernel writes the same buffer; without this a kernel that wrote nothing would pass on the previous one's product.
        static void Clear(Sgemm sgemm) => sgemm.GpuC.AsSpan().Clear();
    }

    private static void VerifyNBodyHandWritten(GraphicsDevice device, Action<Check> add)
    {
        int n = NativeBaselines.NBodyCount;
        using NBody nbody = new(n, device);
        string size = $"{n} bodies";

        nbody.CpuParallel();
        nbody.Gpu();
        Float4[] generatedPositions = nbody.GpuNextPositions.ToArray();
        Float4[] generatedVelocities = nbody.GpuNextVelocities.ToArray();

        nbody.GpuNextPositions.AsSpan().Clear();
        nbody.GpuNextVelocities.AsSpan().Clear();
        nbody.GpuHandWritten();

        add(CompareVectors(size, "GPU hand-written MSL", "positions", nbody.GpuNextPositions.AsSpan(), nbody.NextPositions));
        add(CompareVectors(size, "GPU hand-written MSL", "velocities", nbody.GpuNextVelocities.AsSpan(), nbody.NextVelocities));
        add(CompareVectors(size, "GPU hand-written MSL", "positions", nbody.GpuNextPositions.AsSpan(), generatedPositions) with { Reference = "GPU generated", Tolerance = 0 });
        add(CompareVectors(size, "GPU hand-written MSL", "velocities", nbody.GpuNextVelocities.AsSpan(), generatedVelocities) with { Reference = "GPU generated", Tolerance = 0 });
    }

    private static void VerifySaxpyFloat4(GraphicsDevice device, Action<Check> add)
    {
        int n = NativeBaselines.SaxpyLength;
        using Saxpy saxpy = new(n, device);
        string size = n.ToString("N0", CultureInfo.InvariantCulture);

        saxpy.GpuHandWritten();
        float[] scalar = saxpy.GpuY.ToArray();

        // SAXPY updates y in place: start both sides again from the same y.
        saxpy.Reset();
        saxpy.CpuScalar();
        saxpy.GpuHandWrittenFloat4();

        add(Compare("SAXPY", size, "GPU hand-written float4", "CPU scalar", saxpy.GpuY.AsSpan(), saxpy.Y));
        add(Compare("SAXPY", size, "GPU hand-written float4", "GPU hand-written MSL", saxpy.GpuY.AsSpan(), scalar) with { Tolerance = 0 });
    }

    private static void VerifyHash(GraphicsDevice device, Action<Check> add)
    {
        using Hash hash = new(NativeBaselines.HashLength, NativeBaselines.HashRoundCount, device);
        string size = string.Create(CultureInfo.InvariantCulture, $"{hash.Length:N0} x {hash.Rounds}");
        uint[] reference = hash.CpuReference();

        hash.Generated();
        add(CompareExact("Hash", size, "GPU generated", "CPU Parallel.For", hash.GpuOutput.AsSpan(), reference));

        hash.GpuOutput.AsSpan().Clear();
        hash.HandWritten();
        add(CompareExact("Hash", size, "GPU hand-written MSL", "CPU Parallel.For", hash.GpuOutput.AsSpan(), reference));
    }

    /// <summary>The probes are only timed, but a ceiling measured by a kernel that did not do its work would be no ceiling.</summary>
    private static void VerifyProbes(GraphicsDevice device, Action<Check> add)
    {
        const int threads = 4096, iterations = NativeBaselines.FmaIterations;
        float[] seeds = new float[threads];
        Random random = new(5);

        for (int i = 0; i < threads; i++)
        {
            seeds[i] = random.NextSingle();
        }

        using ReadOnlyBuffer<float> gpuSeeds = device.AllocateReadOnlyBuffer<float>(seeds);
        using ReadWriteBuffer<float> sums = device.AllocateReadWriteBuffer<float>(threads);
        device.For(threads, new FmaChainsHandWritten(gpuSeeds, sums, iterations));

        float[] expected = new float[threads];

        for (int i = 0; i < threads; i++)
        {
            expected[i] = FmaChainsHandWritten.Expected(seeds[i], iterations);
        }

        string fmaSize = string.Create(CultureInfo.InvariantCulture, $"{threads:N0} x {iterations}");
        add(Compare("Probe", fmaSize, "GPU fma chains", "CPU fma", sums.AsSpan(), expected));

        const int length = 1 << 16;
        using ReadOnlyBuffer<float> x = device.AllocateReadOnlyBuffer<float>(length);
        using ReadWriteBuffer<float> y = device.AllocateReadWriteBuffer<float>(length);

        for (int i = 0; i < length; i++)
        {
            x.AsSpan()[i] = random.NextSingle();
        }

        string size = length.ToString("N0", CultureInfo.InvariantCulture);
        device.For(length / 4, new CopyFloat4HandWritten(x, y));
        add(Compare("Probe", size, "GPU float4 copy", "source", y.AsSpan(), x.AsReadOnlySpan()) with { Tolerance = 0 });

        float[] filled = new float[length];

        for (int i = 0; i < length; i++)
        {
            filled[i] = i / 4;
        }

        device.For(length / 4, new FillFloat4HandWritten(y));
        add(Compare("Probe", size, "GPU float4 fill", "thread index", y.AsSpan(), filled) with { Tolerance = 0 });
    }

    /// <summary>The share of elements that differ, which must be none: integer arithmetic has one right answer.</summary>
    private static Check CompareExact(string workload, string size, string variant, string reference, ReadOnlySpan<uint> actual, ReadOnlySpan<uint> expected)
    {
        int different = 0;

        for (int i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i])
            {
                different++;
            }
        }

        return new Check(workload, size, variant, reference, "share differing", (double)different / expected.Length, 0, $"{different} of {expected.Length} elements differ");
    }
}
