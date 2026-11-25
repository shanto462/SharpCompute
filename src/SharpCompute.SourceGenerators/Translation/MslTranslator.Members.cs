using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

internal sealed partial class MslTranslator
{
    private static string MemberName(ISymbol member) =>
        member.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);

    private Expr UnsupportedMember(IOperation operation, ISymbol member, string reason)
    {
        _diagnostics.Report(DiagnosticDescriptors.UnsupportedMember, operation.Syntax.GetLocation(), MemberName(member), reason);
        return Expr.Error;
    }

    private Expr FieldReference(IFieldReferenceOperation operation)
    {
        IFieldSymbol field = operation.Field;
        Location location = operation.Syntax.GetLocation();

        if (IsGroupShared(field))
        {
            // Element accesses and Length never get here; this is the array itself used as a value.
            return GroupSharedAsValue(operation);
        }

        if (field.IsConst && !IsShaderOrContainingType(field.ContainingType))
        {
            // Constants of other types (`MathF.PI`, `int.MaxValue`) are inlined.
            if (ResolveType(field.Type, location) is not { } constantType)
            {
                return Expr.Error;
            }

            return operation.ConstantValue is { HasValue: true, Value: { } value }
                ? Constant(value, constantType, operation)
                : Unsupported(operation, $"the constant '{field.Name}'");
        }

        if (field.IsConst || (field.IsStatic && field.IsReadOnly && IsGlobalSource(field)))
        {
            // The shader's own consts, and static readonly fields declared in source (the shader's or
            // those of a class of shared helpers), become program-scope `constant`s.
            // `::` names the global even where a local of the same name shadows it.
            return GlobalReference(operation, field) is { } global ? new Expr("::" + global.Name, Expr.Primary) : Expr.Error;
        }

        if (field.IsStatic)
        {
            return UnsupportedMember(
                operation,
                field,
                field.IsReadOnly
                    ? "only static readonly fields whose source is part of this compilation can be used (its initializer is what the GPU gets); declare it in the shader struct or in a type of your own"
                    : "static fields that are not readonly are not available on the GPU; make it 'static readonly' or 'const', or capture the value in an instance field");
        }

        if (operation.Instance is IInstanceReferenceOperation && _function.This is null)
        {
            if (_captures.TryGet(field, out Capture capture))
            {
                return CaptureReference(operation, capture);
            }

            // Either its type was already reported, or it is a field we never captured.
            return _captures.IsFailed(field)
                ? Expr.Error
                : UnsupportedMember(operation, field, "it is not part of the captured shader state");
        }

        if (operation.Instance is null)
        {
            return Expr.Error;
        }

        GpuType? instanceType = _types.Resolve(operation.Instance.Type);
        string instance = Expression(operation.Instance).Operand(Expr.Primary);

        switch (instanceType?.Kind)
        {
            case GpuTypeKind.Vector:
                return new Expr(instance + "." + field.Name.ToLowerInvariant(), Expr.Primary);
            case GpuTypeKind.Struct when instanceType.FindField(field) is { } structField:
                return BoundaryRead(operation, structField.Type, new Expr(instance + "." + structField.MslName, Expr.Primary));
            default:
                return UnsupportedMember(operation, field, "its containing type has no MSL equivalent");
        }
    }

    private Expr PropertyReference(IPropertyReferenceOperation operation)
    {
        IPropertySymbol property = operation.Property;
        Location location = operation.Syntax.GetLocation();

        if (SimdGroupProperty(operation) is { } simdGroup)
        {
            return simdGroup;
        }

        if (TryGetNormalized(property, out string normalizedSwizzle))
        {
            return NormalizedThreadId(operation, normalizedSwizzle);
        }

        if (TryGetBuiltin(property, out Builtins builtin, out string swizzle))
        {
            if (_function.IsGlobalInitializer)
            {
                return NotConstant(operation, $"'{MemberName(property)}' is only known while the shader runs");
            }

            if (ResolveType(property.Type, location) is not { IsNumeric: true } builtinType)
            {
                return Expr.Error;
            }

            UseBuiltin(builtin);
            string name = BuiltinInfo.Name(builtin);

            return swizzle switch
            {
                // GroupIds.Index: the built-in is a scalar already.
                IndexMember => Call(builtinType.MslName, name),

                // GroupSize.Count: at most 1024 on any GPU, so the product cannot overflow.
                CountMember => Call(builtinType.MslName, $"{name}.x * {name}.y * {name}.z"),
                _ => Call(builtinType.MslName, name + "." + swizzle),
            };
        }

        if (GroupSharedLength(operation) is { } groupSharedLength)
        {
            return groupSharedLength;
        }

        if (LocalArrayProperty(operation) is { } localArrayMember)
        {
            return localArrayMember;
        }

        if (operation.Instance is null)
        {
            if (TypeResolver.IsSharpComputeType(property.ContainingType, "Sampler"))
            {
                _diagnostics.Report(DiagnosticDescriptors.InvalidSampler, location, $"'{operation.Syntax}' is used outside a Sample call");
                return Expr.Error;
            }

            if (IsComputedProperty(property) && UserMethodProblem(property.GetMethod!) is null)
            {
                // A computed static property in source is a function without parameters.
                return UserCall(operation, property.GetMethod!, receiver: null, operation.Arguments);
            }

            return UnsupportedMember(operation, property, "static properties are not available on the GPU, unless they are computed properties whose source is part of this compilation");
        }

        if (TypeResolver.IsTextureType(WithoutImplicitConversions(operation.Instance)?.Type, out _))
        {
            return TextureProperty(operation);
        }

        ITypeSymbol? instanceSymbol = operation.Instance.Type;

        if (TypeResolver.IsBufferType(instanceSymbol, out _))
        {
            if (property.IsIndexer && operation.Arguments.Length == 1)
            {
                if (ResolveType(property.Type, location) is not { } elementType)
                {
                    return Expr.Error;
                }

                return BoundaryRead(operation, elementType, new Expr(Expression(operation.Instance).Operand(Expr.Primary) + "[" + Expression(operation.Arguments[0].Value).Text + "]", Expr.Primary));
            }

            if (property.Name == "Length")
            {
                return BufferLength(operation);
            }

            return UnsupportedMember(operation, property, "only the indexer and 'Length' of a buffer can be used in a shader");
        }

        GpuType? instanceType = _types.Resolve(instanceSymbol);

        if (instanceType is { Kind: GpuTypeKind.Vector })
        {
            if (ResolveType(property.Type, location) is null)
            {
                return Expr.Error;
            }

            string instance = Expression(operation.Instance).Operand(Expr.Primary);

            if (property.IsIndexer)
            {
                return operation.Arguments.Length == 1
                    ? new Expr(instance + "[" + Expression(operation.Arguments[0].Value).Text + "]", Expr.Primary)
                    : UnsupportedMember(operation, property, "only single-argument vector indexers are supported");
            }

            // Vector properties are swizzles: `v.XY` -> `v.xy`.
            return new Expr(instance + "." + property.Name.ToLowerInvariant(), Expr.Primary);
        }

        bool isShaderProperty = operation.Instance is IInstanceReferenceOperation && _function.This is null;

        if (IsComputedProperty(property) && (isShaderProperty || instanceType is { Kind: GpuTypeKind.Struct }))
        {
            if (IsWriteTarget(operation))
            {
                _diagnostics.Report(DiagnosticDescriptors.ComputedPropertyAssigned, location, property.Name, TypeResolver.Display(property.ContainingType));
                return Expr.Error;
            }

            if (isShaderProperty)
            {
                // A computed property of the shader is an instance helper: the captured state it reads
                // arrives as hidden inputs.
                return UserCall(operation, property.GetMethod!, receiver: null, operation.Arguments);
            }

            // A computed property (or indexer) of a user struct is a function of the struct.
            return UserMethodProblem(property.GetMethod!) is { } problem
                ? UnsupportedMember(operation, property, problem)
                : UserCall(operation, property.GetMethod!, operation.Instance, operation.Arguments);
        }

        if (instanceType is { Kind: GpuTypeKind.Struct } && instanceType.FindField(property) is { } backingField)
        {
            // An auto-property of a user struct is just its backing field.
            return BoundaryRead(operation, backingField.Type, new Expr(Expression(operation.Instance).Operand(Expr.Primary) + "." + backingField.MslName, Expr.Primary));
        }

        if (isShaderProperty)
        {
            return UnsupportedMember(operation, property, "auto-properties of the shader struct are not captured; use a field or a primary constructor parameter");
        }

        return UnsupportedMember(operation, property, "only swizzle properties of [MslType] vectors, and the properties of user structs whose source is part of this compilation, are supported");
    }

    private Expr BufferLength(IPropertyReferenceOperation operation)
    {
        IOperation? instance = operation.Instance;

        while (instance is IConversionOperation { IsImplicit: true } conversion)
        {
            instance = conversion.Operand;
        }

        ISymbol? symbol = instance switch
        {
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } field => field.Field,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null,
        };

        if (symbol is not null && _captures.TryGet(symbol, out Capture capture) && capture.IsBuffer)
        {
            capture.LengthUsed = true;
            _function.Inputs.UsesConstants = true;
            return Call("int", MslNames.Constants + "." + capture.LengthName);
        }

        if (symbol is not null && _captures.IsFailed(symbol))
        {
            return Expr.Error;
        }

        _diagnostics.Report(DiagnosticDescriptors.BufferLengthUnavailable, operation.Syntax.GetLocation(), instance?.Syntax.ToString() ?? "?");
        return Expr.Error;
    }

    /// <summary>Recognizes <c>ThreadIds.Normalized.{X,Y,Z,XY,XYZ}</c>.</summary>
    private static bool TryGetNormalized(IPropertySymbol property, out string swizzle)
    {
        swizzle = property.Name.ToLowerInvariant();

        return property.IsStatic &&
            property.ContainingType is { Name: "Normalized", ContainingType: { } outer } &&
            TypeResolver.IsSharpComputeType(outer, "ThreadIds") &&
            swizzle.Length is >= 1 and <= 3 &&
            swizzle.All(static c => c is 'x' or 'y' or 'z');
    }

    /// <summary>
    /// ComputeSharp's inclusive definition: <c>tid / max(1, grid - 1)</c> per component, so the first
    /// thread is 0 and the last is exactly 1.
    /// </summary>
    private Expr NormalizedThreadId(IPropertyReferenceOperation operation, string swizzle)
    {
        if (_function.IsGlobalInitializer)
        {
            return NotConstant(operation, $"'{MemberName(operation.Property)}' is only known while the shader runs");
        }

        if (ResolveType(operation.Property.Type, operation.Syntax.GetLocation()) is not { IsNumeric: true, IsFloating: true } type)
        {
            return Expr.Error;
        }

        UseBuiltin(Builtins.ThreadPositionInGrid);
        UseBuiltin(Builtins.ThreadsPerGrid);

        string tid = BuiltinInfo.Name(Builtins.ThreadPositionInGrid) + "." + swizzle;
        string grid = BuiltinInfo.Name(Builtins.ThreadsPerGrid) + "." + swizzle;

        if (swizzle.Length == 1)
        {
            return new Expr($"{type.MslName}({tid}) / {type.MslName}(metal::max(1, int({grid}) - 1))", Expr.Multiplicative);
        }

        string integer = "int" + swizzle.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Expr($"{type.MslName}({tid}) / {type.MslName}(metal::max({integer}(1), {integer}({grid}) - 1))", Expr.Multiplicative);
    }

    /// <summary>The <c>swizzle</c> <see cref="TryGetBuiltin"/> reports for <c>GroupIds.Index</c>, which is not a component.</summary>
    private const string IndexMember = "index";

    /// <summary>The <c>swizzle</c> <see cref="TryGetBuiltin"/> reports for <c>GroupSize.Count</c>, the product of the components.</summary>
    private const string CountMember = "count";

    /// <summary>
    /// Maps <c>ThreadIds</c>, <c>DispatchSize</c>, <c>GroupIds</c>, <c>ThreadGroupIds</c>, <c>GroupSize</c>
    /// and <c>ThreadGroupCount</c> members.
    /// </summary>
    private static bool TryGetBuiltin(IPropertySymbol property, out Builtins builtin, out string swizzle)
    {
        builtin = Builtins.None;
        swizzle = string.Empty;

        if (!property.IsStatic ||
            !TypeResolver.IsSharpComputeType(property.ContainingType, "ThreadIds", "DispatchSize", "GroupIds", "ThreadGroupIds", "GroupSize", "ThreadGroupCount"))
        {
            return false;
        }

        string name = property.Name;

        switch (property.ContainingType.Name)
        {
            case "ThreadIds":
                builtin = Builtins.ThreadPositionInGrid;
                break;
            case "DispatchSize":
                builtin = Builtins.ThreadsPerGrid;
                break;
            case "ThreadGroupIds":
                builtin = Builtins.ThreadgroupPositionInGrid;
                break;
            case "ThreadGroupCount":
                builtin = Builtins.ThreadgroupsPerGrid;
                break;
            case "GroupSize" when name == "Count":
                builtin = Builtins.ThreadsPerThreadgroup;
                swizzle = CountMember;
                return true;
            case "GroupSize":
                builtin = Builtins.ThreadsPerThreadgroup;
                break;
            case "GroupIds" when name == "Index":
                builtin = Builtins.ThreadIndexInThreadgroup;
                swizzle = IndexMember;
                return true;
            default:
                // `GroupIds.X` is the position within the threadgroup; `GroupIds.GroupX` is the group's position.
                if (name.StartsWith("Group", System.StringComparison.Ordinal) && name.Length > 5)
                {
                    builtin = Builtins.ThreadgroupPositionInGrid;
                    name = name.Substring(5);
                }
                else
                {
                    builtin = Builtins.ThreadPositionInThreadgroup;
                }

                break;
        }

        swizzle = name.ToLowerInvariant();
        return swizzle.Length is >= 1 and <= 3 && swizzle.All(static c => c is 'x' or 'y' or 'z');
    }

    private Expr ObjectCreation(IObjectCreationOperation operation)
    {
        if (ResolveType(operation.Type, operation.Syntax.GetLocation()) is not { } type)
        {
            return Expr.Error;
        }

        if (operation.Initializer is not null)
        {
            return Unsupported(operation.Initializer, "an object initializer (assign the fields one by one instead)");
        }

        switch (type.Kind)
        {
            case GpuTypeKind.Vector:
                // `new Float4(a, b, c, d)` -> `float4(a, b, c, d)`.
                return operation.Arguments.Length == 0
                    ? Zero(type, operation)
                    : Call(type.MslName, Arguments(operation.Arguments, operation.Constructor, out _));

            case GpuTypeKind.Bool:
            case GpuTypeKind.Scalar:
                return operation.Arguments.Length == 0
                    ? Zero(type, operation)
                    : Unsupported(operation, $"a constructor call on '{TypeResolver.Display(operation.Type!)}'");

            case GpuTypeKind.Struct when operation.Arguments.Length == 0 && operation.Constructor is null or { IsImplicitlyDeclared: true }:
                return Zero(type, operation);

            case GpuTypeKind.Struct when operation.Constructor is { } constructor && UserMethodProblem(constructor) is null:
                // A constructor declared in source is a function that returns the struct.
                return UserCall(operation, constructor, receiver: null, operation.Arguments);

            default:
                return Unsupported(operation, $"a constructor call on the struct '{TypeResolver.Display(operation.Type!)}', whose source is not part of this compilation (use 'default' and assign the fields instead)");
        }
    }

    /// <summary>Translates call arguments in parameter order (they arrive in evaluation order).</summary>
    private string[] Arguments(IEnumerable<IArgumentOperation> arguments, IMethodSymbol? method, out bool ok)
    {
        ok = true;
        var result = new List<(int Ordinal, string Text)>();

        foreach (IArgumentOperation argument in arguments)
        {
            if (argument.ArgumentKind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection)
            {
                Unsupported(argument, "a 'params' argument");
                ok = false;
                continue;
            }

            // A local array becomes two arguments, the array and its length (see MslTranslator.LocalArrays.cs).
            string text = IsLocalArrayType(argument.Parameter?.Type) ? LocalArrayArgument(argument, ref ok) : Expression(argument.Value).Text;
            result.Add((argument.Parameter?.Ordinal ?? result.Count, text));
        }

        return result.OrderBy(static a => a.Ordinal).Select(static a => a.Text).ToArray();
    }

    private Expr Invocation(IInvocationOperation operation)
    {
        IMethodSymbol method = operation.TargetMethod;
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        Location location = operation.Syntax.GetLocation();

        // The SIMD-group (wave and quad) operations of Gpu, which map to Metal's simd_* and quad_* functions.
        if (SimdGroupCall(operation, definition) is { } simdGroupCall)
        {
            return simdGroupCall;
        }

        // Members of textures (Sample) are recognized by name, like the indexers and Width/Height.
        if (TypeResolver.IsSharpComputeType(definition.ContainingType, "Texture2D", "ReadOnlyTexture2D", "ReadWriteTexture2D"))
        {
            return TextureCall(operation);
        }

        // The threadgroup barriers of Gpu, which map to one MSL function with different flags.
        if (Barrier(operation, definition) is { } barrier)
        {
            return barrier;
        }

        // 0. [MslBinaryOperator("*", SwapOperands = true)] makes a call an operator: `(a0 op a1)` / `(a1 op a0)`.
        if (TypeResolver.FindAttribute(definition, "MslBinaryOperatorAttribute") is { } binaryOperator)
        {
            return BinaryOperatorCall(operation, binaryOperator);
        }

        // MSL forbids calls in program-scope initializers: evaluate them here or explain why not.
        if (_function.IsGlobalInitializer)
        {
            return FoldCall(operation);
        }

        // 1. [MslIntrinsic("metal::sqrt")] drives the mapping for Gpu.* and vector methods.
        if (TypeResolver.FindAttribute(definition, "MslIntrinsicAttribute") is { } intrinsic)
        {
            if (intrinsic.ConstructorArguments.Length != 1 || intrinsic.ConstructorArguments[0].Value is not string { Length: > 0 } intrinsicName)
            {
                return UnsupportedCall(operation, "its [MslIntrinsic] attribute does not name an MSL function");
            }

            if (ResolveType(method.ReturnType, location) is null)
            {
                return Expr.Error;
            }

            var intrinsicArguments = new List<string>();

            if (operation.Instance is not null)
            {
                intrinsicArguments.Add(Expression(operation.Instance).Text);
            }

            intrinsicArguments.AddRange(Arguments(operation.Arguments, method, out _));

            if (intrinsic.NamedArguments.Any(static a => a.Key == "ReverseArguments" && a.Value.Value is true))
            {
                // The MSL function takes them last parameter first: Gpu.Select(mask, whenTrue, whenFalse)
                // is metal::select(whenFalse, whenTrue, mask).
                intrinsicArguments.Reverse();
            }

            if (intrinsicName.StartsWith(MslNames.EscapePrefix, System.StringComparison.Ordinal))
            {
                // A helper from MslPrelude.Source (e.g. __ms_radians); the prelude is emitted when used.
                UsesPrelude = true;
            }

            return Call(intrinsicName, intrinsicArguments.ToArray());
        }

        // 1a. [MslAtomic("metal::atomic_fetch_add_explicit")]: an atomic operation on a buffer element.
        if (TypeResolver.FindAttribute(definition, "MslAtomicAttribute") is { } atomic)
        {
            return AtomicCall(operation, atomic);
        }

        // 2. System.MathF / System.Math / float statics map to the same metal:: functions.
        if (method.IsStatic && method.ContainingType is { ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }, Name: "MathF" or "Math" or "Single" })
        {
            return MathCall(operation);
        }

        // 2a. System.BitConverter and System.Numerics.BitOperations: bit casts, counts and rotates.
        if (method.IsStatic && IsBitHelperType(method.ContainingType))
        {
            return BitCall(operation);
        }

        // 3. Local functions and methods of the shader struct become MSL functions.
        bool isLocalFunction = method.MethodKind == MethodKind.LocalFunction;

        if (isLocalFunction || SymbolEqualityComparer.Default.Equals(method.ContainingType, _shaderType))
        {
            if (!isLocalFunction && method.MethodKind != MethodKind.Ordinary)
            {
                return Unsupported(operation, DescribeMethodKind(method));
            }

            if (_failedSymbols.Contains(method.OriginalDefinition))
            {
                // A capturing local function, already reported at its declaration.
                return Expr.Error;
            }

            if (!method.IsStatic && operation.Instance is not (null or IInstanceReferenceOperation))
            {
                _diagnostics.Report(DiagnosticDescriptors.InstanceHelperMethod, location, method.Name);
                return Expr.Error;
            }

            if (isLocalFunction && method.IsGenericMethod)
            {
                return UnsupportedCall(operation, "generic local functions are not supported");
            }

            return UserCall(operation, method, receiver: null, operation.Arguments);
        }

        // 4. Code shared between shaders: static methods of any type in this compilation, and the
        // methods of user structs, which take the struct they are called on as their first parameter.
        if (method.MethodKind != MethodKind.Ordinary || UserMethodProblem(method) is { } problem)
        {
            return UnsupportedCall(operation, method.MethodKind != MethodKind.Ordinary && HasSource(method)
                ? "only ordinary methods can be called by name"
                : UserMethodProblem(method) ?? "only ordinary methods can be called by name");
        }

        return UserCall(operation, method, method.IsStatic ? null : operation.Instance, operation.Arguments);
    }

    // ---------------------------------------------------------------------------------------------
    // Textures: texture2d<float, access::sample> (ReadOnlyTexture2D) and
    // texture2d<float, access::read_write> (ReadWriteTexture2D)
    // ---------------------------------------------------------------------------------------------

    private const string TextureMembers = "only the indexers, 'Width', 'Height' and 'Sample' of a texture can be used in a shader";

    /// <summary>The receiver of a member access without the reference conversions to a base class.</summary>
    private static IOperation? WithoutImplicitConversions(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    /// <summary>The indexer access when <paramref name="operation"/> is a pixel of a texture (<c>texture[x, y]</c>), otherwise null.</summary>
    private static IPropertyReferenceOperation? TextureElement(IOperation operation) =>
        operation is IPropertyReferenceOperation { Property.IsIndexer: true } pixel && TypeResolver.IsTextureType(WithoutImplicitConversions(pixel.Instance)?.Type, out _)
            ? pixel
            : null;

    /// <summary>The MSL expression of the texture a member is accessed on.</summary>
    private string TextureInstance(IOperation? instance) => Expression(WithoutImplicitConversions(instance)!).Operand(Expr.Primary);

    /// <summary><c>uint2(x, y)</c> for the <c>(int x, int y)</c> indexer, <c>uint2(xy)</c> for the <c>Int2</c> one.</summary>
    private string TextureCoordinates(IPropertyReferenceOperation pixel) =>
        "uint2(" + string.Join(", ", Arguments(pixel.Arguments, method: null, out _)) + ")";

    /// <summary>Reads of a texture's members: a pixel (<c>read</c>), <c>Width</c> and <c>Height</c>.</summary>
    private Expr TextureProperty(IPropertyReferenceOperation operation)
    {
        IPropertySymbol property = operation.Property;

        if (property.IsIndexer)
        {
            // A read of a texture the shader also writes puts a fence after those writes.
            RecordTextureRead(operation.Instance);

            return ResolveType(property.Type, operation.Syntax.GetLocation()) is null
                ? Expr.Error
                : Call(TextureInstance(operation.Instance) + ".read", TextureCoordinates(operation));
        }

        if (property.Name is "Width" or "Height")
        {
            // get_width() returns a uint; the C# property is an int.
            return Call("int", TextureInstance(operation.Instance) + (property.Name == "Width" ? ".get_width()" : ".get_height()"));
        }

        return UnsupportedMember(operation, property, TextureMembers);
    }

    /// <summary>
    /// <c>texture[x, y] = value</c> → <c>texture.write(value, uint2(x, y))</c>. <c>write</c> returns
    /// nothing, so the assignment has to be a statement of its own. The write is followed by the
    /// placeholder of a fence, which stays only if the shader also reads the texture (see
    /// <see cref="ResolveFences"/>).
    /// </summary>
    private Expr TextureWrite(ISimpleAssignmentOperation operation, IPropertyReferenceOperation pixel)
    {
        if (operation.Parent is not IExpressionStatementOperation)
        {
            return Unsupported(operation, "a texture write used as a value (assign the pixel in a statement of its own)");
        }

        string texture = TextureInstance(pixel.Instance);
        string coordinates = TextureCoordinates(pixel);

        return new Expr(Call(texture + ".write", Expression(operation.Value).Text, coordinates).Text + FencePlaceholder(pixel.Instance, texture), Expr.Primary);
    }

    /// <summary><c>texture[x, y] += value</c>, <c>texture[x, y]++</c>: a read and a write, which have no single MSL form.</summary>
    private Expr TextureElementModifiedInPlace(IOperation operation)
    {
        if (ContainsInvalid(operation))
        {
            SawInvalidCode = true;
            return Expr.Error;
        }

        _diagnostics.Report(DiagnosticDescriptors.TextureElementModifiedInPlace, operation.Syntax.GetLocation(), operation.Syntax.ToString());
        return Expr.Error;
    }

    /// <summary>Calls of a texture's methods: <c>Sample</c>; everything else is CPU-only.</summary>
    private Expr TextureCall(IInvocationOperation operation)
    {
        IMethodSymbol method = operation.TargetMethod;

        if (method.Name == "AsReadOnly")
        {
            _diagnostics.Report(DiagnosticDescriptors.ReadWriteTextureCannotBeSampled, operation.Syntax.GetLocation(), MemberName(method));
            return Expr.Error;
        }

        if (method.Name != "Sample" || operation.Instance is null || operation.Arguments.Length != 2)
        {
            return UnsupportedCall(operation, TextureMembers);
        }

        if (_function.IsGlobalInitializer)
        {
            return NotConstant(operation, "a texture can only be sampled while the shader runs");
        }

        if (ResolveType(method.ReturnType, operation.Syntax.GetLocation()) is null)
        {
            return Expr.Error;
        }

        IOperation? sampler = null;
        IOperation? coordinates = null;

        foreach (IArgumentOperation argument in operation.Arguments)
        {
            if (argument.Parameter?.Ordinal == 0)
            {
                sampler = argument.Value;
            }
            else
            {
                coordinates = argument.Value;
            }
        }

        if (sampler is null || coordinates is null)
        {
            return UnsupportedCall(operation, TextureMembers);
        }

        string texture = TextureInstance(operation.Instance);
        string? samplerName = SamplerReference(sampler);
        string uv = Expression(coordinates).Text;

        // `metal::level`: a local called `level` would otherwise hide the level-of-detail type. The
        // textures have one mip level, and a compute kernel has no derivatives to choose one from.
        return samplerName is null ? Expr.Error : Call(texture + ".sample", samplerName, uv, "metal::level(0)");
    }

    /// <summary>
    /// The program-scope sampler for a <c>Sampler.X</c> argument, or null (after reporting SCG0030) when
    /// the argument is anything else: a sampler is part of the compiled shader, not a run-time value.
    /// </summary>
    private string? SamplerReference(IOperation argument)
    {
        IOperation value = WithoutImplicitConversions(argument)!;

        if (value is IPropertyReferenceOperation { Instance: null, Property: { IsStatic: true } property } &&
            TypeResolver.IsSharpComputeType(property.ContainingType, "Sampler") &&
            SamplerInfo.IndexOf(property.Name) is >= 0 and int index)
        {
            _samplersUsed[index] = true;
            return SamplerInfo.Name(index);
        }

        if (ContainsInvalid(value))
        {
            SawInvalidCode = true;
        }
        else if (!ReferencesFailedSymbol(value))
        {
            _diagnostics.Report(DiagnosticDescriptors.InvalidSampler, value.Syntax.GetLocation(), $"'{value.Syntax}' is not a static property of Sampler");
        }

        return null;
    }

    /// <summary>
    /// True for storage in the thread address space: a local, a parameter of the current function, an
    /// inline <c>out</c> declaration, or a field of one of those. Captured state and buffer elements
    /// live in other address spaces and cannot bind to a <c>thread T&amp;</c> parameter.
    /// </summary>
    private bool IsThreadLocal(IOperation operation) => operation switch
    {
        ILocalReferenceOperation => true,
        IDeclarationExpressionOperation { Expression: ILocalReferenceOperation } => true,
        IParameterReferenceOperation parameter => _function.Names.ContainsKey(parameter.Parameter),
        IFieldReferenceOperation { Field.IsStatic: false, Instance: { } instance } when instance is not IInstanceReferenceOperation => IsThreadLocal(instance),
        IArrayElementReferenceOperation or IPropertyReferenceOperation or IInlineArrayAccessOperation => IsThreadLocalArrayElement(operation),
        _ => false,
    };

    /// <summary>
    /// <c>[MslBinaryOperator(op)]</c>: a two-argument method that is an operator in MSL. With
    /// <c>SwapOperands</c> the operands are reversed (<c>Gpu.Mul(v, m)</c> → <c>(m * v)</c>).
    /// </summary>
    private Expr BinaryOperatorCall(IInvocationOperation operation, AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not string { Length: > 0 } op)
        {
            return UnsupportedCall(operation, "its [MslBinaryOperator] attribute does not name an operator");
        }

        if (operation.TargetMethod.Parameters.Length != 2 || operation.Instance is not null)
        {
            return UnsupportedCall(operation, "[MslBinaryOperator] methods must be static and take exactly two parameters");
        }

        if (ResolveType(operation.TargetMethod.ReturnType, operation.Syntax.GetLocation()) is null)
        {
            return Expr.Error;
        }

        bool swap = attribute.NamedArguments.Any(static a => a.Key == "SwapOperands" && a.Value.Value is true);
        var operands = new Expr[2];

        foreach (IArgumentOperation argument in operation.Arguments)
        {
            if (argument.Parameter is not { Ordinal: >= 0 and <= 1 } parameter)
            {
                return Unsupported(argument, "a 'params' argument");
            }

            operands[parameter.Ordinal] = NativeOperand(argument.Value, Expression(argument.Value));
        }

        int precedence = TryGetOperatorPrecedence(op, out int known) ? known : Expr.Unary;
        Expr left = swap ? operands[1] : operands[0];
        Expr right = swap ? operands[0] : operands[1];

        // Always parenthesized: the call was a primary expression in C#.
        return new Expr("(" + BinaryOperand(left, precedence, isRight: false) + " " + op + " " + BinaryOperand(right, precedence, isRight: true) + ")", Expr.Primary);
    }

    private static string DescribeMethodKind(IMethodSymbol method) => method.MethodKind switch
    {
        MethodKind.Constructor => "a constructor call",
        _ => $"a call to '{method.Name}'",
    };

    private Expr UnsupportedCall(IInvocationOperation operation, string reason)
    {
        _diagnostics.Report(DiagnosticDescriptors.UnsupportedMethodCall, operation.Syntax.GetLocation(), MemberName(operation.TargetMethod), reason);
        return Expr.Error;
    }

    private Expr MathCall(IInvocationOperation operation)
    {
        IMethodSymbol method = operation.TargetMethod;
        Location location = operation.Syntax.GetLocation();

        // Report `double` (e.g. Math.Sqrt) before anything else: that is the actual problem.
        if (ResolveType(method.ReturnType, location) is not { } returnType)
        {
            return Expr.Error;
        }

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                return UnsupportedCall(operation, "this overload has no MSL equivalent");
            }

            if (ResolveType(parameter.Type, location) is not { } parameterType)
            {
                return Expr.Error;
            }

            if (parameterType.Kind != GpuTypeKind.Scalar)
            {
                return UnsupportedCall(operation, "this overload has no MSL equivalent");
            }
        }

        bool isFloat = method.Parameters.Length > 0 && _types.Resolve(method.Parameters[0].Type) is { IsFloating: true };
        string? function = MathFunction(method.Name, method.Parameters.Length, isFloat, method.ContainingType.Name == "Single");

        if (function is null)
        {
            return UnsupportedCall(operation, "it has no MSL equivalent");
        }

        if (function.StartsWith(MslNames.EscapePrefix, System.StringComparison.Ordinal))
        {
            // .NET's Min, Max and Clamp (MslNames.MathMin): the prelude is emitted when used.
            UsesPrelude = true;
        }

        Expr call = Call(function, Arguments(operation.Arguments, method, out _));

        // `MathF.Sign` returns an int and `metal::abs(int)` may return an unsigned type; pin the C# result type.
        bool needsCast = method.Name == "Sign" || (method.Name == "Abs" && !isFloat) || method.Name == "ILogB";
        return needsCast ? Call(returnType.MslName, call.Text) : call;
    }

    private static string? MathFunction(string name, int arity, bool isFloat, bool isSingle)
    {
        // Available for both integers and floats. Abs is metal::abs; for the minimum of a signed type,
        // where .NET throws OverflowException, it returns the minimum itself (Math.Abs(int.MinValue) is
        // int.MinValue on the GPU). Min and Max of floats, and Clamp of everything, follow .NET, not
        // metal::min/max/clamp: see MslNames.MathMin. Gpu.Min/Max/Clamp are the metal:: functions.
        switch (name, arity)
        {
            case ("Abs", 1):
                return "metal::abs";
            case ("Min", 2):
                return isFloat ? MslNames.MathMin : "metal::min";
            case ("Max", 2):
                return isFloat ? MslNames.MathMax : "metal::max";
            case ("Clamp", 3):
                // float.Clamp is Min(Max(x, lo), hi) in .NET, which differs from Math.Clamp for a NaN bound.
                return isSingle ? MslNames.NumberClamp : MslNames.MathClamp;
        }

        if (!isFloat)
        {
            return null;
        }

        switch (name, arity)
        {
            case ("Acos", 1):
            case ("Acosh", 1):
            case ("Asin", 1):
            case ("Asinh", 1):
            case ("Atan", 1):
            case ("Atanh", 1):
            case ("Cos", 1):
            case ("Cosh", 1):
            case ("Exp", 1):
            case ("Floor", 1):
            case ("Log", 1):
            case ("Log10", 1):
            case ("Log2", 1):
            case ("Sin", 1):
            case ("Sinh", 1):
            case ("Sqrt", 1):
            case ("Tan", 1):
            case ("Tanh", 1):
            case ("Atan2", 2):
            case ("Pow", 2):
            case ("Sign", 1):
                return "metal::" + name.ToLowerInvariant();
            case ("Ceiling", 1):
                return "metal::ceil";
            case ("Truncate", 1):
                return "metal::trunc";
            case ("Round", 1):
                // MathF.Round rounds half to even, like rint (metal::round rounds half away from zero).
                return "metal::rint";
            case ("CopySign", 2):
                return "metal::copysign";
            case ("FusedMultiplyAdd", 3):
                return "metal::fma";
            case ("ScaleB", 2):
                return "metal::ldexp";
            case ("ILogB", 1):
                return MslNames.ILogB;
            case ("ReciprocalSqrtEstimate", 1):
                return "metal::rsqrt";
        }

        if (!isSingle)
        {
            return null;
        }

        // Static members of `float` itself.
        switch (name, arity)
        {
            case ("IsNaN", 1):
                return "metal::isnan";
            case ("IsInfinity", 1):
                return "metal::isinf";
            case ("IsFinite", 1):
                return "metal::isfinite";
            case ("Lerp", 3):
                return MslNames.NumberLerp;
            case ("Exp2", 1):
                return "metal::exp2";
            case ("Exp10", 1):
                return "metal::exp10";
            default:
                return null;
        }
    }
}
