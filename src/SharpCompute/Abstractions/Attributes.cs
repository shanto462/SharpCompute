using System;

namespace SharpCompute;

/// <summary>Opts a shader struct into descriptor generation (C# → MSL translation).</summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedComputeShaderDescriptorAttribute : Attribute;

/// <summary>Threads per threadgroup. When omitted, the runtime picks a size per dispatch rank.</summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ThreadGroupSizeAttribute(int x, int y = 1, int z = 1) : Attribute
{
    public int X { get; } = x;

    public int Y { get; } = y;

    public int Z { get; } = z;
}

/// <summary>
/// Marks a <c>static readonly</c> array field of a shader struct as threadgroup shared memory: one
/// array per threadgroup, read and written by every thread of the group (HLSL's <c>groupshared</c>,
/// <c>threadgroup T name[N];</c> in the generated MSL).
/// </summary>
/// <remarks>
/// <para>
/// Declare it as <c>[GroupShared] private static readonly float[] tile = new float[256];</c> in the
/// shader struct that uses it. The element type is anything a buffer can hold (scalars, packed vectors,
/// user structs) and the length must be a compile-time constant. In <c>Execute</c> and its helpers only
/// the elements (<c>tile[i]</c>) and <c>tile.Length</c> can be used. All the group-shared arrays a
/// shader uses may total at most 32,768 bytes, the threadgroup memory of an Apple GPU.
/// </para>
/// <para>
/// Give the shader an explicit <see cref="ThreadGroupSizeAttribute"/>: an array is sized for the
/// threads that share it, and without the attribute the runtime picks the threadgroup size per
/// dispatch (256, 16 × 16 or 8 × 8 × 4 by rank, smaller if the kernel does not allow that many).
/// </para>
/// <para>
/// <b>The contents are undefined when a threadgroup starts</b>, on the GPU (Metal does not clear
/// threadgroup memory) and on the CPU (where a group sees what the previous group left behind): a
/// thread may only read elements that a thread of its own group has written, with a barrier
/// (<see cref="Gpu.GroupMemoryBarrierWithGroupSync"/>) between the write and the read.
/// </para>
/// <para>
/// On the CPU (<c>GraphicsDevice.ForCpu</c>, <c>ForSplit</c>) the field is the storage itself: the
/// threads of one threadgroup run concurrently on dedicated threads and the groups run one after
/// another, so the one static array serves each group in turn. Do not touch the array from outside
/// the shader, and do not share one array between two shader structs.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class GroupSharedAttribute : Attribute;

/// <summary>
/// Maps a C# vector struct to an MSL type. <see cref="Packed"/> is used wherever the value crosses
/// the CPU/GPU boundary (buffer elements, constants, struct fields). Properties map to lowercase
/// swizzles, constructors to <c>name(...)</c>, operators to native MSL operators.
/// </summary>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class MslTypeAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string? Packed { get; init; }
}

/// <summary>
/// Maps a static two-argument method to an MSL binary operator, e.g. <c>Gpu.Mul</c> →
/// <c>[MslBinaryOperator("*", SwapOperands = true)]</c> emits <c>(arg1 * arg0)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MslBinaryOperatorAttribute(string op) : Attribute
{
    public string Operator { get; } = op;

    public bool SwapOperands { get; init; }
}

/// <summary>Maps a static method to an MSL function, e.g. <c>[MslIntrinsic("metal::sqrt")]</c>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MslIntrinsicAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Maps a static method to an MSL atomic function. The first parameter is the location
/// (<c>ref T</c>); the generator requires it to be an element of a captured
/// <see cref="ReadWriteBuffer{T}"/> (<c>ref buffer[i]</c>) and emits
/// <c>name((device metal::atomic_int*)&amp;(buffer[i]), args...)</c>, with <c>metal::atomic_uint</c>
/// for <c>uint</c> and <c>metal::atomic&lt;float&gt;</c> for <c>float</c> elements. Anything else as
/// the location is diagnostic SCG0034.
/// </summary>
/// <remarks>
/// A function whose name ends in <c>_explicit</c> takes a memory order (the C and MSL naming
/// convention), so <c>metal::memory_order_relaxed</c> is appended for it: the only order MSL has.
/// The address space (<c>device</c>) comes from where the location lives, not from this attribute,
/// which is what lets other atomic storage (threadgroup memory) be added without a second set of
/// methods.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MslAtomicAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
