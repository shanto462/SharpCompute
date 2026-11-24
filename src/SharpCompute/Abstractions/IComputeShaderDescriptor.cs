using System.ComponentModel;

namespace SharpCompute;

/// <summary>
/// Implemented by the source generator for every <see cref="IComputeShader"/> or
/// <see cref="IComputeShader{TPixel}"/> marked with <see cref="GeneratedComputeShaderDescriptorAttribute"/>. This is the ABI between the generator
/// and the runtime: buffers bind at <c>[[buffer(0..n-1)]]</c>, then one packed constants struct.
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
    /// Binds the shader's captured state. The binding order is generator-internal; the runtime
    /// assumes nothing beyond the writer calls made here.
    /// </summary>
    static abstract void SetArguments(in TSelf shader, ref ShaderArgumentWriter writer);
}
