using System;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

internal sealed partial class MslTranslator
{
    private Expr Expression(IOperation operation)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        Expr result = ExpressionCore(operation);

        // IOperation drops parentheses; keep the ones the user wrote so the MSL reads like the C#.
        if (!operation.IsImplicit && operation.Syntax.Parent is ParenthesizedExpressionSyntax)
        {
            result = result.Parenthesize();
        }

        return result;
    }

    private Expr ExpressionCore(IOperation operation)
    {
        if (operation is IInvalidOperation)
        {
            SawInvalidCode = true;
            return Expr.Error;
        }

        if (operation is IDeclarationExpressionOperation { Expression: ILocalReferenceOperation hoisted } &&
            _function.HoistedLocals.Contains(hoisted.Local))
        {
            // `out float x`: declared before the statement (see HoistDeclarations).
            return new Expr(_function.Names[hoisted.Local], Expr.Primary);
        }

        if (DescribeUnsupported(operation) is { } construct)
        {
            return Unsupported(operation, construct);
        }

        // Member accesses validate their own types so they can report a more specific diagnostic.
        switch (operation)
        {
            case IInvocationOperation invocation:
                return Invocation(invocation);
            case IPropertyReferenceOperation property:
                return PropertyReference(property);
            case IFieldReferenceOperation field:
                return FieldReference(field);
            case IObjectCreationOperation creation:
                return ObjectCreation(creation);
        }

        GpuType? type = null;

        if (operation.Type is not null)
        {
            // Resolve quietly first: an unsupported type that flows out of an already rejected
            // declaration (`double d; ... d * 2`) has been reported there.
            type = _types.Resolve(operation.Type) ?? (ReferencesFailedSymbol(operation) ? null : ResolveType(operation.Type, operation.Syntax.GetLocation()));

            if (type is null)
            {
                return Expr.Error;
            }
        }

        switch (operation)
        {
            case ILiteralOperation literal when type is not null && literal.ConstantValue is { HasValue: true, Value: { } value }:
                return Constant(value, type, operation);

            case ILocalReferenceOperation local:
                return new Expr(_function.Names.TryGetValue(local.Local, out string? localName) ? localName : MslNames.Escape(local.Local.Name), Expr.Primary);

            case IParameterReferenceOperation parameter:
                return ParameterReference(parameter);

            case IConversionOperation conversion when type is not null:
                return Conversion(conversion, type);

            case IUnaryOperation unary:
                return Unary(unary);

            case IBinaryOperation binary:
                return Binary(binary);

            case ISimpleAssignmentOperation assignment:
                return Assignment(assignment);

            case ICompoundAssignmentOperation compound:
                return CompoundAssignment(compound);

            case IIncrementOrDecrementOperation increment:
                return IncrementOrDecrement(increment);

            case IConditionalOperation conditional:
                return Conditional(conditional);

            case IDefaultValueOperation when type is not null:
                return Zero(type, operation);

            case ISizeOfOperation when type is not null && operation.ConstantValue is { HasValue: true, Value: { } size }:
                return Constant(size, type, operation);

            default:
                return Unsupported(operation, Humanize(operation.Syntax));
        }
    }

    private GpuType? ResolveType(ITypeSymbol? type, Location location)
    {
        GpuType? result = _types.Resolve(type, _diagnostics, location);

        if (result is null && type?.TypeKind == TypeKind.Error)
        {
            SawInvalidCode = true;
        }

        return result;
    }

    private Expr ParameterReference(IParameterReferenceOperation operation)
    {
        if (_function.Names.TryGetValue(operation.Parameter, out string? name))
        {
            return new Expr(name, Expr.Primary);
        }

        if (_captures.TryGet(operation.Parameter, out Capture capture))
        {
            return CaptureReference(operation, capture);
        }

        if (_captures.IsFailed(operation.Parameter))
        {
            return Expr.Error;
        }

        return Unsupported(operation, $"a reference to the parameter '{operation.Parameter.Name}'");
    }

    /// <summary>A read of captured state, recorded so that helpers receive it as a hidden parameter.</summary>
    private Expr CaptureReference(IOperation operation, Capture capture)
    {
        if (_function.IsGlobalInitializer)
        {
            return NotConstant(operation, "captured shader state is only known when the shader is dispatched");
        }

        if (capture.Type.IsResource)
        {
            // Buffers and textures are kernel parameters, which helpers receive as hidden parameters.
            _function.Inputs.AddBuffer(capture);
            return new Expr(capture.MslName, Expr.Primary);
        }

        _function.Inputs.UsesConstants = true;
        return new Expr(MslNames.Constants + "." + capture.MslName, Expr.Primary);
    }

    private Expr Constant(object value, GpuType type, IOperation operation)
    {
        switch (value)
        {
            case float single:
                return FloatLiteral(single);
            case int int32 when type.Kind == GpuTypeKind.Scalar:
                return Int32Literal(int32);
            case uint uint32:
                return new Expr(uint32.ToString(CultureInfo.InvariantCulture) + "u", Expr.Primary);
            case bool boolean:
                return new Expr(boolean ? "true" : "false", Expr.Primary);
            default:
                return Unsupported(operation, $"a constant of type '{TypeResolver.Display(operation.Type!)}'");
        }
    }

    private static Expr Int32Literal(int value)
    {
        if (value == int.MinValue)
        {
            // `-2147483648` is the negation of a literal that does not fit in an int.
            return new Expr("-2147483647 - 1", Expr.Additive);
        }

        return value < 0
            ? new Expr(value.ToString(CultureInfo.InvariantCulture), Expr.Unary)
            : new Expr(value.ToString(CultureInfo.InvariantCulture), Expr.Primary);
    }

    /// <summary>Formats a float so that it round-trips, independent of the current culture.</summary>
    private static Expr FloatLiteral(float value)
    {
        if (float.IsNaN(value))
        {
            return new Expr("NAN", Expr.Primary);
        }

        if (float.IsPositiveInfinity(value))
        {
            return new Expr("INFINITY", Expr.Primary);
        }

        if (float.IsNegativeInfinity(value))
        {
            return new Expr("-INFINITY", Expr.Unary);
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);

        if (text.IndexOf('.') < 0 && text.IndexOf('E') < 0 && text.IndexOf('e') < 0)
        {
            text += ".0";
        }

        return new Expr(text + "f", text[0] == '-' ? Expr.Unary : Expr.Primary);
    }

    private Expr Zero(GpuType type, IOperation operation)
    {
        switch (type.Kind)
        {
            case GpuTypeKind.Bool:
                return new Expr("false", Expr.Primary);
            case GpuTypeKind.Scalar when type.MslName == "float":
                return new Expr("0.0f", Expr.Primary);
            case GpuTypeKind.Scalar when type.MslName == "int":
                return new Expr("0", Expr.Primary);
            case GpuTypeKind.Scalar when type.MslName == "uint":
                return new Expr("0u", Expr.Primary);
            case GpuTypeKind.Scalar:
            case GpuTypeKind.Vector:
                return new Expr(type.MslName + "(0)", Expr.Primary);
            case GpuTypeKind.Struct:
                return new Expr(type.MslName + "{}", Expr.Primary);
            default:
                return Unsupported(operation, $"a default value of type '{type.MslName}'");
        }
    }

    private static Expr Call(string function, params string[] arguments) =>
        new(function + "(" + string.Join(", ", arguments) + ")", Expr.Primary);

    private Expr Conversion(IConversionOperation operation, GpuType target)
    {
        Conversion conversion = operation.GetConversion();

        if (operation.OperatorMethod is { } method)
        {
            // User-defined conversions between vector/scalar types become MSL constructor-style casts.
            if (!target.IsNumeric || _types.Resolve(method.ContainingType) is not { IsNumeric: true })
            {
                _diagnostics.Report(
                    DiagnosticDescriptors.UnsupportedMethodCall,
                    operation.Syntax.GetLocation(),
                    method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    "only conversion operators declared by [MslType] vector types are supported");
                return Expr.Error;
            }

            return Call(target.MslName, Expression(operation.Operand).Text);
        }

        if (operation.Operand is IDefaultValueOperation && (conversion.IsDefaultLiteral || conversion.IsIdentity))
        {
            return Zero(target, operation);
        }

        // Identity conversions, and target-typed `new(...)`, whose operand already has the target type.
        if (conversion.IsIdentity || SymbolEqualityComparer.Default.Equals(operation.Operand.Type, operation.Type))
        {
            return Expression(operation.Operand);
        }

        // IsConstantExpression covers implicit constant conversions such as `uint u = 2;`.
        if ((conversion.IsNumeric || conversion.IsConstantExpression) && target.Kind == GpuTypeKind.Scalar)
        {
            // `float x = 1;` reads better as `1.0f` than as `float(1)`.
            if (operation.ConstantValue is { HasValue: true, Value: { } constant } && IsLiteral(operation.Operand))
            {
                return Constant(constant, target, operation);
            }

            return Call(target.MslName, Expression(operation.Operand).Text);
        }

        return Unsupported(
            operation,
            $"a conversion from '{(operation.Operand.Type is { } source ? TypeResolver.Display(source) : "?")}' to '{TypeResolver.Display(operation.Type!)}'");
    }

    private static bool IsLiteral(IOperation operation) => operation switch
    {
        ILiteralOperation => true,
        IFieldReferenceOperation { Field.IsConst: true } => true,
        IUnaryOperation { OperatorKind: UnaryOperatorKind.Minus or UnaryOperatorKind.Plus } unary => IsLiteral(unary.Operand),
        IConversionOperation { IsImplicit: true } conversion => IsLiteral(conversion.Operand),
        _ => false,
    };

    /// <summary>User-defined operators are only native in MSL when they belong to a scalar or vector type.</summary>
    private bool CheckOperatorMethod(IMethodSymbol? method, IOperation operation)
    {
        if (method is null || _types.Resolve(method.ContainingType) is { IsNumeric: true })
        {
            return true;
        }

        _diagnostics.Report(
            DiagnosticDescriptors.UnsupportedMethodCall,
            operation.Syntax.GetLocation(),
            method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
            "only operators declared by [MslType] vector types are supported");
        return false;
    }

    private Expr Unary(IUnaryOperation operation)
    {
        string? text = operation.OperatorKind switch
        {
            UnaryOperatorKind.Plus => "+",
            UnaryOperatorKind.Minus => "-",
            UnaryOperatorKind.BitwiseNegation => "~",
            UnaryOperatorKind.Not => "!",
            _ => null,
        };

        if (text is null || operation.IsLifted)
        {
            return Unsupported(operation, Humanize(operation.Syntax));
        }

        if (!CheckOperatorMethod(operation.OperatorMethod, operation))
        {
            return Expr.Error;
        }

        string operand = Expression(operation.Operand).Operand(Expr.Unary);

        // Avoid gluing `-` and `-x` into a decrement.
        if (operand[0] == text[0])
        {
            operand = "(" + operand + ")";
        }

        return new Expr(text + operand, Expr.Unary);
    }

    private static bool TryGetBinaryOperator(BinaryOperatorKind kind, out string text, out int precedence)
    {
        (text, precedence) = kind switch
        {
            BinaryOperatorKind.Add => ("+", Expr.Additive),
            BinaryOperatorKind.Subtract => ("-", Expr.Additive),
            BinaryOperatorKind.Multiply => ("*", Expr.Multiplicative),
            BinaryOperatorKind.Divide => ("/", Expr.Multiplicative),
            BinaryOperatorKind.Remainder => ("%", Expr.Multiplicative),
            BinaryOperatorKind.LeftShift => ("<<", Expr.Shift),
            BinaryOperatorKind.RightShift => (">>", Expr.Shift),
            BinaryOperatorKind.UnsignedRightShift => (">>", Expr.Shift),
            BinaryOperatorKind.And => ("&", Expr.BitwiseAnd),
            BinaryOperatorKind.Or => ("|", Expr.BitwiseOr),
            BinaryOperatorKind.ExclusiveOr => ("^", Expr.BitwiseXor),
            BinaryOperatorKind.ConditionalAnd => ("&&", Expr.LogicalAnd),
            BinaryOperatorKind.ConditionalOr => ("||", Expr.LogicalOr),
            BinaryOperatorKind.Equals => ("==", Expr.Equality),
            BinaryOperatorKind.NotEquals => ("!=", Expr.Equality),
            BinaryOperatorKind.LessThan => ("<", Expr.Relational),
            BinaryOperatorKind.LessThanOrEqual => ("<=", Expr.Relational),
            BinaryOperatorKind.GreaterThan => (">", Expr.Relational),
            BinaryOperatorKind.GreaterThanOrEqual => (">=", Expr.Relational),
            _ => (string.Empty, 0),
        };

        return precedence != 0;
    }

    /// <summary>
    /// Formats an operand of a binary operator. Besides what precedence requires, this adds the
    /// parentheses clang warns about when they are missing (<c>a &amp;&amp; b || c</c>, <c>a + b &lt;&lt; c</c>,
    /// <c>a &amp; b | c</c>), so the generated MSL compiles without warnings.
    /// </summary>
    private static string BinaryOperand(Expr operand, int precedence, bool isRight)
    {
        bool mixes = operand.Precedence < Expr.Unary && operand.Precedence != precedence && precedence switch
        {
            Expr.LogicalOr => operand.Precedence == Expr.LogicalAnd,
            Expr.BitwiseOr or Expr.BitwiseXor or Expr.BitwiseAnd or Expr.Shift => true,
            _ => false,
        };

        return mixes ? "(" + operand.Text + ")" : operand.Operand(isRight ? precedence + 1 : precedence);
    }

    private Expr Binary(IBinaryOperation operation)
    {
        if (operation.IsLifted || !TryGetBinaryOperator(operation.OperatorKind, out string text, out int precedence))
        {
            return Unsupported(operation, Humanize(operation.Syntax));
        }

        if (!CheckOperatorMethod(operation.OperatorMethod, operation))
        {
            return Expr.Error;
        }

        GpuType? leftType = _types.Resolve(operation.LeftOperand.Type);
        GpuType? rightType = _types.Resolve(operation.RightOperand.Type);

        if (precedence is Expr.Equality or Expr.Relational &&
            operation.Type?.SpecialType == SpecialType.System_Boolean &&
            (leftType?.Kind == GpuTypeKind.Vector || rightType?.Kind == GpuTypeKind.Vector))
        {
            // In MSL, comparing vectors yields a bool vector, not the single bool that C# computed.
            return Unsupported(operation, $"a '{text}' comparison of vectors that returns 'bool' (MSL compares component-wise; compare the components or use an intrinsic such as all/any)");
        }

        Expr left = Expression(operation.LeftOperand);
        Expr right = Expression(operation.RightOperand);

        if (operation.OperatorKind == BinaryOperatorKind.Remainder && IsFloating(operation.LeftOperand.Type, operation.RightOperand.Type, operation.Type))
        {
            if (_function.IsGlobalInitializer)
            {
                // metal::fmod is a call, which a program-scope initializer must not contain.
                return Folder.TryEvaluate(operation, out FoldedValue? value, out string? reason) ? Literal(value!) : NotConstant(operation, reason!);
            }

            // `%` on floating-point operands does not compile in MSL.
            return Call("metal::fmod", left.Text, right.Text);
        }

        if (operation.OperatorKind == BinaryOperatorKind.UnsignedRightShift && leftType is not { IsUnsigned: true })
        {
            if (leftType is not { Kind: GpuTypeKind.Scalar, MslName: "int" })
            {
                return Unsupported(operation, "the '>>>' operator on this type");
            }

            return Call("int", "uint(" + left.Text + ") >> " + BinaryOperand(right, Expr.Shift, isRight: true));
        }

        return new Expr(BinaryOperand(left, precedence, isRight: false) + " " + text + " " + BinaryOperand(right, precedence, isRight: true), precedence);
    }

    private bool IsFloating(params ITypeSymbol?[] types)
    {
        foreach (ITypeSymbol? type in types)
        {
            if (_types.Resolve(type) is { IsFloating: true })
            {
                return true;
            }
        }

        return false;
    }

    private Expr Assignment(ISimpleAssignmentOperation operation)
    {
        if (operation.IsRef)
        {
            return Unsupported(operation, "a ref assignment");
        }

        if (operation.Target is IDiscardOperation)
        {
            return Expression(operation.Value);
        }

        if (!CheckWritable(operation.Target))
        {
            return Expr.Error;
        }

        if (TextureElement(operation.Target) is { } pixel)
        {
            return TextureWrite(operation, pixel);
        }

        string target = Expression(operation.Target).Operand(Expr.Unary);
        string value = Expression(operation.Value).Operand(Expr.Assignment);

        return new Expr(target + " = " + value, Expr.Assignment);
    }

    private Expr CompoundAssignment(ICompoundAssignmentOperation operation)
    {
        if (operation.IsLifted || !TryGetBinaryOperator(operation.OperatorKind, out string text, out _))
        {
            return Unsupported(operation, Humanize(operation.Syntax));
        }

        if (!CheckOperatorMethod(operation.OperatorMethod, operation) || !CheckWritable(operation.Target))
        {
            return Expr.Error;
        }

        if (TextureElement(operation.Target) is not null)
        {
            return TextureElementModifiedInPlace(operation);
        }

        Expr target = Expression(operation.Target);
        Expr value = Expression(operation.Value);
        GpuType? targetType = _types.Resolve(operation.Target.Type);

        if (operation.OperatorKind == BinaryOperatorKind.Remainder && IsFloating(operation.Target.Type, operation.Value.Type))
        {
            if (HasSideEffects(operation.Target))
            {
                return Unsupported(operation, "'%=' on a floating-point target whose expression has side effects");
            }

            return new Expr(target.Operand(Expr.Unary) + " = metal::fmod(" + target.Text + ", " + value.Text + ")", Expr.Assignment);
        }

        if (operation.OperatorKind == BinaryOperatorKind.UnsignedRightShift && targetType is not { IsUnsigned: true })
        {
            return Unsupported(operation, "the '>>>=' operator on a signed type");
        }

        return new Expr(target.Operand(Expr.Unary) + " " + text + "= " + value.Operand(Expr.Assignment), Expr.Assignment);
    }

    private Expr IncrementOrDecrement(IIncrementOrDecrementOperation operation)
    {
        if (operation.IsLifted || !CheckOperatorMethod(operation.OperatorMethod, operation) || !CheckWritable(operation.Target))
        {
            return operation.IsLifted ? Unsupported(operation, Humanize(operation.Syntax)) : Expr.Error;
        }

        if (TextureElement(operation.Target) is not null)
        {
            return TextureElementModifiedInPlace(operation);
        }

        string text = operation.Kind == OperationKind.Increment ? "++" : "--";
        Expr target = Expression(operation.Target);

        return operation.IsPostfix
            ? new Expr(target.Operand(Expr.Primary) + text, Expr.Primary)
            : new Expr(text + target.Operand(Expr.Unary), Expr.Unary);
    }

    private Expr Conditional(IConditionalOperation operation)
    {
        if (operation.IsRef || operation.WhenFalse is null)
        {
            return Unsupported(operation, "a ref conditional expression");
        }

        string condition = Expression(operation.Condition).Operand(Expr.LogicalOr);
        string whenTrue = Expression(operation.WhenTrue).Operand(Expr.LogicalOr);
        string whenFalse = Expression(operation.WhenFalse).Operand(Expr.Conditional);

        return new Expr(condition + " ? " + whenTrue + " : " + whenFalse, Expr.Conditional);
    }

    /// <summary>Reports an error when the target of a write is captured state, which is constant on the GPU.</summary>
    private bool CheckWritable(IOperation target)
    {
        IOperation? current = target;

        while (current is not null)
        {
            switch (current)
            {
                case IFieldReferenceOperation { Instance: IInstanceReferenceOperation } field:
                    return ReportReadOnly(target, field.Field.Name);
                case IParameterReferenceOperation parameter when _captures.TryGet(parameter.Parameter, out _) || _captures.IsFailed(parameter.Parameter):
                    return ReportReadOnly(target, parameter.Parameter.Name);
                case IFieldReferenceOperation field:
                    current = field.Instance;
                    break;
                case IPropertyReferenceOperation { Property.IsIndexer: true } indexer when TypeResolver.IsBufferType(indexer.Instance?.Type, out _):
                    // Writing to a buffer element is what shaders are for.
                    return true;
                case IPropertyReferenceOperation { Property.IsIndexer: true } pixel when TypeResolver.IsTextureType(WithoutImplicitConversions(pixel.Instance)?.Type, out _):
                    // So is writing a pixel of a ReadWriteTexture2D (a ReadOnlyTexture2D has no setter).
                    return true;
                case IPropertyReferenceOperation property:
                    current = property.Instance;
                    break;
                default:
                    return true;
            }
        }

        return true;
    }

    private bool ReportReadOnly(IOperation target, string name)
    {
        _diagnostics.Report(DiagnosticDescriptors.CapturedStateIsReadOnly, target.Syntax.GetLocation(), name);
        return false;
    }

    private static bool HasSideEffects(IOperation operation)
    {
        if (operation is IInvocationOperation or IAssignmentOperation or IIncrementOrDecrementOperation or IObjectCreationOperation)
        {
            return true;
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            if (HasSideEffects(child))
            {
                return true;
            }
        }

        return false;
    }
}
