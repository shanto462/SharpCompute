using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCompute.Vectors.Tests;

/// <summary>
/// The CPU implementations of <c>Gpu.Atomic*</c>: each returns the original value, integer
/// arithmetic wraps, and all of them are atomic under real contention (<c>ForCpu</c> runs shader
/// threads on every core).
/// </summary>
public class AtomicIntrinsicTests
{
    private const int Threads = 8;
    private const int PerThread = 50_000;

    /// <summary>Runs <paramref name="body"/>(thread, iteration) on <see cref="Threads"/> threads that start together.</summary>
    private static void Hammer(Action<int, int> body)
    {
        using var start = new Barrier(Threads);
        Task[] tasks = Enumerable.Range(0, Threads).Select(t => Task.Factory.StartNew(
            () =>
            {
                start.SignalAndWait();
                for (int i = 0; i < PerThread; i++)
                {
                    body(t, i);
                }
            },
            TaskCreationOptions.LongRunning)).ToArray();
        Task.WaitAll(tasks);
    }

    // ---- single-threaded semantics ----------------------------------------------------------------

    [Fact]
    public void EveryIntOperation_ReturnsTheOriginalAndStoresTheResult()
    {
        int x = 10;
        Assert.Equal(10, Gpu.AtomicAdd(ref x, 5));
        Assert.Equal(15, x);
        Assert.Equal(15, Gpu.AtomicSub(ref x, 20));
        Assert.Equal(-5, x);
        Assert.Equal(-5, Gpu.AtomicMin(ref x, -7));
        Assert.Equal(-7, x);
        Assert.Equal(-7, Gpu.AtomicMin(ref x, 3));
        Assert.Equal(-7, x);
        Assert.Equal(-7, Gpu.AtomicMax(ref x, 12));
        Assert.Equal(12, x);
        Assert.Equal(12, Gpu.AtomicMax(ref x, -100));
        Assert.Equal(12, x);
        Assert.Equal(12, Gpu.AtomicAnd(ref x, 0b0110));
        Assert.Equal(0b0100, x);
        Assert.Equal(0b0100, Gpu.AtomicOr(ref x, 0b0011));
        Assert.Equal(0b0111, x);
        Assert.Equal(0b0111, Gpu.AtomicXor(ref x, 0b0101));
        Assert.Equal(0b0010, x);
        Assert.Equal(2, Gpu.AtomicExchange(ref x, 99));
        Assert.Equal(99, x);
        Assert.Equal(99, Gpu.AtomicCompareExchange(ref x, comparand: 98, value: 1));
        Assert.Equal(99, x);
        Assert.Equal(99, Gpu.AtomicCompareExchange(ref x, comparand: 99, value: 1));
        Assert.Equal(1, x);
    }

    [Fact]
    public void EveryUIntOperation_ReturnsTheOriginalAndStoresTheResult()
    {
        uint x = 10;
        Assert.Equal(10u, Gpu.AtomicAdd(ref x, 5));
        Assert.Equal(15u, x);
        Assert.Equal(15u, Gpu.AtomicSub(ref x, 5));
        Assert.Equal(10u, x);
        Assert.Equal(10u, Gpu.AtomicMin(ref x, 0xFFFFFFF0u)); // unsigned: not -16
        Assert.Equal(10u, x);
        Assert.Equal(10u, Gpu.AtomicMax(ref x, 0xFFFFFFF0u));
        Assert.Equal(0xFFFFFFF0u, x);
        Assert.Equal(0xFFFFFFF0u, Gpu.AtomicMin(ref x, 7));
        Assert.Equal(7u, x);
        Assert.Equal(7u, Gpu.AtomicAnd(ref x, 0b0110));
        Assert.Equal(0b0110u, x);
        Assert.Equal(0b0110u, Gpu.AtomicOr(ref x, 0x80000000u));
        Assert.Equal(0x80000006u, x);
        Assert.Equal(0x80000006u, Gpu.AtomicXor(ref x, 0xFFFFFFFFu));
        Assert.Equal(0x7FFFFFF9u, x);
        Assert.Equal(0x7FFFFFF9u, Gpu.AtomicExchange(ref x, 3));
        Assert.Equal(3u, Gpu.AtomicCompareExchange(ref x, comparand: 4, value: 9));
        Assert.Equal(3u, x);
        Assert.Equal(3u, Gpu.AtomicCompareExchange(ref x, comparand: 3, value: 9));
        Assert.Equal(9u, x);
    }

    [Fact]
    public void IntegerArithmeticWraps_LikeMsl()
    {
        int i = int.MaxValue;
        Assert.Equal(int.MaxValue, Gpu.AtomicAdd(ref i, 1));
        Assert.Equal(int.MinValue, i);
        Assert.Equal(int.MinValue, Gpu.AtomicSub(ref i, 1));
        Assert.Equal(int.MaxValue, i);
        Assert.Equal(int.MaxValue, Gpu.AtomicSub(ref i, int.MinValue)); // -int.MinValue wraps to itself
        Assert.Equal(-1, i);

        uint u = uint.MaxValue;
        Assert.Equal(uint.MaxValue, Gpu.AtomicAdd(ref u, 2));
        Assert.Equal(1u, u);
        Assert.Equal(1u, Gpu.AtomicSub(ref u, 3));
        Assert.Equal(0xFFFFFFFEu, u);
    }

    [Fact]
    public void FloatAdd_ReturnsTheOriginal_AndHandlesNaNAndSignedZero()
    {
        float x = 1.5f;
        FloatAssert.Same(1.5f, Gpu.AtomicAdd(ref x, 2.25f));
        FloatAssert.Same(3.75f, x);
        FloatAssert.Same(3.75f, Gpu.AtomicAdd(ref x, float.PositiveInfinity));
        FloatAssert.Same(float.PositiveInfinity, x);
        FloatAssert.Same(float.PositiveInfinity, Gpu.AtomicAdd(ref x, float.NegativeInfinity));
        Assert.True(float.IsNaN(x));

        // A NaN never compares equal to itself: the compare-and-swap must compare bits, or this spins forever.
        Assert.True(float.IsNaN(Gpu.AtomicAdd(ref x, 1f)));
        Assert.True(float.IsNaN(x));

        // -0 == +0 as floats, but they are different elements.
        float zero = -0f;
        FloatAssert.Same(-0f, Gpu.AtomicAdd(ref zero, -0f));
        FloatAssert.Same(-0f, zero);
        FloatAssert.Same(-0f, Gpu.AtomicAdd(ref zero, 0f));
        FloatAssert.Same(0f, zero);
    }

    // ---- under contention -------------------------------------------------------------------------

    [Fact]
    public void Add_UnderContention_LosesNothing_AndReturnsAPermutation()
    {
        int counter = 0;
        var seen = new int[Threads * PerThread];

        Hammer((_, _) => Interlocked.Increment(ref seen[Gpu.AtomicAdd(ref counter, 1)]));

        Assert.Equal(Threads * PerThread, counter);

        // Every original value 0..n-1 was handed out exactly once: the proof that no add was lost or doubled.
        Assert.All(seen, static count => Assert.Equal(1, count));
    }

    [Fact]
    public void UIntAddAndSub_UnderContention_CancelOut()
    {
        uint value = 1000;

        Hammer((t, i) =>
        {
            if (t % 2 == 0)
            {
                Gpu.AtomicAdd(ref value, (uint)(i % 7));
            }
            else
            {
                Gpu.AtomicSub(ref value, (uint)(i % 7));
            }
        });

        Assert.Equal(1000u, value);
    }

    [Fact]
    public void MinAndMax_UnderContention_FindTheExtremes()
    {
        int min = int.MaxValue, max = int.MinValue;
        uint umin = uint.MaxValue, umax = 0;

        Hammer((t, i) =>
        {
            int v = unchecked((int)((uint)((t * PerThread) + i) * 2654435761u));
            Gpu.AtomicMin(ref min, v);
            Gpu.AtomicMax(ref max, v);
            Gpu.AtomicMin(ref umin, (uint)v);
            Gpu.AtomicMax(ref umax, (uint)v);
        });

        int[] all = Enumerable.Range(0, Threads * PerThread).Select(static n => unchecked((int)((uint)n * 2654435761u))).ToArray();
        Assert.Equal(all.Min(), min);
        Assert.Equal(all.Max(), max);
        Assert.Equal(all.Min(static v => (uint)v), umin);
        Assert.Equal(all.Max(static v => (uint)v), umax);
    }

    [Fact]
    public void Bitwise_UnderContention_ApplyEveryOperation()
    {
        uint xor = 0, or = 0, and = uint.MaxValue;
        int ixor = 0;

        Hammer((t, i) =>
        {
            uint v = (uint)((t * PerThread) + i);
            Gpu.AtomicXor(ref xor, v * 2654435761u);
            Gpu.AtomicXor(ref ixor, (int)v);
            Gpu.AtomicOr(ref or, 1u << (int)(v % 32));
            Gpu.AtomicAnd(ref and, ~(1u << (int)(v % 16)));
        });

        uint expectedXor = 0;
        int expectedIntXor = 0;
        for (uint v = 0; v < Threads * PerThread; v++)
        {
            expectedXor ^= v * 2654435761u;
            expectedIntXor ^= (int)v;
        }

        Assert.Equal(expectedXor, xor);
        Assert.Equal(expectedIntXor, ixor);
        Assert.Equal(uint.MaxValue, or);
        Assert.Equal(0xFFFF0000u, and);
    }

    [Fact]
    public void ExchangeAndCompareExchange_UnderContention_HandEachValueToExactlyOneThread()
    {
        // Exchange: a token passed around; every value written is read back by exactly one exchange.
        int slot = -1;
        long sumWritten = 0, sumRead = 0;

        Hammer((t, i) =>
        {
            int value = (t * PerThread) + i;
            int previous = Gpu.AtomicExchange(ref slot, value);
            Interlocked.Add(ref sumWritten, value);
            Interlocked.Add(ref sumRead, previous);
        });

        Assert.Equal(sumWritten - 1, sumRead + slot); // the initial -1 was read, the final value was not

        // CompareExchange: a lock-free counter, and a claim that exactly one thread wins.
        uint counter = 0;
        int owner = 0, winners = 0;

        Hammer((t, _) =>
        {
            uint seen = counter;
            while (true)
            {
                uint original = Gpu.AtomicCompareExchange(ref counter, seen, seen + 1);
                if (original == seen)
                {
                    break;
                }

                seen = original;
            }

            if (Gpu.AtomicCompareExchange(ref owner, 0, t + 1) == 0)
            {
                Interlocked.Increment(ref winners);
            }
        });

        Assert.Equal((uint)(Threads * PerThread), counter);
        Assert.Equal(1, winners);
        Assert.InRange(owner, 1, Threads);
    }

    [Fact]
    public void FloatAdd_UnderContention_AppliesEveryAdd()
    {
        // Small integers stay exact in a float up to 2^24, so the sum does not depend on the order here.
        float sum = 0f;

        Hammer((_, i) => Gpu.AtomicAdd(ref sum, (i % 4) + 1));

        Assert.Equal(Threads * (PerThread / 4) * 10f, sum);
    }
}
