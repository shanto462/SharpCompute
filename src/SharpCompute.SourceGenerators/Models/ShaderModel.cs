namespace SharpCompute.SourceGenerators;

/// <summary>The cached output of the pipeline for one shader struct: a model to emit (if any) plus diagnostics.</summary>
internal sealed record ShaderResult(ShaderModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// Everything the emitter needs to write the descriptor partial. Contains only strings and
/// primitives (no symbols or syntax) so that it is cheap to compare between generator runs.
/// <see cref="GridWindowIndex"/> is the buffer index of the hidden grid window (the first index after
/// the buffers and constants), or -1 when every index is taken and the shader cannot run a partial grid.
/// <see cref="UsesAtomics"/> is true when the kernel or a helper it reaches contains an atomic operation.
/// </summary>
internal sealed record ShaderModel(
    string HintName,
    string? Namespace,
    EquatableArray<TypeDeclarationInfo> Types,
    string FullyQualifiedName,
    bool IsStub,
    string EntryPoint,
    string MslSource,
    int ThreadGroupSizeX,
    int ThreadGroupSizeY,
    int ThreadGroupSizeZ,
    EquatableArray<BufferArgument> Buffers,
    EquatableArray<BufferArgument> Textures,
    EquatableArray<ConstantArgument> Constants,
    int ConstantsIndex,
    bool UsesInstanceAccess,
    int GridWindowIndex,
    bool UsesAtomics);

/// <summary>One level of the type hierarchy to re-open, e.g. <c>partial class Outer</c>.</summary>
internal sealed record TypeDeclarationInfo(string Keyword, string Name);

/// <summary>
/// A buffer bound with <c>writer.SetBuffer(Index, Access)</c>, or a texture bound with
/// <c>writer.SetTexture(Index, Access)</c>. <see cref="DisplayName"/> (<c>Shader.field</c>) names it in
/// the exception thrown when it is null, e.g. for <c>default(Shader)</c>.
/// </summary>
internal sealed record BufferArgument(int Index, string Access, string DisplayName);

/// <summary>A field of the C# constants mirror struct, assigned from <see cref="Value"/>.</summary>
internal sealed record ConstantArgument(string TypeName, string FieldName, string Value);
