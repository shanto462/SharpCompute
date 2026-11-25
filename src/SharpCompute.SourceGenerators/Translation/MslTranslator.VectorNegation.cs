using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// The negation of an integer vector, which is emitted as the prelude's <c>__ms_neg(v)</c> instead
/// of <c>-v</c> to work around a miscompile in Apple's Metal compiler.
/// </summary>
/// <remarks>
/// <para>
/// The bug. Valid MSL gives a wrong result, silently, when all of this holds: an integer vector is
/// negated (<c>-v</c>, or anything the optimizer turns into <c>0 - v</c>); at least one lane of that
/// negation is never read; and the same <c>v</c> is subtracted in an add/sub/mul chain elsewhere, as
/// in <c>t += C; t *= 3; t -= v</c>. The chain's result then loses the unread lane: its store is
/// deleted. The optimizer marks the unread lane of the negation as poison (which is legal), then
/// reuses that instruction as "<c>-v</c>" for the chain (which is not), and the poisoned lane of the
/// chain is dropped. It happens at -O1 and above, in every math mode and MSL version, compiled at
/// run time or ahead of time with <c>xcrun metal</c>, for vectors of every integer type (signed and
/// unsigned, 8 to 64 bits). Vectors of floats and halves are not affected, and neither are scalars.
/// </para>
/// <para>
/// The workaround. <c>__ms_neg</c> negates each lane as a scalar and builds the vector from the
/// results, so there is no vector negation for the optimizer to reuse. It is a function, so its
/// operand is evaluated once, and each lane is negated in the unsigned type of its width, so
/// <c>MinValue</c> wraps as it does in C#.
/// </para>
/// <para>
/// What goes through it: unary minus on a vector of any integer type, and, on the <c>int</c> and
/// <c>uint</c> vectors, the operators that are a negation by a constant: <c>0 - v</c> with an
/// all-zero left operand, <c>v * -1</c> and <c>-1 * v</c> with an all-minus-one operand (for
/// <c>uint</c>, all bits set), and <c>v / -1</c>, each of which the optimizer rewrites to
/// <c>0 - v</c>. A constant is one <see cref="ConstantFolder"/> can evaluate, which covers
/// <c>int</c> and <c>uint</c> components.
/// </para>
/// <para>
/// What does not: a negation the generator cannot see. The optimizer also derives one from
/// <c>~v + 1</c>, from <c>v - 2 * v</c>, from a constant operand of which only some lanes are zero
/// or minus one (when the other lanes are the ones that are never read), and from a <c>*= -1</c>
/// whose target has side effects. Those shapes keep their native operators and stay exposed until
/// Apple fixes the compiler. A program-scope initializer also keeps the native operator: it may not
/// call a function, and its operands are constants, which the compiler folds.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    private static bool IsIntegerVector(GpuType? type) =>
        type is { Kind: GpuTypeKind.Vector, IsFloating: false, IsBoolean: false };

    /// <summary>True for the vectors whose constants <see cref="ConstantFolder"/> evaluates: those of <c>int</c> and <c>uint</c>.</summary>
    private static bool HasFoldableLanes(GpuType type) =>
        type.MslName is "int2" or "int3" or "int4" or "uint2" or "uint3" or "uint4";

    /// <summary>
    /// <c>__ms_neg(v)</c> when <paramref name="operation"/> negates an integer vector (see the remarks
    /// on this class for the forms); otherwise null. With <paramref name="unsigned"/>, the operand is
    /// computed in that unsigned vector type and so is the result, for a chain of wrapped arithmetic
    /// (see <see cref="UnsignedArithmetic"/>): <c>__ms_neg(uint3(v))</c>.
    /// </summary>
    private Expr? VectorNegation(IOperation operation, string? unsigned = null)
    {
        if (NegatedOperand(operation, out IOperation? constant) is not { } negated || _types.Resolve(operation.Type) is not { } vector)
        {
            return null;
        }

        if (constant is not null)
        {
            // The constant operand (the 0 of `0 - v`) is not emitted, but it is still translated: a
            // construct it must not contain is reported as it would be anywhere else.
            _ = Expression(constant);
        }

        string operand = unsigned is null ? Expression(negated).Text : UnsignedOperand(negated).Text;

        if (_types.Resolve(negated.Type) is not { Kind: GpuTypeKind.Vector })
        {
            // `new Int3(0) - s` negates the scalar in every lane: `__ms_neg(int3(s))`.
            operand = Call(unsigned ?? vector.MslName, operand).Text;
        }

        UsesPrelude = true;
        return Call(MslNames.Negate, operand);
    }

    /// <summary>
    /// <c>t = __ms_neg(t)</c> for a <c>t *= -1</c> or <c>t /= -1</c> on an <c>int</c> or <c>uint</c>
    /// vector, or null when <paramref name="operation"/> is not one. That names the target twice, so
    /// a target with side effects keeps its native operator.
    /// </summary>
    private Expr? NegatingCompoundAssignment(ICompoundAssignmentOperation operation, Expr target)
    {
        if (_function.IsGlobalInitializer || operation.IsLifted || !IsNativeArithmetic(operation.OperatorMethod) ||
            _types.Resolve(operation.Target.Type) is not { } type || !IsIntegerVector(type) || !HasFoldableLanes(type) ||
            !IsNegatingFactor(operation.OperatorKind, type, operation.Value) || HasSideEffects(operation.Target))
        {
            return null;
        }

        // As in VectorNegation: translated for its diagnostics, not emitted.
        _ = Expression(operation.Value);

        UsesPrelude = true;
        return new Expr(target.Operand(Expr.Unary) + " = " + Call(MslNames.Negate, target.Text).Text, Expr.Assignment);
    }

    /// <summary>
    /// The operand <paramref name="operation"/> negates when it is a negation of an integer vector,
    /// otherwise null. <paramref name="constant"/> is the other operand of a binary form, the constant
    /// that makes it a negation.
    /// </summary>
    private IOperation? NegatedOperand(IOperation operation, out IOperation? constant)
    {
        constant = null;

        if (_function.IsGlobalInitializer || _types.Resolve(operation.Type) is not { } type || !IsIntegerVector(type))
        {
            return null;
        }

        switch (operation)
        {
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Minus, IsLifted: false } unary when IsNativeArithmetic(unary.OperatorMethod):
                return unary.Operand;

            case IBinaryOperation { IsLifted: false } binary when IsNativeArithmetic(binary.OperatorMethod) && HasFoldableLanes(type):
                if (binary.OperatorKind == BinaryOperatorKind.Subtract && IsSplat(binary.LeftOperand, 0))
                {
                    constant = binary.LeftOperand;
                    return binary.RightOperand;
                }

                if (IsNegatingFactor(binary.OperatorKind, type, binary.RightOperand))
                {
                    constant = binary.RightOperand;
                    return binary.LeftOperand;
                }

                if (binary.OperatorKind == BinaryOperatorKind.Multiply && IsSplat(binary.LeftOperand, -1))
                {
                    constant = binary.LeftOperand;
                    return binary.RightOperand;
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// True when <paramref name="right"/> as the right operand of <paramref name="kind"/> negates the
    /// left one: a multiplication by minus one in every lane, or a signed division by it. (An unsigned
    /// division by <c>uint.MaxValue</c> is no negation.)
    /// </summary>
    private bool IsNegatingFactor(BinaryOperatorKind kind, GpuType vector, IOperation right) =>
        (kind == BinaryOperatorKind.Multiply || (kind == BinaryOperatorKind.Divide && !vector.IsUnsigned)) && IsSplat(right, -1);

    /// <summary>
    /// True when <paramref name="operand"/> is a compile-time constant, a scalar or a vector, whose
    /// every component has the bits of <paramref name="value"/> (so -1 is <c>uint.MaxValue</c> for a
    /// <c>uint</c>).
    /// </summary>
    private bool IsSplat(IOperation operand, int value) =>
        Folder.TryEvaluate(operand, out FoldedValue? folded, out _) &&
        folded is { Kind: ScalarKind.Int or ScalarKind.UInt } &&
        folded.Components.All(component => component == (folded.Kind == ScalarKind.UInt ? unchecked((uint)value) : (double)value));
}
