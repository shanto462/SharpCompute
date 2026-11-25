using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SharpCompute;

/// <content>
/// Atomic operations on buffer elements and on threadgroup memory. In a shader the location must be
/// an element of a captured <see cref="ReadWriteBuffer{T}"/> of int, uint or float, or an element of a
/// <see cref="GroupSharedAttribute"/> array of int, uint or float, passed by reference:
/// <c>Gpu.AtomicAdd(ref counts[bin], 1)</c>. Every operation returns the value the element had
/// <b>before</b> it was applied.
/// </content>
/// <remarks>
/// <para>
/// On the GPU these are <c>metal::atomic_fetch_*_explicit</c> on <c>device</c> memory (a buffer
/// element) or on <c>threadgroup</c> memory (a group-shared element). MSL only
/// offers <c>memory_order_relaxed</c>: each operation is atomic on its own element, but nothing is
/// promised about how it is ordered against operations on <i>other</i> elements, and a plain
/// (non-atomic) read or write of the same element in the same dispatch is a data race. Read the
/// results on the CPU after the dispatch, or in a later dispatch.
/// </para>
/// <para>
/// <b>Threadgroup memory.</b> An element of a <see cref="GroupSharedAttribute"/> array is shared by
/// the threads of one threadgroup only, so an atomic on it is contended by at most a threadgroup's
/// worth of threads instead of the whole dispatch: accumulate per group there, and merge into a
/// buffer with one buffer atomic per group (a privatised histogram). Threadgroup memory starts out
/// undefined, so the group clears the elements first, and a plain read or write of an element is
/// separated from the atomics on it by a barrier
/// (<see cref="GroupMemoryBarrierWithGroupSync"/>): clear, barrier, atomics, barrier, read. MSL 3.2
/// has no atomic float in threadgroup memory, so <see cref="AtomicAdd(ref float, float)"/> on a
/// group-shared element is a compare-and-swap loop over the element's bits there: the same API and
/// the same guarantees as on a buffer, at the cost of a retry when two threads collide.
/// </para>
/// <para>
/// The CPU implementations are truly atomic as well (<see cref="Interlocked"/>, and compare-and-swap
/// loops where it has no primitive: min, max, xor and the float add), because <c>ForCpu</c> runs
/// shader threads on every core, and the threads of a threadgroup concurrently when the shader uses
/// threadgroup memory. Integer arithmetic wraps, as in MSL. The final value of an element
/// is the same on both, except for <see cref="AtomicAdd(ref float, float)"/>, where float addition
/// is not associative and the sum depends on the order the threads happened to run in. The values
/// <i>returned</i> to the individual threads depend on that order everywhere, on the CPU and on
/// the GPU alike; only the set of them is deterministic (for an add of 1: a permutation of
/// <c>0..n-1</c>).
/// </para>
/// <para>
/// The guarantee holds among the threads of one dispatch on one device, not between a GPU dispatch
/// and CPU code running at the same time: the GPU's atomics and the CPU's <see cref="Interlocked"/>
/// operations do not exclude each other on shared memory, and updates are lost. <c>ForAsync</c> next
/// to <c>ForCpu</c> (or any other CPU code) on the same buffer is such a race, and so is a CPU+GPU
/// split, which is why <c>ForSplit</c> never divides a shader that uses atomics on buffer elements:
/// an explicit share strictly between 0 and 1 throws <see cref="NotSupportedException"/>, and the
/// adaptive mode runs the whole grid on the GPU. Atomics on threadgroup memory are the exception: a
/// threadgroup runs entirely on one device, so a shader whose only atomics are on group-shared
/// elements is split like any other.
/// </para>
/// </remarks>
public static partial class Gpu
{
    // ---- add / subtract ----------------------------------------------------------------------

    /// <summary>Atomically adds <paramref name="value"/> (wrapping) and returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_add_explicit")]
    public static int AtomicAdd(ref int location, int value) => unchecked(Interlocked.Add(ref location, value) - value);

    /// <summary>Atomically adds <paramref name="value"/> (wrapping) and returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_add_explicit")]
    public static uint AtomicAdd(ref uint location, uint value) => unchecked(Interlocked.Add(ref location, value) - value);

    /// <summary>
    /// Atomically adds <paramref name="value"/> and returns the original value. Every add is applied
    /// exactly once, but float addition is not associative: with rounding, the final sum depends on
    /// the order in which the threads ran, on the GPU and on the CPU.
    /// </summary>
    /// <remarks>
    /// On a buffer element this is MSL's own <c>atomic&lt;float&gt;</c> add. MSL 3.2 has none for
    /// threadgroup memory, so on a <see cref="GroupSharedAttribute"/> element it is a compare-and-swap
    /// loop over the element's bits (<c>__ms_atomic_fetch_add_float</c> in the prelude), which is also
    /// how the CPU does it. The guarantees are the same.
    /// </remarks>
    [MslAtomic("metal::atomic_fetch_add_explicit", ThreadgroupFloat = "__ms_atomic_fetch_add_float")]
    public static float AtomicAdd(ref float location, float value)
    {
        // Compare the bits, not the floats: a NaN never equals itself, and -0 equals +0.
        ref int bits = ref Unsafe.As<float, int>(ref location);
        int original = Volatile.Read(ref bits);

        while (true)
        {
            int updated = BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(original) + value);
            int seen = Interlocked.CompareExchange(ref bits, updated, original);

            if (seen == original)
            {
                return BitConverter.Int32BitsToSingle(original);
            }

            original = seen;
        }
    }

    /// <summary>Atomically subtracts <paramref name="value"/> (wrapping) and returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_sub_explicit")]
    public static int AtomicSub(ref int location, int value) => unchecked(Interlocked.Add(ref location, -value) + value);

    /// <summary>Atomically subtracts <paramref name="value"/> (wrapping) and returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_sub_explicit")]
    public static uint AtomicSub(ref uint location, uint value) => unchecked(Interlocked.Add(ref location, 0u - value) + value);

    // ---- min / max ---------------------------------------------------------------------------

    /// <summary>Atomically stores the smaller of the element and <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_min_explicit")]
    public static int AtomicMin(ref int location, int value)
    {
        int original = Volatile.Read(ref location);

        // Nothing to store once the element is already at or below the value; that read is the result.
        while (value < original)
        {
            int seen = Interlocked.CompareExchange(ref location, value, original);

            if (seen == original)
            {
                break;
            }

            original = seen;
        }

        return original;
    }

    /// <summary>Atomically stores the smaller of the element and <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_min_explicit")]
    public static uint AtomicMin(ref uint location, uint value)
    {
        uint original = Volatile.Read(ref location);

        while (value < original)
        {
            uint seen = Interlocked.CompareExchange(ref location, value, original);

            if (seen == original)
            {
                break;
            }

            original = seen;
        }

        return original;
    }

    /// <summary>Atomically stores the larger of the element and <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_max_explicit")]
    public static int AtomicMax(ref int location, int value)
    {
        int original = Volatile.Read(ref location);

        while (value > original)
        {
            int seen = Interlocked.CompareExchange(ref location, value, original);

            if (seen == original)
            {
                break;
            }

            original = seen;
        }

        return original;
    }

    /// <summary>Atomically stores the larger of the element and <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_max_explicit")]
    public static uint AtomicMax(ref uint location, uint value)
    {
        uint original = Volatile.Read(ref location);

        while (value > original)
        {
            uint seen = Interlocked.CompareExchange(ref location, value, original);

            if (seen == original)
            {
                break;
            }

            original = seen;
        }

        return original;
    }

    // ---- bitwise -----------------------------------------------------------------------------

    /// <summary>Atomically ANDs the element with <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_and_explicit")]
    public static int AtomicAnd(ref int location, int value) => Interlocked.And(ref location, value);

    /// <summary>Atomically ANDs the element with <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_and_explicit")]
    public static uint AtomicAnd(ref uint location, uint value) => Interlocked.And(ref location, value);

    /// <summary>Atomically ORs the element with <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_or_explicit")]
    public static int AtomicOr(ref int location, int value) => Interlocked.Or(ref location, value);

    /// <summary>Atomically ORs the element with <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_or_explicit")]
    public static uint AtomicOr(ref uint location, uint value) => Interlocked.Or(ref location, value);

    /// <summary>Atomically XORs the element with <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_xor_explicit")]
    public static int AtomicXor(ref int location, int value)
    {
        int original = Volatile.Read(ref location);

        while (true)
        {
            int seen = Interlocked.CompareExchange(ref location, original ^ value, original);

            if (seen == original)
            {
                return original;
            }

            original = seen;
        }
    }

    /// <summary>Atomically XORs the element with <paramref name="value"/>; returns the original value.</summary>
    [MslAtomic("metal::atomic_fetch_xor_explicit")]
    public static uint AtomicXor(ref uint location, uint value)
    {
        uint original = Volatile.Read(ref location);

        while (true)
        {
            uint seen = Interlocked.CompareExchange(ref location, original ^ value, original);

            if (seen == original)
            {
                return original;
            }

            original = seen;
        }
    }

    // ---- exchange ----------------------------------------------------------------------------

    /// <summary>Atomically stores <paramref name="value"/> and returns the original value.</summary>
    [MslAtomic("metal::atomic_exchange_explicit")]
    public static int AtomicExchange(ref int location, int value) => Interlocked.Exchange(ref location, value);

    /// <summary>Atomically stores <paramref name="value"/> and returns the original value.</summary>
    [MslAtomic("metal::atomic_exchange_explicit")]
    public static uint AtomicExchange(ref uint location, uint value) => Interlocked.Exchange(ref location, value);

    /// <summary>
    /// Atomically stores <paramref name="value"/> if the element equals <paramref name="comparand"/>.
    /// Returns the original value either way, so the exchange happened exactly when the result equals
    /// <paramref name="comparand"/>.
    /// </summary>
    /// <remarks>
    /// Note the argument order: the comparand comes first (<see cref="Interlocked.CompareExchange(ref int, int, int)"/>
    /// takes the new value first). A strong compare-and-swap: MSL only has the weak form, which may
    /// fail although the element equals the comparand, so the <c>__ms_atomic_compare_exchange</c>
    /// prelude helper retries until it either succeeds or reads a different value.
    /// </remarks>
    [MslAtomic("__ms_atomic_compare_exchange")]
    public static int AtomicCompareExchange(ref int location, int comparand, int value) => Interlocked.CompareExchange(ref location, value, comparand);

    /// <inheritdoc cref="AtomicCompareExchange(ref int, int, int)"/>
    [MslAtomic("__ms_atomic_compare_exchange")]
    public static uint AtomicCompareExchange(ref uint location, uint comparand, uint value) => Interlocked.CompareExchange(ref location, value, comparand);
}
