using System;
using System.Collections.Generic;
using System.Linq;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// The SIMD-group operations on the GPU: how lanes are formed from a threadgroup, which lanes an
/// operation sees, and the value of every operation for known inputs. Expectations are literals, or
/// come from <see cref="Lanes"/>, which is the documented model (32 consecutive threads of a
/// threadgroup, in the order of their linear index) and nothing else.
/// </summary>
public class SimdGroupTests
{
    private const int Width = 32;

    // ---- how SIMD groups are formed ---------------------------------------------------------------

    [MetalFact]
    public void Lanes1D_AreConsecutiveThreadsOfTheThreadgroup()
    {
        // Threadgroups of 64: two full ones would be 128 threads; 100 leaves the second with 36.
        const int threads = 100;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(threads * 4);

        device.For(threads, new SimdLanes1D(output));
        int[] actual = output.ToArray();

        (int Lane, int Count, int Group, int Groups) At(int thread) => (actual[thread * 4], actual[(thread * 4) + 1], actual[(thread * 4) + 2], actual[(thread * 4) + 3]);

        // lane, lanes per group, SIMD group, SIMD groups of the threadgroup
        Assert.Equal((0, 32, 0, 2), At(0));
        Assert.Equal((31, 32, 0, 2), At(31));
        Assert.Equal((0, 32, 1, 2), At(32));
        Assert.Equal((31, 32, 1, 2), At(63));

        // The second threadgroup has 36 threads: one full SIMD group and one of 4 lanes.
        Assert.Equal((0, 32, 0, 2), At(64));
        Assert.Equal((31, 32, 0, 2), At(95));
        Assert.Equal((0, 32, 1, 2), At(96));
        Assert.Equal((3, 32, 1, 2), At(99));

        for (int thread = 0; thread < threads; thread++)
        {
            int index = thread % 64;
            Assert.Equal((index % 32, 32, index / 32, 2), At(thread));
        }
    }

    [MetalFact]
    public void Lanes1D_AThreadgroupSmallerThanASimdGroup_HasOnePartialGroup()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(5 * 4);

        device.For(5, new SimdLanes1D(output));

        // The lane count is the width of a full group, also when only 5 lanes exist.
        Assert.Equal(
            [0, 32, 0, 1, 1, 32, 0, 1, 2, 32, 0, 1, 3, 32, 0, 1, 4, 32, 0, 1],
            output.ToArray());
    }

    [MetalTheory]
    [InlineData(12, 12)] // one full threadgroup: 144 threads, groups of 32, 32, 32, 32 and 16
    [InlineData(20, 10)] // two partial threadgroups: 12 x 10 and 8 x 10
    [InlineData(30, 31)] // partial threadgroups along both axes
    public void Lanes2D_FollowTheLinearIndexOfTheThreadgroup_RowAfterRow(int width, int height)
    {
        const int groupX = 12, groupY = 12;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(width * height * 5);

        device.For(width, height, new SimdLanes2D(output));
        int[] actual = output.ToArray();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                int localX = x % groupX, localY = y % groupY;

                // A threadgroup at the edge of the grid is cut off there, and its threads are numbered
                // with the size it really has.
                int extentX = Math.Min(groupX, width - (x - localX));
                int extentY = Math.Min(groupY, height - (y - localY));
                int index = (localY * extentX) + localX;
                int groups = ((extentX * extentY) + Width - 1) / Width;

                Assert.True(
                    (index % Width, index / Width, groups, localX, localY) == (actual[i * 5], actual[(i * 5) + 1], actual[(i * 5) + 2], actual[(i * 5) + 3], actual[(i * 5) + 4]),
                    $"thread ({x}, {y}): expected lane {index % Width} of SIMD group {index / Width} of {groups}, got lane {actual[i * 5]} of group {actual[(i * 5) + 1]} of {actual[(i * 5) + 2]}");
            }
        }

        if (width == 12 && height == 12)
        {
            // Rows are 12 wide, so a SIMD group runs across rows: row 2 holds lanes 24..31 of group 0
            // and then lanes 0..3 of group 1.
            Assert.Equal(24, actual[((2 * width) + 0) * 5]);
            Assert.Equal(31, actual[((2 * width) + 7) * 5]);
            Assert.Equal((0, 1), (actual[((2 * width) + 8) * 5], actual[(((2 * width) + 8) * 5) + 1]));
            Assert.Equal((15, 4, 5), (actual[^5], actual[^4], actual[^3]));
        }
    }

    [MetalTheory]
    [InlineData(1000, 1, 1)]  // 256 per threadgroup; the last has 232 threads
    [InlineData(40, 24, 1)]   // 16 x 16
    [InlineData(20, 12, 9)]   // 8 x 8 x 4
    public void Lanes_WithTheDefaultThreadgroupSize_FollowTheLinearIndex(int x, int y, int z)
    {
        (int gx, int gy, int gz) = z > 1 ? (8, 8, 4) : y > 1 ? (16, 16, 1) : (256, 1, 1);
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(x * y * z * 6);
        SimdLanesDefault shader = new(output);

        if (z > 1)
        {
            device.For(x, y, z, shader);
        }
        else if (y > 1)
        {
            device.For(x, y, shader);
        }
        else
        {
            device.For(x, shader);
        }

        int[] actual = output.ToArray();

        for (int i = 0; i < x * y * z; i++)
        {
            int tx = i % x, ty = i / x % y, tz = i / (x * y);
            int lx = tx % gx, ly = ty % gy, lz = tz % gz;
            int ex = Math.Min(gx, x - (tx - lx)), ey = Math.Min(gy, y - (ty - ly)), ez = Math.Min(gz, z - (tz - lz));
            int index = (((lz * ey) + ly) * ex) + lx;

            Assert.True(
                actual.AsSpan(i * 6, 6).SequenceEqual([index % Width, index / Width, ((ex * ey * ez) + Width - 1) / Width, lx, ly, lz]),
                $"thread ({tx}, {ty}, {tz}): expected lane {index % Width} of group {index / Width}, got [{string.Join(", ", actual.AsSpan(i * 6, 6).ToArray())}]");
        }
    }

    [MetalFact]
    public void TheSimdGroupWidth_Is32_AsTheBallotAssumes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(4);

        device.For(1, new SimdLanes1D(output));

        Assert.Equal(CpuSimdGroup.Width, output[1]);
    }

    // ---- votes -----------------------------------------------------------------------------------

    [MetalFact]
    public void Votes_SeeTheLanesThatHaveNotReturned()
    {
        // 70 values in threadgroups of 64, dispatched over 96 threads: the threads past 70 return
        // first. SIMD groups: threads 0..31, 32..63, and 64..69 (6 of the 32 lanes of the third).
        const int count = 70, threads = 96;
        int[] input = Enumerable.Range(0, count).ToArray();
        input[40] = 100;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<uint> output = device.AllocateReadWriteBuffer<uint>(threads * SimdVotes.Stride);
        output.AsSpan().Fill(uint.MaxValue);

        device.For(threads, new SimdVotes(inputBuffer, output, count));
        uint[] actual = output.ToArray();

        uint[] At(int thread) => actual.AsSpan(thread * SimdVotes.Stride, SimdVotes.Stride).ToArray();

        // ballot(odd), ballot(true), any(v >= 100), all(v < 100), count(odd), prefix count(odd), first lane
        Assert.Equal([0xAAAAAAAAu, 0xFFFFFFFFu, 0u, 1u, 16u, 0u, 1u], At(0));
        Assert.Equal([0xAAAAAAAAu, 0xFFFFFFFFu, 0u, 1u, 16u, 0u, 0u], At(1));
        Assert.Equal([0xAAAAAAAAu, 0xFFFFFFFFu, 0u, 1u, 16u, 1u, 0u], At(2));
        Assert.Equal([0xAAAAAAAAu, 0xFFFFFFFFu, 0u, 1u, 16u, 15u, 0u], At(31));

        // The second group holds the one value of 100, at thread 40 (lane 8; still even, so the ballot is the same).
        Assert.Equal([0xAAAAAAAAu, 0xFFFFFFFFu, 1u, 0u, 16u, 0u, 1u], At(32));
        Assert.Equal([0xAAAAAAAAu, 0xFFFFFFFFu, 1u, 0u, 16u, 4u, 0u], At(40));

        // The third group has 6 active lanes: 64..69. Odd values are in lanes 1, 3 and 5.
        Assert.Equal([0b101010u, 0b111111u, 0u, 1u, 3u, 0u, 1u], At(64));
        Assert.Equal([0b101010u, 0b111111u, 0u, 1u, 3u, 2u, 0u], At(69));

        // Threads that returned wrote nothing.
        Assert.All(actual.AsSpan(count * SimdVotes.Stride).ToArray(), static value => Assert.Equal(uint.MaxValue, value));
    }

    // ---- reductions and scans --------------------------------------------------------------------

    [MetalFact]
    public void IntReductions_OfKnownValues()
    {
        // One full SIMD group (values 1..32 with a few signs) and a partial one of 5 lanes: 3, -2, 7, 1, 4.
        int[] input = [.. Enumerable.Range(1, 32).Select(static v => v % 5 == 0 ? -v : v), 3, -2, 7, 1, 4];
        int[] actual = Run(input, threads: 64);

        int[] At(int thread) => actual.AsSpan(thread * SimdReduceInt.Stride, SimdReduceInt.Stride).ToArray();

        // sum, product, min, max, and, or, xor, prefix sum, prefix product, inclusive sum, inclusive product
        Assert.Equal([13, -168, -2, 7, 0, -1, -1, 0, 1, 3, 3], At(32));
        Assert.Equal([13, -168, -2, 7, 0, -1, -1, 3, 3, 1, -6], At(33));
        Assert.Equal([13, -168, -2, 7, 0, -1, -1, 1, -6, 8, -42], At(34));
        Assert.Equal([13, -168, -2, 7, 0, -1, -1, 8, -42, 9, -42], At(35));
        Assert.Equal([13, -168, -2, 7, 0, -1, -1, 9, -42, 13, -168], At(36));

        // The full group against the model: 32! wraps, as integer arithmetic does on both sides.
        AssertReductions(input, actual, SimdReduceInt.Stride, threadgroup: 64, thread: 0);
        AssertReductions(input, actual, SimdReduceInt.Stride, threadgroup: 64, thread: 17);
        AssertReductions(input, actual, SimdReduceInt.Stride, threadgroup: 64, thread: 31);

        static int[] Run(int[] input, int threads)
        {
            GraphicsDevice device = GraphicsDevice.GetDefault();
            using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
            using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(threads * SimdReduceInt.Stride);

            device.For(threads, new SimdReduceInt(inputBuffer, output, input.Length));
            return output.ToArray();
        }
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(100)]
    [InlineData(333)]
    public void IntReductions_MatchTheModel_ForPartialSimdGroupsAndThreadgroups(int count)
    {
        Random random = new(count);
        int[] input = Enumerable.Range(0, count).Select(_ => random.Next(-1000, 1000)).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(count * SimdReduceInt.Stride);

        // The grid is exactly the data: the last SIMD group is partial because the lanes do not exist.
        device.For(count, new SimdReduceInt(inputBuffer, output, count));
        int[] actual = output.ToArray();

        for (int thread = 0; thread < count; thread++)
        {
            AssertReductions(input, actual, SimdReduceInt.Stride, threadgroup: 64, thread);
        }
    }

    [MetalFact]
    public void UIntReductions_OfKnownValues_WrapAndUseUnsignedOrder()
    {
        // 0xFFFFFFF0 is the largest as a uint (it would be negative, and the smallest, as an int).
        uint[] input = [5u, 0xFFFFFFF0u, 3u, 0x80000001u];

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<uint> inputBuffer = device.AllocateReadOnlyBuffer<uint>(input);
        using ReadWriteBuffer<uint> output = device.AllocateReadWriteBuffer<uint>(input.Length * SimdReduceUInt.Stride);

        device.For(input.Length, new SimdReduceUInt(inputBuffer, output, input.Length));
        uint[] actual = output.ToArray();

        uint[] At(int thread) => actual.AsSpan(thread * SimdReduceUInt.Stride, SimdReduceUInt.Stride).ToArray();

        uint sum = unchecked(5u + 0xFFFFFFF0u + 3u + 0x80000001u);
        uint product = unchecked(5u * 0xFFFFFFF0u * 3u * 0x80000001u);
        Assert.Equal(0x7FFFFFF9u, sum);

        // sum, product, min, max, and, or, xor, prefix sum, prefix product, inclusive sum, inclusive product
        Assert.Equal([sum, product, 3u, 0xFFFFFFF0u, 0u, 0xFFFFFFF7u, 0x7FFFFFF7u, 0u, 1u, 5u, 5u], At(0));
        Assert.Equal([sum, product, 3u, 0xFFFFFFF0u, 0u, 0xFFFFFFF7u, 0x7FFFFFF7u, 5u, 5u, 0xFFFFFFF5u, 0xFFFFFFB0u], At(1));
        Assert.Equal([sum, product, 3u, 0xFFFFFFF0u, 0u, 0xFFFFFFF7u, 0x7FFFFFF7u, 0xFFFFFFF5u, 0xFFFFFFB0u, 0xFFFFFFF8u, 0xFFFFFF10u], At(2));
        Assert.Equal([sum, product, 3u, 0xFFFFFFF0u, 0u, 0xFFFFFFF7u, 0x7FFFFFF7u, 0xFFFFFFF8u, 0xFFFFFF10u, sum, product], At(3));
    }

    [MetalFact]
    public void FloatReductions_OfExactlyRepresentableValues_AreExact()
    {
        // Small integers and powers of two: every sum and product is exact, whatever order the GPU uses.
        float[] input = [1f, 2f, 0.5f, -4f, 8f, 3f];

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<float> output = device.AllocateReadWriteBuffer<float>(input.Length * SimdReduceFloat.Stride);

        device.For(input.Length, new SimdReduceFloat(inputBuffer, output, input.Length));
        float[] actual = output.ToArray();

        float[] At(int thread) => actual.AsSpan(thread * SimdReduceFloat.Stride, SimdReduceFloat.Stride).ToArray();

        // sum, product, min, max, prefix sum, prefix product, inclusive sum, inclusive product
        Assert.Equal([10.5f, -96f, -4f, 8f, 0f, 1f, 1f, 1f], At(0));
        Assert.Equal([10.5f, -96f, -4f, 8f, 1f, 1f, 3f, 2f], At(1));
        Assert.Equal([10.5f, -96f, -4f, 8f, 3f, 2f, 3.5f, 1f], At(2));
        Assert.Equal([10.5f, -96f, -4f, 8f, 3.5f, 1f, -0.5f, -4f], At(3));
        Assert.Equal([10.5f, -96f, -4f, 8f, -0.5f, -4f, 7.5f, -32f], At(4));
        Assert.Equal([10.5f, -96f, -4f, 8f, 7.5f, -32f, 10.5f, -96f], At(5));
    }

    [MetalFact]
    public void FloatSums_OfArbitraryValues_AreWithinRoundingOfTheExactSum()
    {
        const int count = 1000;
        Random random = new(7);
        float[] input = Enumerable.Range(0, count).Select(_ => (random.NextSingle() * 200f) - 100f).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<float> output = device.AllocateReadWriteBuffer<float>(count * SimdReduceFloat.Stride);

        device.For(count, new SimdReduceFloat(inputBuffer, output, count));
        float[] actual = output.ToArray();

        foreach (int[] group in Lanes(count, threadgroup: 64))
        {
            double sum = group.Sum(thread => (double)input[thread]);
            double magnitude = group.Sum(thread => Math.Abs((double)input[thread]));

            // 32 additions of values of this magnitude: each rounds by at most half an ULP of the running sum.
            double tolerance = magnitude * 32 * 6e-8;
            double running = 0;

            foreach (int thread in group)
            {
                Assert.InRange(actual[thread * SimdReduceFloat.Stride], sum - tolerance, sum + tolerance);
                Assert.Equal(group.Min(t => input[t]), actual[(thread * SimdReduceFloat.Stride) + 2]);
                Assert.Equal(group.Max(t => input[t]), actual[(thread * SimdReduceFloat.Stride) + 3]);
                Assert.InRange(actual[(thread * SimdReduceFloat.Stride) + 4], running - tolerance, running + tolerance);
                running += input[thread];
                Assert.InRange(actual[(thread * SimdReduceFloat.Stride) + 6], running - tolerance, running + tolerance);
            }
        }
    }

    [MetalFact]
    public void VectorReductions_AreComponentwise_AlsoForPackedBufferElements()
    {
        // 40 threads in threadgroups of 32: a full SIMD group and one of 8 lanes.
        const int count = 40;
        Float3[] floats = Enumerable.Range(0, count).Select(static i => new Float3(i, -i, i % 4)).ToArray();
        Int2[] ints = Enumerable.Range(0, count).Select(static i => new Int2(i, 1 << (i % 8))).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<Float3> floatBuffer = device.AllocateReadOnlyBuffer<Float3>(floats);
        using ReadOnlyBuffer<Int2> intBuffer = device.AllocateReadOnlyBuffer<Int2>(ints);
        using ReadWriteBuffer<Float3> floatSums = device.AllocateReadWriteBuffer<Float3>(count);
        using ReadWriteBuffer<Float3> floatMax = device.AllocateReadWriteBuffer<Float3>(count);
        using ReadWriteBuffer<Int2> intSums = device.AllocateReadWriteBuffer<Int2>(count);
        using ReadWriteBuffer<Int2> intOr = device.AllocateReadWriteBuffer<Int2>(count);
        using ReadWriteBuffer<UInt4> prefix = device.AllocateReadWriteBuffer<UInt4>(count);

        device.For(count, new SimdReduceVectors(floatBuffer, intBuffer, floatSums, floatMax, intSums, intOr, prefix));

        // Threads 0..31: sum of 0..31 is 496; i % 4 sums to 8 * (0 + 1 + 2 + 3).
        Assert.All(floatSums.ToArray().Take(32), static v => Assert.Equal(new Float3(496f, -496f, 48f), v));
        Assert.All(floatMax.ToArray().Take(32), static v => Assert.Equal(new Float3(31f, 0f, 3f), v));
        Assert.All(intSums.ToArray().Take(32), static v => Assert.Equal(new Int2(496, 4 * 255), v));
        Assert.All(intOr.ToArray().Take(32), static v => Assert.Equal(new Int2(31, 255), v));

        // Threads 32..39: 32 + ... + 39 = 284.
        Assert.All(floatSums.ToArray().Skip(32), static v => Assert.Equal(new Float3(284f, -284f, 12f), v));
        Assert.All(floatMax.ToArray().Skip(32), static v => Assert.Equal(new Float3(39f, -32f, 3f), v));
        Assert.All(intSums.ToArray().Skip(32), static v => Assert.Equal(new Int2(284, 255), v));
        Assert.All(intOr.ToArray().Skip(32), static v => Assert.Equal(new Int2(32 | 39, 255), v));

        // An exclusive scan of (1, 2, i, 0): lane L has L, 2L and the sum of the thread indices below it.
        UInt4[] scan = prefix.ToArray();
        Assert.Equal(new UInt4(0u, 0u, 0u, 0u), scan[0]);
        Assert.Equal(new UInt4(5u, 10u, 10u, 0u), scan[5]);
        Assert.Equal(new UInt4(31u, 62u, 465u, 0u), scan[31]);
        Assert.Equal(new UInt4(0u, 0u, 0u, 0u), scan[32]);
        Assert.Equal(new UInt4(7u, 14u, 32u + 33u + 34u + 35u + 36u + 37u + 38u, 0u), scan[39]);
    }

    // ---- which lanes are active -------------------------------------------------------------------

    [MetalFact]
    public void ActiveLanes_AreTheOnesExecutingTheCall_ReturnedAndOtherBranchLanesDoNotCount()
    {
        // One full SIMD group, values 100 + lane.
        int[] input = Enumerable.Range(100, 32).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(32 * SimdActiveLanes.Stride);
        output.AsSpan().Fill(int.MinValue);

        device.For(32, new SimdActiveLanes(inputBuffer, output));
        int[] actual = output.ToArray();

        int[] At(int lane) => actual.AsSpan(lane * SimdActiveLanes.Stride, SimdActiveLanes.Stride).ToArray();

        const int all = (32 * 100) + 496;                    // every lane
        const int even = (16 * 100) + 240, odd = (16 * 100) + 256;
        const int remaining = all - ((11 * 100) + 165);      // without lanes 0, 3, ..., 30
        const int mask = unchecked((int)0xB6DB6DB6);         // the lanes that are not multiples of 3

        // sum of all, sum within the branch, then after lanes 0, 3, 6, ... returned:
        // sum, exclusive count, first lane's value, is first, active mask, active count
        Assert.Equal([all, even, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue], At(0));
        Assert.Equal([all, -odd, remaining, 0, 101, 1, mask, 21], At(1));
        Assert.Equal([all, even, remaining, 1, 101, 0, mask, 21], At(2));
        Assert.Equal([all, -odd, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue], At(3));
        Assert.Equal([all, even, remaining, 2, 101, 0, mask, 21], At(4));
        Assert.Equal([all, -odd, remaining, 20, 101, 0, mask, 21], At(31));
    }

    // ---- exchanges -------------------------------------------------------------------------------

    [MetalFact]
    public void Exchanges_InAFullSimdGroup()
    {
        int[] input = Enumerable.Range(1000, 32).ToArray();
        int[] actual = RunExchange(input, delta: 3);

        int[] At(int lane) => actual.AsSpan(lane * SimdExchange.Stride, SimdExchange.Stride).ToArray();

        // lane 2, the mirrored lane, the first lane, up 3, down 3, xor 1, xor 16, down 1 then up 1.
        // (Down 1 then up 1 is the identity except at the ends: lane 0 has no lane below, so it keeps what
        // the shuffle down gave it, lane 1's value.)
        // The lowest 3 lanes of a shuffle up and the highest 3 of a shuffle down keep their own value.
        Assert.Equal([1002, 1031, 1000, 1000, 1003, 1001, 1016, 1001], At(0));
        Assert.Equal([1002, 1030, 1000, 1001, 1004, 1000, 1017, 1001], At(1));
        Assert.Equal([1002, 1029, 1000, 1002, 1005, 1003, 1018, 1002], At(2));
        Assert.Equal([1002, 1028, 1000, 1000, 1006, 1002, 1019, 1003], At(3));
        Assert.Equal([1002, 1015, 1000, 1013, 1019, 1017, 1000, 1016], At(16));
        Assert.Equal([1002, 1003, 1000, 1025, 1031, 1029, 1012, 1028], At(28));
        Assert.Equal([1002, 1002, 1000, 1026, 1029, 1028, 1013, 1029], At(29));
        Assert.Equal([1002, 1001, 1000, 1027, 1030, 1031, 1014, 1030], At(30));

        // Down 1 leaves lane 31 with its own value, so up 1 hands lane 31 the value lane 30 got (1031).
        Assert.Equal([1002, 1000, 1000, 1028, 1031, 1030, 1015, 1031], At(31));
    }

    [MetalFact]
    public void Exchanges_InAPartialSimdGroup_AreDefinedWhereTheSourceLaneExists()
    {
        // 8 lanes: 0..7.
        int[] input = Enumerable.Range(1000, 8).ToArray();
        int[] actual = RunExchange(input, delta: 3);

        for (int lane = 0; lane < 8; lane++)
        {
            int[] row = actual.AsSpan(lane * SimdExchange.Stride, SimdExchange.Stride).ToArray();

            Assert.Equal(1002, row[0]);
            Assert.Equal(1007 - lane, row[1]);
            Assert.Equal(1000, row[2]);
            Assert.Equal(lane < 3 ? 1000 + lane : 1000 + lane - 3, row[3]);
            Assert.Equal(1000 + (lane ^ 1), row[5]);

            // Shuffling down reads lanes 3..10 of a group that ends at 7, and xor 16 reads lanes 16..23:
            // reading a lane that does not exist is undefined, so only the lanes that have a source count.
            if (lane + 3 < 8)
            {
                Assert.Equal(1000 + lane + 3, row[4]);
            }
        }
    }

    [MetalTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    public void Shuffles_ByAnyDelta_KeepTheLanesWithoutASource(int delta)
    {
        int[] input = Enumerable.Range(1000, 32).ToArray();
        int[] actual = RunExchange(input, delta);

        for (int lane = 0; lane < 32; lane++)
        {
            Assert.Equal(lane < delta ? 1000 + lane : 1000 + lane - delta, actual[(lane * SimdExchange.Stride) + 3]);
            Assert.Equal(lane + delta >= 32 ? 1000 + lane : 1000 + lane + delta, actual[(lane * SimdExchange.Stride) + 4]);
        }
    }

    private static int[] RunExchange(int[] input, int delta)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(input.Length * SimdExchange.Stride);

        device.For(input.Length, new SimdExchange(inputBuffer, output, delta));
        return output.ToArray();
    }

    // ---- quads -----------------------------------------------------------------------------------

    [MetalFact]
    public void Quads_InAThreadgroupTwoWide_Are2x2BlocksOfTheGrid()
    {
        // 2 x 6 threads, value 100 * y + x. Quads: rows 0-1, rows 2-3, rows 4-5.
        const int width = 2, height = 6;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(width * height * SimdQuads2D.Stride);

        device.For(width, height, new SimdQuads2D(output));
        int[] actual = output.ToArray();

        int[] At(int x, int y) => actual.AsSpan(((y * width) + x) * SimdQuads2D.Stride, SimdQuads2D.Stride).ToArray();

        // across x, across y, diagonal, lane 0 of the quad, the opposite lane (3 - own lane)
        Assert.Equal([1, 100, 101, 0, 101], At(0, 0));
        Assert.Equal([0, 101, 100, 0, 100], At(1, 0));
        Assert.Equal([101, 0, 1, 0, 1], At(0, 1));
        Assert.Equal([100, 1, 0, 0, 0], At(1, 1));
        Assert.Equal([201, 300, 301, 200, 301], At(0, 2));
        Assert.Equal([300, 201, 200, 200, 200], At(1, 3));
        Assert.Equal([401, 500, 501, 400, 501], At(0, 4));
        Assert.Equal([500, 401, 400, 400, 400], At(1, 5));
    }

    [MetalFact]
    public void Quads_InA1DThreadgroup_AreFourThreadsInARow()
    {
        float[] input = [10f, 11f, 12f, 13f, 20f, 21f, 22f, 23f];

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<float> output = device.AllocateReadWriteBuffer<float>(input.Length * SimdQuads1D.Stride);

        device.For(input.Length, new SimdQuads1D(inputBuffer, output));

        // across x (lane ^ 1), across y (lane ^ 2), diagonal (lane ^ 3), lane 1 of the quad
        Assert.Equal(
            [
                11f, 12f, 13f, 11f,
                10f, 13f, 12f, 11f,
                13f, 10f, 11f, 11f,
                12f, 11f, 10f, 11f,
                21f, 22f, 23f, 21f,
                20f, 23f, 22f, 21f,
                23f, 20f, 21f, 21f,
                22f, 21f, 20f, 21f,
            ],
            output.ToArray());
    }

    // ---- helpers, pixel shaders, a real reduction -------------------------------------------------

    [MetalFact]
    public void HelperMethods_CanUseSimdGroupOperationsAndBuiltins()
    {
        int[] input = Enumerable.Range(0, 40).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(input.Length * 2);

        device.For(input.Length, new SimdInHelpers(inputBuffer, output));
        int[] actual = output.ToArray();

        Assert.Equal((496, 0), (actual[0], actual[1]));
        Assert.Equal((496, 31), (actual[62], actual[63]));
        Assert.Equal((284, 100), (actual[64], actual[65]));
        Assert.Equal((284, 107), (actual[78], actual[79]));
    }

    [MetalFact]
    public void PixelShaders_CanUseSimdGroupOperations()
    {
        // 16 x 16 threadgroups: 20 x 16 pixels are one full threadgroup and one that is 4 wide.
        const int width = 20, height = 16;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        device.ForEach(texture, default(SimdPixels));
        Rgba32[] pixels = texture.ToArray();

        // red: lane, green: SIMD group, blue: active lanes
        Assert.Equal(new Rgba32(0, 0, 32, 255), pixels[0]);
        Assert.Equal(new Rgba32(15, 0, 32, 255), pixels[15]);
        Assert.Equal(new Rgba32(16, 0, 32, 255), pixels[width]);
        Assert.Equal(new Rgba32(0, 1, 32, 255), pixels[2 * width]);
        Assert.Equal(new Rgba32(31, 7, 32, 255), pixels[(15 * width) + 15]);

        // The 4 x 16 threadgroup on the right: 64 threads, numbered 4 to a row.
        Assert.Equal(new Rgba32(0, 0, 32, 255), pixels[16]);
        Assert.Equal(new Rgba32(3, 0, 32, 255), pixels[19]);
        Assert.Equal(new Rgba32(4, 0, 32, 255), pixels[width + 16]);
        Assert.Equal(new Rgba32(31, 1, 32, 255), pixels[(15 * width) + 19]);
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(65_536)]
    [InlineData(100_003)]
    public void PartialSums_PerSimdGroup_AddUpToTheSumOfTheBuffer(int count)
    {
        Random random = new(count);
        float[] input = Enumerable.Range(0, count).Select(_ => random.NextSingle()).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<float> partials = device.AllocateReadWriteBuffer<float>((count + 31) / 32);

        // The default threadgroup (256 threads) is a multiple of 32, so SIMD group g of the whole
        // dispatch holds elements 32g .. 32g + 31, and the grid may overshoot the data.
        device.For(((count + 255) / 256) * 256, new SimdPartialSums(inputBuffer, partials));
        float[] actual = partials.ToArray();

        for (int group = 0; group < actual.Length; group++)
        {
            double expected = input.Skip(group * 32).Take(32).Sum(static v => (double)v);
            Assert.InRange(actual[group], expected - 1e-4, expected + 1e-4);
        }

        Assert.InRange(actual.Sum(static v => (double)v), input.Sum(static v => (double)v) * (1 - 1e-5), input.Sum(static v => (double)v) * (1 + 1e-5));
    }

    [MetalFact]
    public async System.Threading.Tasks.Task BatchedAndAsyncDispatches_RunThemLikeADirectDispatch()
    {
        const int count = 100;
        int[] input = Enumerable.Range(0, count).Select(static i => (i * 37 % 19) - 9).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> direct = device.AllocateReadWriteBuffer<int>(count * SimdReduceInt.Stride);
        using ReadWriteBuffer<int> batched = device.AllocateReadWriteBuffer<int>(count * SimdReduceInt.Stride);
        using ReadWriteBuffer<int> asynchronous = device.AllocateReadWriteBuffer<int>(count * SimdReduceInt.Stride);

        device.For(count, new SimdReduceInt(inputBuffer, direct, count));

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(count, new SimdReduceInt(inputBuffer, batched, count));
        }

        await device.ForAsync(count, new SimdReduceInt(inputBuffer, asynchronous, count));

        for (int thread = 0; thread < count; thread++)
        {
            AssertReductions(input, direct.ToArray(), SimdReduceInt.Stride, threadgroup: 64, thread);
        }

        Assert.Equal(direct.ToArray(), batched.ToArray());
        Assert.Equal(direct.ToArray(), asynchronous.ToArray());
    }

    // ---- the CPU ---------------------------------------------------------------------------------

    [MetalFact]
    public void OnTheCpu_AShaderThatUsesSimdGroups_IsRefusedBeforeAnyThreadRuns()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer<int>(64);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(64 * SimdReduceInt.Stride);
        using ReadWriteBuffer<int> lanes = device.AllocateReadWriteBuffer<int>(64 * 4);
        using ReadWriteBuffer<float> floats = device.AllocateReadWriteBuffer<float>(64);
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(16, 16);
        output.AsSpan().Fill(-1);

        SimdReduceInt shader = new(input, output, 64);

        Action[] dispatches =
        [
            () => device.ForCpu(64, shader),
            () => device.ForCpu(8, 8, shader),
            () => device.ForCpu(4, 4, 4, shader),
            () => device.ForCpu(0, shader), // also an empty grid: what is accepted does not depend on the size
            () => device.ForSplit(64, shader),
            () => device.ForSplit(64, shader, cpuFraction: 0.5),
            () => device.ForSplit(64, shader, cpuFraction: 0), // also when the CPU would get nothing
            () => device.ForCpu(64, new SimdLanes1D(lanes)), // the built-ins alone
            () => device.ForCpu(64, new ReportedSimdGroups(floats)), // a hand-written descriptor that reports them
            () => device.ForEachCpu(texture, default(SimdPixels)),
            () => device.ForEachSplit(texture, default(SimdPixels)),
            () => device.ForEachSplit(texture, default(SimdPixels), cpuFraction: 1),
        ];

        foreach (Action dispatch in dispatches)
        {
            NotSupportedException exception = Assert.Throws<NotSupportedException>(dispatch);
            Assert.Contains("SIMD-group operations on the CPU arrive with the group executor", exception.Message);
        }

        Assert.Contains(nameof(SimdReduceInt), Assert.Throws<NotSupportedException>(dispatches[0]).Message);
        Assert.All(output.ToArray(), static value => Assert.Equal(-1, value));
    }

    [MetalFact]
    public void OnTheCpu_AnUnreportedSimdGroupOperation_ThrowsFromTheOperation()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(8);

        // The descriptor says nothing, so the dispatch starts; the operation itself then refuses.
        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForCpu(8, new UnreportedSimdGroups(buffer)));
        Assert.Contains("SIMD-group operations on the CPU arrive with the group executor", exception.Message);

        // On the GPU the same shader runs.
        device.For(8, new UnreportedSimdGroups(buffer));
        Assert.Equal([8f, 8f, 8f, 8f, 8f, 8f, 8f, 8f], buffer.ToArray());
    }

    [Fact]
    public void OutsideADispatch_EveryOperationThrowsLikeTheOtherBuiltins()
    {
        Func<object>[] calls =
        [
            () => Gpu.WaveGetLaneIndex(), () => Gpu.WaveGetLaneCount(), () => Gpu.WaveIsFirstLane(),
            () => SimdGroupIds.Index, () => SimdGroupIds.Count,
            () => Gpu.WaveActiveAnyTrue(true), () => Gpu.WaveActiveAllTrue(true), () => Gpu.WaveActiveBallot(true),
            () => Gpu.WaveActiveCountBits(true), () => Gpu.WavePrefixCountBits(true),
            () => Gpu.WaveActiveSum(1f), () => Gpu.WaveActiveProduct(1), () => Gpu.WaveActiveMin(1u), () => Gpu.WaveActiveMax(new Float3(1f, 2f, 3f)),
            () => Gpu.WaveActiveBitAnd(1), () => Gpu.WaveActiveBitOr(new UInt2(1u, 2u)), () => Gpu.WaveActiveBitXor(new Int4(1, 2, 3, 4)),
            () => Gpu.WavePrefixSum(1f), () => Gpu.WavePrefixProduct(1f), () => Gpu.WaveInclusivePrefixSum(1), () => Gpu.WaveInclusivePrefixProduct(1u),
            () => Gpu.WaveReadLaneAt(1f, 0), () => Gpu.WaveReadLaneFirst(new Float4(1f, 2f, 3f, 4f)),
            () => Gpu.WaveShuffleUp(1, 1), () => Gpu.WaveShuffleDown(1u, 1), () => Gpu.WaveShuffleXor(new Float2(1f, 2f), 1),
            () => Gpu.QuadReadAcrossX(1f), () => Gpu.QuadReadAcrossY(1), () => Gpu.QuadReadAcrossDiagonal(1u), () => Gpu.QuadReadLaneAt(new Int3(1, 2, 3), 0),
        ];

        foreach (Func<object> call in calls)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(call);
            Assert.Contains("Execute", exception.Message);
        }
    }

    // ---- the model -------------------------------------------------------------------------------

    /// <summary>
    /// The SIMD groups of a 1D dispatch of <paramref name="threads"/> threads in threadgroups of
    /// <paramref name="threadgroup"/>: each is up to 32 consecutive threads of one threadgroup.
    /// </summary>
    private static IEnumerable<int[]> Lanes(int threads, int threadgroup)
    {
        for (int start = 0; start < threads; start += threadgroup)
        {
            int end = Math.Min(threads, start + threadgroup);

            for (int first = start; first < end; first += Width)
            {
                yield return Enumerable.Range(first, Math.Min(Width, end - first)).ToArray();
            }
        }
    }

    /// <summary>Checks the 11 results <see cref="SimdReduceInt"/> wrote for <paramref name="thread"/> against the model.</summary>
    private static void AssertReductions(int[] input, int[] actual, int stride, int threadgroup, int thread)
    {
        int[] group = Lanes(input.Length, threadgroup).Single(g => g.Contains(thread));
        int[] values = group.Select(t => input[t]).ToArray();
        int[] below = group.Where(t => t < thread).Select(t => input[t]).ToArray();

        int[] expected =
        [
            values.Aggregate(0, static (a, b) => unchecked(a + b)),
            values.Aggregate(1, static (a, b) => unchecked(a * b)),
            values.Min(),
            values.Max(),
            values.Aggregate(-1, static (a, b) => a & b),
            values.Aggregate(0, static (a, b) => a | b),
            values.Aggregate(0, static (a, b) => a ^ b),
            below.Aggregate(0, static (a, b) => unchecked(a + b)),
            below.Aggregate(1, static (a, b) => unchecked(a * b)),
            unchecked(below.Aggregate(0, static (a, b) => unchecked(a + b)) + input[thread]),
            unchecked(below.Aggregate(1, static (a, b) => unchecked(a * b)) * input[thread]),
        ];

        Assert.True(
            actual.AsSpan(thread * stride, stride).SequenceEqual(expected),
            $"thread {thread} (lane {Array.IndexOf(group, thread)} of {group.Length}): expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual.AsSpan(thread * stride, stride).ToArray())}]");
    }
}
