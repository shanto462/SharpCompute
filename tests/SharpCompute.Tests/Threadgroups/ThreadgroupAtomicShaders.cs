using SharpCompute;

namespace SharpCompute.Tests.Threadgroups;

// Shaders for the atomics on threadgroup memory: Gpu.Atomic* on elements of [GroupShared] arrays. Each
// runs unchanged on the GPU (For) and on the CPU's group executor (ForCpu). Those whose only atomics are
// on threadgroup memory write nothing but their own group's results, so they also run split (ForSplit).

/// <summary>
/// Every thread of a threadgroup applies every atomic operation, int and uint, to the same few
/// group-shared elements: the heaviest contention a group has. Slots: 0 add, 1 sub, 2 min, 3 max, 4 and,
/// 5 or, 6 xor, 7 exchange, 8 the sum of what the exchanges returned. <c>order</c> counts how often each
/// value returned by the add was seen; if the add is atomic that is exactly once per value, so no two
/// threads ever write the same element of it. After the last barrier thread 0 copies the group's slots
/// to the group's rows of <c>ints</c> and <c>uints</c>, and every thread its own count.
/// </summary>
[ThreadGroupSize(GroupThreads)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct GroupAtomicEveryOperation(
    ReadWriteBuffer<int> ints, ReadWriteBuffer<uint> uints, ReadWriteBuffer<int> addOrder, ReadWriteBuffer<uint> uintAddOrder) : IComputeShader
{
    public const int GroupThreads = 256;
    public const int Slots = 9;

    [GroupShared]
    private static readonly int[] sharedInts = new int[Slots];

    [GroupShared]
    private static readonly uint[] sharedUInts = new uint[Slots];

    [GroupShared]
    private static readonly int[] order = new int[GroupThreads];

    [GroupShared]
    private static readonly uint[] uintOrder = new uint[GroupThreads];

    /// <summary>A bijection of the 32-bit integers (an odd multiplier and an xor-shift), so the values of a dispatch are all distinct.</summary>
    public static uint Mix(uint x)
    {
        x *= 2654435761u;
        x ^= x >> 15;
        return x;
    }

    /// <summary>Where each int slot starts, so that every operation has work to do: min starts high, max low, and all ones.</summary>
    public static int InitialInt(int slot) => slot == 2 ? int.MaxValue : slot == 3 ? int.MinValue : slot == 4 ? -1 : 0;

    /// <summary>The same for the uint slots (unsigned: the max starts at 0).</summary>
    public static uint InitialUInt(int slot) => slot == 2 || slot == 4 ? 0xFFFFFFFFu : 0u;

    public void Execute()
    {
        int i = ThreadIds.X;
        int local = GroupIds.Index;
        uint u = (uint)i;
        int mixed = (int)Mix(u);

        // Threadgroup memory starts out undefined: clear it, and meet before the first atomic.
        if (local == 0)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                sharedInts[slot] = InitialInt(slot);
                sharedUInts[slot] = InitialUInt(slot);
            }
        }

        order[local] = 0;
        uintOrder[local] = 0u;
        Gpu.GroupMemoryBarrierWithGroupSync();

        order[Gpu.AtomicAdd(ref sharedInts[0], 1)] += 1;
        uintOrder[(int)Gpu.AtomicAdd(ref sharedUInts[0], 1u)] += 1u;

        Gpu.AtomicSub(ref sharedInts[1], 3);
        Gpu.AtomicSub(ref sharedUInts[1], 3u);

        Gpu.AtomicMin(ref sharedInts[2], mixed);
        Gpu.AtomicMin(ref sharedUInts[2], (uint)mixed);
        Gpu.AtomicMax(ref sharedInts[3], mixed);
        Gpu.AtomicMax(ref sharedUInts[3], (uint)mixed);

        // Every thread clears one of the low 12 bits and sets one of the low 20.
        Gpu.AtomicAnd(ref sharedInts[4], ~(1 << (i % 12)));
        Gpu.AtomicAnd(ref sharedUInts[4], ~(1u << (i % 12)));
        Gpu.AtomicOr(ref sharedInts[5], 1 << (i % 20));
        Gpu.AtomicOr(ref sharedUInts[5], 1u << (i % 20));

        Gpu.AtomicXor(ref sharedInts[6], mixed);
        Gpu.AtomicXor(ref sharedUInts[6], (uint)mixed);

        // What an exchange returns was stored by exactly one other exchange (or is the initial value).
        Gpu.AtomicAdd(ref sharedInts[8], Gpu.AtomicExchange(ref sharedInts[7], i + 1));
        Gpu.AtomicAdd(ref sharedUInts[8], Gpu.AtomicExchange(ref sharedUInts[7], u + 1u));

        Gpu.GroupMemoryBarrierWithGroupSync();

        if (local == 0)
        {
            int row = ThreadGroupIds.X * Slots;

            for (int slot = 0; slot < Slots; slot++)
            {
                ints[row + slot] = sharedInts[slot];
                uints[row + slot] = sharedUInts[slot];
            }
        }

        addOrder[i] = order[local];
        uintAddOrder[i] = uintOrder[local];
    }
}

/// <summary>
/// Compare-and-swap within a threadgroup: every thread tries to claim one group-shared element (exactly
/// one may win), and increments a counter with the classic retry loop, which only counts right if the
/// swap is a strong one. The claims are made in a helper, which reaches the arrays through hidden
/// <c>threadgroup</c> pointers. Per group, 3 ints and 3 uints: the claim, the number of winners, the counter.
/// </summary>
[ThreadGroupSize(GroupThreads)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct GroupAtomicCompareExchangeLoop(ReadWriteBuffer<int> ints, ReadWriteBuffer<uint> uints) : IComputeShader
{
    public const int GroupThreads = 128;
    public const int Slots = 3;

    [GroupShared]
    private static readonly int[] sharedInts = new int[Slots];

    [GroupShared]
    private static readonly uint[] sharedUInts = new uint[Slots];

    public void Execute()
    {
        int i = ThreadIds.X;

        if (GroupIds.Index < Slots)
        {
            sharedInts[GroupIds.Index] = 0;
            sharedUInts[GroupIds.Index] = 0u;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();
        Claim(i + 1);

        int seen = 0;

        for (int attempt = 0; attempt < 100000000; attempt++)
        {
            int original = Gpu.AtomicCompareExchange(ref sharedInts[2], seen, seen + 1);

            if (original == seen)
            {
                break;
            }

            seen = original;
        }

        uint useen = 0u;

        for (int attempt = 0; attempt < 100000000; attempt++)
        {
            uint original = Gpu.AtomicCompareExchange(ref sharedUInts[2], useen, useen + 1u);

            if (original == useen)
            {
                break;
            }

            useen = original;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        if (GroupIds.Index < Slots)
        {
            int at = (ThreadGroupIds.X * Slots) + GroupIds.Index;
            ints[at] = sharedInts[GroupIds.Index];
            uints[at] = sharedUInts[GroupIds.Index];
        }
    }

    private static void Claim(int id)
    {
        if (Gpu.AtomicCompareExchange(ref sharedInts[0], 0, id) == 0)
        {
            Gpu.AtomicAdd(ref sharedInts[1], 1);
        }

        if (Gpu.AtomicCompareExchange(ref sharedUInts[0], 0u, (uint)id) == 0u)
        {
            Gpu.AtomicAdd(ref sharedUInts[1], 1u);
        }
    }
}

/// <summary>
/// One thread per threadgroup runs a fixed sequence of atomics at the wrap-around points on its group's
/// memory and records every returned value: the sequence of <c>AtomicSequence</c> (the buffer version),
/// float add included. <c>big</c> is <c>int.MaxValue</c> and <c>ubig</c> is <c>0xC0000000</c>, passed
/// as constants so that some operands only exist at run time. Each group writes its own rows.
/// </summary>
[ThreadGroupSize(4)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct GroupAtomicSequence(
    ReadWriteBuffer<int> finalInts,
    ReadWriteBuffer<uint> finalUInts,
    ReadWriteBuffer<float> finalFloats,
    ReadWriteBuffer<int> returned,
    ReadWriteBuffer<uint> returnedUInts,
    ReadWriteBuffer<float> returnedFloats,
    int big,
    uint ubig) : IComputeShader
{
    public const int Ints = 10;
    public const int UInts = 7;
    public const int Floats = 2;
    public const int Returned = 19;
    public const int ReturnedUInts = 16;
    public const int ReturnedFloats = 4;

    [GroupShared]
    private static readonly int[] ints = new int[Ints];

    [GroupShared]
    private static readonly uint[] uints = new uint[UInts];

    [GroupShared]
    private static readonly float[] floats = new float[Floats];

    public void Execute()
    {
        // Only the group's first thread touches its memory, so no barrier is needed.
        if (GroupIds.Index != 0)
        {
            return;
        }

        int group = ThreadGroupIds.X;
        int r = group * Returned;
        int ur = group * ReturnedUInts;

        ints[0] = 5;
        ints[1] = 7;
        ints[2] = int.MinValue;
        ints[3] = 0;
        ints[4] = -5;
        ints[5] = 0x0F0F0F0F;
        ints[6] = 42;
        ints[7] = 1;
        ints[8] = 1;
        ints[9] = -2;
        uints[0] = 0u;
        uints[1] = 0u;
        uints[2] = 5u;
        uints[3] = 5u;
        uints[4] = 0xF0F0F0F0u;
        uints[5] = 9u;
        uints[6] = 0xC0000000u;
        floats[0] = 1.5f;
        floats[1] = -0f;

        // int: add past int.MaxValue, subtract int.MinValue, signed min/max, bitwise, exchange, both compare-exchanges.
        returned[r + 0] = Gpu.AtomicAdd(ref ints[0], int.MaxValue);
        returned[r + 1] = Gpu.AtomicAdd(ref ints[0], 1);
        returned[r + 2] = Gpu.AtomicSub(ref ints[1], int.MinValue);
        returned[r + 3] = Gpu.AtomicSub(ref ints[2], 1);
        returned[r + 4] = Gpu.AtomicSub(ref ints[2], -1);
        returned[r + 5] = Gpu.AtomicMin(ref ints[3], -1);
        returned[r + 6] = Gpu.AtomicMin(ref ints[3], 5);
        returned[r + 7] = Gpu.AtomicMax(ref ints[4], int.MinValue);
        returned[r + 8] = Gpu.AtomicMax(ref ints[4], 9);
        returned[r + 9] = Gpu.AtomicAnd(ref ints[5], 0x00FF00FF);
        returned[r + 10] = Gpu.AtomicOr(ref ints[5], int.MinValue);
        returned[r + 11] = Gpu.AtomicXor(ref ints[5], -1);
        returned[r + 12] = Gpu.AtomicExchange(ref ints[6], int.MinValue);
        returned[r + 13] = Gpu.AtomicCompareExchange(ref ints[7], 3, 9);
        returned[r + 14] = Gpu.AtomicCompareExchange(ref ints[7], 1, 11);
        returned[r + 15] = Gpu.AtomicAdd(ref ints[8], big);
        returned[r + 16] = Gpu.AtomicAdd(ref ints[8], big);
        returned[r + 17] = Gpu.AtomicSub(ref ints[9], big);
        returned[r + 18] = Gpu.AtomicSub(ref ints[9], -big - 1);

        // uint: add and subtract through zero, unsigned min/max (0xFFFFFFFF is the largest, not -1).
        returnedUInts[ur + 0] = Gpu.AtomicAdd(ref uints[0], 0xFFFFFFFFu);
        returnedUInts[ur + 1] = Gpu.AtomicAdd(ref uints[0], 2u);
        returnedUInts[ur + 2] = Gpu.AtomicSub(ref uints[1], 1u);
        returnedUInts[ur + 3] = Gpu.AtomicSub(ref uints[1], ubig);
        returnedUInts[ur + 4] = Gpu.AtomicSub(ref uints[1], ubig);
        returnedUInts[ur + 5] = Gpu.AtomicMin(ref uints[2], 0xFFFFFFFFu);
        returnedUInts[ur + 6] = Gpu.AtomicMin(ref uints[2], 3u);
        returnedUInts[ur + 7] = Gpu.AtomicMax(ref uints[3], 0x80000000u);
        returnedUInts[ur + 8] = Gpu.AtomicMax(ref uints[3], 7u);
        returnedUInts[ur + 9] = Gpu.AtomicAnd(ref uints[4], 0xFF00FF00u);
        returnedUInts[ur + 10] = Gpu.AtomicOr(ref uints[4], 0x0000000Fu);
        returnedUInts[ur + 11] = Gpu.AtomicXor(ref uints[4], 0xFFFFFFFFu);
        returnedUInts[ur + 12] = Gpu.AtomicExchange(ref uints[5], 0xDEADBEEFu);
        returnedUInts[ur + 13] = Gpu.AtomicCompareExchange(ref uints[6], 1u, 2u);
        returnedUInts[ur + 14] = Gpu.AtomicCompareExchange(ref uints[6], ubig, 1u);
        returnedUInts[ur + 15] = Gpu.AtomicMax(ref uints[6], ubig);

        // float: as on a buffer, then an element that holds a negative zero, whose bits (not its
        // value, which equals +0) are what the loop must compare.
        int fr = group * ReturnedFloats;
        returnedFloats[fr + 0] = Gpu.AtomicAdd(ref floats[0], 0.25f);
        returnedFloats[fr + 1] = Gpu.AtomicAdd(ref floats[0], -2f);
        returnedFloats[fr + 2] = Gpu.AtomicAdd(ref floats[1], -0f);
        returnedFloats[fr + 3] = Gpu.AtomicAdd(ref floats[1], 3f);

        for (int k = 0; k < Floats; k++)
        {
            finalFloats[(group * Floats) + k] = floats[k];
        }

        for (int k = 0; k < Ints; k++)
        {
            finalInts[(group * Ints) + k] = ints[k];
        }

        for (int k = 0; k < UInts; k++)
        {
            finalUInts[(group * UInts) + k] = uints[k];
        }
    }
}

/// <summary>
/// The float add within a threadgroup, every thread on the same three group-shared elements.
/// Slot 0: all threads add 1 (the returned values are the integers 0..n-1, each once, counted in
/// <c>order</c>). Slot 1: a multiple of 0.25, so every partial sum is exact and the order the threads
/// ran in does not show. Slot 2: values with no exact sum, where the result depends on that order
/// within rounding. The add is made in a helper for slot 2. Each group writes its own row.
/// </summary>
[ThreadGroupSize(GroupThreads)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct GroupAtomicFloatSum(ReadWriteBuffer<float> sums, ReadWriteBuffer<int> addOrder) : IComputeShader
{
    public const int GroupThreads = 256;
    public const int Slots = 3;

    [GroupShared]
    private static readonly float[] shared = new float[Slots];

    [GroupShared]
    private static readonly int[] order = new int[GroupThreads];

    /// <summary>What thread <paramref name="i"/> adds to slot 2: seven different values that are not sums of powers of two.</summary>
    public static float Inexact(int i) => 0.1f * ((i % 7) + 1);

    public void Execute()
    {
        int i = ThreadIds.X;
        int local = GroupIds.Index;

        // The group's first thread clears and copies out every slot: a partial group may have fewer threads than slots.
        if (local == 0)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                shared[slot] = 0f;
            }
        }

        order[local] = 0;
        Gpu.GroupMemoryBarrierWithGroupSync();

        float before = Gpu.AtomicAdd(ref shared[0], 1f);
        order[(int)before] += 1;
        Gpu.AtomicAdd(ref shared[1], (i & 3) * 0.25f);
        Accumulate(Inexact(i));

        Gpu.GroupMemoryBarrierWithGroupSync();

        if (local == 0)
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                sums[(ThreadGroupIds.X * Slots) + slot] = shared[slot];
            }
        }

        addOrder[i] = order[local];
    }

    private static void Accumulate(float value) => Gpu.AtomicAdd(ref shared[2], value);
}

/// <summary>
/// A threadgroup atomic and a SIMD-group reduction together: every SIMD group sums its lanes with
/// <see cref="Gpu.WaveActiveSum(int, string, int)"/>, and its first lane adds that sum to a group-shared
/// total, counts the SIMD group and keeps the largest SIMD-group sum, one atomic each per SIMD group.
/// After a barrier the threadgroup's first thread writes the three to the group's row. Dispatched over
/// whole threadgroups (five SIMD groups of 32 lanes each).
/// </summary>
[ThreadGroupSize(GroupThreads)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdGroupAtomicTotals(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> totals) : IComputeShader
{
    public const int GroupThreads = 160;
    public const int Stride = 4;

    [GroupShared]
    private static readonly int[] shared = new int[3];

    public void Execute()
    {
        int local = GroupIds.Index;

        if (local == 0)
        {
            shared[0] = 0;
            shared[1] = 0;
            shared[2] = int.MinValue;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        // Every lane gets its SIMD group's sum; one lane per SIMD group hands it to the threadgroup.
        int sum = Gpu.WaveActiveSum(input[ThreadIds.X]);

        if (Gpu.WaveIsFirstLane())
        {
            Gpu.AtomicAdd(ref shared[0], sum);
            Gpu.AtomicAdd(ref shared[1], 1);
            Gpu.AtomicMax(ref shared[2], sum);
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        if (local == 0)
        {
            int row = ThreadGroupIds.X * Stride;
            totals[row + 0] = shared[0];
            totals[row + 1] = shared[1];
            totals[row + 2] = shared[2];
            totals[row + 3] = SimdGroupIds.Count;
        }
    }
}

/// <summary>
/// A privatised histogram: each threadgroup counts its values in a histogram of its own in group-shared
/// memory, with threadgroup atomics that only its 256 threads contend for, and then adds each non-empty
/// bin to the global histogram with one buffer atomic. The last group may be partial: its threads share
/// out the bins by <see cref="GroupSize.Count"/>. Atomics on a buffer, so it is never split.
/// </summary>
[ThreadGroupSize(GroupThreads)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct PrivatisedHistogram(ReadOnlyBuffer<uint> values, ReadWriteBuffer<int> counts, int bins) : IComputeShader
{
    public const int GroupThreads = 256;
    public const int MaxBins = 256;

    [GroupShared]
    private static readonly int[] local = new int[MaxBins];

    public void Execute()
    {
        int first = GroupIds.Index;
        int stride = GroupSize.Count;

        for (int bin = first; bin < bins; bin += stride)
        {
            local[bin] = 0;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();
        Count((int)(values[ThreadIds.X] % (uint)bins));
        Gpu.GroupMemoryBarrierWithGroupSync();

        for (int bin = first; bin < bins; bin += stride)
        {
            int count = local[bin];

            if (count != 0)
            {
                Gpu.AtomicAdd(ref counts[bin], count);
            }
        }
    }

    private static void Count(int bin) => Gpu.AtomicAdd(ref local[bin], 1);
}

/// <summary>
/// The first half of that alone: each threadgroup's own histogram, written to the group's row of
/// <c>counts</c>. Its only atomics are on threadgroup memory, and a group writes only its own row, so it
/// can be split between the CPU and the GPU.
/// </summary>
[ThreadGroupSize(GroupThreads)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct GroupHistogram(ReadOnlyBuffer<uint> values, ReadWriteBuffer<int> counts, int bins) : IComputeShader
{
    public const int GroupThreads = 64;
    public const int MaxBins = 64;

    [GroupShared]
    private static readonly int[] local = new int[MaxBins];

    public void Execute()
    {
        int first = GroupIds.Index;
        int stride = GroupSize.Count;

        for (int bin = first; bin < bins; bin += stride)
        {
            local[bin] = 0;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();
        Gpu.AtomicAdd(ref local[(int)(values[ThreadIds.X] % (uint)bins)], 1);
        Gpu.GroupMemoryBarrierWithGroupSync();

        for (int bin = first; bin < bins; bin += stride)
        {
            counts[(ThreadGroupIds.X * bins) + bin] = local[bin];
        }
    }
}
