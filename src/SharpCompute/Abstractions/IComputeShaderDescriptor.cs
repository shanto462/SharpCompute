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
    /// call, or any other <see cref="MslAtomicAttribute"/> method). Such a shader lets many threads
    /// update one element, which is only safe among the threads of one dispatch on one device: the
    /// GPU's atomics and the CPU's interlocked operations do not exclude each other on shared memory.
    /// <c>GraphicsDevice.ForSplit</c> therefore never divides it between the CPU and the GPU. The
    /// default is false: hand-written descriptors and generated shaders without atomics.
    /// </summary>
    static virtual bool UsesAtomics => false;

    /// <summary>
    /// Binds the shader's captured state. The binding order is generator-internal; the runtime
    /// assumes nothing beyond the writer calls made here.
    /// </summary>
    static abstract void SetArguments(in TSelf shader, ref ShaderArgumentWriter writer);
}
