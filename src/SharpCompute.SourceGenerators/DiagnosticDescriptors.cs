using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators;

/// <summary>All diagnostics reported by the SharpCompute shader generator.</summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "SharpCompute";

    public static readonly DiagnosticDescriptor ShaderMustBePartial = new(
        id: "SCG0001",
        title: "Shader struct must be partial",
        messageFormat: "Shader struct '{0}' must be declared 'partial' so that SharpCompute can generate its descriptor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ContainingTypeMustBePartial = new(
        id: "SCG0002",
        title: "Type containing a shader struct must be partial",
        messageFormat: "Type '{0}' contains shader struct '{1}' and must be declared 'partial'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingComputeShaderInterface = new(
        id: "SCG0003",
        title: "Shader struct must implement IComputeShader or IComputeShader<TPixel>",
        messageFormat: "Shader struct '{0}' is marked [GeneratedComputeShaderDescriptor] but implements neither SharpCompute.IComputeShader nor SharpCompute.IComputeShader<TPixel>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ShaderShouldBeReadOnly = new(
        id: "SCG0004",
        title: "Shader struct should be readonly",
        messageFormat: "Shader struct '{0}' should be declared 'readonly': captured state cannot be modified on the GPU",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedShaderDeclaration = new(
        id: "SCG0005",
        title: "Unsupported shader struct declaration",
        messageFormat: "Shader struct '{0}' cannot be {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedCaptureType = new(
        id: "SCG0006",
        title: "Unsupported captured field or parameter type",
        messageFormat: "{0} '{1}' of type '{2}' cannot be captured by a compute shader: {3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "SCG0007",
        title: "Unsupported type in shader code",
        messageFormat: "Type '{0}' is not supported in a compute shader: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PrimaryConstructorParameterStoredInField = new(
        id: "SCG0008",
        title: "Primary constructor parameter is also stored in a field",
        messageFormat: "Primary constructor parameter '{0}' is also stored in {1}; capture it either as the parameter or as a field, not both",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidStruct = new(
        id: "SCG0009",
        title: "Struct cannot be used in a shader",
        messageFormat: "Struct '{0}' cannot be used in a compute shader: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BufferInsideStruct = new(
        id: "SCG0010",
        title: "Buffer nested inside a struct",
        messageFormat: "Struct '{0}' has a buffer field '{1}'; buffers must be captured directly by the shader struct",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TooManyBoundArguments = new(
        id: "SCG0011",
        title: "Too many bound arguments",
        messageFormat: "Shader struct '{0}' binds {1} arguments, but Metal supports at most {2} (buffers plus one constants block)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedSyntax = new(
        id: "SCG0012",
        title: "Unsupported construct in shader code",
        messageFormat: "Unsupported construct in a compute shader: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InstanceHelperMethod = new(
        id: "SCG0013",
        title: "Instance method called on another instance",
        messageFormat: "Method '{0}' is an instance method called on a value other than the shader itself; instance helper methods can only be called on 'this'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RecursiveHelperMethod = new(
        id: "SCG0014",
        title: "Recursive helper method",
        messageFormat: "Method '{0}' is recursive ({1}); recursion is not supported in Metal Shading Language",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidThreadGroupSize = new(
        id: "SCG0015",
        title: "Invalid thread group size",
        messageFormat: "[ThreadGroupSize({0}, {1}, {2})] is invalid: {3}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMethodCall = new(
        id: "SCG0016",
        title: "Method cannot be called from a shader",
        messageFormat: "Method '{0}' cannot be called from a compute shader: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMember = new(
        id: "SCG0017",
        title: "Member cannot be used in a shader",
        messageFormat: "Member '{0}' cannot be used in a compute shader: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CapturedStateIsReadOnly = new(
        id: "SCG0018",
        title: "Captured shader state is read-only",
        messageFormat: "Cannot modify '{0}': captured shader state is read-only on the GPU (write to a ReadWriteBuffer<T> or a local instead)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BufferLengthUnavailable = new(
        id: "SCG0019",
        title: "Buffer length is unavailable",
        messageFormat: "'Length' is only available on buffers captured directly by the shader struct, not on '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingExecuteMethod = new(
        id: "SCG0020",
        title: "Execute method is missing",
        messageFormat: "Shader struct '{0}' must declare 'Execute()' with a body",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsequencedModification = new(
        id: "SCG0021",
        title: "Expression depends on C# evaluation order",
        messageFormat: "'{0}' is modified and used again within one expression; C# evaluates it left to right, but Metal Shading Language leaves the order undefined. Split the expression into separate statements.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConflictingShaderInterfaces = new(
        id: "SCG0022",
        title: "Shader implements both shader interfaces",
        messageFormat: "Shader struct '{0}' implements both IComputeShader and IComputeShader<{1}>; implement exactly one of them",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedPixelType = new(
        id: "SCG0023",
        title: "Unsupported pixel type",
        messageFormat: "Shader struct '{0}' implements IComputeShader<{1}>, but pixel shaders must return Float4 (an RGBA color written to the output texture)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonConstantStaticInitializer = new(
        id: "SCG0024",
        title: "Static field cannot be lowered to an MSL constant",
        messageFormat: "Static field '{0}' cannot be lowered to an MSL program-scope constant: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CapturingLocalFunction = new(
        id: "SCG0025",
        title: "Local function captures a local or parameter",
        messageFormat: "Local function '{0}' captures '{1}' from its enclosing method; pass it as a parameter instead (a local function becomes a separate MSL function)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StaticFieldAssignedInStaticConstructor = new(
        id: "SCG0026",
        title: "Static field is assigned in a static constructor",
        messageFormat: "Static field '{0}' is assigned in a static constructor, which is not translated to MSL: the shader would read its initializer's value instead. Give the field its final value in its initializer.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InternalError = new(
        id: "SCG9999",
        title: "Internal generator error",
        messageFormat: "SharpCompute failed to generate a descriptor for '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
