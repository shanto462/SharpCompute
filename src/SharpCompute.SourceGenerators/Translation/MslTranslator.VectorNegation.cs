using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// The arithmetic on integer vectors that is emitted as calls of lane-by-lane helpers instead of the
/// native operators, to work around a miscompile in Apple's Metal compiler (reported to Apple as
/// FB25732151): <c>a + b</c> and <c>a - b</c> as <c>__ms_add(a, b)</c> and <c>__ms_sub(a, b)</c>,
/// and <c>-v</c> as <c>__ms_neg(v)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The bug. Valid MSL gives a wrong result, silently, when all of this holds: an integer vector is
/// negated (<c>-v</c>, or anything the optimizer turns into <c>0 - v</c>); at least one lane of that
/// negation is never read; and the same <c>v</c> is subtracted in an add/sub/mul chain elsewhere, as
/// in <c>t += C; t *= 3; t -= v</c>. The chain's result then loses the unread lane: its store is
/// deleted. The optimizer marks the unread lane of the negation as poison (which is legal), then,
/// rewriting the chain's <c>x - v</c> to <c>x + (-v)</c>, reuses that instruction as its
/// "<c>-v</c>" (which is not), and the poisoned lane of the chain is dropped. It happens at -O1 and
/// above, in every math mode and MSL version, compiled at run time or ahead of time with
/// <c>xcrun metal</c>, for vectors of every integer type (signed and unsigned, 8 to 64 bits).
/// Vectors of floats and halves are not affected, and neither are scalars.
/// </para>
/// <para>
/// The rule. The negation need not be written as one: the optimizer derives <c>0 - v</c> from
/// <c>~v + 1</c>, <c>v - 2 * v</c>, <c>v - (v + v)</c>, <c>v - (v &lt;&lt; 1)</c>, <c>~(v - 1)</c>,
/// and from <c>v * s</c> and <c>v / s</c> once it knows that <c>s</c> is -1. No list of shapes
/// covers that. The other half of the bug is narrow, though: the reuse only happens while a
/// <i>vector</i> add or subtract is rewritten. So every native <c>+</c> and <c>-</c> whose result is
/// an integer vector is computed lane by lane, by the helpers of <c>MslPrelude.VectorArithmetic</c>
/// (<see cref="VectorSum"/>, <see cref="VectorSumAssignment"/>). The chain is then a chain of
/// scalars: whatever negation the optimizer derives, a scalar has no unread lane to poison, and a
/// vector negation has no vector chain to be reused in. Measured on the GPU: with only the
/// subtractions lane by lane, or only the additions, lanes are still lost (a chain of <c>+</c>
/// subtracts through <c>~v + 1</c> or <c>v * s</c>); with both, none is. The helpers are functions,
/// so each operand is evaluated once, and each lane is computed in the unsigned type of its width,
/// so it wraps as it does in C#. Multiplication, the shifts and the bitwise operators keep their
/// native operators: they are not rewritten that way.
/// </para>
/// <para>
/// The negations the generator can see are emitted lane by lane as well, which keeps the vector
/// negation out of the MSL to begin with: <c>__ms_neg(v)</c> for unary minus on a vector of any
/// integer type, and, on the <c>int</c> and <c>uint</c> vectors, for the operators that are a
/// negation by a constant: <c>0 - v</c> with an all-zero left operand, <c>v * -1</c> and
/// <c>-1 * v</c> with an all-minus-one operand (for <c>uint</c>, all bits set), and <c>v / -1</c>.
/// A constant is one <see cref="ConstantFolder"/> can evaluate, which covers <c>int</c> and
/// <c>uint</c> components. <c>v * C</c>, where a lane of the constant <c>C</c> is minus one
/// (<c>v * new Int3(1, -1, 1)</c>, to flip an axis), is the prelude's <c>__ms_mul(v, C)</c>, which
/// multiplies each lane as a scalar. A division by such a constant keeps its operator.
/// </para>
/// <para>
/// A compound <c>v *= C</c> or <c>v /= -1</c> of those forms assigns the helper's result,
/// <c>v = __ms_neg(v)</c>, which names the target twice. A target whose expression has side effects
/// cannot be named twice, so that is unsupported (SCG0012). <c>v += w</c> and <c>v -= w</c> have a
/// by-reference helper for such a target, <c>__ms_add_to(v, w)</c>, and are only restricted on
/// components (<c>data[i++].XY += w</c>), which bind no reference.
/// </para>
/// <para>
/// What does not: a program-scope initializer keeps the native operators. It may not call a
/// function, and its operands are constants, which the compiler folds.
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
    /// A <c>v * C</c> on an <c>int</c> or <c>uint</c> vector whose constant negates some lanes (a
    /// lane of minus one): <c>__ms_mul(v, C)</c>; otherwise null. A constant that negates every lane
    /// is a negation and has been taken by <see cref="VectorNegation"/> before. With
    /// <paramref name="unsigned"/>, both operands are computed in that unsigned vector type, for a
    /// chain of wrapped arithmetic. (<c>C - v</c>, where a lane of the constant is zero, is a
    /// subtraction like any other: <see cref="VectorSum"/>.)
    /// </summary>
    private Expr? LaneNegation(IOperation operation, string? unsigned = null)
    {
        if (_function.IsGlobalInitializer || operation is not IBinaryOperation { IsLifted: false, OperatorKind: BinaryOperatorKind.Multiply } binary ||
            !IsNativeArithmetic(binary.OperatorMethod) || _types.Resolve(binary.Type) is not { } vector ||
            !IsIntegerVector(vector) || !HasFoldableLanes(vector) ||
            !(HasLane(binary.LeftOperand, -1) || HasLane(binary.RightOperand, -1)))
        {
            return null;
        }

        UsesPrelude = true;
        return Call(MslNames.Multiply, VectorOperand(binary.LeftOperand, vector, unsigned), VectorOperand(binary.RightOperand, vector, unsigned));
    }

    /// <summary>
    /// <c>__ms_add(a, b)</c> or <c>__ms_sub(a, b)</c> for a native <c>+</c> or <c>-</c> whose result
    /// is an integer vector (see the remarks on this class for why); otherwise null. A scalar operand
    /// is converted to the vector type, as MSL would broadcast it. With <paramref name="unsigned"/>,
    /// both operands are computed in that unsigned vector type and so is the result, for a chain of
    /// wrapped arithmetic (see <see cref="UnsignedArithmetic"/>):
    /// <c>__ms_add(uint3(a) * uint3(b), uint3(c))</c>.
    /// </summary>
    private Expr? VectorSum(IOperation operation, string? unsigned = null)
    {
        if (_function.IsGlobalInitializer ||
            operation is not IBinaryOperation { IsLifted: false, OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Subtract } binary ||
            !IsNativeArithmetic(binary.OperatorMethod) || _types.Resolve(binary.Type) is not { } vector || !IsIntegerVector(vector))
        {
            return null;
        }

        UsesVectorArithmetic = true;

        return Call(
            binary.OperatorKind == BinaryOperatorKind.Add ? MslNames.Add : MslNames.Subtract,
            VectorOperand(binary.LeftOperand, vector, unsigned),
            VectorOperand(binary.RightOperand, vector, unsigned));
    }

    /// <summary>
    /// A native <c>+=</c> or <c>-=</c> on an integer vector. Where nothing in it has side effects,
    /// <c>t = __ms_add(t, w)</c>, which names the target twice. Otherwise the by-reference
    /// <c>__ms_add_to(t, int3(w))</c>, which names the target once, reads it after the value is
    /// evaluated, as the native <c>+=</c> does, and returns the new value as the assignment does.
    /// Null for every other compound assignment.
    /// </summary>
    /// <remarks>
    /// The by-reference helper binds the target, so the target has to be a whole vector. Components
    /// of one (<c>v.XY += F()</c>) bind no reference and are assigned as where nothing has side
    /// effects; components whose own expression has side effects (<c>data[i++].XY += w</c>) cannot be
    /// named twice, and their native operator would be the one this avoids, so that is unsupported.
    /// </remarks>
    private Expr? VectorSumAssignment(ICompoundAssignmentOperation operation, Expr target, string text)
    {
        if (_function.IsGlobalInitializer || operation.IsLifted ||
            operation.OperatorKind is not (BinaryOperatorKind.Add or BinaryOperatorKind.Subtract) ||
            !IsNativeArithmetic(operation.OperatorMethod) || _types.Resolve(operation.Target.Type) is not { } type || !IsIntegerVector(type))
        {
            return null;
        }

        bool add = operation.OperatorKind == BinaryOperatorKind.Add;
        bool components = IsSwizzle(operation.Target);

        if (components && HasSideEffects(operation.Target))
        {
            return Unsupported(
                operation,
                $"'{text}=' on components of an integer vector whose expression has side effects " +
                "(the result is assigned back, which names the target twice; put the index in a local first)");
        }

        string value = VectorOperand(operation.Value, type, unsigned: null);
        UsesVectorArithmetic = true;

        if (!components && (HasSideEffects(operation.Target) || HasSideEffects(operation.Value)))
        {
            // The helper's value type is deduced from the argument, which has to be the vector itself:
            // a scalar has been converted, a new vector and a local are one, and the conversion makes
            // anything else (a packed buffer element or struct field among them) one.
            bool isVector = operation.Value is ILocalReferenceOperation or IObjectCreationOperation ||
                _types.Resolve(operation.Value.Type) is not { Kind: GpuTypeKind.Vector };

            return Call(add ? MslNames.AddTo : MslNames.SubtractFrom, target.Text, isVector ? value : Call(type.MslName, value).Text);
        }

        return new Expr(target.Operand(Expr.Unary) + " = " + Call(add ? MslNames.Add : MslNames.Subtract, target.Text, value).Text, Expr.Assignment);
    }

    /// <summary>True for components of a vector (<c>v.XY</c>), which are no object a reference can bind.</summary>
    private bool IsSwizzle(IOperation target) =>
        target is IPropertyReferenceOperation { Property.IsIndexer: false, Instance: { } instance } &&
        _types.Resolve(instance.Type) is { Kind: GpuTypeKind.Vector };

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
