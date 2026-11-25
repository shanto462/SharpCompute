namespace SharpCompute.SourceGenerators;

/// <summary>The cached output of the pipeline for one shader struct: a model to emit (if any) plus diagnostics.</summary>
internal sealed record ShaderResult(ShaderModel? Model, EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>
/// Everything the emitter needs to write the descriptor partial. Contains only strings and
/// primitives (no symbols or syntax) so that it is cheap to compare between generator runs.
/// <see cref="GridWindowIndex"/> is the buffer index of the hidden grid window (the first index after
/// the buffers and constants), or -1 when every index is taken and the shader cannot run a partial grid.
/// <see cref="UsesAtomics"/> is true when the kernel or a helper it reaches contains an atomic operation,
/// and <see cref="UsesDeviceAtomics"/> when one of them is on a buffer element rather than on threadgroup
/// memory (only those keep a CPU+GPU split from dividing the shader).
/// <see cref="UsesThreadgroupMemory"/> is true when the kernel uses group-shared memory or a barrier (its
/// threads then cooperate, which the CPU has to run differently), and <see cref="ThreadgroupMemoryLength"/>
/// is the bytes of group-shared memory it declares.
/// <see cref="CompileOptions"/> holds the shader's <c>[ShaderOptions]</c> as <c>ShaderCompileOptions</c> bits.
/// <see cref="UsesSimdGroups"/> is true when the kernel uses a SIMD-group operation or built-in, whose
/// results the lanes of a SIMD group compute together (the CPU has to run such a shader differently).
/// <see cref="ExplicitConstantsSize"/> is the size in bytes of the constants struct when it holds a
/// 64-bit integer and its C# mirror is laid out explicitly, and 0 for the usual sequential
/// <c>Pack = 4</c> mirror.
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
    bool UsesAtomics,
    bool UsesDeviceAtomics,
    bool UsesThreadgroupMemory,
    int ThreadgroupMemoryLength,
    int CompileOptions,
    bool UsesSimdGroups,
    int ExplicitConstantsSize)
{
    /// <summary><c>ShaderCompileOptions.FastMath</c>.</summary>
    public const int FastMath = 1;

    /// <summary><c>ShaderCompileOptions.NoFpContract</c>.</summary>
    public const int NoFpContract = 2;
}

/// <summary>One level of the type hierarchy to re-open, e.g. <c>partial class Outer</c>.</summary>
internal sealed record TypeDeclarationInfo(string Keyword, string Name);

/// <summary>
/// A buffer bound with <c>writer.SetBuffer(Index, Access)</c>, or a texture bound with
/// <c>writer.SetTexture(Index, Access)</c>. <see cref="DisplayName"/> (<c>Shader.field</c>) names it in
/// the exception thrown when it is null, e.g. for <c>default(Shader)</c>.
/// </summary>
internal sealed record BufferArgument(int Index, string Access, string DisplayName);

/// <summary>
/// A field of the C# constants mirror struct, assigned from <see cref="Value"/>. <see cref="Offset"/>
/// is its byte offset, which the MSL asserts and an explicitly laid out mirror states.
/// </summary>
internal sealed record ConstantArgument(string TypeName, string FieldName, string Value, int Offset);
