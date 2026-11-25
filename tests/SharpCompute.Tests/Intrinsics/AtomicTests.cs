using System;
using System.Linq;

namespace SharpCompute.Tests.Intrinsics;

/// <summary>
/// <c>Gpu.Atomic*</c> under the heaviest contention: every thread of a large dispatch on the same
/// element. The GPU, the parallel CPU dispatcher and a single-threaded reference must agree on
/// everything that does not depend on the order the threads ran in.
/// </summary>
public class AtomicTests
{
    public enum Target
    {
        Gpu,
        Cpu,
    }

    private const int Threads = 1 << 20;

    private static void Run<T>(GraphicsDevice device, Target target, int threads, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        if (target == Target.Gpu)
        {
            device.For(threads, in shader);
        }
        else
        {
            device.ForCpu(threads, in shader);
        }
    }

    [MetalTheory]
    [InlineData(Target.Gpu, Threads)]
    [InlineData(Target.Cpu, Threads)]
    [InlineData(Target.Gpu, 1)]
    [InlineData(Target.Cpu, 1)]
    [InlineData(Target.Gpu, 1000)]
    public void EveryOperation_AllThreadsOnOneElement(Target target, int threads)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        // Initial values chosen so every operation has work to do: min starts high, max low, and all ones, or and xor zero.
        int[] initialInts = [0, 0, int.MaxValue, int.MinValue, -1, 0, 0, 0, 0];
        uint[] initialUInts = [0, 0, uint.MaxValue, 0, uint.MaxValue, 0, 0, 0, 0];
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(initialInts);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>(initialUInts);
        using ReadWriteBuffer<int> addOrder = device.AllocateReadWriteBuffer<int>(new int[threads]);
        using ReadWriteBuffer<uint> uintAddOrder = device.AllocateReadWriteBuffer<uint>(new uint[threads]);

        Run(device, target, threads, new AtomicEveryOperation(ints, uints, addOrder, uintAddOrder));

        // The single-threaded reference.
        int min = int.MaxValue, max = int.MinValue, and = -1, or = 0, xor = 0;
        uint umin = uint.MaxValue, umax = 0, uand = uint.MaxValue, uor = 0, uxor = 0, exchangedSum = 0;

        for (int i = 0; i < threads; i++)
        {
            int mixed = (int)AtomicEveryOperation.Mix((uint)i);
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

        int[] gotInts = ints.ToArray();
        uint[] gotUInts = uints.ToArray();

        Assert.Equal(threads, gotInts[0]);
        Assert.Equal((uint)threads, gotUInts[0]);
        Assert.Equal(unchecked(-3 * threads), gotInts[1]);
        Assert.Equal(unchecked(0u - (3u * (uint)threads)), gotUInts[1]); // wraps below zero
        Assert.Equal(min, gotInts[2]);
        Assert.Equal(umin, gotUInts[2]);
        Assert.Equal(max, gotInts[3]);
        Assert.Equal(umax, gotUInts[3]);
        Assert.Equal(and, gotInts[4]);
        Assert.Equal(uand, gotUInts[4]);
        Assert.Equal(or, gotInts[5]);
        Assert.Equal(uor, gotUInts[5]);
        Assert.Equal(xor, gotInts[6]);
        Assert.Equal(uxor, gotUInts[6]);

        // Add returns the original value: with n adds of 1 those are 0..n-1, each exactly once.
        Assert.All(addOrder.ToArray(), static count => Assert.Equal(1, count));
        Assert.All(uintAddOrder.ToArray(), static count => Assert.Equal(1u, count));

        // Exchange: every value stored (i + 1 for each thread, plus the initial 0) is either returned
        // to exactly one thread or still there at the end. Which one is last depends on the schedule.
        Assert.InRange(gotInts[7], 1, threads);
        Assert.InRange(gotUInts[7], 1u, (uint)threads);
        Assert.Equal(exchangedSum, unchecked((uint)gotInts[8] + (uint)gotInts[7]));
        Assert.Equal(exchangedSum, unchecked(gotUInts[8] + gotUInts[7]));
    }

    [MetalTheory]
    [InlineData(Target.Gpu, 1 << 14)]
    [InlineData(Target.Cpu, 1 << 14)]
    [InlineData(Target.Gpu, 1)]
    public void CompareExchange_IsStrong_OneWinnerAndAnExactCounter(Target target, int threads)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(new int[3]);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>(new uint[3]);

        Run(device, target, threads, new AtomicCompareExchangeLoop(ints, uints));

        // Exactly one thread saw the comparand and stored its id; a weak compare-and-swap that fails
        // spuriously could leave the claim to a later thread, but never to two or to none.
        Assert.InRange(ints[0], 1, threads);
        Assert.Equal(1, ints[1]);
        Assert.InRange(uints[0], 1u, (uint)threads);
        Assert.Equal(1u, uints[1]);

        // The retry loop only counts every thread if a failed swap really returns the current value.
        Assert.Equal(threads, ints[2]);
        Assert.Equal((uint)threads, uints[2]);
    }

    [MetalTheory]
    [InlineData(Target.Gpu)]
    [InlineData(Target.Cpu)]
    public void FloatAdd_AppliesEveryAdd_AndReturnsEachPartialSumOnce(Target target)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> sums = device.AllocateReadWriteBuffer<float>(new float[2]);
        using ReadWriteBuffer<int> order = device.AllocateReadWriteBuffer<int>(new int[Threads]);

        Run(device, target, Threads, new AtomicFloatSum(sums, order));

        // 2^20 ones and quarters: every partial sum is exact, so no add may be lost to rounding or a race.
        Assert.Equal((float)Threads, sums[0]);
        Assert.Equal(Threads / 4 * 1.5f, sums[1]);
        Assert.All(order.ToArray(), static count => Assert.Equal(1, count));
    }

    [MetalTheory]
    [InlineData(256)]
    [InlineData(4)]
    [InlineData(1)]
    public void Histogram_MatchesTheCpuReference_WhateverTheContention(int bins)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(bins);
        uint[] values = Enumerable.Range(0, Threads).Select(_ => (uint)random.Next()).ToArray();
        int[] expected = new int[bins];

        foreach (uint value in values)
        {
            expected[value % (uint)bins]++;
        }

        using ReadOnlyBuffer<uint> valueBuffer = device.AllocateReadOnlyBuffer<uint>(values);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(new int[bins]);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(new int[bins]);

        device.For(Threads, new AtomicHistogram(valueBuffer, gpu, bins));
        device.ForCpu(Threads, new AtomicHistogram(valueBuffer, cpu, bins));

        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
    }

    [MetalFact]
    public void Atomics_AccumulateAcrossDispatches_AndInsideAComputeContext()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] values = Enumerable.Range(0, 4096).Select(static i => (uint)i).ToArray();
        using ReadOnlyBuffer<uint> valueBuffer = device.AllocateReadOnlyBuffer<uint>(values);
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(new int[8]);

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(values.Length, new AtomicHistogram(valueBuffer, counts, 8));
            context.For(values.Length, new AtomicHistogram(valueBuffer, counts, 2));
        }

        device.For(values.Length, new AtomicHistogram(valueBuffer, counts, 8));
        device.ForCpu(values.Length, new AtomicHistogram(valueBuffer, counts, 1));

        // 512 per bin from each 8-bin pass, 2048 more in bins 0 and 1, all 4096 in bin 0.
        Assert.Equal([1024 + 2048 + 4096, 1024 + 2048, 1024, 1024, 1024, 1024, 1024, 1024], counts.ToArray());
    }
}
