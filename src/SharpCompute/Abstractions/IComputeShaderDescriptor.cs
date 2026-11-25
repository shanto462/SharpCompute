using System.ComponentModel;

namespace SharpCompute;

/// <summary>
/// Implemented by the source generator for every <see cref="IComputeShader"/> or
/// <see cref="IComputeShader{TPixel}"/> marked with <see cref="GeneratedComputeShaderDescriptorAttribute"/>. This is the ABI between the generator
/// and the runtime: buffers bind at <c>[[buffer(0..n-1)]]</c>, then one packed constants struct;
/// textures bind at <c>[[texture(0..m-1)]]</c>, or from 1 in a pixel shader, whose output the runtime
/// binds at texture 0.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IComputeShaderDescriptor<TSelf>
    where TSelf : struct, IComputeShaderDescriptor<TSelf>
{
    /// <summary>The name of the <c>kernel</c> function in <see cref="MslSource"/>.</summary>
    static abstract string EntryPoint { get; }

    /// <summary>A complete MSL translation unit containing the kernel.</summary>
    static abstract string MslSource { get; }

    /// <summary>
    /// Threads per threadgroup from <see cref="ThreadGroupSizeAttribute"/>, or 0 for all three
    /// when the attribute is omitted (the runtime then picks a size per dispatch rank).
    /// </summary>
    static abstract int ThreadGroupSizeX { get; }

    static abstract int ThreadGroupSizeY { get; }

    static abstract int ThreadGroupSizeZ { get; }

    /// <summary>
    /// The buffer index at which a CPU+GPU split dispatch (<c>GraphicsDevice.ForSplit</c>) binds the
    /// hidden grid window: the offset of the GPU's part, the full grid size and the threadgroup offset
    /// (<c>__GridWindow</c> in the MSL). The kernel's windowed specialization (function constant 0 =
    /// true) rebases its built-ins with it. -1, the default, means the shader cannot run a partial
    /// grid: hand-written descriptors, and generated shaders that use all 31 buffer indices.
    /// </summary>
    static virtual int GridWindowIndex => -1;

    /// <summary>
    /// True when the kernel, or a helper it calls, contains an atomic operation (a <c>Gpu.Atomic*</c>
    /// call, or any other <see cref="MslAtomicAttribute"/> method), on a buffer element or on an
    /// element of threadgroup memory (<see cref="GroupSharedAttribute"/>). Whether that keeps the shader
    /// on one device is said by <see cref="UsesDeviceAtomics"/>. The default is false: hand-written
    /// descriptors and generated shaders without atomics.
    /// </summary>
    static virtual bool UsesAtomics => false;

    /// <summary>
    /// True when one of those atomic operations is on a buffer element (<c>device</c> memory). Such a
    /// shader lets many threads of the dispatch update one element, which is only safe among the
    /// threads of one dispatch on one device: the GPU's atomics and the CPU's interlocked operations
    /// do not exclude each other on shared memory. <c>GraphicsDevice.ForSplit</c> therefore never
    /// divides it between the CPU and the GPU. An atomic on threadgroup memory is shared only by the
    /// threads of one threadgroup, and a split never divides a threadgroup, so a shader whose atomics
    /// are all of that kind reports false here and can be split.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="UsesAtomics"/>: a descriptor that only says it uses atomics is taken
    /// to use them on buffers, the case that must not be divided.
    /// </remarks>
    static virtual bool UsesDeviceAtomics => TSelf.UsesAtomics;

    /// <summary>
    /// True when the kernel declares threadgroup memory (<see cref="GroupSharedAttribute"/>) or calls a
    /// threadgroup barrier (<see cref="Gpu.GroupMemoryBarrierWithGroupSync"/> and its siblings). The
    /// threads of a threadgroup of such a shader cooperate, so the CPU (<c>GraphicsDevice.ForCpu</c>,
    /// <c>ForSplit</c>) runs each group's threads concurrently on dedicated threads, one group after
    /// another, instead of spreading independent threads over the thread pool. False, the default,
    /// for hand-written descriptors and for generated shaders that use neither.
    /// </summary>
    static virtual bool UsesThreadgroupMemory => false;

    /// <summary>
    /// The bytes of threadgroup memory the kernel declares (the total of its <see cref="GroupSharedAttribute"/>
    /// arrays), which a dispatch checks against the device's <c>maxThreadgroupMemoryLength</c> before the
    /// kernel is compiled. 0, the default, when it declares none.
    /// </summary>
    static virtual int ThreadgroupMemoryLength => 0;

    /// <summary>
    /// How the kernel is compiled, from the shader's <see cref="ShaderOptionsAttribute"/>. The runtime
    /// maps <see cref="ShaderCompileOptions.FastMath"/> to <c>MTLCompileOptions.mathMode = Fast</c> with
    /// fast math functions, and <see cref="ShaderCompileOptions.NoFpContract"/> to
    /// <c>#pragma clang fp contract(off)</c> ahead of <see cref="MslSource"/>, which therefore never
    /// contains the pragma itself. The default is <see cref="ShaderCompileOptions.None"/>:
    /// hand-written descriptors and generated shaders without the attribute.
    /// </summary>
    static virtual ShaderCompileOptions CompileOptions => ShaderCompileOptions.None;

    /// <summary>
    /// True when the kernel uses a SIMD-group operation (the <c>Wave*</c> and <c>Quad*</c> methods of
    /// <see cref="Gpu"/>) or reads <see cref="SimdGroupIds"/>. The lanes of a SIMD group of such a shader
    /// compute their results together, so the CPU (<c>GraphicsDevice.ForCpu</c>, <c>ForIndirectCpu</c>, <c>ForSplit</c>) runs
    /// each lane on a dedicated thread and lets the lanes of a group meet at every operation: one SIMD
    /// group of 32 at a time, or whole threadgroups when the shader also reports
    /// <see cref="UsesThreadgroupMemory"/>. A GPU dispatch checks that the compiled kernel's SIMD groups
    /// are at most 32 threads wide, which <see cref="Gpu.WaveActiveBallot"/> relies on. False, the
    /// default, for hand-written descriptors and for generated shaders that use none; a shader that
    /// calls an operation without reporting it gets an <see cref="System.InvalidOperationException"/>
    /// from the operation on the CPU.
    /// </summary>
    static virtual bool UsesSimdGroups => false;

    /// <summary>
    /// Binds the shader's captured state. The binding order is generator-internal; the runtime
    /// assumes nothing beyond the writer calls made here.
    /// </summary>
    static abstract void SetArguments(in TSelf shader, ref ShaderArgumentWriter writer);
}
