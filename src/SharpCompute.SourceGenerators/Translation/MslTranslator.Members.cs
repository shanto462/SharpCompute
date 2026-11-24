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

        if (field.IsConst || (field.IsStatic && field.IsReadOnly && IsShaderOrContainingType(field.ContainingType)))
        {
            // The shader's own consts and static readonly fields become program-scope `constant`s.
            // `::` names the global even where a local of the same name shadows it.
            return GlobalReference(operation, field) is { } global ? new Expr("::" + global.Name, Expr.Primary) : Expr.Error;
        }

        if (field.IsStatic)
        {
            return UnsupportedMember(
                operation,
                field,
                field.IsReadOnly
                    ? "only static readonly fields of the shader struct (or a type containing it) can be used; declare it there"
                    : "static fields that are not readonly are not available on the GPU; make it 'static readonly' or 'const', or capture the value in an instance field");
        }

        if (operation.Instance is IInstanceReferenceOperation)
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
                return new Expr(instance + "." + structField.MslName, Expr.Primary);
            default:
                return UnsupportedMember(operation, field, "its containing type has no MSL equivalent");
        }
    }

    private Expr PropertyReference(IPropertyReferenceOperation operation)
    {
        IPropertySymbol property = operation.Property;
        Location location = operation.Syntax.GetLocation();

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
            return Call(builtinType.MslName, BuiltinInfo.Name(builtin) + "." + swizzle);
        }

        if (operation.Instance is null)
        {
            return UnsupportedMember(operation, property, "static properties are not available on the GPU");
        }

        ITypeSymbol? instanceSymbol = operation.Instance.Type;

        if (TypeResolver.IsBufferType(instanceSymbol, out _))
        {
            if (property.IsIndexer && operation.Arguments.Length == 1)
            {
                if (ResolveType(property.Type, location) is null)
                {
                    return Expr.Error;
                }

                return new Expr(Expression(operation.Instance).Operand(Expr.Primary) + "[" + Expression(operation.Arguments[0].Value).Text + "]", Expr.Primary);
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

        if (instanceType is { Kind: GpuTypeKind.Struct } && instanceType.FindField(property) is { } backingField)
        {
            // An auto-property of a user struct is just its backing field.
            return new Expr(Expression(operation.Instance).Operand(Expr.Primary) + "." + backingField.MslName, Expr.Primary);
        }

        if (operation.Instance is IInstanceReferenceOperation)
        {
            return UnsupportedMember(operation, property, "properties of the shader struct are not captured; use a field or a primary constructor parameter");
        }

        return UnsupportedMember(operation, property, "only swizzle properties of [MslType] vectors and auto-properties of user structs are supported");
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

    /// <summary>Maps <c>ThreadIds</c>, <c>DispatchSize</c>, <c>GroupIds</c>, and <c>ThreadGroupIds</c> members.</summary>
    private static bool TryGetBuiltin(IPropertySymbol property, out Builtins builtin, out string swizzle)
    {
        builtin = Builtins.None;
        swizzle = string.Empty;

        if (!property.IsStatic || !TypeResolver.IsSharpComputeType(property.ContainingType, "ThreadIds", "DispatchSize", "GroupIds", "ThreadGroupIds"))
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

            default:
                return Unsupported(operation, $"a constructor call on the struct '{TypeResolver.Display(operation.Type!)}' (use 'default' and assign the fields instead)");
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

            result.Add((argument.Parameter?.Ordinal ?? result.Count, Expression(argument.Value).Text));
        }

        return result.OrderBy(static a => a.Ordinal).Select(static a => a.Text).ToArray();
    }

    private Expr Invocation(IInvocationOperation operation)
    {
        IMethodSymbol method = operation.TargetMethod;
        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        Location location = operation.Syntax.GetLocation();

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

            if (intrinsicName.StartsWith(MslNames.EscapePrefix, System.StringComparison.Ordinal))
            {
                // A helper from MslPrelude.Source (e.g. __ms_radians); the prelude is emitted when used.
                UsesPrelude = true;
            }

            return Call(intrinsicName, intrinsicArguments.ToArray());
        }

        // 2. System.MathF / System.Math / float statics map to the same metal:: functions.
        if (method.IsStatic && method.ContainingType is { ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }, Name: "MathF" or "Math" or "Single" })
        {
            return MathCall(operation);
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

            if (method.IsGenericMethod)
            {
                return UnsupportedCall(operation, isLocalFunction ? "generic local functions are not supported" : "generic helper methods are not supported");
            }

            // Translate the helper first: if its signature is rejected, the arguments converted to
            // its parameter types would only repeat that diagnostic.
            HelperFunction? helper = GetHelper(method, operation);

            if (helper is null)
            {
                return Expr.Error;
            }

            foreach (IArgumentOperation argument in operation.Arguments)
            {
                if (argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out } && !IsThreadLocal(argument.Value))
                {
                    return Unsupported(argument, $"the by-reference argument '{argument.Syntax}' (only locals and parameters, or their fields, can be passed by reference to a helper)");
                }
            }

            string[] arguments = Arguments(operation.Arguments, method, out bool argumentsOk);

            if (!argumentsOk)
            {
                return Expr.Error;
            }

            // Forward the captured state and kernel built-ins the helper (transitively) reads.
            _function.Inputs.Add(helper.Inputs);

            // `::Name` selects our global function even if `metal::` declares one with the same name.
            return Call("::" + helper.Name, arguments.Concat(helper.Inputs.Arguments()).ToArray());
        }

        return UnsupportedCall(operation, "it is not an [MslIntrinsic] method, a supported System.Math/MathF method, or a method of the shader struct");
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

            operands[parameter.Ordinal] = Expression(argument.Value);
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

        Expr call = Call(function, Arguments(operation.Arguments, method, out _));

        // `MathF.Sign` returns an int and `metal::abs(int)` may return an unsigned type; pin the C# result type.
        bool needsCast = method.Name == "Sign" || (method.Name == "Abs" && !isFloat) || method.Name == "ILogB";
        return needsCast ? Call(returnType.MslName, call.Text) : call;
    }

    private static string? MathFunction(string name, int arity, bool isFloat, bool isSingle)
    {
        // Available for both integers and floats.
        switch (name, arity)
        {
            case ("Abs", 1):
                return "metal::abs";
            case ("Min", 2):
                return "metal::min";
            case ("Max", 2):
                return "metal::max";
            case ("Clamp", 3):
                return "metal::clamp";
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
                return "metal::ilogb";
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
                return "metal::mix";
            case ("Exp2", 1):
                return "metal::exp2";
            case ("Exp10", 1):
                return "metal::exp10";
            default:
                return null;
        }
    }
}
