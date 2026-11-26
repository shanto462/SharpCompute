using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// The negation of an integer vector, which is emitted as the prelude's <c>__ms_neg(v)</c> instead
/// of <c>-v</c> to work around a miscompile in Apple's Metal compiler (reported to Apple as
/// FB25732151).
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
/// A constant that negates only some lanes is as bad: when the other lanes of the result are the
/// ones that are never read, the optimizer drops them from the constant and is left with the same
/// negation. So <c>C - v</c>, where a lane of the constant <c>C</c> is zero, is emitted as
/// <c>C + __ms_neg(v)</c> (in the unsigned type for an <c>int</c> vector, so that it wraps:
/// <c>int3(uint3(C) + __ms_neg(uint3(v)))</c>), and <c>v * C</c>, where a lane of <c>C</c> is minus
/// one, as the prelude's <c>__ms_mul(v, C)</c>, which multiplies each lane as a scalar
/// (<c>v * new Int3(1, -1, 1)</c>, to flip an axis). A division by such a constant does not lose a
/// lane and keeps its operator.
/// </para>
/// <para>
/// A compound <c>v *= C</c> or <c>v /= -1</c> of those forms assigns the helper's result,
/// <c>v = __ms_neg(v)</c>, which names the target twice. A target whose expression has side effects
/// cannot be named twice and its native operator would be the miscompiled one, so that is
/// unsupported (SCG0012).
/// </para>
/// <para>
/// What does not: a negation the generator cannot see. The optimizer also derives one from
/// <c>~v + 1</c> and from <c>v - 2 * v</c>. Those shapes keep their native operators and stay
/// exposed until Apple fixes the compiler. A program-scope initializer also keeps the native
/// operator: it may not call a function, and its operands are constants, which the compiler folds.
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

        UsesPrelude = true;
        return Call(MslNames.Negate, VectorOperand(negated, vector, unsigned));
    }

    /// <summary>
    /// A <c>C - v</c> or <c>v * C</c> on an <c>int</c> or <c>uint</c> vector whose constant negates
    /// some lanes (a lane of zero, or of minus one): <c>C + __ms_neg(v)</c> or <c>__ms_mul(v, C)</c>;
    /// otherwise null. A constant that negates every lane is a negation and has been taken by
    /// <see cref="VectorNegation"/> before. With <paramref name="unsigned"/>, both operands are
    /// computed in that unsigned vector type, for a chain of wrapped arithmetic. Without it, the
    /// difference of an <c>int</c> vector is left to that chain (it has to wrap) and null here.
    /// </summary>
    private Expr? LaneNegation(IOperation operation, string? unsigned = null)
    {
        if (_function.IsGlobalInitializer || operation is not IBinaryOperation { IsLifted: false } binary ||
            !IsNativeArithmetic(binary.OperatorMethod) || _types.Resolve(binary.Type) is not { } vector ||
            !IsIntegerVector(vector) || !HasFoldableLanes(vector))
        {
            return null;
        }

        if (binary.OperatorKind == BinaryOperatorKind.Subtract && HasLane(binary.LeftOperand, 0))
        {
            if (unsigned is null && UnsignedArithmeticType(binary.Type) is not null)
            {
                return null;
            }

            UsesPrelude = true;
            Expr left = unsigned is null ? Expression(binary.LeftOperand) : UnsignedOperand(binary.LeftOperand);

            return new Expr(
                BinaryOperand(left, Expr.Additive, isRight: false) + " + " + Call(MslNames.Negate, VectorOperand(binary.RightOperand, vector, unsigned)).Text,
                Expr.Additive);
        }

        if (binary.OperatorKind == BinaryOperatorKind.Multiply && (HasLane(binary.LeftOperand, -1) || HasLane(binary.RightOperand, -1)))
        {
            UsesPrelude = true;
            return Call(MslNames.Multiply, VectorOperand(binary.LeftOperand, vector, unsigned), VectorOperand(binary.RightOperand, vector, unsigned));
        }

        return null;
    }

    /// <summary>
    /// An operand of a prelude helper, which takes vectors: a scalar operand (the <c>s</c> of
    /// <c>new Int3(0) - s</c>, which MSL would broadcast) is converted to the vector type.
    /// </summary>
    private string VectorOperand(IOperation operand, GpuType vector, string? unsigned)
    {
        string text = unsigned is null ? Expression(operand).Text : UnsignedOperand(operand).Text;

        return _types.Resolve(operand.Type) is { Kind: GpuTypeKind.Vector } ? text : Call(unsigned ?? vector.MslName, text).Text;
    }

    /// <summary>
    /// <c>t = __ms_neg(t)</c> for a <c>t *= -1</c> or <c>t /= -1</c> on an <c>int</c> or <c>uint</c>
    /// vector, and <c>t = __ms_mul(t, C)</c> for a <c>t *= C</c> where a lane of the constant is
    /// minus one; null when <paramref name="operation"/> is neither. That names the target twice, and
    /// the native operator is the one the Metal compiler miscompiles, so a target whose expression
    /// has side effects is unsupported.
    /// </summary>
    private Expr? NegatingCompoundAssignment(ICompoundAssignmentOperation operation, Expr target, string text)
    {
        if (_function.IsGlobalInitializer || operation.IsLifted || !IsNativeArithmetic(operation.OperatorMethod) ||
            _types.Resolve(operation.Target.Type) is not { } type || !IsIntegerVector(type) || !HasFoldableLanes(type))
        {
            return null;
        }

        bool negates = IsNegatingFactor(operation.OperatorKind, type, operation.Value);

        if (!negates && !(operation.OperatorKind == BinaryOperatorKind.Multiply && HasLane(operation.Value, -1)))
        {
            return null;
        }

        if (HasSideEffects(operation.Target))
        {
            return Unsupported(
                operation,
                $"'{text}=' by a constant that negates lanes of an integer vector, on a target whose expression has side effects " +
                "(the result is assigned back, which names the target twice; put the index in a local first)");
        }

        UsesPrelude = true;

        if (negates)
        {
            // As in VectorNegation: the constant is translated for its diagnostics, not emitted.
            _ = Expression(operation.Value);
            return new Expr(target.Operand(Expr.Unary) + " = " + Call(MslNames.Negate, target.Text).Text, Expr.Assignment);
        }

        return new Expr(
            target.Operand(Expr.Unary) + " = " + Call(MslNames.Multiply, target.Text, VectorOperand(operation.Value, type, unsigned: null)).Text,
            Expr.Assignment);
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
        folded.Components.All(component => component == Bits(folded.Kind, value));

    /// <summary>As <see cref="IsSplat"/>, for at least one component.</summary>
    private bool HasLane(IOperation operand, int value) =>
        Folder.TryEvaluate(operand, out FoldedValue? folded, out _) &&
        folded is { Kind: ScalarKind.Int or ScalarKind.UInt } &&
        folded.Components.Any(component => component == Bits(folded.Kind, value));

    private static double Bits(ScalarKind kind, int value) => kind == ScalarKind.UInt ? unchecked((uint)value) : value;
}
