using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// Values in boundary storage (buffer elements, struct fields, the constants block) have their
/// <c>Packed</c> type. Metal's <c>packed_*</c> vectors convert implicitly almost everywhere. A
/// boundary type of the prelude (<see cref="GpuType.NeedsExplicitPacking"/>, the packed matrix)
/// never does: it is unpacked where it is read and packed where it is stored, so the rest of the
/// translation only ever sees the native type.
/// </summary>
internal sealed partial class MslTranslator
{
    private IOperation? _storeTarget;
    private bool _storeTargetIsPacked;

    /// <summary>
    /// Wraps a read of boundary storage in <c>__ms_unpack</c> when its type needs it, unless
    /// <paramref name="operation"/> is the target of the store being translated: that stays an
    /// lvalue and is reported back through <see cref="StoreTarget"/>.
    /// </summary>
    private Expr BoundaryRead(IOperation operation, GpuType type, Expr lvalue)
    {
        if (!type.NeedsExplicitPacking)
        {
            return lvalue;
        }

        if (ReferenceEquals(operation, _storeTarget))
        {
            _storeTargetIsPacked = true;
            return lvalue;
        }

        return Call(MslNames.Unpack, lvalue.Text);
    }

    /// <summary>
    /// Translates the target of an assignment as an lvalue. <paramref name="packed"/> is true when
    /// it is boundary storage of a type that must be stored through <c>__ms_pack</c>.
    /// </summary>
    private Expr StoreTarget(IOperation target, out bool packed)
    {
        // Saved and restored: an index expression of the target may contain another assignment.
        IOperation? outerTarget = _storeTarget;
        bool outerIsPacked = _storeTargetIsPacked;

        _storeTarget = target;
        _storeTargetIsPacked = false;

        try
        {
            Expr result = Expression(target);
            packed = _storeTargetIsPacked;
            return result;
        }
        finally
        {
            _storeTarget = outerTarget;
            _storeTargetIsPacked = outerIsPacked;
        }
    }

    /// <summary>True for a struct field whose type is stored through <c>__ms_pack</c>; it has no native lvalue to bind a reference to.</summary>
    private bool IsPackedField(IOperation operation) =>
        operation is IFieldReferenceOperation or IPropertyReferenceOperation or IInlineArrayAccessOperation &&
        _types.Resolve(operation.Type) is { NeedsExplicitPacking: true };

    /// <summary>
    /// An operand of an <c>[MslBinaryOperator]</c> call. MSL's matrix operators are templates, which
    /// do not deduce through the implicit conversion of a <c>packed_*</c> vector, and a packed value is
    /// not only a direct read of a buffer: a conditional between two reads, a swizzle of one, or
    /// anything else built from them can still have the packed type. Rather than try to recognize
    /// those, every vector operand whose type has a different packed form is converted explicitly,
    /// <c>(m * float3(v))</c>; on a value that already is a <c>float3</c> that is a no-op.
    /// </summary>
    private Expr NativeOperand(IOperation operand, Expr expression) =>
        _types.Resolve(operand.Type) is { Kind: GpuTypeKind.Vector, HasPackedForm: true, NeedsExplicitPacking: false } type &&
        type.BoundaryName != type.MslName
            ? Call(type.MslName, expression.Text)
            : expression;
}
