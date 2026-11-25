namespace SharpCompute.Build;

/// <summary>
/// One shader of an assembly, as the source generator recorded it: what is needed to compile its
/// kernel ahead of time. <see cref="TypeName"/> only names the shader in messages.
/// </summary>
internal sealed record ShaderRecord(string TypeName, string EntryPoint, int CompileOptions, string MslSource)
{
    /// <summary>The name the kernel gets in the precompiled library, which the runtime looks it up by.</summary>
    public string FunctionName { get; } = PrecompiledShaderContract.FunctionName(EntryPoint, MslSource, CompileOptions);

    /// <summary>The text handed to the compiler: the same text the runtime compiles for this shader.</summary>
    public string CompiledSource => PrecompiledShaderContract.CompiledSource(MslSource, CompileOptions);
}
