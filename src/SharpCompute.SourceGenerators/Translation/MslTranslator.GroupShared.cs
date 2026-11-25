using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// Threadgroup memory and barriers. A <c>[GroupShared] static readonly T[] name = new T[N];</c> field of
/// the shader struct becomes <c>threadgroup T name[N];</c> at the top of the kernel; helpers that touch
/// it receive <c>threadgroup T* name</c> as a hidden parameter, the way they receive buffers. The
/// barriers of <c>Gpu</c> become <c>metal::threadgroup_barrier</c>.
/// </summary>
/// <remarks>
/// TODO: atomics on group-shared memory are not translated. <c>ref name[i]</c> is rejected as the
/// location of a <c>Gpu.Atomic*</c> call (SCG0034, with a reason of its own) and, like any other
/// by-reference argument that is not a local, as an argument of a helper (see <c>IsThreadLocal</c>).
/// The extension point is <c>AtomicTarget</c> in <c>MslTranslator.Intrinsics.cs</c>: accept a
/// <see cref="GroupSharedElement"/> of int, uint or float there, in the <c>threadgroup</c> address
/// space instead of <c>device</c>.
/// </remarks>
internal sealed partial class MslTranslator
{
    private const string GroupSharedAttributeName = "GroupSharedAttribute";

    /// <summary>The largest total of threadgroup memory an Apple GPU accepts (<c>maxThreadgroupMemoryLength</c>).</summary>
    public const int MaxThreadgroupMemoryLength = 32768;

    private readonly List<GroupSharedField> _groupShared = new();
    private readonly Dictionary<IFieldSymbol, GroupSharedField> _groupSharedBySymbol = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IFieldSymbol> _groupSharedFailed = new(SymbolEqualityComparer.Default);

    /// <summary>True when the shader (or a helper it calls) calls a threadgroup barrier.</summary>
    public bool UsesBarriers { get; private set; }

    public static bool IsGroupShared(IFieldSymbol field) => TypeResolver.FindAttribute(field, GroupSharedAttributeName) is not null;

    /// <summary>
    /// Validates every <c>[GroupShared]</c> field the shader struct declares, used or not, so that a
    /// mistake is reported where it is written. Call once, before <see cref="TranslateKernel"/>.
    /// </summary>
    public void CollectGroupShared()
    {
        foreach (IFieldSymbol field in _shaderType.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsImplicitlyDeclared || !IsGroupShared(field))
            {
                continue;
            }

            if (CreateGroupShared(field) is { } groupShared)
            {
                _groupShared.Add(groupShared);
                _groupSharedBySymbol.Add(field, groupShared);
            }
            else
            {
                _groupSharedFailed.Add(field);
            }
        }
    }

    private GroupSharedField? CreateGroupShared(IFieldSymbol field)
    {
        var declarator = field.DeclaringSyntaxReferences.Select(r => r.GetSyntax(_cancellationToken)).OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        Location location = declarator?.Identifier.GetLocation() ?? field.Locations.FirstOrDefault() ?? Location.None;
        Location typeLocation = declarator?.Parent is VariableDeclarationSyntax declaration ? declaration.Type.GetLocation() : location;

        if (field.Type.TypeKind == TypeKind.Error)
        {
            SawInvalidCode = true;
            return null;
        }

        if (!field.IsStatic || !field.IsReadOnly)
        {
            return Invalid("it must be 'static readonly': the array is the threadgroup's memory, not state of one shader instance");
        }

        if (field.Type is not IArrayTypeSymbol { Rank: 1 } array)
        {
            return Invalid($"its type '{TypeResolver.Display(field.Type)}' is not a one-dimensional array; declare it as 'T[]' with an initializer 'new T[length]'");
        }

        if (array.ElementType.TypeKind == TypeKind.Error)
        {
            SawInvalidCode = true;
            return null;
        }

        GpuType? element = _types.Resolve(array.ElementType, out TypeFailure? failure);

        if (TypeResolver.IsStructFailure(failure))
        {
            // The user struct is the real culprit: report its own diagnostic.
            _diagnostics.Report(failure!.Descriptor, typeLocation, failure.Arguments);
            return null;
        }

        if (element is null || !element.CanCrossBoundary)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.UnsupportedGroupSharedElementType,
                typeLocation,
                field.Name,
                TypeResolver.Display(array.ElementType),
                failure?.Reason ?? _types.BoundaryReason(array.ElementType));
            return null;
        }

        if (declarator?.Initializer is not { } equalsValue ||
            GetSemanticModel(equalsValue.SyntaxTree).GetOperation(equalsValue, _cancellationToken) is not IFieldInitializerOperation { Value: { } value })
        {
            return NonConstantLength(location, "it has no initializer; initialize it with 'new T[length]'");
        }

        if (ContainsInvalid(value))
        {
            SawInvalidCode = true;
            return null;
        }

        if (WithoutImplicitConversions(value) is not IArrayCreationOperation { DimensionSizes.Length: 1 } creation)
        {
            return NonConstantLength(value.Syntax.GetLocation(), "its initializer must be an array creation of the form 'new T[length]'");
        }

        if (creation.Initializer is not null)
        {
            return NonConstantLength(
                creation.Initializer.Syntax.GetLocation(),
                "its initializer lists elements, but threadgroup memory starts out undefined; use 'new T[length]' and let the shader fill it");
        }

        IOperation size = creation.DimensionSizes[0];

        if (size.ConstantValue is not { HasValue: true, Value: { } constant } || !TryGetLength(constant, out long length))
        {
            return NonConstantLength(size.Syntax.GetLocation(), $"'{size.Syntax}' is not a compile-time constant; use a literal, a const, or an expression of those");
        }

        if (length is < 1 or > int.MaxValue)
        {
            return NonConstantLength(size.Syntax.GetLocation(), $"the length {length.ToString(CultureInfo.InvariantCulture)} is out of range; it must be at least 1");
        }

        if (FindStaticConstructorWrite(field) is { } write)
        {
            _diagnostics.Report(DiagnosticDescriptors.StaticFieldAssignedInStaticConstructor, write.GetLocation(), field.ContainingType.Name + "." + field.Name);
            return null;
        }

        // The array lives in the kernel's scope next to the buffer and texture parameters, and reaches
        // helpers under the same name. Like a local, it must not hide a user struct type of that name
        // (`Particle[] Particle`), which the declarations after it could then no longer name.
        string name = MslNames.Escape(field.Name);

        if (_reservedLocalNames.Contains(name))
        {
            name = MslNames.LocalPrefix + MslNames.Sanitize(field.Name);
        }

        name = _captures.ResourceNames.Allocate(name);
        _captures.BufferNames.Add(name);
        LocalNames.Add(name);

        return new GroupSharedField(field, element, (int)length, name);

        GroupSharedField? Invalid(string reason)
        {
            _diagnostics.Report(DiagnosticDescriptors.InvalidGroupSharedField, location, field.Name, reason);
            return null;
        }

        GroupSharedField? NonConstantLength(Location where, string reason)
        {
            _diagnostics.Report(DiagnosticDescriptors.GroupSharedLengthNotConstant, where, field.Name, reason);
            return null;
        }
    }

    private static bool TryGetLength(object constant, out long length)
    {
        switch (constant)
        {
            case int value:
                length = value;
                return true;
            case uint value:
                length = value;
                return true;
            case long value:
                length = value;
                return true;
            case short value:
                length = value;
                return true;
            case ushort value:
                length = value;
                return true;
            case byte value:
                length = value;
                return true;
            case sbyte value:
                length = value;
                return true;
            default:
                length = 0;
                return false;
        }
    }

    /// <summary>The <c>[GroupShared]</c> field an expression names directly, or null.</summary>
    private static IFieldSymbol? GroupSharedReference(IOperation? operation) =>
        WithoutImplicitConversions(operation) is IFieldReferenceOperation { Field: { } field } && IsGroupShared(field) ? field.OriginalDefinition : null;

    /// <summary>
    /// Looks up a referenced <c>[GroupShared]</c> field. Returns null after reporting why it cannot be
    /// used (once per field), or silently when its declaration was already reported.
    /// </summary>
    private GroupSharedField? FindGroupShared(IFieldSymbol field, IOperation reference)
    {
        if (_groupSharedBySymbol.TryGetValue(field, out GroupSharedField? groupShared))
        {
            return groupShared;
        }

        if (_groupSharedFailed.Add(field))
        {
            // Not a field of this shader struct. On the CPU the static array is the memory of the
            // threadgroup that is running, and dispatches are serialized per shader type: an array
            // shared between two shader types would be used by both at once.
            _diagnostics.Report(
                DiagnosticDescriptors.InvalidGroupSharedField,
                reference.Syntax.GetLocation(),
                field.Name,
                $"it is declared in '{field.ContainingType.Name}', but a [GroupShared] field must be declared in the shader struct that uses it ('{_shaderType.Name}')");
        }

        return null;
    }

    /// <summary><c>name[i]</c> on a <c>[GroupShared]</c> array, as a value or as the target of a write; null for any other array.</summary>
    private Expr? GroupSharedElement(IArrayElementReferenceOperation operation)
    {
        if (GroupSharedReference(operation.ArrayReference) is not { } field)
        {
            return null;
        }

        if (_function.IsGlobalInitializer)
        {
            return NotConstant(operation, "threadgroup memory only exists while the shader runs");
        }

        if (FindGroupShared(field, operation.ArrayReference) is not { } groupShared ||
            operation.Indices.Length != 1 ||
            ResolveType(operation.Type, operation.Syntax.GetLocation()) is null)
        {
            return Expr.Error;
        }

        _function.Inputs.AddGroupShared(groupShared);
        return new Expr(groupShared.MslName + "[" + Expression(operation.Indices[0]).Text + "]", Expr.Primary);
    }

    /// <summary><c>name.Length</c> on a <c>[GroupShared]</c> array: the constant from its declaration. Null for anything else.</summary>
    private Expr? GroupSharedLength(IPropertyReferenceOperation operation)
    {
        if (operation.Property is not { Name: "Length", ContainingType.SpecialType: SpecialType.System_Array } ||
            GroupSharedReference(operation.Instance) is not { } field)
        {
            return null;
        }

        return FindGroupShared(field, operation.Instance!) is { } groupShared ? Int32Literal(groupShared.Length) : Expr.Error;
    }

    /// <summary>A <c>[GroupShared]</c> array named anywhere but in <c>name[i]</c> and <c>name.Length</c>.</summary>
    private Expr GroupSharedAsValue(IFieldReferenceOperation operation)
    {
        IFieldSymbol field = operation.Field.OriginalDefinition;

        if (FindGroupShared(field, operation) is null)
        {
            return Expr.Error;
        }

        return UnsupportedMember(operation, field, "only the elements ('name[i]') and 'Length' of a [GroupShared] array can be used in a shader");
    }

    /// <summary>
    /// The barriers of <c>Gpu</c>, recognized by name like the thread built-ins:
    /// <c>GroupMemoryBarrierWithGroupSync</c>, <c>DeviceMemoryBarrierWithGroupSync</c> and
    /// <c>AllMemoryBarrierWithGroupSync</c> (the HLSL names). Null for any other method.
    /// </summary>
    private Expr? Barrier(IInvocationOperation operation, IMethodSymbol method)
    {
        if (!method.IsStatic || method.Parameters.Length != 0 || !TypeResolver.IsSharpComputeType(method.ContainingType, "Gpu"))
        {
            return null;
        }

        (bool group, bool device) = method.Name switch
        {
            "GroupMemoryBarrierWithGroupSync" => (true, false),
            "DeviceMemoryBarrierWithGroupSync" => (false, true),
            "AllMemoryBarrierWithGroupSync" => (true, true),
            _ => (false, false),
        };

        if (!group && !device)
        {
            return null;
        }

        if (_function.IsGlobalInitializer)
        {
            return NotConstant(operation, "a barrier can only be called while the shader runs");
        }

        var flags = new List<string>();

        if (device)
        {
            flags.Add("metal::mem_flags::mem_device");

            // HLSL's device memory includes textures a shader writes; Metal orders those with a flag of
            // their own. Only shaders that can read back what they wrote (a read-write texture) need it.
            if (_captures.Textures.Any(static t => !t.Type.IsReadOnlyTexture))
            {
                flags.Add("metal::mem_flags::mem_texture");
            }
        }

        if (group)
        {
            flags.Add("metal::mem_flags::mem_threadgroup");
        }

        UsesBarriers = true;
        return Call("metal::threadgroup_barrier", string.Join(" | ", flags));
    }
}
