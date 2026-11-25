using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// Signed integer arithmetic that wraps as it does in unchecked C#. A signed <c>+</c>, <c>-</c>,
/// <c>*</c> or negation that overflows wraps in C#; in MSL (C++14) it is undefined, and the Metal
/// compiler optimizes on the assumption that it does not happen: <c>(a * b) / b</c> becomes
/// <c>a</c>, <c>a + 1 &gt; a</c> becomes <c>true</c>. So these operators are computed in the unsigned
/// type of the same width, whose arithmetic is defined modulo 2^n, and converted back:
/// <c>a * b + c</c> on <c>int</c>s is <c>int(uint(a) * uint(b) + uint(c))</c>. The conversion back
/// keeps the bits (Apple GPUs are two's complement, as C# is), so the result is C#'s, bit for bit.
/// </summary>
/// <remarks>
/// <para>
/// This applies to the operators whose type is <c>int</c>, <c>long</c> or an <c>int</c> vector
/// (<c>IntN</c>). C# has no arithmetic on the 8- and 16-bit integers: it promotes them to
/// <c>int</c>, so <c>s1 * s2</c> on <c>short</c>s is an <c>int</c> operator and is covered, while
/// <c>s++</c> on a <c>short</c> is <c>int</c> arithmetic that cannot overflow (MSL promotes the same
/// way) followed by a truncation, which wraps in both languages and stays native. Their <c>*=</c> is
/// the exception: see <see cref="SignedCompoundAssignment"/>.
/// </para>
/// <para>
/// A chain stays unsigned: an operand that is itself wrapped arithmetic of the same type is used
/// without converting it back and forth, a literal operand is an unsigned literal (<c>12345u</c>),
/// and an operand converted from another integer type is converted to the unsigned type directly
/// (<c>ulong(i)</c> for <c>(long)i</c>), which gives the same bits as C#'s conversion.
/// </para>
/// <para>
/// A compound assignment or an increment assigns the wrapped result: <c>x += y</c> is
/// <c>x = int(uint(x) + uint(y))</c>, which names the target twice. That is only done where nothing
/// in the expression has side effects. Otherwise, and for an increment whose value is used, the
/// native operator is kept on the target viewed as its unsigned type
/// (<c>reinterpret_cast&lt;device uint&amp;&gt;(counts[Bin(v)]) += 1u</c>): the target is evaluated once,
/// and the parts are evaluated in the same order as before. On the 8- and 16-bit targets only
/// <c>*=</c> can overflow, and it multiplies as <c>uint</c> (see <see cref="SignedCompoundAssignment"/>).
/// </para>
/// <para>
/// The casts cost no instructions: signed and unsigned addition, subtraction and multiplication are
/// the same machine operations. What changes is only what the compiler may assume about the result.
/// Division, remainder, shifts and comparisons are unchanged. MSL defines a shift by the low bits
/// of its count, as C# does; and a division by zero or of <c>MinValue</c> by -1, which throws in C#,
/// gives an unspecified value in MSL rather than undefined behavior, so there is no C# result to
/// reproduce and nothing the compiler may assume.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    /// <summary>
    /// The unsigned MSL type a signed integer operator of <paramref name="type"/> is computed in:
    /// <c>uint</c> for <c>int</c>, <c>ulong</c> for <c>long</c>, <c>uintN</c> for <c>intN</c>.
    /// Null for every other type, including the 8- and 16-bit integers (see the remarks above).
    /// </summary>
    private string? UnsignedArithmeticType(ITypeSymbol? type) => _types.Resolve(type) switch
    {
        { Kind: GpuTypeKind.Scalar, MslName: "int" } => "uint",
        { Kind: GpuTypeKind.Scalar, MslName: "long" } => "ulong",
        { Kind: GpuTypeKind.Vector, IsFloating: false, IsUnsigned: false, IsBoolean: false } vector
            when vector.MslName.StartsWith("int", System.StringComparison.Ordinal) => "u" + vector.MslName,
        _ => null,
    };

    private static bool IsWrappingOperator(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply;

    /// <summary>True for a native (non-user) operator method, or none: the operators MSL has.</summary>
    private bool IsNativeArithmetic(IMethodSymbol? method) => method is null || IsNativeOperator(method);

    /// <summary>
    /// The MSL of a signed <c>+</c>, <c>-</c>, <c>*</c> or negation, or null when
    /// <paramref name="operation"/> is not one: <c>int(uint(a) * uint(b))</c>.
    /// </summary>
    private Expr? SignedArithmetic(IOperation operation)
    {
        if (UnsignedArithmeticType(operation.Type) is not { } unsigned || UnsignedArithmetic(operation, unsigned) is not { } wrapped)
        {
            return null;
        }

        return Call(_types.Resolve(operation.Type)!.MslName, wrapped.Text);
    }

    /// <summary>
    /// <paramref name="operation"/> computed in <paramref name="unsigned"/>, without the conversion
    /// back, when it is a signed <c>+</c>, <c>-</c>, <c>*</c> or negation of that width; otherwise null.
    /// </summary>
    private Expr? UnsignedArithmetic(IOperation operation, string unsigned)
    {
        switch (operation)
        {
            case IBinaryOperation binary
                when !binary.IsLifted && IsWrappingOperator(binary.OperatorKind) && IsNativeArithmetic(binary.OperatorMethod) &&
                     UnsignedArithmeticType(binary.Type) == unsigned &&
                     TryGetBinaryOperator(binary.OperatorKind, out string text, out int precedence):
            {
                Expr left = UnsignedOperand(binary.LeftOperand);
                Expr right = UnsignedOperand(binary.RightOperand);

                return new Expr(BinaryOperand(left, precedence, isRight: false) + " " + text + " " + BinaryOperand(right, precedence, isRight: true), precedence);
            }

            // `-5` is a constant that C# has already negated; it stays a literal.
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Minus } unary
                when !unary.IsLifted && IsNativeArithmetic(unary.OperatorMethod) && UnaryOperandIsNotALiteral(unary) &&
                     UnsignedArithmeticType(unary.Type) == unsigned:
            {
                string operand = UnsignedOperand(unary.Operand).Operand(Expr.Unary);

                // Avoid gluing `-` and `-x` into a decrement.
                return new Expr("-" + (operand[0] == '-' ? "(" + operand + ")" : operand), Expr.Unary);
            }

            default:
                return null;
        }
    }

    private static bool UnaryOperandIsNotALiteral(IUnaryOperation unary) => unary.Operand is not ILiteralOperation;

    /// <summary>An operand of wrapped arithmetic, in the unsigned type of its own width (<c>uint</c> for the <c>int</c> of <c>int2 * 3</c>).</summary>
    private Expr UnsignedOperand(IOperation operand)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if (UnsignedArithmeticType(operand.Type) is not { } unsigned)
        {
            // Not reached for the operators above, whose operands C# has converted to int, long or intN.
            return Expression(operand);
        }

        if (UnsignedArithmetic(operand, unsigned) is { } inner)
        {
            // Keep the parentheses the user wrote, as Expression does.
            return !operand.IsImplicit && operand.Syntax.Parent is ParenthesizedExpressionSyntax ? inner.Parenthesize() : inner;
        }

        if (UnsignedLiteral(operand, unsigned) is { } literal)
        {
            return literal;
        }

        // Any integer conversion to the operator's type agrees with the conversion to the unsigned
        // type of that width modulo 2^n, so `ulong(i)` stands for `ulong(long(i))`.
        if (operand is IConversionOperation { OperatorMethod: null } conversion &&
            _types.Resolve(conversion.Operand.Type) is { IsIntegerScalar: true } source &&
            _types.Resolve(conversion.Type) is { IsIntegerScalar: true })
        {
            // A value that already has the unsigned type (`(int)u` for a uint) needs no conversion at all.
            Expr converted = Expression(conversion.Operand);
            return source.MslName == unsigned ? converted : Call(unsigned, converted.Text);
        }

        Expr signed = Expression(operand);

        // `ThreadIds.X` is `int(__tid.x)`, the conversion of a uint built-in: use the built-in itself.
        if (operand is IPropertyReferenceOperation { Property: var property } && TryGetBuiltin(property, out _, out string swizzle) &&
            _types.Resolve(property.Type) is { } builtinType && signed.Text.StartsWith(builtinType.MslName + "(", System.StringComparison.Ordinal) &&
            signed.Text.EndsWith(")", System.StringComparison.Ordinal) && "u" + builtinType.MslName == unsigned)
        {
            string builtin = signed.Text.Substring(builtinType.MslName.Length + 1, signed.Text.Length - builtinType.MslName.Length - 2);
            return new Expr(builtin, swizzle == CountMember ? Expr.Multiplicative : Expr.Primary);
        }

        return Call(unsigned, signed.Text);
    }

    /// <summary>A literal operand (<c>3</c>, <c>-7</c>, <c>5L</c>, or a constant C# widened to long) as a literal of the unsigned type.</summary>
    private static Expr? UnsignedLiteral(IOperation operand, string unsigned)
    {
        // What Conversion writes as a literal: a literal, or a constant C# widened implicitly. A
        // constant field read as it is keeps its name (`::Limit`), as everywhere else.
        if (!IsLiteral(operand) || operand is IFieldReferenceOperation || operand.ConstantValue is not { HasValue: true, Value: { } value })
        {
            return null;
        }

        switch (value)
        {
            case int int32 when unsigned == "uint":
                return int32 >= 0
                    ? new Expr(int32.ToString(System.Globalization.CultureInfo.InvariantCulture) + "u", Expr.Primary)
                    : Call(unsigned, Int32Literal(int32).Text);
            case long int64 when unsigned == "ulong":
                return int64 >= 0
                    ? new Expr(int64.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL", Expr.Primary)
                    : Call(unsigned, Int64Literal(int64).Text);
            default:
                return null;
        }
    }

    /// <summary>
    /// A <c>+=</c>, <c>-=</c> or <c>*=</c> on an <c>int</c>, <c>long</c> or <c>intN</c> target. Where
    /// nothing in it has side effects, <c>x = int(uint(x) + uint(y))</c>. Otherwise the target is
    /// viewed as its unsigned type and the compound operator kept, <c>reinterpret_cast&lt;device
    /// uint&amp;&gt;(counts[Bin(v)]) += uint(Weight(v))</c>, which evaluates the target once and the parts
    /// in the same order as the native <c>+=</c> did (a signed and an unsigned integer may alias).
    /// </summary>
    /// <remarks>
    /// On an 8- or 16-bit target, C# only accepts a value of at most the target's width, and both are
    /// promoted to <c>int</c>, as in MSL. A sum or difference of two such values cannot overflow an
    /// <c>int</c>, so <c>+=</c> and <c>-=</c> stay native. A product can: <c>65535 * 65535</c> on
    /// <c>ushort</c>s is beyond <c>int.MaxValue</c>. So <c>*=</c> keeps the native operator with the
    /// value converted to <c>uint</c> (<c>u *= uint(v)</c>), which makes the whole product unsigned;
    /// the truncation back to the target's width then wraps in MSL as in C#. Null for every other
    /// compound assignment.
    /// </remarks>
    private Expr? SignedCompoundAssignment(ICompoundAssignmentOperation operation, Expr target, string text, int precedence)
    {
        if (operation.OperatorKind == BinaryOperatorKind.Multiply && operation.OperatorMethod is null &&
            _types.Resolve(operation.Target.Type) is { Kind: GpuTypeKind.Scalar, IsFloating: false, Size: < 4 } &&
            UnsignedArithmeticType(operation.Value.Type) == "uint")
        {
            return new Expr(target.Operand(Expr.Unary) + " *= " + UnsignedOperand(operation.Value).Operand(Expr.Assignment), Expr.Assignment);
        }

        if (!IsWrappingOperator(operation.OperatorKind) ||
            _types.Resolve(operation.Target.Type) is not { } targetType ||
            UnsignedArithmeticType(operation.Target.Type) is not { } unsigned)
        {
            return null;
        }

        Expr value = UnsignedOperand(operation.Value);

        if (HasSideEffects(operation.Target) || HasSideEffects(operation.Value))
        {
            if (UnsignedLvalue(operation.Target, target, unsigned) is { } lvalue)
            {
                Expr assignment = new(lvalue + " " + text + "= " + value.Operand(Expr.Assignment), Expr.Assignment);
                return operation.Parent is IExpressionStatementOperation ? assignment : Call(targetType.MslName, assignment.Text);
            }

            if (HasSideEffects(operation.Target))
            {
                // Naming the target twice would repeat its side effects.
                return Unsupported(operation, $"'{text}=' on this target when its expression has side effects");
            }
        }

        string result = Call(unsigned, target.Text).Text + " " + text + " " + BinaryOperand(value, precedence, isRight: true);

        return new Expr(target.Operand(Expr.Unary) + " = " + Call(targetType.MslName, result).Text, Expr.Assignment);
    }

    /// <summary>
    /// <c>++</c> or <c>--</c> on an <c>int</c> or <c>long</c>. As a statement, on a target without side
    /// effects, <c>i = int(uint(i) + 1u)</c>; otherwise the native operator on the target viewed as its
    /// unsigned type, <c>int(reinterpret_cast&lt;thread uint&amp;&gt;(n)++)</c>, which gives the old or new
    /// value as C# does and evaluates the target once. Null for the other types, whose increments stay
    /// native (an 8- or 16-bit increment is <c>int</c> arithmetic that cannot overflow, then a truncation).
    /// </summary>
    private Expr? SignedIncrementOrDecrement(IIncrementOrDecrementOperation operation, Expr target)
    {
        if (operation.OperatorMethod is not null || UnsignedArithmeticType(operation.Type) is not { } unsigned || _types.Resolve(operation.Type) is not { Kind: GpuTypeKind.Scalar } type)
        {
            return null;
        }

        string text = operation.Kind == OperationKind.Increment ? "++" : "--";
        string one = unsigned == "ulong" ? "1UL" : "1u";

        // A statement (`i++;`, the increment of a `for`) does not use the value.
        bool isStatement = operation.Parent is IExpressionStatementOperation;

        if (isStatement && !HasSideEffects(operation.Target))
        {
            string result = Call(unsigned, target.Text).Text + (text == "++" ? " + " : " - ") + one;
            return new Expr(target.Operand(Expr.Unary) + " = " + Call(type.MslName, result).Text, Expr.Assignment);
        }

        if (UnsignedLvalue(operation.Target, target, unsigned) is { } lvalue)
        {
            Expr native = operation.IsPostfix ? new Expr(lvalue + text, Expr.Primary) : new Expr(text + lvalue, Expr.Unary);
            return isStatement ? native : Call(type.MslName, native.Text);
        }

        if (HasSideEffects(operation.Target))
        {
            return Unsupported(operation, $"'{text}' on this target when its expression has side effects");
        }

        // A target whose address space is not known here, with its value used: assign, then take the
        // old value back out if it is a postfix.
        string assigned = "(" + target.Operand(Expr.Unary) + " = " + Call(type.MslName, Call(unsigned, target.Text).Text + (text == "++" ? " + " : " - ") + one).Text + ")";
        return operation.IsPostfix
            ? Call(type.MslName, Call(unsigned, assigned).Text + (text == "++" ? " - " : " + ") + one)
            : new Expr(assigned, Expr.Primary);
    }

    /// <summary>
    /// The target viewed as an lvalue of the unsigned type, <c>reinterpret_cast&lt;device uint&amp;&gt;(data[i])</c>,
    /// or null when its address space is not known here. A <c>packed_intN</c> in boundary storage is
    /// viewed as a <c>packed_uintN</c>.
    /// </summary>
    private string? UnsignedLvalue(IOperation target, Expr text, string unsigned)
    {
        // A component (or swizzle) of a vector binds no reference, but the vector does: view the
        // whole vector as unsigned and take the component of that, `reinterpret_cast<...>(v).x`.
        if (target is IPropertyReferenceOperation { Property.IsIndexer: false, Instance: { } vector } component &&
            _types.Resolve(vector.Type) is { Kind: GpuTypeKind.Vector } vectorType &&
            UnsignedArithmeticType(vector.Type) is { } unsignedVector &&
            text.Text.EndsWith("." + component.Property.Name.ToLowerInvariant(), System.StringComparison.Ordinal) &&
            StorageOf(vector) is { } vectorStorage)
        {
            string swizzle = "." + component.Property.Name.ToLowerInvariant();
            string instance = text.Text.Substring(0, text.Text.Length - swizzle.Length);
            string vectorView = vectorStorage.Boundary && vectorType.BoundaryName != vectorType.MslName ? "packed_" + unsignedVector : unsignedVector;
            return "reinterpret_cast<" + vectorStorage.AddressSpace + " " + vectorView + "&>(" + instance + ")" + swizzle;
        }

        if (StorageOf(target) is not { } storage || _types.Resolve(target.Type) is not { } type)
        {
            return null;
        }

        string view = storage.Boundary && type.Kind == GpuTypeKind.Vector && type.BoundaryName != type.MslName ? "packed_" + unsigned : unsigned;
        return "reinterpret_cast<" + storage.AddressSpace + " " + view + "&>(" + text.Text + ")";
    }

    /// <summary>
    /// Where an lvalue lives: its address space, and whether it is boundary storage (a buffer element,
    /// a group-shared element or a struct field, which hold <c>packed_*</c> vectors) rather than a
    /// local, a parameter or an element of a local array. Null for anything else, a vector component
    /// among them.
    /// </summary>
    private (string AddressSpace, bool Boundary)? StorageOf(IOperation target)
    {
        switch (target)
        {
            case ILocalReferenceOperation:
            case IParameterReferenceOperation parameter when _function.Names.ContainsKey(parameter.Parameter):
                return ("thread", false);

            case IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } buffer } when TypeResolver.IsBufferType(buffer.Type, out _):
                return ("device", true);

            case IArrayElementReferenceOperation element:
                return GroupSharedReference(element.ArrayReference) is not null ? ("threadgroup", true) : ("thread", false);

            case IPropertyReferenceOperation { Property.IsIndexer: true, Instance.Type: INamedTypeSymbol { Name: "Span", ContainingNamespace.Name: "System" } }:
                return ("thread", false);

            case IFieldReferenceOperation { Instance: IInstanceReferenceOperation } when _function.This is { } self:
                return self.Mode switch
                {
                    ReceiverMode.Device => ("device", true),
                    ReceiverMode.Threadgroup => ("threadgroup", true),
                    _ => ("thread", true),
                };

            case IFieldReferenceOperation { Instance: { } instance } when _types.Resolve(instance.Type) is { Kind: GpuTypeKind.Struct }:
                return StorageOf(instance) is { } outer ? (outer.AddressSpace, true) : null;

            // An auto-property of a user struct is its backing field.
            case IPropertyReferenceOperation { Property.IsIndexer: false, Instance: { } owner } when _types.Resolve(owner.Type) is { Kind: GpuTypeKind.Struct }:
                return StorageOf(owner) is { } container ? (container.AddressSpace, true) : null;

            case IInlineArrayAccessOperation { Instance: { } array }:
                return StorageOf(array);

            default:
                return null;
        }
    }
}
