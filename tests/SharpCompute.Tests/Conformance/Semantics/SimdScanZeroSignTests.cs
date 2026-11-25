using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCompute.Tests.Conformance.Semantics;

/// <summary>
/// The SIMD-group scans and reductions of floats and halves, on zeros of both signs, with lanes made
/// inactive by their own data. Each active lane writes <see cref="Slots"/> results as raw bits; a lane
/// that returns early writes none. The SIMD-group calls are all after the early return, never inside a
/// divergent branch, which <c>ForCpu</c> refuses.
/// </summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ScanZeroSignShader(ReadOnlyBuffer<float> floats, ReadOnlyBuffer<Half> halves, ReadOnlyBuffer<int> gate, ReadWriteBuffer<uint> output) : IComputeShader
{
    public const int Slots = 28;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Slots;
        int g = gate[i];

        if (g == 0)
        {
            return;
        }

        float f = floats[i];
        Half h = halves[i];
        float unit = f > 0f ? 1f : f < 0f ? -1f : f;
        Half unitHalf = (Half)unit;

        output[o + 0] = 1u;
        output[o + 1] = Gpu.AsUInt(Gpu.WavePrefixSum(f));
        output[o + 2] = Gpu.AsUInt(Gpu.WaveInclusivePrefixSum(f));
        output[o + 3] = Gpu.AsUInt(Gpu.WaveActiveSum(f));
        output[o + 4] = Gpu.AsUInt(Gpu.WavePrefixProduct(unit));
        output[o + 5] = Gpu.AsUInt(Gpu.QuadPrefixSum(f));
        output[o + 6] = Gpu.AsUInt(Gpu.QuadActiveSum(f));

        Float4 s = Gpu.WavePrefixSum(new Float4(f, -f, f + f, 0f));
        output[o + 7] = Gpu.AsUInt(s.X);
        output[o + 8] = Gpu.AsUInt(s.Y);
        output[o + 9] = Gpu.AsUInt(s.Z);
        output[o + 10] = Gpu.AsUInt(s.W);

        output[o + 11] = Gpu.AsUShort(Gpu.WavePrefixSum(h));
        output[o + 12] = Gpu.AsUShort(Gpu.WaveInclusivePrefixSum(h));
        output[o + 13] = Gpu.AsUShort(Gpu.WaveActiveSum(h));
        output[o + 14] = Gpu.AsUShort(Gpu.WavePrefixProduct(unitHalf));
        output[o + 15] = Gpu.AsUShort(Gpu.QuadPrefixSum(h));
        output[o + 16] = Gpu.AsUShort(Gpu.WaveActiveMin(h));
        output[o + 17] = Gpu.AsUShort(Gpu.WaveActiveMax(h));
        output[o + 18] = Gpu.WaveActiveAllEqual(h) ? 1u : 0u;

        float signed = g == 2 ? f : -f;
        Half signedHalf = g == 2 ? h : -h;
        output[o + 19] = Gpu.AsUInt(Gpu.WavePrefixSum(signed));
        output[o + 20] = Gpu.AsUShort(Gpu.WavePrefixSum(new Half3(h, signedHalf, h)).Y);
        output[o + 21] = Gpu.AsUInt(Gpu.WaveActiveSum(signed));
        output[o + 22] = Gpu.AsUShort(Gpu.QuadActiveSum(signedHalf));

        output[o + 23] = (uint)Gpu.WaveGetLaneIndex();
        output[o + 24] = Gpu.AsUShort(Gpu.WaveReadLaneFirst(h));
        output[o + 25] = Gpu.AsUInt(Gpu.WaveInclusivePrefixProduct(unit));
        output[o + 26] = Gpu.AsUShort(Gpu.QuadInclusivePrefixSum(h));
        output[o + 27] = Gpu.AsUShort(Gpu.WaveActiveSum(new Half2(h, -h)).Y);
    }
}

/// <summary>
/// The GPU and <c>ForCpu</c> agree bit for bit on SIMD-group scans and sums of zeros, the sign of each
/// zero included. The values are ±0, ±0.5 and ±1, whose sums and products are exact in any order (a
/// sum is -0 only when every addend is -0, whichever order the hardware adds them in), so a mismatch is
/// a wrong identity, a wrong set of active lanes or a wrong sign, never rounding.
/// </summary>
/// <remarks>
/// The grid is 40 full SIMD groups and a partial one of 11 lanes. Groups 2 and 3 have no lane active,
/// group 6 exactly one (lane 17); elsewhere a random share of lanes returns before the calls. The one
/// tolerance is the minimum and maximum of zeros of both signs (slots 16 and 17), which may be either
/// zero: which one an implementation returns depends on the order it compares in.
/// </remarks>
[Trait("Category", "Conformance")]
public class SimdScanZeroSignTests
{
    private const int SimdWidth = 32;
    private const int Threads = (SimdWidth * 40) + 11;

    private static readonly float[] Mixed = [0f, -0f, 0.5f, -0.5f, 1f, -1f];

    private static readonly HashSet<int> MinMaxSlots = [16, 17];

    private static readonly Dictionary<int, string> Names = new()
    {
        [0] = "ran", [1] = "WavePrefixSum(float)", [2] = "WaveInclusivePrefixSum(float)", [3] = "WaveActiveSum(float)",
        [4] = "WavePrefixProduct(float)", [5] = "QuadPrefixSum(float)", [6] = "QuadActiveSum(float)",
        [7] = "WavePrefixSum(Float4).X", [8] = "WavePrefixSum(Float4).Y", [9] = "WavePrefixSum(Float4).Z", [10] = "WavePrefixSum(Float4).W",
        [11] = "WavePrefixSum(Half)", [12] = "WaveInclusivePrefixSum(Half)", [13] = "WaveActiveSum(Half)", [14] = "WavePrefixProduct(Half)",
        [15] = "QuadPrefixSum(Half)", [16] = "WaveActiveMin(Half)", [17] = "WaveActiveMax(Half)", [18] = "WaveActiveAllEqual(Half)",
        [19] = "WavePrefixSum(float, per-lane sign)", [20] = "WavePrefixSum(Half3).Y", [21] = "WaveActiveSum(float, per-lane sign)",
        [22] = "QuadActiveSum(Half, per-lane sign)", [23] = "WaveGetLaneIndex", [24] = "WaveReadLaneFirst(Half)",
        [25] = "WaveInclusivePrefixProduct(float)", [26] = "QuadInclusivePrefixSum(Half)", [27] = "WaveActiveSum(Half2).Y",
    };

    public static TheoryData<string, double> Cases()
    {
        TheoryData<string, double> cases = [];

        foreach (string kind in new[] { "zeros", "negative zeros", "mixed" })
        {
            foreach (double returned in new[] { 0.0, 0.3, 0.7, 0.95 })
            {
                cases.Add(kind, returned);
            }
        }

        return cases;
    }

    [MetalTheory]
    [MemberData(nameof(Cases))]
    public void Scans_MatchForCpu_BitForBit(string kind, double returned)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        foreach (int seed in new[] { 1, 2 })
        {
            Random random = new((seed * 1000) + (int)(returned * 100) + kind.Length);
            float[] values = Enumerable.Range(0, Threads).Select(_ => kind switch
            {
                "zeros" => random.Next(2) == 0 ? 0f : -0f,
                "negative zeros" => -0f,
                _ => random.Next(10) < 7 ? (random.Next(2) == 0 ? 0f : -0f) : Mixed[random.Next(Mixed.Length)],
            }).ToArray();

            // 0 returns before the calls; 1 and 2 run them, with the per-lane sign of slots 19-22.
            int[] gates = Enumerable.Range(0, Threads).Select(_ => random.NextDouble() < returned ? 0 : 1 + random.Next(2)).ToArray();
            Array.Fill(gates, 0, 2 * SimdWidth, 2 * SimdWidth);
            Array.Fill(gates, 0, 6 * SimdWidth, SimdWidth);
            gates[(6 * SimdWidth) + 17] = 2;

            uint[] gpu = Run(device, values, gates, cpu: false);
            uint[] cpu = Run(device, values, gates, cpu: true);
            string context = $"{kind}, {returned:P0} returned, seed {seed}";

            AssertSame(context, gpu, cpu, values, gates);
            AssertFirstLanesSeeNegativeZero(context, gpu, gates);
        }
    }

    private static uint[] Run(GraphicsDevice device, float[] values, int[] gates, bool cpu)
    {
        using ReadOnlyBuffer<float> floats = device.AllocateReadOnlyBuffer<float>(values);
        using ReadOnlyBuffer<Half> halves = device.AllocateReadOnlyBuffer<Half>(values.Select(static v => (Half)v).ToArray());
        using ReadOnlyBuffer<int> gate = device.AllocateReadOnlyBuffer<int>(gates);
        using ReadWriteBuffer<uint> output = device.AllocateReadWriteBuffer<uint>(Threads * ScanZeroSignShader.Slots);

        if (cpu)
        {
            device.ForCpu(Threads, new ScanZeroSignShader(floats, halves, gate, output));
        }
        else
        {
            device.For(Threads, new ScanZeroSignShader(floats, halves, gate, output));
        }

        return output.ToArray();
    }

    /// <summary>Equal bits everywhere, except that the minimum and maximum of zeros may be either zero.</summary>
    private static void AssertSame(string context, uint[] gpu, uint[] cpu, float[] values, int[] gates)
    {
        const int slots = ScanZeroSignShader.Slots;
        uint[] comparable = (uint[])cpu.Clone();

        for (int thread = 0; thread < Threads; thread++)
        {
            foreach (int slot in MinMaxSlots)
            {
                int k = (thread * slots) + slot;

                if (gpu[k] is 0u or 0x8000u && cpu[k] is 0u or 0x8000u)
                {
                    comparable[k] = gpu[k];
                }
            }
        }

        SlotComparison.AssertEqual(
            context,
            gpu,
            comparable,
            slots,
            Names,
            thread => $"(lane {thread % SimdWidth}, gate {gates[thread]}, value 0x{BitConverter.SingleToUInt32Bits(values[thread]):X8})");
    }

    /// <summary>
    /// The exclusive sum of the first active lane of each SIMD group is the identity of addition, -0,
    /// on both sides (ForCpu already matched the GPU above). Checked so that the cases cannot pass with
    /// nothing of interest in them: every case has first lanes, and each of them must see -0.
    /// </summary>
    private static void AssertFirstLanesSeeNegativeZero(string context, uint[] gpu, int[] gates)
    {
        int firstLanes = 0;

        for (int group = 0; group * SimdWidth < Threads; group++)
        {
            int start = group * SimdWidth;
            int first = Enumerable.Range(start, Math.Min(SimdWidth, Threads - start)).FirstOrDefault(t => gates[t] != 0, -1);

            if (group is 2 or 3)
            {
                Assert.Equal(-1, first);
            }

            if (first < 0)
            {
                continue;
            }

            firstLanes++;
            int o = first * ScanZeroSignShader.Slots;
            Assert.True(gpu[o + 1] == 0x80000000u, $"{context}: the float WavePrefixSum of the first active lane {first} is 0x{gpu[o + 1]:X8}, not -0.");
            Assert.True(gpu[o + 11] == 0x8000u, $"{context}: the half WavePrefixSum of the first active lane {first} is 0x{gpu[o + 11]:X4}, not -0.");
        }

        Assert.True(firstLanes > 0, $"{context}: no SIMD group had an active lane.");
    }
}
