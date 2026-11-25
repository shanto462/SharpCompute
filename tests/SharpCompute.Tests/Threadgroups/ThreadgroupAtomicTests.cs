using System;
using System.Linq;
using static SharpCompute.Tests.Threadgroups.CpuGroupExecutionTests;

namespace SharpCompute.Tests.Threadgroups;

/// <summary>
/// <c>Gpu.Atomic*</c> on <c>[GroupShared]</c> elements: every thread of a threadgroup on the same
/// element, on the GPU, on the CPU's group executor (which runs a group's threads concurrently) and
/// split between the two. Everything that does not depend on the order the threads ran in must agree
/// with a single-threaded reference.
/// </summary>
public class ThreadgroupAtomicTests
{
    public enum Target
    {
        Gpu,
        Cpu,
    }

    private static void Run<T>(GraphicsDevice device, Target target, int threads, T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        if (target == Target.Gpu)
        {
            device.For(threads, in shader);
        }
        else
        {
            WithinTimeout(() => device.ForCpu(threads, in shader));
        }
    }

    private static (bool Atomics, bool DeviceAtomics) Atomics<T>()
        where T : struct, IComputeShaderDescriptor<T> => (T.UsesAtomics, T.UsesDeviceAtomics);

    // ---- every operation, all threads of a group on one element ---------------------------------------

    private static void AssertEveryOperation(int threads, int[] gotInts, uint[] gotUInts, int[] addOrder, uint[] uintAddOrder, string what)
    {
        const int size = GroupAtomicEveryOperation.GroupThreads, slots = GroupAtomicEveryOperation.Slots;
        int groups = (threads + size - 1) / size;

        for (int group = 0; group < groups; group++)
        {
            int first = group * size;
            int n = Math.Min(size, threads - first);

            // The single-threaded reference for this group's threads.
            int min = int.MaxValue, max = int.MinValue, and = -1, or = 0, xor = 0;
            uint umin = uint.MaxValue, umax = 0, uand = uint.MaxValue, uor = 0, uxor = 0, exchangedSum = 0;

            for (int i = first; i < first + n; i++)
            {
                int mixed = (int)GroupAtomicEveryOperation.Mix((uint)i);
                min = Math.Min(min, mixed);
                max = Math.Max(max, mixed);
                umin = Math.Min(umin, (uint)mixed);
                umax = Math.Max(umax, (uint)mixed);
                and &= ~(1 << (i % 12));
                uand &= ~(1u << (i % 12));
                or |= 1 << (i % 20);
                uor |= 1u << (i % 20);
                xor ^= mixed;
                uxor ^= (uint)mixed;
                exchangedSum += (uint)i + 1u;
            }

            int[] ints = gotInts.AsSpan(group * slots, slots).ToArray();
            uint[] uints = gotUInts.AsSpan(group * slots, slots).ToArray();
            string where = $"{what}, group {group} ({n} threads)";

            Assert.True(n == ints[0] && (uint)n == uints[0], $"add: {ints[0]}, {uints[0]}; {where}");
            Assert.True(-3 * n == ints[1] && unchecked(0u - (3u * (uint)n)) == uints[1], $"sub: {ints[1]}, {uints[1]}; {where}");
            Assert.True(min == ints[2] && umin == uints[2], $"min: {ints[2]}, {uints[2]}; {where}");
            Assert.True(max == ints[3] && umax == uints[3], $"max: {ints[3]}, {uints[3]}; {where}");
            Assert.True(and == ints[4] && uand == uints[4], $"and: {ints[4]:X8}, {uints[4]:X8}; {where}");
            Assert.True(or == ints[5] && uor == uints[5], $"or: {ints[5]:X8}, {uints[5]:X8}; {where}");
            Assert.True(xor == ints[6] && uxor == uints[6], $"xor: {ints[6]:X8}, {uints[6]:X8}; {where}");

            // Exchange: every value stored (i + 1 for each thread, plus the initial 0) is either returned
            // to exactly one thread or still there at the end. Which one is last depends on the schedule.
            Assert.InRange(ints[7], first + 1, first + n);
            Assert.InRange(uints[7], (uint)first + 1u, (uint)(first + n));
            Assert.True(exchangedSum == unchecked((uint)ints[8] + (uint)ints[7]), $"exchange: {ints[7]}, {ints[8]}; {where}");
            Assert.True(exchangedSum == unchecked(uints[8] + uints[7]), $"exchange: {uints[7]}, {uints[8]}; {where}");
        }

        // Add returns the original value: with n adds of 1 in a group those are 0..n-1, each exactly once.
        Assert.All(addOrder, static count => Assert.Equal(1, count));
        Assert.All(uintAddOrder, static count => Assert.Equal(1u, count));
    }

    [MetalTheory]
    [InlineData(Target.Gpu, 256 * 512)]
    [InlineData(Target.Gpu, (256 * 3) + 100)] // a partial last group
    [InlineData(Target.Gpu, 256)]
    [InlineData(Target.Gpu, 1)]
    [InlineData(Target.Cpu, 256 * 6)]
    [InlineData(Target.Cpu, (256 * 2) + 37)]
    [InlineData(Target.Cpu, 1)]
    public void EveryOperation_AllThreadsOfAGroupOnOneElement(Target target, int threads)
    {
        const int size = GroupAtomicEveryOperation.GroupThreads, slots = GroupAtomicEveryOperation.Slots;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int groups = (threads + size - 1) / size;
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(groups * slots);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>(groups * slots);
        using ReadWriteBuffer<int> addOrder = device.AllocateReadWriteBuffer<int>(threads);
        using ReadWriteBuffer<uint> uintAddOrder = device.AllocateReadWriteBuffer<uint>(threads);

        Run(device, target, threads, new GroupAtomicEveryOperation(ints, uints, addOrder, uintAddOrder));

        AssertEveryOperation(threads, ints.ToArray(), uints.ToArray(), addOrder.ToArray(), uintAddOrder.ToArray(), target.ToString());
    }

    [MetalFact]
    public void EveryOperation_RepeatedOnTheCpu_NeverLosesAnUpdate()
    {
        // The group executor starts a group's threads together; repeat so that a non-atomic update
        // (a lost add, a torn min) would show in at least one of a few hundred contended groups.
        const int size = GroupAtomicEveryOperation.GroupThreads, slots = GroupAtomicEveryOperation.Slots, threads = size * 8;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(8 * slots);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>(8 * slots);
        using ReadWriteBuffer<int> addOrder = device.AllocateReadWriteBuffer<int>(threads);
        using ReadWriteBuffer<uint> uintAddOrder = device.AllocateReadWriteBuffer<uint>(threads);

        for (int run = 0; run < 25; run++)
        {
            Run(device, Target.Cpu, threads, new GroupAtomicEveryOperation(ints, uints, addOrder, uintAddOrder));
            AssertEveryOperation(threads, ints.ToArray(), uints.ToArray(), addOrder.ToArray(), uintAddOrder.ToArray(), $"run {run}");
        }
    }

    // ---- returned values, one thread ---------------------------------------------------------------

    [MetalTheory]
    [InlineData(Target.Gpu)]
    [InlineData(Target.Cpu)]
    public void EveryOperation_ReturnsTheOriginalValue_AndWrapsAtTheLimits_ExactlyAsOnABuffer(Target target)
    {
        // One thread per group, so nothing depends on scheduling: every returned value and every final
        // element is a literal, the same for the GPU and the CPU, and the same as for a buffer element
        // (AtomicTests.EveryOperation_ReturnsTheOriginalValue_AndWrapsAtTheLimits). Three groups: each
        // has its own memory and gets the same answers.
        const int groups = 3;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(groups * GroupAtomicSequence.Ints);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>(groups * GroupAtomicSequence.UInts);
        using ReadWriteBuffer<float> floats = device.AllocateReadWriteBuffer<float>(groups * GroupAtomicSequence.Floats);
        using ReadWriteBuffer<int> returned = device.AllocateReadWriteBuffer<int>(groups * GroupAtomicSequence.Returned);
        using ReadWriteBuffer<uint> returnedUInts = device.AllocateReadWriteBuffer<uint>(groups * GroupAtomicSequence.ReturnedUInts);
        using ReadWriteBuffer<float> returnedFloats = device.AllocateReadWriteBuffer<float>(groups * GroupAtomicSequence.ReturnedFloats);

        Run(device, target, groups * 4, new GroupAtomicSequence(ints, uints, floats, returned, returnedUInts, returnedFloats, int.MaxValue, 0xC0000000u));

        int[] expectedReturned =
        [
            5,              // add int.MaxValue to 5: wraps to -2147483644
            -2147483644,    // add 1
            7,              // subtract int.MinValue from 7: wraps to -2147483641
            int.MinValue,   // subtract 1 from int.MinValue: wraps to int.MaxValue
            int.MaxValue,   // subtract -1: wraps back to int.MinValue
            0,              // min(0, -1) stores -1
            -1,             // min(-1, 5) stores nothing new
            -5,             // max(-5, int.MinValue): signed, so -5 stays
            -5,             // max(-5, 9) stores 9
            0x0F0F0F0F,     // and 0x00FF00FF: 0x000F000F
            0x000F000F,     // or the sign bit: 0x800F000F
            -2146500593,    // (0x800F000F) xor -1: 0x7FF0FFF0
            42,             // exchange with int.MinValue
            1,              // compare-exchange expecting 3: fails, 1 stays
            1,              // compare-exchange expecting 1: succeeds, stores 11
            1,              // add big (int.MaxValue) to 1: wraps to int.MinValue
            int.MinValue,   // add big again: -1
            -2,             // subtract big from -2: wraps to int.MaxValue
            int.MaxValue,   // subtract int.MinValue (-big - 1): wraps to -1
        ];
        uint[] expectedReturnedUInts =
        [
            0u,             // add 0xFFFFFFFF to 0
            0xFFFFFFFFu,    // add 2: through zero to 1
            0u,             // subtract 1 from 0: through zero to 0xFFFFFFFF
            0xFFFFFFFFu,    // subtract ubig (0xC0000000): 0x3FFFFFFF
            0x3FFFFFFFu,    // subtract ubig again: through zero to 0x7FFFFFFF
            5u,             // min(5, 0xFFFFFFFF): unsigned, so 5 stays
            5u,             // min(5, 3) stores 3
            5u,             // max(5, 0x80000000): unsigned, so it is stored
            0x80000000u,    // max(0x80000000, 7) stores nothing new
            0xF0F0F0F0u,    // and 0xFF00FF00: 0xF000F000
            0xF000F000u,    // or 0xF: 0xF000F00F
            0xF000F00Fu,    // xor 0xFFFFFFFF: 0x0FFF0FF0
            9u,             // exchange with 0xDEADBEEF
            0xC0000000u,    // compare-exchange expecting 1: fails
            0xC0000000u,    // compare-exchange expecting ubig: succeeds, stores 1
            1u,             // max(1, ubig) stores ubig
        ];
        int[] expectedInts = [-2147483643, -2147483641, int.MinValue, -1, 9, 0x7FF0FFF0, int.MinValue, 11, -1, -1];
        uint[] expectedUInts = [1u, 0x7FFFFFFFu, 3u, 0x80000000u, 0x0FFF0FF0u, 0xDEADBEEFu, 0xC0000000u];

        Assert.Equal(Enumerable.Repeat(expectedReturned, groups).SelectMany(static g => g), returned.ToArray());
        Assert.Equal(Enumerable.Repeat(expectedReturnedUInts, groups).SelectMany(static g => g), returnedUInts.ToArray());
        Assert.Equal(Enumerable.Repeat(expectedInts, groups).SelectMany(static g => g), ints.ToArray());
        Assert.Equal(Enumerable.Repeat(expectedUInts, groups).SelectMany(static g => g), uints.ToArray());

        // The float add, as on a buffer: 1.5 + 0.25, then - 2. Then -0 + -0, which is -0 (its sign
        // bit shows in the bits, not in the value), and + 3. Compared as bits.
        uint[] expectedReturnedFloats = Bits([1.5f, 1.75f, -0f, -0f]);
        uint[] expectedFloats = Bits([-0.25f, 3f]);
        Assert.Equal(Enumerable.Repeat(expectedReturnedFloats, groups).SelectMany(static g => g), Bits(returnedFloats.ToArray()));
        Assert.Equal(Enumerable.Repeat(expectedFloats, groups).SelectMany(static g => g), Bits(floats.ToArray()));
    }

    private static uint[] Bits(float[] values) => values.Select(BitConverter.SingleToUInt32Bits).ToArray();

    // ---- the float add --------------------------------------------------------------------------------
    // MSL has no atomic float in threadgroup memory: the add is a compare-and-swap loop over the bits.

    private static void AssertFloatSums(int threads, float[] sums, int[] addOrder, string what)
    {
        const int size = GroupAtomicFloatSum.GroupThreads, slots = GroupAtomicFloatSum.Slots;
        int groups = (threads + size - 1) / size;

        for (int group = 0; group < groups; group++)
        {
            int first = group * size;
            int n = Math.Min(size, threads - first);
            float quarters = 0f;
            double inexact = 0;

            for (int i = first; i < first + n; i++)
            {
                quarters += (i & 3) * 0.25f;
                inexact += GroupAtomicFloatSum.Inexact(i);
            }

            string where = $"{what}, group {group} ({n} threads)";

            // Ones and quarters: every partial sum is exact in a float, so no add may be lost to a
            // race, and the order the threads ran in cannot show.
            Assert.True(n == sums[group * slots], $"ones: {sums[group * slots]}; {where}");
            Assert.True(quarters == sums[(group * slots) + 1], $"quarters: {sums[(group * slots) + 1]}, expected {quarters}; {where}");

            // Tenths: each add rounds, so the sum depends on the order, within the rounding of n adds
            // (half an ULP of the running sum each). A lost add would be off by at least 0.1.
            double tolerance = n * 0.5 * ((double)MathF.BitIncrement((float)inexact) - (float)inexact);
            Assert.True(tolerance < 0.01, $"tolerance {tolerance}");
            Assert.True(Math.Abs(inexact - sums[(group * slots) + 2]) <= tolerance, $"tenths: {sums[(group * slots) + 2]}, expected {inexact} within {tolerance}; {where}");
        }

        // The add returns the original value: with n adds of 1 in a group, the integers 0..n-1, each once.
        Assert.All(addOrder, static count => Assert.Equal(1, count));
    }

    [MetalTheory]
    [InlineData(Target.Gpu, 256 * 512)]
    [InlineData(Target.Gpu, (256 * 3) + 100)] // a partial last group
    [InlineData(Target.Gpu, 256)]
    [InlineData(Target.Gpu, 1)]
    [InlineData(Target.Cpu, 256 * 6)]
    [InlineData(Target.Cpu, (256 * 2) + 37)]
    [InlineData(Target.Cpu, 1)]
    public void FloatAdd_AllThreadsOfAGroupOnOneElement_AppliesEveryAdd_AndReturnsEachPartialSumOnce(Target target, int threads)
    {
        const int size = GroupAtomicFloatSum.GroupThreads;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int groups = (threads + size - 1) / size;
        using ReadWriteBuffer<float> sums = device.AllocateReadWriteBuffer<float>(groups * GroupAtomicFloatSum.Slots);
        using ReadWriteBuffer<int> addOrder = device.AllocateReadWriteBuffer<int>(threads);

        Run(device, target, threads, new GroupAtomicFloatSum(sums, addOrder));

        AssertFloatSums(threads, sums.ToArray(), addOrder.ToArray(), target.ToString());
    }

    [MetalFact]
    public void FloatAdd_TheGpuAndTheCpuAgree_ExactlyWhereTheSumsAreExact_AndWithinRoundingWhereNot()
    {
        const int size = GroupAtomicFloatSum.GroupThreads, slots = GroupAtomicFloatSum.Slots, groups = 8, threads = size * groups;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(groups * slots);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(groups * slots);
        using ReadWriteBuffer<int> gpuOrder = device.AllocateReadWriteBuffer<int>(threads);
        using ReadWriteBuffer<int> cpuOrder = device.AllocateReadWriteBuffer<int>(threads);

        for (int run = 0; run < 10; run++)
        {
            Run(device, Target.Gpu, threads, new GroupAtomicFloatSum(gpu, gpuOrder));
            Run(device, Target.Cpu, threads, new GroupAtomicFloatSum(cpu, cpuOrder));

            float[] onGpu = gpu.ToArray(), onCpu = cpu.ToArray();
            AssertFloatSums(threads, onGpu, gpuOrder.ToArray(), $"GPU, run {run}");
            AssertFloatSums(threads, onCpu, cpuOrder.ToArray(), $"CPU, run {run}");

            for (int group = 0; group < groups; group++)
            {
                Assert.Equal(onCpu[group * slots], onGpu[group * slots]);
                Assert.Equal(onCpu[(group * slots) + 1], onGpu[(group * slots) + 1]);
                Assert.Equal(onCpu[(group * slots) + 2], onGpu[(group * slots) + 2], 2e-3f); // each is within 1e-3 of the true sum
            }
        }
    }

    // ---- compare-and-swap ---------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(Target.Gpu, 128 * 256)]
    [InlineData(Target.Gpu, 128 + 50)]
    [InlineData(Target.Cpu, 128 * 8)]
    [InlineData(Target.Cpu, 3)]
    public void CompareExchange_IsStrong_OneWinnerAndAnExactCounterPerGroup(Target target, int threads)
    {
        const int size = GroupAtomicCompareExchangeLoop.GroupThreads, slots = GroupAtomicCompareExchangeLoop.Slots;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int groups = (threads + size - 1) / size;
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(groups * slots);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>(groups * slots);

        Run(device, target, threads, new GroupAtomicCompareExchangeLoop(ints, uints));

        int[] gotInts = ints.ToArray();
        uint[] gotUInts = uints.ToArray();

        for (int group = 0; group < groups; group++)
        {
            int first = group * size;
            int n = Math.Min(size, threads - first);

            // Exactly one thread of the group saw the comparand and stored its id; a weak compare-and-swap
            // that fails spuriously could leave the claim to a later thread, but never to two or to none.
            Assert.InRange(gotInts[group * slots], first + 1, first + n);
            Assert.Equal(1, gotInts[(group * slots) + 1]);
            Assert.InRange(gotUInts[group * slots], (uint)first + 1u, (uint)(first + n));
            Assert.Equal(1u, gotUInts[(group * slots) + 1]);

            // The retry loop only counts every thread if a failed swap really returns the current value.
            Assert.Equal(n, gotInts[(group * slots) + 2]);
            Assert.Equal((uint)n, gotUInts[(group * slots) + 2]);
        }
    }

    // ---- the privatised histogram ---------------------------------------------------------------------

    private static uint[] Values(int count, int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => (uint)random.Next()).ToArray();
    }

    private static int[] Histogram(ReadOnlySpan<uint> values, int bins)
    {
        int[] expected = new int[bins];

        foreach (uint value in values)
        {
            expected[value % (uint)bins]++;
        }

        return expected;
    }

    [MetalTheory]
    [InlineData(Target.Gpu, 1 << 20, 256)]
    [InlineData(Target.Gpu, 1 << 20, 4)]
    [InlineData(Target.Gpu, 1 << 20, 1)]
    [InlineData(Target.Gpu, 100_003, 256)] // a last group of 163 threads shares out 256 bins
    [InlineData(Target.Gpu, 100_003, 4)]
    [InlineData(Target.Gpu, 5, 256)]
    [InlineData(Target.Cpu, 256 * 12, 256)]
    [InlineData(Target.Cpu, 256 * 12, 4)]
    [InlineData(Target.Cpu, (256 * 3) + 77, 256)]
    [InlineData(Target.Cpu, 5, 7)]
    public void PrivatisedHistogram_EqualsAPlainCount_WhateverTheContention(Target target, int count, int bins)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] values = Values(count, seed: count + bins);
        using ReadOnlyBuffer<uint> valueBuffer = device.AllocateReadOnlyBuffer<uint>(values);
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(new int[bins]);

        Run(device, target, count, new PrivatisedHistogram(valueBuffer, counts, bins));

        Assert.Equal(Histogram(values, bins), counts.ToArray());
    }

    [MetalFact]
    public void PrivatisedHistogram_AccumulatesAcrossDispatches_LikeTheGlobalOne()
    {
        // The merge is an atomic add into the buffer, so a second dispatch adds to the first, and the
        // result equals what the global-atomics histogram counts for the same values.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] values = Values(1 << 16, seed: 5);
        using ReadOnlyBuffer<uint> valueBuffer = device.AllocateReadOnlyBuffer<uint>(values);
        using ReadWriteBuffer<int> privatised = device.AllocateReadWriteBuffer<int>(new int[16]);
        using ReadWriteBuffer<int> global = device.AllocateReadWriteBuffer<int>(new int[16]);

        device.For(values.Length, new PrivatisedHistogram(valueBuffer, privatised, 16));
        device.For(values.Length, new PrivatisedHistogram(valueBuffer, privatised, 16));
        device.For(values.Length, new Intrinsics.AtomicHistogram(valueBuffer, global, 16));

        Assert.Equal(global.ToArray().Select(static n => n * 2), privatised.ToArray());
    }

    // ---- CPU+GPU splits ------------------------------------------------------------------------------
    // Atomics on threadgroup memory stay within one threadgroup, and a threadgroup runs entirely on one
    // device: only atomics on buffer elements keep a shader from being divided.

    [Fact]
    public void GeneratedDescriptors_TellAtomicsOnBuffersFromAtomicsOnThreadgroupMemory()
    {
        // (uses atomics, uses atomics on buffer elements)
        Assert.Equal((true, false), Atomics<GroupAtomicEveryOperation>());
        Assert.Equal((true, false), Atomics<GroupAtomicCompareExchangeLoop>()); // also the prelude's, and in a helper
        Assert.Equal((true, false), Atomics<GroupAtomicSequence>());
        Assert.Equal((true, false), Atomics<GroupHistogram>());
        Assert.Equal((true, false), Atomics<GroupAtomicFloatSum>()); // the float add's loop over the bits

        Assert.Equal((true, true), Atomics<PrivatisedHistogram>()); // counts in the group, merges into a buffer
        Assert.Equal((true, true), Atomics<CountOddPerGroup>());    // shared memory, but its only atomic is on a buffer
        Assert.Equal((true, true), Atomics<Intrinsics.AtomicHistogram>());

        Assert.Equal((false, false), Atomics<BlockSum>());
        Assert.Equal((false, false), Atomics<Shaders.MultiplyByTwo>()); // hand-written: the interface's defaults
    }

    [Fact]
    public void AHandWrittenDescriptor_ThatOnlySaysItUsesAtomics_IsTakenToUseThemOnBuffers()
    {
        // UsesDeviceAtomics defaults to UsesAtomics: the conservative reading, which is never split.
        Assert.Equal((true, true), Atomics<HandWrittenAtomics>());
        Assert.Equal((true, false), Atomics<HandWrittenThreadgroupAtomics>());
    }

    [MetalTheory]
    [InlineData(0.5, 64 * 32, 64)]
    [InlineData(0.5, 64 * 32, 4)]
    [InlineData(0.25, (64 * 20) + 9, 64)] // the partial group goes to the GPU
    [InlineData(0.0, 64 * 8, 16)]
    [InlineData(1.0, 64 * 8, 16)]
    public void ForSplit_OfAShaderWhoseOnlyAtomicsAreOnThreadgroupMemory_DividesIt_AndMatches(double cpuFraction, int count, int bins)
    {
        const int size = GroupHistogram.GroupThreads;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] values = Values(count, seed: count ^ bins);
        int groups = (count + size - 1) / size;
        using ReadOnlyBuffer<uint> valueBuffer = device.AllocateReadOnlyBuffer<uint>(values);
        using ReadWriteBuffer<int> split = device.AllocateReadWriteBuffer<int>(groups * bins);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(groups * bins);

        device.For(count, new GroupHistogram(valueBuffer, gpu, bins));
        SplitDispatchInfo info = default;
        WithinTimeout(() => info = device.ForSplit(count, new GroupHistogram(valueBuffer, split, bins), cpuFraction));

        // Each group's row is the plain count of its own values, whichever device ran the group.
        int[] expected = values.Chunk(size).SelectMany(group => Histogram(group, bins)).ToArray();
        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, split.ToArray());

        Assert.False(info.SingleDeviceOnly);
        Assert.Equal(count, info.CpuExtent + info.GpuExtent);
        Assert.Equal(cpuFraction > 0, info.CpuExtent > 0);
        Assert.Equal(cpuFraction < 1, info.GpuExtent > 0);
        Assert.True(info.CpuExtent % size == 0 || info.CpuExtent == count, $"CPU extent {info.CpuExtent}: a threadgroup was divided");
    }

    [MetalFact]
    public void ForSplit_EveryOperationUnderContention_HalfTheGroupsOnEachDevice()
    {
        const int size = GroupAtomicEveryOperation.GroupThreads, slots = GroupAtomicEveryOperation.Slots, groups = 8, threads = size * groups;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(groups * slots);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>(groups * slots);
        using ReadWriteBuffer<int> addOrder = device.AllocateReadWriteBuffer<int>(threads);
        using ReadWriteBuffer<uint> uintAddOrder = device.AllocateReadWriteBuffer<uint>(threads);

        SplitDispatchInfo info = default;
        WithinTimeout(() => info = device.ForSplit(threads, new GroupAtomicEveryOperation(ints, uints, addOrder, uintAddOrder), 0.5));

        Assert.False(info.SingleDeviceOnly);
        Assert.Equal((threads / 2, threads / 2), (info.CpuExtent, info.GpuExtent));
        AssertEveryOperation(threads, ints.ToArray(), uints.ToArray(), addOrder.ToArray(), uintAddOrder.ToArray(), "split");
    }

    [MetalFact]
    public void ForSplit_FloatAdd_HalfTheGroupsOnEachDevice()
    {
        const int size = GroupAtomicFloatSum.GroupThreads, groups = 8, threads = size * groups;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> sums = device.AllocateReadWriteBuffer<float>(groups * GroupAtomicFloatSum.Slots);
        using ReadWriteBuffer<int> addOrder = device.AllocateReadWriteBuffer<int>(threads);

        SplitDispatchInfo info = default;
        WithinTimeout(() => info = device.ForSplit(threads, new GroupAtomicFloatSum(sums, addOrder), 0.5));

        Assert.False(info.SingleDeviceOnly);
        Assert.Equal((threads / 2, threads / 2), (info.CpuExtent, info.GpuExtent));
        AssertFloatSums(threads, sums.ToArray(), addOrder.ToArray(), "split");
    }

    // ---- together with SIMD-group operations ----------------------------------------------------------

    [MetalTheory]
    [InlineData(160 * 64)]
    [InlineData(160 * 6)]
    [InlineData(160)]
    public void AThreadgroupAtomic_AndASimdGroupReduction_WorkTogether_OnTheGpu_TheCpu_AndSplit(int threads)
    {
        // Each SIMD group reduces its 32 lanes, then makes one atomic add to its threadgroup's total.
        // On the CPU such a shader needs the whole threadgroup on threads of its own (it has
        // [GroupShared] memory and barriers), not just the lanes of one SIMD group at a time: the
        // totals below are per threadgroup, so running SIMD groups on their own would not produce them.
        const int size = SimdGroupAtomicTotals.GroupThreads, stride = SimdGroupAtomicTotals.Stride, lanes = 32;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] input = GroupSharedTests.Ints(threads, seed: threads, max: 1000);
        int groups = threads / size;
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(groups * stride);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(groups * stride);
        using ReadWriteBuffer<int> split = device.AllocateReadWriteBuffer<int>(groups * stride);

        static (bool Atomics, bool DeviceAtomics, bool Threadgroup, bool Simd) Read<T>()
            where T : struct, IComputeShaderDescriptor<T> => (T.UsesAtomics, T.UsesDeviceAtomics, T.UsesThreadgroupMemory, T.UsesSimdGroups);

        // Atomics, but none on a buffer; threadgroup memory; SIMD-group operations.
        Assert.Equal((true, false, true, true), Read<SimdGroupAtomicTotals>());

        int[] expected = input.Chunk(size).SelectMany(group =>
        {
            int[] sums = group.Chunk(lanes).Select(static simdGroup => simdGroup.Sum()).ToArray();
            return new[] { sums.Sum(), sums.Length, sums.Max(), size / lanes };
        }).ToArray();

        device.For(threads, new SimdGroupAtomicTotals(source, gpu));
        Assert.Equal(expected, gpu.ToArray());

        WithinTimeout(() => device.ForCpu(threads, new SimdGroupAtomicTotals(source, cpu)));
        Assert.Equal(expected, cpu.ToArray());

        // Its atomics stay within a threadgroup, so it is divided like any other shader: whole
        // threadgroups, and with them whole SIMD groups, on each device.
        SplitDispatchInfo info = default;
        WithinTimeout(() => info = device.ForSplit(threads, new SimdGroupAtomicTotals(source, split), 0.5));

        Assert.Equal(expected, split.ToArray());
        Assert.False(info.SingleDeviceOnly);
        Assert.Equal(threads, info.CpuExtent + info.GpuExtent);
        Assert.Equal(0, info.CpuExtent % size);
        Assert.Equal(groups > 1, info.CpuExtent > 0 && info.GpuExtent > 0);
    }

    // ---- indirect dispatch -----------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(256 * 24, 256)]
    [InlineData((256 * 24) - 100, 4)] // rounded up to 24 threadgroups by the GPU
    [InlineData(1, 16)]
    [InlineData(0, 16)]
    public void ForIndirect_OfAShaderWithThreadgroupAtomics_RunsWholeGroups_OnTheGpuAndTheCpu(int count, int bins)
    {
        // An indirect dispatch runs whole threadgroups, whose number the GPU works out from a count
        // in a buffer. The privatised histogram counts every thread of those groups, so the values
        // are padded to whole groups and the expected histogram is of the padded part.
        const int size = PrivatisedHistogram.GroupThreads, capacity = size * 24;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] values = Values(capacity, seed: count + bins);
        int dispatched = (count + size - 1) / size * size;
        int[] expected = Histogram(values.AsSpan(0, dispatched), bins);
        using ReadOnlyBuffer<uint> valueBuffer = device.AllocateReadOnlyBuffer<uint>(values);
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([count]);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(1);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(new int[bins]);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(new int[bins]);
        using ReadWriteBuffer<int> rows = device.AllocateReadWriteBuffer<int>(capacity / GroupHistogram.GroupThreads * GroupHistogram.MaxBins);

        using (ComputeContext context = device.CreateComputeContext())
        {
            // The count becomes threadgroup counts on the GPU and is consumed in the same execution.
            context.WriteDispatchArguments<PrivatisedHistogram>(counter, arguments);
            context.ForIndirect(arguments, new PrivatisedHistogram(valueBuffer, gpu, bins));
        }

        Assert.Equal(DispatchArguments.FromCount(count, size), arguments[0]);
        Assert.Equal(expected, gpu.ToArray());

        // The same grid on the CPU's group executor, and once more on the GPU on its own.
        WithinTimeout(() => device.ForIndirectCpu(arguments, new PrivatisedHistogram(valueBuffer, cpu, bins)));
        Assert.Equal(expected, cpu.ToArray());

        device.ForIndirect(arguments, new PrivatisedHistogram(valueBuffer, gpu, bins));
        Assert.Equal(expected.Select(static n => n * 2), gpu.ToArray());

        // A shader whose only atomics are on threadgroup memory: one row per group that ran, the rest untouched.
        using ReadWriteBuffer<DispatchArguments> smallGroups = device.AllocateReadWriteBuffer<DispatchArguments>(
            [DispatchArguments.FromCount(dispatched, GroupHistogram.GroupThreads)]);
        int rowBins = Math.Min(bins, GroupHistogram.MaxBins);
        rows.AsSpan().Fill(-1);

        device.ForIndirect(smallGroups, new GroupHistogram(valueBuffer, rows, rowBins));

        int[] expectedRows = values.AsSpan(0, dispatched).ToArray().Chunk(GroupHistogram.GroupThreads).SelectMany(group => Histogram(group, rowBins)).ToArray();
        Assert.Equal(expectedRows, rows.ToArray()[..expectedRows.Length]);
        Assert.All(rows.ToArray()[expectedRows.Length..], static value => Assert.Equal(-1, value));
    }

    [MetalFact]
    public void ForSplit_Adaptive_TreatsAThreadgroupAtomicsShaderLikeAnyOther()
    {
        // Not pinned to the GPU: the adaptive mode divides its first dispatches, as for a shader without atomics.
        const int size = GroupHistogram.GroupThreads, count = size * 64, bins = 8;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] values = Values(count, seed: 3);
        int[] expected = values.Chunk(size).SelectMany(group => Histogram(group, bins)).ToArray();
        using ReadOnlyBuffer<uint> valueBuffer = device.AllocateReadOnlyBuffer<uint>(values);
        bool divided = false;

        for (int run = 0; run < 4; run++)
        {
            using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(count / size * bins);
            SplitDispatchInfo info = default;
            WithinTimeout(() => info = device.ForSplit(count, new GroupHistogram(valueBuffer, counts, bins)));

            Assert.Equal(expected, counts.ToArray());
            Assert.False(info.SingleDeviceOnly, $"run {run}");
            divided |= info.CpuExtent > 0 && info.GpuExtent > 0;
        }

        Assert.True(divided, "no adaptive dispatch was divided");
    }

    [MetalTheory]
    [InlineData(0.5)]
    [InlineData(0.1)]
    [InlineData(0.999999)]
    public void ForSplit_OfAShaderThatAlsoHasAnAtomicOnABuffer_IsStillRefused_BeforeAnythingRuns(double cpuFraction)
    {
        const int count = 256 * 16;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<uint> values = device.AllocateReadOnlyBuffer<uint>(Values(count, seed: 9));
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(new int[4]);
        PrivatisedHistogram shader = new(values, counts, 4);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForSplit(count, shader, cpuFraction));

        Assert.Contains("PrivatisedHistogram", exception.Message);
        Assert.Contains("uses atomic operations (Gpu.Atomic*) on buffer elements", exception.Message);
        Assert.Contains("cannot be split between the CPU and the GPU", exception.Message);
        Assert.Equal([0, 0, 0, 0], counts.ToArray());
    }

    [MetalFact]
    public void ForSplit_OfAShaderThatAlsoHasAnAtomicOnABuffer_RunsOnOneDevice_AndIsExact()
    {
        const int count = 256 * 16, bins = 4;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] data = Values(count, seed: 9);
        int[] expected = Histogram(data, bins);
        using ReadOnlyBuffer<uint> values = device.AllocateReadOnlyBuffer<uint>(data);

        foreach (double? fraction in new double?[] { null, 0.0, 1.0 })
        {
            using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(new int[bins]);
            PrivatisedHistogram shader = new(values, counts, bins);
            SplitDispatchInfo info = default;

            WithinTimeout(() => info = fraction is { } f ? device.ForSplit(count, shader, f) : device.ForSplit(count, shader));

            Assert.Equal(expected, counts.ToArray());
            Assert.True(info.SingleDeviceOnly, $"fraction {fraction}");
            Assert.Equal(fraction == 1.0 ? (count, 0) : (0, count), (info.CpuExtent, info.GpuExtent));
        }
    }
}

/// <summary>A hand-written descriptor that reports atomics the way a descriptor written before <c>UsesDeviceAtomics</c> existed does.</summary>
public readonly struct HandWrittenAtomics : IComputeShaderDescriptor<HandWrittenAtomics>
{
    public static string EntryPoint => "main_kernel";

    public static string MslSource => string.Empty;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static bool UsesAtomics => true;

    public static void SetArguments(in HandWrittenAtomics shader, ref ShaderArgumentWriter writer)
    {
    }
}

/// <summary>A hand-written descriptor that says its atomics stay in threadgroup memory.</summary>
public readonly struct HandWrittenThreadgroupAtomics : IComputeShaderDescriptor<HandWrittenThreadgroupAtomics>
{
    public static string EntryPoint => "main_kernel";

    public static string MslSource => string.Empty;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static bool UsesAtomics => true;

    public static bool UsesDeviceAtomics => false;

    public static void SetArguments(in HandWrittenThreadgroupAtomics shader, ref ShaderArgumentWriter writer)
    {
    }
}
