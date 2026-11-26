using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// The SIMD-group operations on halves and half vectors, and the half overloads of
/// <c>Gpu.Radians</c>, <c>Gpu.Degrees</c> and the bit casts, on the GPU against the CPU
/// (<c>ForCpu</c> of the same shader) and against plain C#. What is compared bit for bit was measured
/// on an Apple M4 Pro: the exchanges move bits, minima and maxima pick a lane's value, and a sum or
/// product whose partial results are all halves is the same in any order. Bit for bit has one
/// allowance throughout: a NaN is any NaN, whatever its sign and payload. Where the order of the
/// lanes shows (the GPU's is unspecified, the CPU's is lane order) the two agree within the rounding
/// of the 32 operations of a SIMD group.
/// </summary>
public class SimdGroupHalfTests
{
    private static Half H(double value) => (Half)value;

    private static ushort Bits(Half value) => BitConverter.HalfToUInt16Bits(value);

    private static ushort[] Words(Half4 value) => [Bits(value.X), Bits(value.Y), Bits(value.Z), Bits(value.W)];

    private static int Ordinal(ushort bits) => (bits & 0x8000) != 0 ? -(bits & 0x7FFF) : bits;

    /// <summary>
    /// Values whose sums and products are exact in any order: multiples of 1/2 up to 2 in magnitude,
    /// and as factors powers of two that cancel within every eight lanes.
    /// </summary>
    private static Half[] Exact(int count) =>
        Enumerable.Range(0, count).Select(static i => (i % 8) switch { 0 => 1.0, 1 => -1.0, 2 => 2.0, 3 => 0.5, 4 => -2.0, 5 => -0.5, 6 => 1.0, _ => 1.0 }).Select(H).ToArray();

    /// <summary>Arbitrary values between 1/4 and 4 in magnitude, of either sign: sums and products of 32 of them round on the way.</summary>
    private static Half[] Arbitrary(int count, int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => H((0.25 + (random.NextDouble() * 3.75)) * (random.Next(2) == 0 ? 1 : -1))).ToArray();
    }

    private static (ushort[] Gpu, ushort[] Cpu) Scalars(Half[] input, int delta)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<Half> buffer = device.AllocateReadOnlyBuffer<Half>(input);
        using ReadWriteBuffer<Half> gpu = device.AllocateReadWriteBuffer<Half>(input.Length * SimdHalfScalars.Stride);
        using ReadWriteBuffer<Half> cpu = device.AllocateReadWriteBuffer<Half>(input.Length * SimdHalfScalars.Stride);

        device.For(input.Length, new SimdHalfScalars(buffer, gpu, delta));
        device.ForCpu(input.Length, new SimdHalfScalars(buffer, cpu, delta));
        return (MemoryMarshal.Cast<Half, ushort>(gpu.AsSpan()).ToArray(), MemoryMarshal.Cast<Half, ushort>(cpu.AsSpan()).ToArray());
    }

    private static void AssertSameBits(ushort[] gpu, ushort[] cpu, int stride, int perValue, Func<int, bool> compared, string what)
    {
        Assert.Equal(gpu.Length, cpu.Length);
        string[] differences = Enumerable.Range(0, gpu.Length)
            .Where(i => compared(i / perValue % stride) && !FloatBits.SameBits(cpu[i], gpu[i]))
            .Select(i => $"thread {i / perValue / stride}, output {i / perValue % stride}, component {i % perValue}: GPU 0x{gpu[i]:X4}, CPU 0x{cpu[i]:X4}")
            .ToArray();

        Assert.True(differences.Length == 0, $"{what}: {differences.Length} halves differ between the GPU and the CPU:\n" + string.Join("\n", differences.Take(30)));
    }

    [MetalTheory]
    [InlineData(64, 3)]  // two full SIMD groups
    [InlineData(40, 5)]  // a full one and a partial one
    [InlineData(32, 0)]
    [InlineData(32, 31)]
    public void ScalarOperations_OnExactValues_AreBitExact(int threads, int delta)
    {
        Half[] input = Exact(threads);
        (ushort[] gpu, ushort[] cpu) = Scalars(input, delta);

        // Every output: reads of lanes that do not exist give 0 on the GPU (measured) and on the CPU.
        AssertSameBits(gpu, cpu, SimdHalfScalars.Stride, 1, static _ => true, "half SIMD-group operations");

        // The first SIMD group, restated in plain C#: lane i is thread i.
        int width = Math.Min(32, threads);
        Half[] lanes = input[..width];
        Half sum = lanes.Aggregate(H(0), static (a, b) => a + b), product = lanes.Aggregate(H(1), static (a, b) => a * b);
        // An exclusive scan of sums starts from -0, the identity of IEEE addition, on the GPU and on the CPU.
        Half negativeZero = BitConverter.UInt16BitsToHalf(0x8000);
        Half running = negativeZero, runningProduct = H(1);

        for (int lane = 0; lane < width; lane++)
        {
            ReadOnlySpan<ushort> o = cpu.AsSpan(lane * SimdHalfScalars.Stride, SimdHalfScalars.Stride);
            string at = $"lane {lane}";

            Assert.True(Bits(sum) == o[0] && Bits(product) == o[1], at);
            Assert.True(Bits(H(-2)) == o[2] && Bits(H(2)) == o[3], at);
            Assert.True(Bits(running) == o[4] && Bits(runningProduct) == o[5], at);                  // exclusive: before this lane
            running += lanes[lane];
            runningProduct *= lanes[lane];
            Assert.True(Bits(running) == o[6] && Bits(runningProduct) == o[7], at);                  // inclusive
            Assert.True(Bits(lanes[2]) == o[8] && Bits(lanes[0]) == o[9], at);
            Assert.True(Bits(lane >= delta ? lanes[lane - delta] : lanes[lane]) == o[10], at);       // the lowest lanes keep their own value
            Assert.True(Bits(lanes[lane ^ 1]) == o[12], at);
            Assert.True(o[17] == 0 && Bits(H(1)) == o[18], at);                                      // not all equal; the first lane's value is

            if (width == 32)
            {
                Assert.True(Bits(lanes[(lane - delta + 32) % 32]) == o[13], at);
                Assert.True(Bits(lanes[(lane + delta) % 32]) == o[14], at);
                Assert.True(Bits(lane >= delta ? lanes[lane - delta] : -lanes[lane - delta + 32]) == o[15], at);  // filled with the fill of lane i - delta + 32
                Assert.True(Bits(lane + delta < 32 ? lanes[lane + delta] : -lanes[lane + delta - 32]) == o[16], at);
            }

            // The quad: four lanes in a row.
            int quad = lane & ~3;
            Half[] four = lanes[quad..(quad + 4)];
            Assert.True(Bits(four.Aggregate(H(0), static (a, b) => a + b)) == o[19], at);
            Assert.True(Bits(four.Aggregate(H(1), static (a, b) => a * b)) == o[20], at);
            Assert.True(Bits(four.Min()) == o[21] && Bits(four.Max()) == o[22], at);
            Assert.True(Bits(four.Take(lane - quad).Aggregate(negativeZero, static (a, b) => a + b)) == o[23], at);
            Assert.True(Bits(four.Take(lane - quad + 1).Aggregate(H(1), static (a, b) => a * b)) == o[26], at);
            Assert.True(Bits(lanes[lane ^ 1]) == o[27] && Bits(lanes[lane ^ 2]) == o[28] && Bits(lanes[lane ^ 3]) == o[29], at);
            Assert.True(Bits(lanes[quad + 3 - (lane & 3)]) == o[30], at);
        }
    }

    [MetalFact]
    public void ScalarOperations_OnArbitraryValues_AreBitExactExceptWhereTheOrderOfTheLanesShows()
    {
        const int threads = 512;
        Half[] input = Arbitrary(threads, seed: 31);

        // Values the exchanges must carry as they are: -0, a subnormal and an infinity bit for bit, and a
        // NaN as a NaN (it has a sign and a payload here, which are not asked for on the other side).
        // Each sits in a SIMD group of its own, so that the sums and products of the others stay finite.
        input[32 + 5] = BitConverter.UInt16BitsToHalf(0xFE55);
        input[96 + 7] = BitConverter.UInt16BitsToHalf(0x8000);
        input[160 + 9] = BitConverter.UInt16BitsToHalf(0x0003);
        input[224 + 11] = Half.NegativeInfinity;

        (ushort[] gpu, ushort[] cpu) = Scalars(input, delta: 7);

        // Everything that only moves or picks values, on every SIMD group.
        bool Moves(int output) => !SimdHalfScalars.Sums.Contains(output) && !SimdHalfScalars.Products.Contains(output) && output is not (2 or 3 or 21 or 22);
        AssertSameBits(gpu, cpu, SimdHalfScalars.Stride, 1, Moves, "half exchanges");

        // The groups without a special value: minima and maxima exactly (a NaN or zeros of both signs
        // leave the answer open), sums and products within the rounding of their 32 operations. Measured
        // on an Apple M4 Pro with these inputs: sums up to 3/64 apart (the partial sums reach 64, where
        // halves are 1/32 apart) and products up to 4 ULP; the bounds leave a margin of four around that.

        for (int thread = 0; thread < threads; thread++)
        {
            if (thread / 32 is 1 or 3 or 5 or 7)
            {
                continue;
            }

            for (int output = 0; output < SimdHalfScalars.Stride; output++)
            {
                ushort g = gpu[(thread * SimdHalfScalars.Stride) + output], c = cpu[(thread * SimdHalfScalars.Stride) + output];

                if (output is 2 or 3 or 21 or 22)
                {
                    Assert.True(FloatBits.SameBits(c, g), $"thread {thread}, output {output}: GPU 0x{g:X4}, CPU 0x{c:X4}");
                }
                else if (SimdHalfScalars.Sums.Contains(output))
                {
                    // A sum can cancel to a small result that carries the rounding of large partial
                    // sums, so the bound is in units of the largest partial sum's spacing, not the result's.
                    float difference = MathF.Abs((float)BitConverter.UInt16BitsToHalf(g) - (float)BitConverter.UInt16BitsToHalf(c));
                    Assert.True(difference <= 0.1875f, $"thread {thread}, sum {output}: GPU {(float)BitConverter.UInt16BitsToHalf(g):R}, CPU {(float)BitConverter.UInt16BitsToHalf(c):R}");
                }
                else if (SimdHalfScalars.Products.Contains(output))
                {
                    // A product of 32 values between 1/4 and 4 overflows or underflows in one order and
                    // not in the other only at the very ends of the range; in between it is a matter of ULPs.
                    if (FloatBits.IsNaN(g) || FloatBits.IsNaN(c) || (g & 0x7C00) == 0x7C00 || (c & 0x7C00) == 0x7C00 || (g & 0x7C00) == 0 || (c & 0x7C00) == 0)
                    {
                        continue;
                    }

                    int ulps = Math.Abs(Ordinal(g) - Ordinal(c));
                    Assert.True(ulps <= 16, $"thread {thread}, product {output}: GPU 0x{g:X4}, CPU 0x{c:X4} ({ulps} ULP)");
                }
            }
        }

        // The special values did travel: lane 5 of the second group read by its neighbour is a NaN on
        // both sides, and the others arrive bit for bit.
        Assert.True(FloatBits.IsNaN(cpu[((32 + 4) * SimdHalfScalars.Stride) + 12]));
        Assert.True(FloatBits.IsNaN(gpu[((32 + 4) * SimdHalfScalars.Stride) + 12]));
        Assert.Equal(0x8000, gpu[((96 + 6) * SimdHalfScalars.Stride) + 12]);
        Assert.Equal(0x0003, gpu[((160 + 8) * SimdHalfScalars.Stride) + 12]);
        Assert.Equal(0xFC00, gpu[((224 + 10) * SimdHalfScalars.Stride) + 12]);
    }

    [MetalTheory]
    [InlineData(64, 3)]
    [InlineData(40, 5)]
    public void VectorOperations_AreBitExactOnExactValues_AndWithinRoundingOnArbitraryOnes(int threads, int delta)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        (ushort[] Gpu, ushort[] Cpu) Run(Half4[] input)
        {
            using ReadOnlyBuffer<Half4> buffer = device.AllocateReadOnlyBuffer<Half4>(input);
            using ReadWriteBuffer<Half4> gpu = device.AllocateReadWriteBuffer<Half4>(threads * SimdHalfVectors.Stride);
            using ReadWriteBuffer<Half4> cpu = device.AllocateReadWriteBuffer<Half4>(threads * SimdHalfVectors.Stride);

            device.For(threads, new SimdHalfVectors(buffer, gpu, delta));
            device.ForCpu(threads, new SimdHalfVectors(buffer, cpu, delta));
            return (MemoryMarshal.Cast<Half4, ushort>(gpu.AsSpan()).ToArray(), MemoryMarshal.Cast<Half4, ushort>(cpu.AsSpan()).ToArray());
        }

        // Exact values, a different rotation of the pattern in every component: everything is bit for bit.
        Half[] exact = Exact(threads + 8);
        Half4[] exactInput = Enumerable.Range(0, threads).Select(i => new Half4(exact[i], exact[i + 1], exact[i + 2], exact[i + 5])).ToArray();
        (ushort[] gpu, ushort[] cpu) = Run(exactInput);
        AssertSameBits(gpu, cpu, SimdHalfVectors.Stride, 4, static _ => true, "half vector SIMD-group operations");

        // The sum of the first group's vectors, component by component, and the vector lane 2 brought.
        Half4 sum = exactInput.Take(Math.Min(32, threads)).Aggregate(new Half4(H(0)), static (a, b) => a + b);
        Assert.Equal(new[] { Bits(sum.X), Bits(sum.Y), Bits(sum.Z), Bits(sum.W) }, cpu.AsSpan(0, 4).ToArray());
        Assert.Equal(MemoryMarshal.Cast<Half4, ushort>(exactInput.AsSpan(2, 1)).ToArray(), cpu.AsSpan(7 * 4, 4).ToArray());

        // Arbitrary values: what moves or picks is exact, what adds or multiplies is within rounding.
        Half[] arbitrary = Arbitrary((threads * 4) + 4, seed: 32);
        Half4[] arbitraryInput = Enumerable.Range(0, threads).Select(i => new Half4(arbitrary[i * 4], arbitrary[(i * 4) + 1], arbitrary[(i * 4) + 2], arbitrary[(i * 4) + 3])).ToArray();
        (gpu, cpu) = Run(arbitraryInput);
        AssertSameBits(gpu, cpu, SimdHalfVectors.Stride, 4, static output => !SimdHalfVectors.OrderDependent.Contains(output), "half vector exchanges, minima and maxima");

        for (int i = 0; i < gpu.Length; i++)
        {
            if (SimdHalfVectors.OrderDependent.Contains(i / 4 % SimdHalfVectors.Stride))
            {
                float g = (float)BitConverter.UInt16BitsToHalf(gpu[i]), c = (float)BitConverter.UInt16BitsToHalf(cpu[i]);
                Assert.True(
                    (float.IsNaN(g) && float.IsNaN(c)) || g == c || MathF.Abs(g - c) <= MathF.Max(0.5f, MathF.Max(MathF.Abs(g), MathF.Abs(c)) * (32f / 1024f)),
                    $"thread {i / 4 / SimdHalfVectors.Stride}, output {i / 4 % SimdHalfVectors.Stride}, component {i % 4}: GPU {g:R}, CPU {c:R}");
            }
        }
    }

    [MetalFact]
    public void SmallIntegers_StillGoToTheIntOverloads()
    {
        // Half converts implicitly from byte and sbyte, and a byte is a shader type: the scalar Half
        // overloads have the lower priority, so these calls are the int ones they always were.
        byte small = 3;
        sbyte negative = -3;
        System.Linq.Expressions.Expression<Func<int>> fromByte = () => Gpu.WaveActiveSum(small, "", 0);
        System.Linq.Expressions.Expression<Func<int>> fromSByte = () => Gpu.WaveReadLaneFirst(negative, "", 0);
        System.Linq.Expressions.Expression<Func<int>> fromConstant = () => Gpu.QuadActiveMax(7, "", 0);

        Assert.All(new[] { fromByte, fromSByte, fromConstant }, static e =>
            Assert.Equal(typeof(int), ((System.Linq.Expressions.MethodCallExpression)e.Body).Method.GetParameters()[0].ParameterType));
    }

    [MetalFact]
    public void RadiansDegreesAndTheBitCasts_AreBitExact_OverEveryHalf()
    {
        const int count = 65536;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(33);
        Half4[] input = Enumerable.Range(0, count)
            .Select(i => new Half4(BitConverter.UInt16BitsToHalf((ushort)i), BitConverter.UInt16BitsToHalf((ushort)random.Next(65536)), H((random.NextDouble() * 720) - 360), H((random.NextDouble() * 12) - 6)))
            .ToArray();

        using ReadOnlyBuffer<Half4> buffer = device.AllocateReadOnlyBuffer<Half4>(input);
        using ReadWriteBuffer<Half4> gpuScaled = device.AllocateReadWriteBuffer<Half4>(count * HalfScalesAndBits.ScaledStride), cpuScaled = device.AllocateReadWriteBuffer<Half4>(count * HalfScalesAndBits.ScaledStride);
        using ReadWriteBuffer<ushort> gpuBits = device.AllocateReadWriteBuffer<ushort>(count * HalfScalesAndBits.BitsStride), cpuBits = device.AllocateReadWriteBuffer<ushort>(count * HalfScalesAndBits.BitsStride);
        using ReadWriteBuffer<Half> gpuFromBits = device.AllocateReadWriteBuffer<Half>(count * 4), cpuFromBits = device.AllocateReadWriteBuffer<Half>(count * 4);

        device.For(count, new HalfScalesAndBits(buffer, gpuScaled, gpuBits, gpuFromBits));
        device.ForCpu(count, new HalfScalesAndBits(buffer, cpuScaled, cpuBits, cpuFromBits));

        // Radians and degrees: one half multiplication on both sides, so the same bits (any NaN for any NaN).
        AssertSameBits(
            MemoryMarshal.Cast<Half4, ushort>(gpuScaled.AsSpan()).ToArray(), MemoryMarshal.Cast<Half4, ushort>(cpuScaled.AsSpan()).ToArray(),
            HalfScalesAndBits.ScaledStride, 4, static _ => true, "half radians and degrees");

        // The bit casts: the bits of every number as they are, and a NaN as a NaN, whatever its sign
        // and payload. The outputs are the bits of halves, so the allowance reads them as halves; the
        // last one is the sign of the short alone, which for a NaN is not compared.
        ushort[] gpuWords = gpuBits.ToArray(), cpuWords = cpuBits.ToArray();
        AssertSameBits(gpuWords, cpuWords, HalfScalesAndBits.BitsStride, 1, static output => output != HalfScalesAndBits.BitsStride - 1, "the bits of a half");

        for (int i = 0; i < count; i++)
        {
            int sign = (i * HalfScalesAndBits.BitsStride) + HalfScalesAndBits.BitsStride - 1;
            Assert.True(gpuWords[sign] == cpuWords[sign] || FloatBits.IsNaN((ushort)i), $"the sign of the short of 0x{i:X4}: GPU {gpuWords[sign]}, CPU {cpuWords[sign]}");
        }

        AssertSameBits(
            MemoryMarshal.Cast<Half, ushort>(gpuFromBits.AsSpan()).ToArray(), MemoryMarshal.Cast<Half, ushort>(cpuFromBits.AsSpan()).ToArray(),
            4, 1, static _ => true, "halves from bits");

        // And they are what the definitions say.
        Half radiansPerDegree = (Half)0.017453292519943295f, degreesPerRadian = (Half)57.29577951308232f;
        Half4[] scaled = cpuScaled.ToArray();
        ushort[] bits = cpuBits.ToArray();
        ushort[] fromBits = MemoryMarshal.Cast<Half, ushort>(cpuFromBits.AsSpan()).ToArray();

        for (int i = 0; i < count; i += 13)
        {
            Half4 x = input[i];
            ushort u = (ushort)i, y = Bits(x.Y);

            Assert.True(Words(x * radiansPerDegree).Zip(Words(scaled[i * 4]), FloatBits.SameBits).All(static same => same), $"radians of {x}");
            Assert.True(Words(x * degreesPerRadian).Zip(Words(scaled[(i * 4) + 1]), FloatBits.SameBits).All(static same => same), $"degrees of {x}");

            // The same allowance on the CPU side: a NaN's bits are a NaN's bits, and its sign is not looked at.
            ushort[] expectedBits = [u, u, y, y, (ushort)(u ^ 0x8000), (ushort)(u >> 15)];
            ushort[] expectedHalves = [u, u, (ushort)(u | 1), (ushort)(u & 0x7FFF)];

            for (int k = 0; k < HalfScalesAndBits.BitsStride; k++)
            {
                ushort actual = bits[(i * HalfScalesAndBits.BitsStride) + k];
                Assert.True(k == HalfScalesAndBits.BitsStride - 1 ? actual == expectedBits[k] || FloatBits.IsNaN(u) : FloatBits.SameBits(expectedBits[k], actual), $"bits output {k} of 0x{u:X4}: 0x{actual:X4}");
            }

            for (int k = 0; k < 4; k++)
            {
                Assert.True(FloatBits.SameBits(expectedHalves[k], fromBits[(i * 4) + k]), $"half {k} from the bits 0x{u:X4}: 0x{fromBits[(i * 4) + k]:X4}");
            }
        }
    }
}
