using System;
using System.Globalization;
using System.Linq;
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
        // Not around a conversion or a constructor, which are already primary expressions and where
        // they can change the parse: C++ reads `(float(uint(x))) * y` as a C-style cast of `*y` to
        // the function type `float(uint x)`, so `((float)(uint)x) * y` did not compile.
        if (!operation.IsImplicit && operation.Syntax.Parent is ParenthesizedExpressionSyntax && !IsTypeNamedPrimary(operation, result))
        {
            result = result.Parenthesize();
        }

        return result;
    }

    /// <summary>A conversion or a constructor whose MSL is a type name applied to its operands: <c>float(x)</c>, <c>float3(a, b, c)</c>.</summary>
    private static bool IsTypeNamedPrimary(IOperation operation, Expr result) =>
        result.Precedence == Expr.Primary && operation is IConversionOperation or IObjectCreationOperation;

    private Expr ExpressionCore(IOperation operation)
    {
        if (operation is IInvalidOperation invalid)
        {
            // The C# compiler reports this code. The cases with something to add are an atomic
            // operation on an element type that has no atomic in MSL, and a wave matrix loaded from
            // or stored to elements of another type than its own.
            ExplainUnboundAtomicCall(invalid);
            ExplainUnboundWaveMatrixCall(invalid);
            SawInvalidCode = true;
            return Expr.Error;
        }

        // `checked(a + b)` throws on the CPU and cannot on the GPU (SCG0052).
        if (ReportIfChecked(operation))
        {
            return Expr.Error;
        }

        if (operation is IDeclarationExpressionOperation { Expression: ILocalReferenceOperation hoisted } &&
            _function.HoistedLocals.Contains(hoisted.Local))
        {
            // `out float x`: declared before the statement (see HoistDeclarations).
            return new Expr(_function.Names[hoisted.Local], Expr.Primary);
        }

        // Before anything that recognizes a property or indexer by its receiver (buffers, textures,
        // vectors, local and threadgroup arrays): an instance extension member only looks like one.
        if (operation is IPropertyReferenceOperation { Property: var extensionMember } && TypeResolver.IsInstanceExtensionMember(extensionMember))
        {
            return UnsupportedMember(operation, extensionMember, InstanceExtensionMembers);
        }

        // The one array a shader can index: threadgroup memory. Any other array is rejected just below.
        if (operation is IArrayElementReferenceOperation element && GroupSharedElement(element) is { } groupSharedElement)
        {
            return groupSharedElement;
        }

        // Local arrays: their elements, inline array elements, and everything a local array cannot do.
        if (LocalArrayExpression(operation) is { } localArray)
        {
            return localArray;
        }

        if (operation is IInstanceReferenceOperation && _function.This is not null)
        {
            // In a member of a user struct, `this` is the struct the function was called on.
            return new Expr(MslNames.This, Expr.Primary);
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

        if (_function.This is { } self && IsKeptParameter(operation.Parameter, self.Method.ContainingType) && KeptParameterField(self.Type, operation.Parameter) is { } kept)
        {
            // A primary-constructor parameter that a method of the struct reads is a (hidden) field of it.
            return BoundaryRead(operation, kept.Type, new Expr(MslNames.This + "." + kept.MslName, Expr.Primary));
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
        return BoundaryRead(operation, capture.Type, new Expr(MslNames.Constants + "." + capture.MslName, Expr.Primary));
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
            case long int64:
                return Int64Literal(int64);
            case ulong uint64:
                return new Expr(uint64.ToString(CultureInfo.InvariantCulture) + "UL", Expr.Primary);

            // MSL has no literal suffix for the small integers, and a bare `5` is an int: as an argument
            // of an overloaded function (`metal::min(b, 5)`) it would pick another overload than the
            // C# call did. The cast gives the literal the type it has in C#.
            case byte or sbyte or short or ushort when type.Kind == GpuTypeKind.Scalar:
                return Call(type.MslName, System.Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
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

    /// <summary>A 64-bit literal: MSL's <c>long</c> is 64 bits wide and <c>L</c> is its suffix.</summary>
    private static Expr Int64Literal(long value)
    {
        if (value == long.MinValue)
        {
            // As for int: `-9223372036854775808L` negates a literal that does not fit in a long.
            return new Expr("-9223372036854775807L - 1L", Expr.Additive);
        }

        return value < 0
            ? new Expr(value.ToString(CultureInfo.InvariantCulture) + "L", Expr.Unary)
            : new Expr(value.ToString(CultureInfo.InvariantCulture) + "L", Expr.Primary);
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
            case GpuTypeKind.Vector when type.IsBoolean:
                return new Expr(type.MslName + "(false)", Expr.Primary);
            case GpuTypeKind.Scalar:
            case GpuTypeKind.Vector:
                return new Expr(type.MslName + "(0)", Expr.Primary);
            case GpuTypeKind.Struct:
                return new Expr(type.MslName + "{}", Expr.Primary);
            case GpuTypeKind.WaveMatrix:
                return WaveMatrixZero(type);
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
            // User-defined conversions between vector/scalar types become MSL constructor-style casts
            // (System.Half declares its conversions to and from the integers as operators); one that a
            // type of the user's declares in source becomes a function.
            return target.IsNumeric && IsNativeOperator(method)
                ? NumericCast(operation, target)
                : UserConversion(operation, method, target);
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

            return NumericCast(operation, target);
        }

        return Unsupported(
            operation,
            $"a conversion from '{(operation.Operand.Type is { } source ? TypeResolver.Display(source) : "?")}' to '{TypeResolver.Display(operation.Type!)}'");
    }

    /// <summary>
    /// A conversion between scalar or vector types: the MSL constructor-style cast, <c>int(x)</c>.
    /// Integer conversions truncate or extend as unchecked C# does. A float or a half converts to an
    /// integer through <see cref="SaturatingCast"/>, which saturates as C# does.
    /// </summary>
    private Expr NumericCast(IConversionOperation operation, GpuType target)
    {
        if (target is { Kind: GpuTypeKind.Scalar or GpuTypeKind.Vector, IsFloating: false, IsBoolean: false }
            && _types.Resolve(operation.Operand.Type) is { Kind: GpuTypeKind.Scalar or GpuTypeKind.Vector, IsFloating: true } source)
        {
            return SaturatingCast(operation, source, target);
        }

        if (target is { Kind: GpuTypeKind.Scalar or GpuTypeKind.Vector, IsFloating: true } && target.MslName.StartsWith("half", StringComparison.Ordinal)
            && _types.Resolve(operation.Operand.Type) is { Kind: GpuTypeKind.Scalar or GpuTypeKind.Vector, IsFloating: true } wide
            && wide.MslName.StartsWith("float", StringComparison.Ordinal))
        {
            return HalfCast(operation, target);
        }

        return Call(target.MslName, Expression(operation.Operand).Text);
    }

    /// <summary>
    /// A conversion of a float to a half, scalar or vector: <c>__ms_to_half(x)</c>, the helper of
    /// <c>MslPrelude.Conversions</c>. C# rounds to the nearest half, so a magnitude of 65520 or more is
    /// ±∞, and so does an Apple GPU when it converts. Where the Metal compiler converts a constant
    /// itself, one beyond the range of a half becomes 65504, the largest half, and it moves the
    /// conversion to the constants of a max, a min or a select to do that:
    /// <c>half(metal::max(x, 1e30f))</c> was 65504, and so was <c>half(float(__ms_sat_int(x)))</c>
    /// for <c>x</c> = 3e9. The helper selects the infinity for such a magnitude itself.
    /// </summary>
    /// <remarks>
    /// A constant within the range of a half, which the compiler converts correctly, keeps the plain
    /// cast: <c>half(0.5f)</c>. A program-scope initializer must not call a function, so there the
    /// selection is written out, per lane for a vector.
    /// </remarks>
    private Expr HalfCast(IConversionOperation operation, GpuType target)
    {
        Expr operand = Expression(operation.Operand);

        if (IsConstantInHalfRange(operation.Operand))
        {
            return Call(target.MslName, operand.Text);
        }

        if (!_function.IsGlobalInitializer)
        {
            string helper = target.Kind == GpuTypeKind.Scalar ? MslNames.ToHalf : MslNames.ToHalf + target.MslName.Substring(4);
            _conversionHelpers.Add(helper);
            return Call(helper, operand.Text);
        }

        string x = operand.Precedence == Expr.Primary ? operand.Text : "(" + operand.Text + ")";

        if (target.Kind == GpuTypeKind.Scalar)
        {
            return new Expr("(" + Selected(x) + ")", Expr.Primary);
        }

        int lanes = target.MslName[4] - '0';
        return Call(target.MslName, Enumerable.Range(0, lanes).Select(lane => Selected(x + "." + "xyzw"[lane])).ToArray());

        static string Selected(string value) =>
            $"{value} >= 65520.0f ? half(INFINITY) : ({value} <= -65520.0f ? half(-INFINITY) : half({value}))";
    }

    /// <summary>
    /// True for a float constant, or a vector constructed from such constants, that is NaN or within
    /// the range of a half: one the Metal compiler converts to the half C# gives.
    /// </summary>
    private static bool IsConstantInHalfRange(IOperation operation)
    {
        while (operation is IParenthesizedOperation or IConversionOperation { IsImplicit: true, ConstantValue.HasValue: false })
        {
            operation = operation is IParenthesizedOperation parenthesized ? parenthesized.Operand : ((IConversionOperation)operation).Operand;
        }

        if (operation.ConstantValue is { HasValue: true, Value: { } value })
        {
            return value switch
            {
                float single => float.IsNaN(single) || Math.Abs(single) < 65520f,
                int or uint or long or ulong or short or ushort or byte or sbyte => Math.Abs(System.Convert.ToDouble(value, CultureInfo.InvariantCulture)) < 65520d,
                _ => false,
            };
        }

        return operation is IObjectCreationOperation { Arguments.Length: > 0 } creation
            && creation.Arguments.All(static argument => IsConstantInHalfRange(argument.Value));
    }

    /// <summary>
    /// A conversion of a float or a half, scalar or vector, to an integer: <c>__ms_sat_int(x)</c>, the
    /// helper of <c>MslPrelude.Conversions</c> for the target type. C# saturates (NaN gives 0, a value
    /// beyond the range of the target its MinValue or MaxValue) and so does <c>ForCpu</c>. MSL leaves
    /// a conversion out of range undefined; an Apple GPU saturates when it converts (except
    /// <c>long(x)</c>, which wraps), but the Metal compiler folds on the undefined cases wherever it
    /// can see or bound the operand: <c>uchar(float(i))</c> becomes a truncation of <c>i</c>,
    /// <c>int(b ? NAN : 1e10f)</c> is 0, and what is computed from <c>int(h)</c> of a half is
    /// narrowed to the ±65504 of a finite half. The helpers clamp in the float domain first, so what
    /// they convert is in range for every input.
    /// </summary>
    /// <remarks>
    /// A conversion C# folds is emitted as the constant C# folded it to, which is what <c>ForCpu</c>
    /// runs. A program-scope initializer must not call a function: there the conversion of a scalar is
    /// written out as a constant expression of comparisons, of which the compiler evaluates only the
    /// arm that is in range.
    /// </remarks>
    private Expr SaturatingCast(IConversionOperation operation, GpuType source, GpuType target)
    {
        if (target.Kind == GpuTypeKind.Scalar && operation.ConstantValue is { HasValue: true, Value: { } folded })
        {
            return Constant(folded, target, operation);
        }

        if (!_function.IsGlobalInitializer)
        {
            string helper = MslNames.Saturate + target.MslName;
            _conversionHelpers.Add(helper);
            return Call(helper, Expression(operation.Operand).Text);
        }

        if (target.Kind != GpuTypeKind.Scalar)
        {
            return NotConstant(operation, $"a conversion from a floating-point vector to '{target.MslName}' is a function call in MSL (it saturates as C# does)");
        }

        // The bounds as floats: 2^bits (2^(bits - 1) for a signed type) is exact, and so is the lower bound.
        Expr operand = source.MslName == "half" ? Call("float", Expression(operation.Operand).Text) : Expression(operation.Operand);
        string x = operand.Precedence == Expr.Primary ? operand.Text : "(" + operand.Text + ")";
        int bits = target.Size * 8;
        string above = System.Numerics.BigInteger.Pow(2, target.IsUnsigned ? bits : bits - 1).ToString(CultureInfo.InvariantCulture) + ".0f";
        string max = Bound(target.IsUnsigned ? ulong.MaxValue >> (64 - bits) : (ulong)(long.MaxValue >> (64 - bits)));
        string cast = Call(target.MslName, operand.Text).Text;

        if (target.IsUnsigned)
        {
            return Call(target.MslName, $"{x} >= {above} ? {max} : ({x} > 0.0f ? {cast} : {Bound(0)})");
        }

        string min = target.Size switch
        {
            4 => "(-2147483647 - 1)",
            8 => "(-9223372036854775807L - 1L)",
            _ => (-(1L << (bits - 1))).ToString(CultureInfo.InvariantCulture),
        };

        return Call(target.MslName, $"{x} >= {above} ? {max} : ({x} >= -{above} ? {cast} : ({x} < 0.0f ? {min} : {Bound(0)}))");

        string Bound(ulong value) => value.ToString(CultureInfo.InvariantCulture) + (target.Size, target.IsUnsigned) switch
        {
            (4, true) => "u",
            (8, false) => "L",
            (8, true) => "UL",
            _ => string.Empty,
        };
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
        if (method is null || IsNativeOperator(method))
        {
            return true;
        }

        _diagnostics.Report(
            DiagnosticDescriptors.UnsupportedMethodCall,
            operation.Syntax.GetLocation(),
            method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
            "only operators declared by [MslType] vector types are supported here; write the operation as a method call");
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

        if (operation.OperatorMethod is { } userOperator && !IsNativeOperator(userOperator))
        {
            // An operator a user type declares is a function.
            return OperatorCall(operation, userOperator, operation.Operand) ?? Expr.Error;
        }

        if (VectorNegation(operation) is { } negation)
        {
            // `-v` on an integer vector: `__ms_neg(v)`. The Metal compiler miscompiles the native
            // operator when a lane of its result is unused and `v` is subtracted elsewhere (see
            // MslTranslator.VectorNegation).
            return negation;
        }

        if (SignedArithmetic(operation) is { } wrapped)
        {
            // `-a` on a signed integer: `int(-uint(a))`, which wraps for MinValue as C# does.
            return wrapped;
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

        if (operation.OperatorMethod is { } userOperator && !IsNativeOperator(userOperator))
        {
            // An operator a user type declares is a function: `a + b` -> `::Complex_op_Addition(a, b)`.
            // (`&&` and `||` on such a type would also need its `true`/`false` operators.)
            return operation.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr
                ? Unsupported(operation, $"a user-defined '{text}' operator")
                : OperatorCall(operation, userOperator, operation.LeftOperand, operation.RightOperand) ?? Expr.Error;
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

        if (VectorNegation(operation) is { } negation)
        {
            // `0 - v`, `v * -1` and `v / -1` on an integer vector are negations as well, which the
            // Metal compiler turns into the `-v` it miscompiles: `__ms_neg(v)`.
            return negation;
        }

        if (LaneNegation(operation) is { } lanes)
        {
            // A constant that negates some lanes, `v * new Int3(1, -1, 1)`: `__ms_mul(v, C)`.
            return lanes;
        }

        if (SignedArithmetic(operation) is { } wrapped)
        {
            // Signed `+`, `-` and `*` are computed unsigned, where overflow wraps as in C#.
            return wrapped;
        }

        if (VectorSum(operation) is { } sum)
        {
            // `+` and `-` on the other integer vectors (uint, and the [MslType] ones): `__ms_add(a, b)`,
            // lane by lane, as inside the chain of an int vector (see MslTranslator.VectorNegation).
            return sum;
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
            return Call(FloatRemainder(operation.Type), left.Text, right.Text);
        }

        if (operation.OperatorKind == BinaryOperatorKind.UnsignedRightShift && leftType is not { IsUnsigned: true })
        {
            // C# promotes the smaller signed integers to int before it shifts, so only int and long get here.
            string? unsigned = leftType is { Kind: GpuTypeKind.Scalar } ? leftType.MslName switch
            {
                "int" => "uint",
                "long" => "ulong",
                _ => null,
            } : null;

            if (unsigned is null)
            {
                return Unsupported(operation, "the '>>>' operator on this type");
            }

            return Call(leftType!.MslName, unsigned + "(" + left.Text + ") >> " + BinaryOperand(right, Expr.Shift, isRight: true));
        }

        return new Expr(BinaryOperand(left, precedence, isRight: false) + " " + text + " " + BinaryOperand(right, precedence, isRight: true), precedence);
    }

    /// <summary>
    /// The MSL of a floating-point <c>%</c>: <c>metal::fmod</c>, except for a scalar <c>Half</c>, where
    /// .NET's operator (which ForCpu runs) gives a zero remainder the sign of the dividend and the
    /// GPU's half fmod gives <c>+0</c>; the prelude's <c>__ms_half_rem</c> copies the sign back. The
    /// half vectors' <c>%</c> is <c>Gpu.Fmod</c>'s on the CPU as well, which returns <c>+0</c> like the GPU.
    /// </summary>
    private string FloatRemainder(ITypeSymbol? type)
    {
        if (_types.Resolve(type) is { Kind: GpuTypeKind.Scalar, MslName: "half" })
        {
            UsesPrelude = true;
            return MslNames.HalfRemainder;
        }

        return "metal::fmod";
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

        string target = StoreTarget(operation.Target, out bool packed).Operand(Expr.Unary);
        Expr value = Expression(operation.Value);

        return new Expr(target + " = " + (packed ? Call(MslNames.Pack, value.Text).Text : value.Operand(Expr.Assignment)), Expr.Assignment);
    }

    private Expr CompoundAssignment(ICompoundAssignmentOperation operation)
    {
        if (operation.IsLifted || !TryGetBinaryOperator(operation.OperatorKind, out string text, out int precedence))
        {
            return Unsupported(operation, Humanize(operation.Syntax));
        }

        if (!CheckWritable(operation.Target))
        {
            return Expr.Error;
        }

        if (TextureElement(operation.Target) is not null)
        {
            return TextureElementModifiedInPlace(operation);
        }

        if (operation.OperatorMethod is { } userOperator && !IsNativeOperator(userOperator))
        {
            return UserCompoundAssignment(operation, userOperator);
        }

        Expr target = StoreTarget(operation.Target, out bool packed);

        if (!packed && NegatingCompoundAssignment(operation, target, text) is { } negation)
        {
            // `v *= -1` on an integer vector: `v = __ms_neg(v)`.
            return negation;
        }

        if (!packed && VectorSumAssignment(operation, target, text) is { } sum)
        {
            // `v += w` on an integer vector: `v = __ms_add(v, w)`, or `__ms_add_to(v, int3(w))`.
            return sum;
        }

        if (!packed && SignedCompoundAssignment(operation, target, text, precedence) is { } wrapped)
        {
            return wrapped;
        }

        Expr value = Expression(operation.Value);
        GpuType? targetType = _types.Resolve(operation.Target.Type);

        if (packed)
        {
            // Packed storage has no operators: unpack, apply, pack. That names the target twice.
            if (HasSideEffects(operation.Target))
            {
                return Unsupported(operation, $"'{text}=' on a matrix in a buffer or struct field whose expression has side effects");
            }

            string result = Call(MslNames.Unpack, target.Text).Text + " " + text + " " + BinaryOperand(value, precedence, isRight: true);
            return new Expr(target.Operand(Expr.Unary) + " = " + Call(MslNames.Pack, result).Text, Expr.Assignment);
        }

        if (operation.OperatorKind == BinaryOperatorKind.Remainder && IsFloating(operation.Target.Type, operation.Value.Type))
        {
            if (HasSideEffects(operation.Target))
            {
                return Unsupported(operation, "'%=' on a floating-point target whose expression has side effects");
            }

            if (FloatRemainder(operation.Target.Type) != "metal::fmod")
            {
                UsesPrelude = true;
            }

            return new Expr(target.Operand(Expr.Unary) + " = " + FloatRemainder(operation.Target.Type) + "(" + target.Text + ", " + value.Text + ")", Expr.Assignment);
        }

        if (operation.OperatorKind == BinaryOperatorKind.UnsignedRightShift && targetType is not { IsUnsigned: true })
        {
            // MSL's >> is arithmetic on a signed type. C# shifts the target as the unsigned type of its
            // promoted width (int for sbyte and short) and converts back: `t = int(uint(t) >> n)`, which
            // names the target twice.
            string? unsigned = targetType is { Kind: GpuTypeKind.Scalar } ? targetType.MslName switch
            {
                "int" or "short" or "char" => "uint",
                "long" => "ulong",
                _ => null,
            } : null;

            if (unsigned is null)
            {
                return Unsupported(operation, "the '>>>=' operator on this type");
            }

            if (HasSideEffects(operation.Target))
            {
                return Unsupported(operation, "'>>>=' on a signed target whose expression has side effects");
            }

            string promoted = targetType!.MslName is "short" or "char" ? "int(" + target.Text + ")" : target.Text;
            string shifted = unsigned + "(" + promoted + ") >> " + BinaryOperand(value, Expr.Shift, isRight: true);
            return new Expr(target.Operand(Expr.Unary) + " = " + Call(targetType.MslName, shifted).Text, Expr.Assignment);
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

        if (SignedIncrementOrDecrement(operation, target) is { } wrapped)
        {
            return wrapped;
        }

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
                case IFieldReferenceOperation { Instance: IInstanceReferenceOperation } field when _function.This is null:
                    return ReportReadOnly(target, field.Field.Name);
                case IParameterReferenceOperation parameter when _captures.TryGet(parameter.Parameter, out _) || _captures.IsFailed(parameter.Parameter):
                    return ReportReadOnly(target, parameter.Parameter.Name);
                case IFieldReferenceOperation field:
                    current = field.Instance;
                    break;
                case IInlineArrayAccessOperation element:
                    current = element.Instance;
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

    /// <summary>
    /// True when evaluating <paramref name="operation"/> may have effects, so that its MSL must not be
    /// evaluated twice: a call, an assignment, an increment, a construction, or anything else that
    /// runs code of the user's (see <see cref="CallsUserCode"/>).
    /// </summary>
    private bool HasSideEffects(IOperation operation)
    {
        if (operation is IInvocationOperation or IAssignmentOperation or IIncrementOrDecrementOperation or IObjectCreationOperation || CallsUserCode(operation))
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

    /// <summary>
    /// True for an operation that is a function call in MSL although it is no invocation in C#: a
    /// computed property or indexer (one whose getter has a body, <c>int Next { get { ... } }</c>),
    /// and an operator or conversion a user type declares. The properties and operators of
    /// <c>[MslType]</c> vectors and of SharpCompute's own types are native, not user code.
    /// </summary>
    private bool CallsUserCode(IOperation operation) => operation switch
    {
        IPropertyReferenceOperation { Property: var property } => IsComputedProperty(property) && _types.Resolve(property.ContainingType) is not { IsNumeric: true },
        IBinaryOperation { OperatorMethod: { } method } => !IsNativeOperator(method),
        IUnaryOperation { OperatorMethod: { } method } => !IsNativeOperator(method),
        IConversionOperation { OperatorMethod: { } method } => !IsNativeOperator(method),
        _ => false,
    };
}
