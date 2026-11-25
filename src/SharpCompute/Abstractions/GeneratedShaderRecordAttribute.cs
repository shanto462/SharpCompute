using System;
using System.ComponentModel;

namespace SharpCompute;

/// <summary>
/// The source generator's record of a shader it translated, put on the shader struct next to the
/// descriptor it implements: the kernel's name and how the kernel is compiled.
/// </summary>
/// <remarks>
/// <para>
/// It exists for build tools. A source generator can neither run a compiler nor write a file, so the
/// step that precompiles an assembly's shaders into a <c>.metallib</c> runs after the build and finds
/// the shaders in the assembly's metadata, without loading or running the assembly: every struct that
/// carries this attribute is a shader, the attribute says what its kernel is called and with which
/// options it is compiled, and the MSL is the string literal that the struct's
/// <see cref="IComputeShaderDescriptor{TSelf}.MslSource"/> returns. The source is deliberately not
/// repeated here, which would store every shader's MSL in the assembly a second time.
/// </para>
/// <para>
/// A shader with errors gets no record, and neither does a hand-written descriptor: those are always
/// compiled from source at run time.
/// </para>
/// </remarks>
/// <param name="entryPoint">The same value as <see cref="IComputeShaderDescriptor{TSelf}.EntryPoint"/>.</param>
/// <param name="compileOptions">The same value as <see cref="IComputeShaderDescriptor{TSelf}.CompileOptions"/>.</param>
[AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedShaderRecordAttribute(string entryPoint, ShaderCompileOptions compileOptions) : Attribute
{
    /// <summary>The name of the <c>kernel</c> function in the shader's MSL.</summary>
    public string EntryPoint { get; } = entryPoint;

    /// <summary>The shader's <see cref="ShaderOptionsAttribute"/>, as its descriptor reports them.</summary>
    public ShaderCompileOptions CompileOptions { get; } = compileOptions;
}
