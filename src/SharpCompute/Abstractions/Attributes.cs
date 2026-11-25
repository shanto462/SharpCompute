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
/// <c>Float4x4</c>, user structs) and the length must be a compile-time constant. In <c>Execute</c> and its helpers only
/// the elements (<c>tile[i]</c>) and <c>tile.Length</c> can be used. All the group-shared arrays a
/// shader uses may total at most 32,768 bytes, the threadgroup memory of an Apple GPU.
/// </para>
/// <para>
/// Give the shader an explicit <see cref="ThreadGroupSizeAttribute"/>: an array is sized for the
/// threads that share it, and without the attribute the runtime picks the threadgroup size per
/// dispatch (256, 16 × 16 or 8 × 8 × 4 by rank, smaller if the kernel does not allow that many), so
/// an array that is too short for the size it picks is indexed out of bounds, silently, on the GPU.
/// The generator warns (SCG0044) about every array a shader without the attribute uses, unless the
/// array has at least 1,024 elements, one for each thread of the largest threadgroup there is.
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
/// Compile options for one shader. Without the attribute a shader is compiled the way every
/// SharpCompute shader is: IEEE-safe math, precise math functions and Metal's default floating-point
/// contraction.
/// </summary>
/// <example>
/// <code>
/// [GeneratedComputeShaderDescriptor]
/// [ShaderOptions(FastMath = true)]
/// public readonly partial struct Glow(ReadWriteBuffer&lt;float&gt; data) : IComputeShader { ... }
/// </code>
/// </example>
/// <remarks>
/// The options are part of the shader type, so they apply to every dispatch of it, on every device,
/// and the compiled pipeline is cached per shader type and options. They only change how the GPU
/// compiles the kernel: a CPU dispatch (<c>ForCpu</c>) runs the same C# either way.
/// </remarks>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ShaderOptionsAttribute : Attribute
{
    /// <summary>
    /// Compiles the kernel with <c>MTLMathModeFast</c> and fast math functions (Metal's own defaults,
    /// which SharpCompute otherwise turns off). The compiler may then assume that no value is NaN or
    /// infinite, reorder and reassociate arithmetic, and use faster, less accurate
    /// <c>sin</c>/<c>exp</c>/<c>pow</c>. Default false.
    /// </summary>
    /// <remarks>
    /// Use it for kernels whose results are looked at rather than compared (a visual effect, a
    /// heuristic). Do not use it when the kernel relies on NaN or infinity propagating, tests for them,
    /// or must match the CPU closely: a comparison such as <c>x != x</c> is compiled to <c>false</c>,
    /// and Metal makes no promise for <c>Gpu.IsNaN</c>, <c>Gpu.IsInfinity</c> and <c>Gpu.IsFinite</c>
    /// either (an Apple M4 Pro was measured to still answer <c>Gpu.IsNaN</c> correctly).
    /// </remarks>
    public bool FastMath { get; init; }

    /// <summary>
    /// False compiles the kernel with floating-point contraction off
    /// (<c>#pragma clang fp contract(off)</c>): <c>a * b + c</c> is then a rounded multiply followed by a
    /// rounded add, as it is in C#, instead of one fused multiply-add. Default true, Metal's default,
    /// which lets the compiler fuse them.
    /// </summary>
    /// <remarks>
    /// With contraction off, arithmetic that uses only <c>+ - * /</c> and <c>Gpu.Sqrt</c> gives the
    /// same bits on the GPU as in C# on the CPU, at the cost of the fused operations. Two things it
    /// does not change: math functions (<c>Gpu.Sin</c> and so on) still differ by a few ULP, and the
    /// GPU still flushes subnormal results to zero. <c>Gpu.Fma</c> is always fused.
    /// </remarks>
    public bool FpContract { get; init; } = true;
}

/// <summary>
/// The compile options of a shader as its descriptor reports them
/// (<see cref="IComputeShaderDescriptor{TSelf}.CompileOptions"/>); set with
/// <see cref="ShaderOptionsAttribute"/>.
/// </summary>
[Flags]
public enum ShaderCompileOptions
{
    /// <summary>The defaults: IEEE-safe math, precise math functions, contraction allowed.</summary>
    None = 0,

    /// <summary><see cref="ShaderOptionsAttribute.FastMath"/> is true.</summary>
    FastMath = 1,

    /// <summary><see cref="ShaderOptionsAttribute.FpContract"/> is false.</summary>
    NoFpContract = 2,
}

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

    /// <summary>
    /// True when the MSL function takes the arguments in the opposite order, last parameter first:
    /// <c>Gpu.Select(mask, whenTrue, whenFalse)</c> keeps HLSL's order and maps to
    /// <c>metal::select(whenFalse, whenTrue, mask)</c>. The arguments are still evaluated once each;
    /// MSL does not define the order in which a call evaluates them either way. Default false.
    /// </summary>
    public bool ReverseArguments { get; init; }
}

/// <summary>
/// Maps a static method to an MSL atomic function. The first parameter is the location
/// (<c>ref T</c>); the generator requires it to be an element of a captured
/// <see cref="ReadWriteBuffer{T}"/> (<c>ref buffer[i]</c>) or of a <see cref="GroupSharedAttribute"/>
/// array (<c>ref tile[i]</c>) and emits <c>name((device metal::atomic_int*)&amp;(buffer[i]), args...)</c>,
/// or <c>name((threadgroup metal::atomic_int*)&amp;(tile[i]), args...)</c>, with <c>metal::atomic_uint</c>
/// for <c>uint</c> and <c>metal::atomic&lt;float&gt;</c> for <c>float</c> elements of a buffer. A
/// <c>float</c> element of threadgroup memory needs <see cref="ThreadgroupFloat"/>. A <c>ulong</c>
/// element of a buffer is viewed as <c>metal::atomic_ulong</c>, for which MSL 3.2 only has
/// <c>atomic_min_explicit</c> and <c>atomic_max_explicit</c>. Anything else as the location is
/// diagnostic SCG0034, or SCG0047 when it is the element type that has no atomic.
/// </summary>
/// <remarks>
/// A function whose name ends in <c>_explicit</c> takes a memory order (the C and MSL naming
/// convention), so <c>metal::memory_order_relaxed</c> is appended for it: the only order MSL has.
/// The address space (<c>device</c> or <c>threadgroup</c>) comes from where the location lives, not
/// from this attribute, which is why one set of methods serves both kinds of atomic storage. A
/// prelude function (<c>__ms_*</c>) named here needs an overload per address space.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MslAtomicAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>
    /// The function to call instead of <see cref="Name"/> when the location is a <c>float</c> element
    /// of a <see cref="GroupSharedAttribute"/> array, or null (the default) when the method cannot be
    /// used there. MSL 3.2 has no atomic float in the <c>threadgroup</c> address space, so the
    /// function receives the element as <c>threadgroup metal::atomic_uint*</c>, followed by the other
    /// arguments, and works on the float's bits: a prelude helper such as
    /// <c>__ms_atomic_fetch_add_float</c>, a compare-and-swap loop. No memory order is appended for a
    /// name that does not end in <c>_explicit</c>.
    /// </summary>
    public string? ThreadgroupFloat { get; init; }
}
