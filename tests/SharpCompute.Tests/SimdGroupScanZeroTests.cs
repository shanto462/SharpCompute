using System;
using System.Linq;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// What an exclusive scan gives the first active lane, for floats and for halves, on the GPU and on
/// the CPU (<c>ForCpu</c> of the same shader): <c>-0</c> for a scan of sums, the identity of IEEE
/// addition (<c>-0 + x</c> is <c>x</c> for every <c>x</c>, <c>-0</c> included), and 1 for a scan of
/// products. Measured on an Apple M4 Pro. The sign of that zero is what is pinned here; it shows as
/// soon as the result is divided by, passed to <c>CopySign</c> or looked at as bits.
/// </summary>
public class SimdGroupScanZeroTests
{
    private const uint FloatNegativeZero = 0x80000000u, FloatOne = 0x3F800000u;
    private const uint HalfNegativeZero = 0x8000u, HalfOne = 0x3C00u;

    private static (uint[] Gpu, uint[] Cpu) Run(float[] values, int skip)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> floats = device.AllocateReadOnlyBuffer<float>(values);
        using ReadOnlyBuffer<Half> halves = device.AllocateReadOnlyBuffer<Half>(values.Select(static v => (Half)v).ToArray());
        using ReadWriteBuffer<uint> gpu = device.AllocateReadWriteBuffer<uint>(values.Length * SimdScanZeros.Stride);
        using ReadWriteBuffer<uint> cpu = device.AllocateReadWriteBuffer<uint>(values.Length * SimdScanZeros.Stride);

        device.For(values.Length, new SimdScanZeros(floats, halves, gpu, skip));
        device.ForCpu(values.Length, new SimdScanZeros(floats, halves, cpu, skip));
        return (gpu.ToArray(), cpu.ToArray());
    }

    private static uint NegativeZero(int output) => SimdScanZeros.Halves.Contains(output) ? HalfNegativeZero : FloatNegativeZero;

    private static uint One(int output) => SimdScanZeros.Halves.Contains(output) ? HalfOne : FloatOne;

    /// <summary>The values of the threads: exact in every sum and product, so that the two sides can be compared bit for bit.</summary>
    private static float[] Values(string kind, int threads) => Enumerable.Range(0, threads).Select(i => kind switch
    {
        "numbers" => (i % 4) switch { 0 => 2f, 1 => -0.5f, 2 => 4f, _ => -0.25f }, // powers of two: every partial product and sum is a half
        "negative zeros" => -0f,
        "positive zeros" => 0f,
        _ => (i % 3) switch { 0 => -0f, 1 => 0f, _ => -0f }, // zeros of both signs
    }).ToArray();

    [MetalTheory]
    [InlineData("numbers", 64, 0)]          // two full SIMD groups
    [InlineData("numbers", 40, 0)]          // a full one and a partial one
    [InlineData("numbers", 64, 3)]          // lanes 0, 3, 6, ... returned: the first active lane is lane 1
    [InlineData("negative zeros", 64, 0)]
    [InlineData("negative zeros", 40, 5)]
    [InlineData("positive zeros", 64, 0)]
    [InlineData("positive zeros", 40, 3)]
    [InlineData("zeros of both signs", 64, 0)]
    [InlineData("zeros of both signs", 64, 2)]
    public void TheFirstLaneOfAnExclusiveScan_IsNegativeZeroForSumsAndOneForProducts_OnTheGpuAndOnTheCpu(string kind, int threads, int skip)
    {
        float[] values = Values(kind, threads);
        (uint[] gpu, uint[] cpu) = Run(values, skip);

        // The two sides agree on every output, bit for bit (no NaN can occur with these values).
        string[] differences = Enumerable.Range(0, gpu.Length)
            .Where(i => gpu[i] != cpu[i])
            .Select(i => $"thread {i / SimdScanZeros.Stride}, output {i % SimdScanZeros.Stride}: GPU 0x{gpu[i]:X8}, CPU 0x{cpu[i]:X8}")
            .ToArray();
        Assert.True(differences.Length == 0, $"{differences.Length} outputs differ between the GPU and the CPU:\n" + string.Join("\n", differences.Take(30)));

        int firstLanes = 0, firstQuadLanes = 0;

        foreach ((string side, uint[] result) in new[] { ("GPU", gpu), ("CPU", cpu) })
        {
            for (int thread = 0; thread < threads; thread++)
            {
                uint[] o = result[(thread * SimdScanZeros.Stride)..((thread + 1) * SimdScanZeros.Stride)];
                string at = $"{side}, thread {thread} (lane {o[21]})";

                if (o[0] == 0)
                {
                    // The lane returned before the scans and wrote nothing.
                    Assert.True(skip > 0 && thread % 32 % skip == 0, at);
                    continue;
                }

                // The first active lane of the SIMD group: nothing below it.
                if (o[1] == 0)
                {
                    firstLanes++;
                    Assert.All(SimdScanZeros.ExclusiveSums, output => Assert.True(o[output] == NegativeZero(output), $"{at}, output {output}: 0x{o[output]:X8}"));
                    Assert.All(SimdScanZeros.ExclusiveProducts, output => Assert.True(o[output] == One(output), $"{at}, output {output}: 0x{o[output]:X8}"));
                }

                // The first active lane of its quad.
                if (o[2] == 0)
                {
                    firstQuadLanes++;
                    Assert.All(SimdScanZeros.QuadExclusiveSums, output => Assert.True(o[output] == NegativeZero(output), $"{at}, output {output}: 0x{o[output]:X8}"));
                    Assert.All(SimdScanZeros.QuadExclusiveProducts, output => Assert.True(o[output] == One(output), $"{at}, output {output}: 0x{o[output]:X8}"));
                }

                if (kind == "negative zeros")
                {
                    // -0 + -0 is -0: every scan of sums and every sum stays -0, in every lane. A scan that
                    // started from +0 would give +0 from the first lane on.
                    Assert.All(
                        SimdScanZeros.ExclusiveSums.Concat(SimdScanZeros.QuadExclusiveSums).Concat(SimdScanZeros.InclusiveSumsAndSums),
                        output => Assert.True(o[output] == NegativeZero(output), $"{at}, output {output}: 0x{o[output]:X8}"));
                }
                else if (kind == "positive zeros")
                {
                    // -0 + +0 is +0: the first lane's -0 does not go further, and nothing inclusive is negative.
                    Assert.All(SimdScanZeros.ExclusiveSums, output => Assert.True(o[output] == (o[1] == 0 ? NegativeZero(output) : 0u), $"{at}, output {output}: 0x{o[output]:X8}"));
                    Assert.All(SimdScanZeros.QuadExclusiveSums, output => Assert.True(o[output] == (o[2] == 0 ? NegativeZero(output) : 0u), $"{at}, output {output}: 0x{o[output]:X8}"));
                    Assert.All(SimdScanZeros.InclusiveSumsAndSums, output => Assert.True(o[output] == 0u, $"{at}, output {output}: 0x{o[output]:X8}"));
                }
                else if (kind == "numbers" && o[1] == 0)
                {
                    // The identity changes nothing: the first lane's inclusive sum is its own value.
                    Assert.True(o[4] == BitConverter.SingleToUInt32Bits(values[thread]) && o[11] == BitConverter.HalfToUInt16Bits((Half)values[thread]), at);
                }
            }
        }

        // One first lane per SIMD group and one per quad that has an active lane, on each side.
        int groups = (threads + 31) / 32;
        Assert.Equal(2 * groups, firstLanes);
        Assert.True(firstQuadLanes >= 2 * groups * 4, $"{firstQuadLanes} first lanes of quads");
    }

    /// <summary>The sign is there to be seen: dividing by the first lane's scan gives minus infinity, on both sides.</summary>
    [MetalFact]
    public void TheSignOfTheFirstLanesZero_IsTheSameInPlainArithmetic()
    {
        (uint[] gpu, uint[] cpu) = Run(Values("numbers", 32), skip: 0);
        float gpuFirst = BitConverter.UInt32BitsToSingle(gpu[3]), cpuFirst = BitConverter.UInt32BitsToSingle(cpu[3]);

        Assert.Equal(float.NegativeInfinity, 1f / gpuFirst);
        Assert.Equal(float.NegativeInfinity, 1f / cpuFirst);
        Assert.True(Half.IsNegative(BitConverter.UInt16BitsToHalf((ushort)gpu[10])) && Half.IsNegative(BitConverter.UInt16BitsToHalf((ushort)cpu[10])));
    }
}
