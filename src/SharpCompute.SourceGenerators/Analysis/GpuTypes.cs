using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators;

internal enum GpuTypeKind
{
    Void,
    Bool,
    Scalar,
    Vector,
    Struct,
    Buffer,
    Texture,
}

/// <summary>A field of a user struct (or of a vector type, for layout purposes).</summary>
internal sealed class GpuField
{
    public GpuField(IFieldSymbol symbol, string csharpName, string mslName, GpuType type, int offset, string? declaration = null)
    {
        Symbol = symbol;
        CSharpName = csharpName;
        MslName = mslName;
        Type = type;
        Offset = offset;
        Declaration = declaration ?? type.BoundaryName + " " + mslName;
    }

    public IFieldSymbol Symbol { get; }

    /// <summary>The name used in C# (the property name for auto-property backing fields).</summary>
    public string CSharpName { get; }

    public string MslName { get; }

    public GpuType Type { get; }

    /// <summary>Byte offset in the C# layout.</summary>
    public int Offset { get; }

    /// <summary>
    /// The member declaration in the MSL struct, without the <c>;</c>: the boundary type and the name,
    /// or for the element field of an <c>[InlineArray]</c> struct, an MSL array (<c>float e[8]</c>).
    /// </summary>
    public string Declaration { get; }
}

/// <summary>A C# type that has an MSL equivalent, with the layout it has on the C# side.</summary>
internal sealed class GpuType
{
    public GpuType(GpuTypeKind kind, string mslName, string boundaryName, string csharpName, int size, int alignment)
    {
        Kind = kind;
        MslName = mslName;
        BoundaryName = boundaryName;
        CSharpName = csharpName;
        Size = size;
        Alignment = alignment;
    }

    public GpuTypeKind Kind { get; }

    /// <summary>The MSL type used for locals, parameters, and return values.</summary>
    public string MslName { get; }

    /// <summary>The MSL type used where the value crosses the CPU/GPU boundary (<c>packed_*</c> for vectors).</summary>
    public string BoundaryName { get; }

    /// <summary>The fully qualified C# type name (<c>global::</c>-prefixed).</summary>
    public string CSharpName { get; }

    public int Size { get; }

    public int Alignment { get; }

    public bool IsFloating { get; set; }

    public bool IsUnsigned { get; set; }

    /// <summary>
    /// True when <c>[MslType]</c> names a <c>Packed</c> type. Only that type has the same layout as the
    /// C# struct; without it (matrices, bool vectors) the type is limited to locals and parameters.
    /// </summary>
    public bool HasPackedForm { get; set; }

    /// <summary>
    /// True when the <c>Packed</c> type is declared by the MSL prelude (its name starts with
    /// <c>__ms_</c>) instead of by Metal. Metal's own <c>packed_*</c> vectors convert implicitly to
    /// and from the native type; a prelude type (the packed matrix) is a separate struct, so every
    /// load goes through the prelude's <c>__ms_unpack</c> and every store through <c>__ms_pack</c>.
    /// </summary>
    public bool NeedsExplicitPacking =>
        Kind == GpuTypeKind.Vector && HasPackedForm && BoundaryName.StartsWith(MslNames.EscapePrefix, StringComparison.Ordinal);

    /// <summary>True for vectors of <c>bool</c>, which (like <c>bool</c> itself) may not cross the CPU/GPU boundary.</summary>
    public bool IsBoolean { get; set; }

    public IReadOnlyList<GpuField> Fields { get; set; } = Array.Empty<GpuField>();

    /// <summary>For buffers and <c>[InlineArray]</c> structs: the element type.</summary>
    public GpuType? Element { get; set; }

    /// <summary>
    /// For an <c>[InlineArray(N)]</c> struct: N. Such a struct is an MSL struct whose only member is an
    /// array of N elements (see <see cref="TypeResolver"/>); 0 for every other type.
    /// </summary>
    public int ArrayLength { get; set; }

    /// <summary>True for an <c>[InlineArray]</c> struct.</summary>
    public bool IsInlineArray => ArrayLength > 0;

    /// <summary>
    /// True for an <c>[InlineArray]</c> struct whose elements cannot cross the CPU/GPU boundary
    /// (<c>bool</c>, a matrix without a <c>Packed</c> form): its MSL struct holds the native element
    /// type, which has another layout than C#, so it is limited to locals, parameters and return values.
    /// </summary>
    public bool IsLocalOnly { get; set; }

    public bool IsReadOnlyBuffer { get; set; }

    /// <summary>For textures: true for <c>ReadOnlyTexture2D</c> (<c>access::sample</c>), false for <c>ReadWriteTexture2D</c> (<c>access::read_write</c>).</summary>
    public bool IsReadOnlyTexture { get; set; }

    /// <summary>True for the kinds that are bound as kernel arguments of their own (and forwarded to helpers) rather than stored in the constants block.</summary>
    public bool IsResource => Kind is GpuTypeKind.Buffer or GpuTypeKind.Texture;

    /// <summary>True when values of this type can be stored in buffers, constants, and struct fields.</summary>
    public bool CanCrossBoundary => Kind switch
    {
        GpuTypeKind.Scalar => true,
        GpuTypeKind.Struct => !IsLocalOnly,
        GpuTypeKind.Vector => HasPackedForm && !IsBoolean,
        _ => false,
    };

    /// <summary>True for scalar and vector types, whose operators and conversions map to native MSL.</summary>
    public bool IsNumeric => Kind is GpuTypeKind.Scalar or GpuTypeKind.Vector;

    /// <summary>The MSL type of a kernel or function parameter of this type.</summary>
    public string ParameterTypeName =>
        Kind == GpuTypeKind.Buffer
            ? (IsReadOnlyBuffer ? "const device " : "device ") + Element!.BoundaryName + "*"
            : MslName;

    public GpuField? FindField(ISymbol member)
    {
        foreach (GpuField field in Fields)
        {
            if (SymbolEqualityComparer.Default.Equals(field.Symbol, member) ||
                SymbolEqualityComparer.Default.Equals(field.Symbol.AssociatedSymbol, member))
            {
                return field;
            }
        }

        return null;
    }
}

/// <summary>Why a type could not be mapped to MSL.</summary>
internal sealed class TypeFailure
{
    public TypeFailure(DiagnosticDescriptor descriptor, string reason, params string[] arguments)
    {
        Descriptor = descriptor;
        Reason = reason;
        Arguments = arguments;
    }

    public DiagnosticDescriptor Descriptor { get; }

    /// <summary>A human-readable reason, reused when the failure is reported in another context.</summary>
    public string Reason { get; }

    public string[] Arguments { get; }
}

/// <summary>
/// Maps C# types to MSL types and computes their C# layout. Nothing is keyed on vector type names:
/// vectors are recognized by <c>[MslType]</c>, and their size and alignment come from their fields.
/// </summary>
internal sealed class TypeResolver
{
    private readonly Compilation _compilation;
    private readonly NameAllocator _globalNames;
    private readonly Dictionary<ITypeSymbol, GpuType?> _cache = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, TypeFailure> _failures = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ITypeSymbol> _inProgress = new(SymbolEqualityComparer.Default);
    private readonly List<GpuType> _structs = new();

    public TypeResolver(Compilation compilation, NameAllocator globalNames)
    {
        _compilation = compilation;
        _globalNames = globalNames;
    }

    /// <summary>User structs that were resolved, ordered so that dependencies come first.</summary>
    public IReadOnlyList<GpuType> Structs => _structs;

    /// <summary>Set when an error type was seen: the user's code does not compile, so we stay quiet.</summary>
    public bool SawErrorType { get; private set; }

    /// <summary>
    /// The type arguments of the generic method being translated, by type parameter. While it is set,
    /// a type parameter resolves to its argument, which is how one generic method becomes one MSL
    /// function per instantiation.
    /// </summary>
    public IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol>? TypeArguments { get; set; }

    /// <summary>Replaces the type parameters in <paramref name="type"/> that <see cref="TypeArguments"/> has an argument for.</summary>
    public ITypeSymbol Substitute(ITypeSymbol type)
    {
        if (TypeArguments is not { Count: > 0 } map)
        {
            return type;
        }

        switch (type)
        {
            case ITypeParameterSymbol parameter:
                return map.TryGetValue(parameter, out ITypeSymbol? argument) ? argument : type;

            case INamedTypeSymbol { IsGenericType: true, IsUnboundGenericType: false } named:
                ITypeSymbol[] arguments = named.TypeArguments.Select(Substitute).ToArray();

                // Only when something changed, so that an ordinary type keeps its identity.
                return arguments.Zip(named.TypeArguments, static (a, b) => SymbolEqualityComparer.Default.Equals(a, b)).All(static same => same)
                    ? type
                    : named.ConstructedFrom.Construct(arguments);

            default:
                return type;
        }
    }

    /// <summary>Resolves a type, reporting a diagnostic at <paramref name="usage"/> when it is unsupported.</summary>
    public GpuType? Resolve(ITypeSymbol? type, DiagnosticBag diagnostics, Location? usage)
    {
        GpuType? result = Resolve(type, out TypeFailure? failure);

        if (failure is not null)
        {
            diagnostics.Report(failure.Descriptor, usage, failure.Arguments);
        }

        return result;
    }

    public GpuType? Resolve(ITypeSymbol? type) => Resolve(type, out _);

    public GpuType? Resolve(ITypeSymbol? type, out TypeFailure? failure)
    {
        failure = null;

        if (type is null)
        {
            return null;
        }

        type = Substitute(type);

        if (type.TypeKind == TypeKind.Error)
        {
            SawErrorType = true;
            return null;
        }

        if (!_cache.TryGetValue(type, out GpuType? result))
        {
            if (!_inProgress.Add(type))
            {
                // A struct that contains itself: already a C# error.
                SawErrorType = true;
                return null;
            }

            try
            {
                result = Create(type, out TypeFailure? created);

                if (created is not null)
                {
                    _failures[type] = created;
                }
            }
            finally
            {
                _inProgress.Remove(type);
            }

            _cache[type] = result;
        }

        if (result is null)
        {
            _failures.TryGetValue(type, out failure);
        }

        return result;
    }

    public static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    public static bool IsBufferType(ITypeSymbol? type, out bool isReadOnly)
    {
        isReadOnly = false;

        if (type is INamedTypeSymbol { IsGenericType: true, Arity: 1 } named && IsSharpComputeType(named, "ReadWriteBuffer", "ReadOnlyBuffer"))
        {
            isReadOnly = named.Name == "ReadOnlyBuffer";
            return true;
        }

        return false;
    }

    /// <summary>Recognizes <c>SharpCompute.ReadOnlyTexture2D&lt;T&gt;</c> and <c>SharpCompute.ReadWriteTexture2D&lt;T&gt;</c>.</summary>
    public static bool IsTextureType(ITypeSymbol? type, out bool isReadOnly)
    {
        isReadOnly = false;

        if (type is INamedTypeSymbol { IsGenericType: true, Arity: 1 } named && IsSharpComputeType(named, "ReadOnlyTexture2D", "ReadWriteTexture2D"))
        {
            isReadOnly = named.Name == "ReadOnlyTexture2D";
            return true;
        }

        return false;
    }

    /// <summary>The element types the runtime has a pixel format for (<c>TextureFormats</c>), for messages.</summary>
    public const string TextureElementTypes = "Rgba32, Rgba32Srgb, R8, Rg16, Half, Rgba16Float, float or Float4";

    /// <summary>
    /// True for the eight element types the runtime has a Metal pixel format for. Unlike vectors these
    /// are recognized by name: the set is fixed by the runtime's format table, not by an attribute.
    /// <paramref name="supportsReadWrite"/> is false for the formats an Apple GPU cannot read and write
    /// in one shader, which only <c>ReadOnlyTexture2D</c> accepts.
    /// </summary>
    public static bool IsTextureElementType(ITypeSymbol type, out bool supportsReadWrite)
    {
        supportsReadWrite = true;

        if (type.SpecialType == SpecialType.System_Single || IsHalf(type) || IsSharpComputeType(type, "Rgba32", "R8", "Rgba16Float", "Float4"))
        {
            return true;
        }

        if (IsSharpComputeType(type, "Rgba32Srgb", "Rg16"))
        {
            supportsReadWrite = false;
            return true;
        }

        return false;
    }

    private static bool IsHalf(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Half", TypeKind: TypeKind.Struct, ContainingType: null, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };

    /// <summary>True for types named <paramref name="names"/> declared directly in the <c>SharpCompute</c> namespace.</summary>
    public static bool IsSharpComputeType(ITypeSymbol? type, params string[] names) =>
        type is INamedTypeSymbol { ContainingType: null, ContainingNamespace: { Name: "SharpCompute", ContainingNamespace.IsGlobalNamespace: true } } named &&
        Array.IndexOf(names, named.Name) >= 0;

    public static AttributeData? FindAttribute(ISymbol symbol, string name)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (IsSharpComputeType(attribute.AttributeClass, name))
            {
                return attribute;
            }
        }

        return null;
    }

    private GpuType? Create(ITypeSymbol type, out TypeFailure? failure)
    {
        failure = null;
        string csharpName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        switch (type.SpecialType)
        {
            case SpecialType.System_Void:
                return new GpuType(GpuTypeKind.Void, "void", "void", csharpName, 0, 1);
            case SpecialType.System_Boolean:
                return new GpuType(GpuTypeKind.Bool, "bool", "bool", csharpName, 1, 1);
            case SpecialType.System_Single:
                return new GpuType(GpuTypeKind.Scalar, "float", "float", csharpName, 4, 4) { IsFloating = true };
            case SpecialType.System_Int32:
                return new GpuType(GpuTypeKind.Scalar, "int", "int", csharpName, 4, 4);
            case SpecialType.System_UInt32:
                return new GpuType(GpuTypeKind.Scalar, "uint", "uint", csharpName, 4, 4) { IsUnsigned = true };
        }

        if (IsHalf(type))
        {
            return new GpuType(GpuTypeKind.Scalar, "half", "half", csharpName, 2, 2) { IsFloating = true };
        }

        if (IsTextureType(type, out bool isReadOnlyTexture))
        {
            ITypeSymbol pixel = ((INamedTypeSymbol)type).TypeArguments[0];

            if (pixel.TypeKind == TypeKind.Error)
            {
                SawErrorType = true;
                return null;
            }

            string? problem = null;

            if (!IsTextureElementType(pixel, out bool supportsReadWrite))
            {
                problem = $"'{Display(pixel)}' is not a texture element type; use {TextureElementTypes}";
            }
            else if (!isReadOnlyTexture && !supportsReadWrite)
            {
                problem = $"the GPU cannot read and write '{Display(pixel)}' pixels in one shader; use ReadOnlyTexture2D<{Display(pixel)}>";
            }

            if (problem is not null)
            {
                failure = new TypeFailure(DiagnosticDescriptors.UnsupportedTextureElementType, problem, Display(type), problem);
                return null;
            }

            // Every format is a texture2d<float> in MSL: the pixel type only selects the Metal pixel
            // format, which the runtime does when it allocates the texture.
            string texture = isReadOnlyTexture ? "texture2d<float, access::sample>" : "texture2d<float, access::read_write>";

            return new GpuType(GpuTypeKind.Texture, texture, texture, csharpName, 0, 1) { IsReadOnlyTexture = isReadOnlyTexture };
        }

        if (IsSharpComputeType(type, "Sampler"))
        {
            const string Reason = "a Sampler cannot be stored or passed around in shader code";
            failure = new TypeFailure(DiagnosticDescriptors.InvalidSampler, Reason, "A Sampler cannot be stored or passed around in shader code");
            return null;
        }

        if (IsBufferType(type, out bool isReadOnly))
        {
            ITypeSymbol elementSymbol = ((INamedTypeSymbol)type).TypeArguments[0];
            GpuType? element = Resolve(elementSymbol, out TypeFailure? elementFailure);

            if (IsStructFailure(elementFailure))
            {
                // Keep the specific struct diagnostic instead of wrapping it in a generic one.
                failure = elementFailure;
                return null;
            }

            if (element is null || !element.CanCrossBoundary)
            {
                string reason = elementFailure?.Reason ?? BoundaryReason(elementSymbol);
                failure = new TypeFailure(DiagnosticDescriptors.UnsupportedType, $"its element type '{Display(elementSymbol)}' is not supported ({reason})", Display(type), $"its element type '{Display(elementSymbol)}' is not supported ({reason})");
                return null;
            }

            return new GpuType(GpuTypeKind.Buffer, element.BoundaryName + "*", element.BoundaryName + "*", csharpName, 0, 1)
            {
                Element = element,
                IsReadOnlyBuffer = isReadOnly,
            };
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct, SpecialType: SpecialType.None } named && named.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
        {
            return FindAttribute(named, "MslTypeAttribute") is { } mslType
                ? CreateVector(named, mslType, csharpName, out failure)
                : CreateStruct(named, csharpName, out failure);
        }

        string hint = UnsupportedReason(type);
        failure = new TypeFailure(DiagnosticDescriptors.UnsupportedType, hint, Display(type), hint);
        return null;
    }

    private GpuType? CreateVector(INamedTypeSymbol type, AttributeData attribute, string csharpName, out TypeFailure? failure)
    {
        failure = null;

        if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not string { Length: > 0 } name)
        {
            return Fail(out failure, type, "its [MslType] attribute does not name an MSL type");
        }

        string packed = name;
        bool hasPackedForm = false;

        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == "Packed" && argument.Value.Value is string { Length: > 0 } value)
            {
                packed = value;
                hasPackedForm = true;
            }
        }

        var fields = new List<(IFieldSymbol Symbol, GpuType Type)>();

        foreach (IFieldSymbol field in InstanceFields(type))
        {
            GpuType? fieldType = Resolve(field.Type);

            // `bool` fields make a bool vector (e.g. the result of a component-wise comparison).
            if (fieldType is null || !(fieldType.IsNumeric || fieldType.Kind == GpuTypeKind.Bool))
            {
                return Fail(out failure, type, $"the field '{field.Name}' of this [MslType] struct is not a scalar or vector");
            }

            fields.Add((field, fieldType));
        }

        if (fields.Count == 0)
        {
            return Fail(out failure, type, "this [MslType] struct has no instance fields");
        }

        StructLayoutInfo layout = GetLayout(type);
        ComputeLayout(fields, layout, out int size, out int alignment, out _, out _);

        return new GpuType(GpuTypeKind.Vector, name, packed, csharpName, size, alignment)
        {
            IsFloating = fields[0].Type.IsFloating,
            IsUnsigned = fields[0].Type.IsUnsigned,
            IsBoolean = fields[0].Type.IsBoolean || fields[0].Type.Kind == GpuTypeKind.Bool,
            HasPackedForm = hasPackedForm,
        };
    }

    private GpuType? CreateStruct(INamedTypeSymbol type, string csharpName, out TypeFailure? failure)
    {
        failure = null;

        if (type.TypeKind == TypeKind.Enum || type.EnumUnderlyingType is not null)
        {
            return Fail(out failure, type, "enums are not supported; use an 'int' or 'uint' instead");
        }

        if (type.IsGenericType || type.ContainingType is { IsGenericType: true })
        {
            return Fail(out failure, type, "generic structs are not supported");
        }

        if (type.IsRefLikeType)
        {
            return Fail(out failure, type, "ref structs are not supported");
        }

        if (InlineArrayLength(type) is { } inlineArrayLength)
        {
            return CreateInlineArray(type, csharpName, inlineArrayLength, out failure);
        }

        StructLayoutInfo layout = GetLayout(type);

        if (layout.Kind == LayoutKind.Explicit)
        {
            return Fail(out failure, type, "it uses LayoutKind.Explicit, but only sequential layout can be mirrored in MSL");
        }

        if (layout.Kind == LayoutKind.Auto)
        {
            return Fail(out failure, type, "it uses LayoutKind.Auto, but only sequential layout can be mirrored in MSL");
        }

        if (layout.Size != 0)
        {
            return Fail(out failure, type, "it specifies an explicit StructLayout Size, which cannot be mirrored in MSL");
        }

        var fields = new List<(IFieldSymbol Symbol, GpuType Type)>();

        foreach (IFieldSymbol field in InstanceFields(type))
        {
            string fieldName = field.AssociatedSymbol?.Name ?? field.Name;

            if (field.IsFixedSizeBuffer)
            {
                return Fail(out failure, type, $"its field '{fieldName}' is a fixed-size buffer, which is not supported");
            }

            if (IsBufferType(field.Type, out _))
            {
                failure = new TypeFailure(DiagnosticDescriptors.BufferInsideStruct, $"its field '{fieldName}' is a buffer", Display(type), fieldName);
                return null;
            }

            if (IsTextureType(field.Type, out _))
            {
                failure = new TypeFailure(DiagnosticDescriptors.TextureInsideStruct, $"its field '{fieldName}' is a texture", Display(type), fieldName);
                return null;
            }

            GpuType? fieldType = Resolve(field.Type, out TypeFailure? fieldFailure);

            if (IsStructFailure(fieldFailure))
            {
                // A nested struct is the real culprit: name it rather than the struct that contains it.
                failure = fieldFailure;
                return null;
            }

            if (fieldType is null || !fieldType.CanCrossBoundary)
            {
                string reason = fieldFailure?.Reason ?? BoundaryReason(field.Type);
                return Fail(out failure, type, $"its field '{fieldName}' has the unsupported type '{Display(field.Type)}' ({reason})");
            }

            fields.Add((field, fieldType));
        }

        if (fields.Count == 0)
        {
            return Fail(out failure, type, "it has no instance fields");
        }

        ComputeLayout(fields, layout, out int size, out int alignment, out int naturalAlignment, out int[] offsets);

        if (layout.Pack != 0 && layout.Pack < naturalAlignment)
        {
            return Fail(
                out failure,
                type,
                $"its StructLayout Pack = {layout.Pack.ToString(CultureInfo.InvariantCulture)} is below the natural alignment ({naturalAlignment.ToString(CultureInfo.InvariantCulture)}) of its fields, so its layout cannot be mirrored in MSL");
        }

        string mslName = _globalNames.Allocate(MslNames.Escape(type.Name));
        var gpuFields = new GpuField[fields.Count];
        var memberNames = new NameAllocator();

        for (int i = 0; i < fields.Count; i++)
        {
            IFieldSymbol symbol = fields[i].Symbol;
            string fieldName = symbol.AssociatedSymbol?.Name ?? KeptParameterName(symbol) ?? symbol.Name;
            gpuFields[i] = new GpuField(symbol, fieldName, memberNames.Allocate(MslNames.Escape(fieldName)), fields[i].Type, offsets[i]);
        }

        var result = new GpuType(GpuTypeKind.Struct, mslName, mslName, csharpName, size, alignment) { Fields = gpuFields };
        _structs.Add(result);
        return result;
    }

    /// <summary>The N of <c>[System.Runtime.CompilerServices.InlineArray(N)]</c> on <paramref name="type"/>, or null.</summary>
    public static int? InlineArrayLength(INamedTypeSymbol type)
    {
        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "InlineArrayAttribute", ContainingNamespace: { Name: "CompilerServices", ContainingNamespace: { Name: "Runtime", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } })
            {
                return attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int length ? length : 0;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>[InlineArray(N)] struct Float8 { float _e; }</c> → <c>struct Float8 { float _e[8]; };</c>. The CLR
    /// lays an inline array out as N elements back to back, which is what an MSL array of the boundary
    /// type is, so such a struct crosses the CPU/GPU boundary (constants, buffer elements, struct fields)
    /// whenever its element type can; the size assert of the struct guards it like any other. With an
    /// element that cannot (bool, a matrix without a Packed form), the struct holds the native MSL type
    /// and is limited to locals, parameters and return values (<see cref="GpuType.IsLocalOnly"/>).
    /// </summary>
    private GpuType? CreateInlineArray(INamedTypeSymbol type, string csharpName, int length, out TypeFailure? failure)
    {
        failure = null;
        List<IFieldSymbol> fields = InstanceFields(type).ToList();

        if (fields.Count != 1 || length <= 0)
        {
            // The C# compiler requires exactly one instance field and a positive length.
            SawErrorType = true;
            return null;
        }

        IFieldSymbol field = fields[0];
        string fieldName = field.AssociatedSymbol?.Name ?? field.Name;
        GpuType? element = Resolve(field.Type, out TypeFailure? elementFailure);

        if (IsStructFailure(elementFailure))
        {
            failure = elementFailure;
            return null;
        }

        if (element is null || element.Kind is GpuTypeKind.Void or GpuTypeKind.Buffer or GpuTypeKind.Texture)
        {
            string reason = elementFailure?.Reason ?? "buffers and textures are kernel arguments, not values";
            return Fail(out failure, type, $"the element type '{Display(field.Type)}' of this inline array is not supported ({reason})");
        }

        long size = (long)element.Size * length;

        if (size > int.MaxValue)
        {
            return Fail(out failure, type, "this inline array is larger than 2 GB");
        }

        bool crossesBoundary = element.CanCrossBoundary;
        string mslName = _globalNames.Allocate(MslNames.Escape(type.Name));
        string memberName = MslNames.Escape(fieldName);
        string declaration = (crossesBoundary ? element.BoundaryName : element.MslName) + " " + memberName + "[" + length.ToString(CultureInfo.InvariantCulture) + "]";
        var gpuField = new GpuField(field, fieldName, memberName, element, 0, declaration);

        var result = new GpuType(GpuTypeKind.Struct, mslName, mslName, csharpName, (int)size, element.Alignment)
        {
            Fields = new[] { gpuField },
            Element = element,
            ArrayLength = length,
            IsLocalOnly = !crossesBoundary,
        };

        _structs.Add(result);
        return result;
    }

    /// <summary>
    /// The parameter name for the hidden field (<c>&lt;seed&gt;P</c>) in which the compiler keeps a
    /// primary-constructor parameter that the struct's methods read; null for any other field.
    /// </summary>
    public static string? KeptParameterName(IFieldSymbol field) =>
        field.IsImplicitlyDeclared && field.Name.Length > 3 && field.Name[0] == '<' && field.Name.EndsWith(">P", StringComparison.Ordinal)
            ? field.Name.Substring(1, field.Name.Length - 3)
            : null;

    /// <summary>True for failures that describe a problem with a user struct (SCG0009, SCG0010, SCG0027).</summary>
    public static bool IsStructFailure(TypeFailure? failure) =>
        failure is not null &&
        (ReferenceEquals(failure.Descriptor, DiagnosticDescriptors.InvalidStruct) ||
         ReferenceEquals(failure.Descriptor, DiagnosticDescriptors.BufferInsideStruct) ||
         ReferenceEquals(failure.Descriptor, DiagnosticDescriptors.TextureInsideStruct));

    private static GpuType? Fail(out TypeFailure? failure, INamedTypeSymbol type, string reason)
    {
        failure = new TypeFailure(DiagnosticDescriptors.InvalidStruct, reason, Display(type), reason);
        return null;
    }

    private static IEnumerable<IFieldSymbol> InstanceFields(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IFieldSymbol>().Where(static f => !f.IsStatic && !f.IsConst);

    /// <summary>Applies the CLR's sequential layout rules.</summary>
    private static void ComputeLayout(
        List<(IFieldSymbol Symbol, GpuType Type)> fields,
        StructLayoutInfo layout,
        out int size,
        out int alignment,
        out int naturalAlignment,
        out int[] offsets)
    {
        int pack = layout.Pack == 0 ? 8 : layout.Pack;
        int offset = 0;
        alignment = 1;
        naturalAlignment = 1;
        offsets = new int[fields.Count];

        for (int i = 0; i < fields.Count; i++)
        {
            GpuType fieldType = fields[i].Type;
            int fieldAlignment = Math.Min(fieldType.Alignment, pack);

            offset = AlignUp(offset, fieldAlignment);
            offsets[i] = offset;
            offset += fieldType.Size;
            alignment = Math.Max(alignment, fieldAlignment);
            naturalAlignment = Math.Max(naturalAlignment, fieldType.Alignment);
        }

        size = AlignUp(offset, alignment);
    }

    public static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    public const string BoolReason = "'bool' has no stable layout across the CPU/GPU boundary; use an 'int' or 'uint' instead";

    /// <summary>Explains why a type that is fine for locals cannot be stored in a buffer, constant, or struct field.</summary>
    public string BoundaryReason(ITypeSymbol type)
    {
        GpuType? resolved = Resolve(type);

        if (type.SpecialType == SpecialType.System_Boolean || resolved is { IsBoolean: true })
        {
            return BoolReason;
        }

        if (resolved is { IsInlineArray: true, IsLocalOnly: true } && resolved.Fields[0].Symbol.Type is { } element)
        {
            return $"its elements of type '{Display(element)}' cannot cross the CPU/GPU boundary ({BoundaryReason(element)}), so the inline array can only be used for locals, parameters, and return values";
        }

        if (resolved is { Kind: GpuTypeKind.Vector, HasPackedForm: false })
        {
            return $"its [MslType(\"{resolved.MslName}\")] declares no Packed type, so its MSL layout does not match C#; it can only be used for locals, parameters, and return values";
        }

        return UnsupportedReason(type);
    }

    /// <summary>Explains why a type has no MSL equivalent, and what to use instead.</summary>
    public static string UnsupportedReason(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Double:
                return "'double' is not available in Metal Shading Language; use 'float' instead";
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
                return "64-bit integers are not supported; use 'int' or 'uint' instead";
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Char:
                return "8- and 16-bit integers are not supported; use 'int' or 'uint' instead";
            case SpecialType.System_Decimal:
                return "'decimal' is not available on the GPU; use 'float' instead";
            case SpecialType.System_String:
                return "strings are not available on the GPU";
            case SpecialType.System_Object:
                return "managed objects are not available on the GPU";
        }

        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return "nullable value types are not supported";
        }

        switch (type.TypeKind)
        {
            case TypeKind.Enum:
                return "enums are not supported; use an 'int' or 'uint' instead";
            case TypeKind.Array:
                return "arrays are managed objects; use a ReadWriteBuffer<T> or ReadOnlyBuffer<T> instead";
            case TypeKind.Pointer:
            case TypeKind.FunctionPointer:
                return "pointers are not supported";
            case TypeKind.TypeParameter:
                return "generic type parameters are not supported";
            case TypeKind.Delegate:
                return "delegates are not available on the GPU";
            case TypeKind.Dynamic:
                return "'dynamic' is not available on the GPU";
        }

        if (type.IsReferenceType)
        {
            return "managed (reference) types are not available on the GPU";
        }

        return "only float, int, uint, bool, System.Half, [MslType] vectors, and structs of those are supported";
    }

    private readonly struct StructLayoutInfo
    {
        public StructLayoutInfo(LayoutKind kind, int pack, int size)
        {
            Kind = kind;
            Pack = pack;
            Size = size;
        }

        public LayoutKind Kind { get; }

        public int Pack { get; }

        public int Size { get; }
    }

    private StructLayoutInfo GetLayout(INamedTypeSymbol type)
    {
        // Source symbols expose [StructLayout] as a regular attribute.
        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is not { Name: "StructLayoutAttribute" } attributeClass ||
                attributeClass.ContainingNamespace.ToDisplayString() != "System.Runtime.InteropServices")
            {
                continue;
            }

            LayoutKind kind = LayoutKind.Sequential;
            int pack = 0;
            int size = 0;

            if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is { } value)
            {
                kind = (LayoutKind)Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }

            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == "Pack" && argument.Value.Value is int packValue)
                {
                    pack = packValue;
                }
                else if (argument.Key == "Size" && argument.Value.Value is int sizeValue)
                {
                    size = sizeValue;
                }
            }

            return new StructLayoutInfo(kind, pack, size);
        }

        // For metadata symbols the layout lives in the type's flags and ClassLayout row instead.
        return TryGetMetadataLayout(type, out StructLayoutInfo layout) ? layout : new StructLayoutInfo(LayoutKind.Sequential, 0, 0);
    }

    private bool TryGetMetadataLayout(INamedTypeSymbol type, out StructLayoutInfo layout)
    {
        layout = default;

        try
        {
            if (type.Locations.Length == 0 || !type.Locations[0].IsInMetadata || type.ContainingAssembly is not { } assembly)
            {
                return false;
            }

            if (_compilation.GetMetadataReference(assembly) is not PortableExecutableReference reference)
            {
                return false;
            }

            ModuleMetadata? module = reference.GetMetadata() switch
            {
                AssemblyMetadata assemblyMetadata => assemblyMetadata.GetModules().FirstOrDefault(m => m.Name == type.ContainingModule.Name),
                ModuleMetadata moduleMetadata => moduleMetadata,
                _ => null,
            };

            int token = type.MetadataToken;

            if (module is null || token == 0)
            {
                return false;
            }

            MetadataReader reader = module.GetMetadataReader();
            EntityHandle handle = MetadataTokens.EntityHandle(token);

            if (handle.Kind != HandleKind.TypeDefinition)
            {
                return false;
            }

            TypeDefinition definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            LayoutKind kind = (definition.Attributes & TypeAttributes.LayoutMask) switch
            {
                TypeAttributes.ExplicitLayout => LayoutKind.Explicit,
                TypeAttributes.SequentialLayout => LayoutKind.Sequential,
                _ => LayoutKind.Auto,
            };

            TypeLayout typeLayout = definition.GetLayout();

            // The C# compiler emits Size = 1 for empty structs; an explicit Size on a non-empty struct is unusual.
            layout = new StructLayoutInfo(kind, typeLayout.IsDefault ? 0 : typeLayout.PackingSize, typeLayout.IsDefault ? 0 : typeLayout.Size);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }
}
