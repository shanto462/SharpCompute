using System;
using System.Runtime.CompilerServices;

namespace SharpCompute;

/// <summary>A SIMD-group operation, as the CPU implementations of <see cref="Gpu"/>'s wave and quad methods name it.</summary>
/// <remarks>
/// Together with the line of the call (<c>callSite</c>) this identifies a call site: the lanes of a SIMD
/// group must all be at the same one for the operation to have a meaning on the CPU.
/// </remarks>
internal enum SimdOp : byte
{
    IsFirstLane,
    AnyTrue,
    AllTrue,
    Ballot,
    CountBits,
    PrefixCountBits,
    Sum,
    Product,
    Min,
    Max,
    BitAnd,
    BitOr,
    BitXor,
    PrefixSum,
    PrefixProduct,
    InclusivePrefixSum,
    InclusivePrefixProduct,
    ReadLaneAt,
    ReadLaneFirst,
    ShuffleUp,
    ShuffleDown,
    ShuffleXor,
    QuadReadAcrossX,
    QuadReadAcrossY,
    QuadReadAcrossDiagonal,
    QuadReadLaneAt,
}

/// <summary>The component type of the value a SIMD-group operation works on; vectors are 2 to 4 of them.</summary>
internal enum SimdScalar : byte
{
    Float,
    Int,
    UInt,
}

/// <summary>
/// The CPU side of the SIMD-group (wave and quad) operations of <see cref="Gpu"/> and of
/// <see cref="SimdGroupIds"/>. Every public operation funnels into one of the methods below with its
/// <see cref="SimdOp"/>, so that the executor has a single place to implement.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not implemented yet.</b> A SIMD-group operation is computed from what the other lanes of the group
/// bring to the same call, so the threads of a SIMD group have to run side by side and meet at each
/// operation, which the CPU dispatcher (independent threads spread over the thread pool) cannot do.
/// Until the threadgroup executor that can is in place, <c>GraphicsDevice.ForCpu</c> and
/// <c>ForSplit</c> refuse shaders whose descriptor reports
/// <see cref="IComputeShaderDescriptor{TSelf}.UsesSimdGroups"/>, and every method here throws:
/// <see cref="ShaderOnly.Error"/> outside a CPU dispatch (like every other shader built-in), and
/// <see cref="NotSupportedException"/> inside one (reachable only through a hand-written descriptor
/// that does not report its SIMD-group operations).
/// </para>
/// <para>
/// <b>The model the executor implements.</b> A SIMD group is <see cref="Width"/> consecutive threads of
/// a threadgroup in the order of their linear index (<c>(z * sizeY + y) * sizeX + x</c>, with the real
/// size of a partial threadgroup at the edge of the grid): lane <c>index % 32</c> of group
/// <c>index / 32</c>. That is how Apple GPUs form them (measured; see the SIMD-group tests). The lanes
/// of a group meet at each operation; a lane that has returned from <c>Execute</c> no longer takes
/// part, and lanes past the end of a partial group do not exist.
/// </para>
/// </remarks>
internal static class CpuSimdGroup
{
    /// <summary>
    /// Threads per SIMD group: 32 on every Apple GPU. <see cref="Gpu.WaveActiveBallot"/> returns one bit
    /// per lane in a <see cref="uint"/> on the strength of it, and a dispatch checks it against the
    /// compiled kernel (<c>threadExecutionWidth</c>) for shaders that use SIMD-group operations.
    /// </summary>
    internal const int Width = 32;

    /// <summary>The message of the exception <c>ForCpu</c> and <c>ForSplit</c> throw for a shader that uses SIMD-group operations.</summary>
    internal const string UnavailableMessage =
        "SIMD-group operations on the CPU arrive with the group executor: this shader uses the wave or quad operations of Gpu " +
        "(or SimdGroupIds), which need the threads of a SIMD group to run side by side. Run it on the GPU (For, ForEach, ForAsync).";

    /// <summary>What a CPU dispatch throws for shader type <paramref name="shaderType"/>.</summary>
    internal static NotSupportedException Unavailable(Type shaderType) =>
        new($"Shader '{shaderType}' cannot run on the CPU yet. {UnavailableMessage}");

    /// <summary><c>thread_index_in_simdgroup</c>.</summary>
    internal static int LaneIndex() => throw Unavailable();

    /// <summary><c>threads_per_simdgroup</c>.</summary>
    internal static int LaneCount() => throw Unavailable();

    /// <summary><c>simdgroup_index_in_threadgroup</c>.</summary>
    internal static int GroupIndex() => throw Unavailable();

    /// <summary><c>simdgroups_per_threadgroup</c>.</summary>
    internal static int GroupCount() => throw Unavailable();

    // The parameters below are what the executor will receive: the operation and the line of the call
    // (together the call site the lanes must agree on) and what this lane brings. They are final, so
    // that the hundreds of generated overloads of Gpu that call them need not change.

    /// <summary><see cref="SimdOp.IsFirstLane"/>, <see cref="SimdOp.AnyTrue"/> and <see cref="SimdOp.AllTrue"/>.</summary>
    internal static bool Vote(SimdOp op, bool value, int callSite) => throw Unavailable();

    /// <summary><see cref="SimdOp.Ballot"/>: bit <c>i</c> is lane <c>i</c>'s <paramref name="value"/>.</summary>
    internal static uint Ballot(bool value, int callSite) => throw Unavailable();

    /// <summary><see cref="SimdOp.CountBits"/> and <see cref="SimdOp.PrefixCountBits"/>.</summary>
    internal static int Count(SimdOp op, bool value, int callSite) => throw Unavailable();

    /// <summary>
    /// The reductions and scans: the result is computed componentwise from the
    /// <typeparamref name="T"/> of every participating lane. <typeparamref name="T"/> is a
    /// <paramref name="scalar"/> or a vector of 2 to 4 of them, which is all the executor needs to know
    /// to do the arithmetic without a method per type.
    /// </summary>
    internal static T Reduce<T>(SimdOp op, SimdScalar scalar, T value, int callSite)
        where T : unmanaged => throw Unavailable();

    /// <summary>
    /// The exchanges: the result is the <typeparamref name="T"/> another lane brought, chosen by
    /// <paramref name="argument"/> (a lane, a delta or a mask; unused by some operations).
    /// </summary>
    internal static T Exchange<T>(SimdOp op, T value, int argument, int callSite)
        where T : unmanaged => throw Unavailable();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception Unavailable()
    {
        // Outside a CPU dispatch this is the usual "only inside a shader" error.
        _ = CpuThread.Current;
        return new NotSupportedException(UnavailableMessage);
    }
}
