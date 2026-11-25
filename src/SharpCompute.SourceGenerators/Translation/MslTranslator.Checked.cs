using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

// Checked arithmetic (SCG0052). In a checked context, `checked(a + b)`, a `checked { }` block or a
// project built with <CheckForOverflowUnderflow>, C# throws OverflowException where integer
// arithmetic overflows and where an explicit conversion to an integer type does not fit. ForCpu runs
// the C#, so it throws; the GPU has no exceptions, and the MSL wraps (or, for a conversion from a
// float, saturates). Rather than run something else on the GPU, the operation is refused, as
// ComputeSharp does. Only the operations that a checked context changes are reported: integer `+`,
// `-`, `*`, negation, their compound assignments, `++` and `--`, and explicit conversions that can
// overflow. A `checked` block around float arithmetic, or around a widening conversion, changes
// nothing and is accepted, and so is `unchecked`.
internal sealed partial class MslTranslator
{
    /// <summary>Reports <paramref name="operation"/> when it is checked arithmetic, and returns whether it was.</summary>
    private bool ReportIfChecked(IOperation operation)
    {
        if (!IsCheckedArithmetic(operation))
        {
            return false;
        }

        // The project-wide switch is easy to miss: every shader then reports its arithmetic.
        string projectHint = _compilation.Options.CheckOverflow && !InsideCheckedSyntax(operation.Syntax)
            ? "; the project enables CheckForOverflowUnderflow, which makes all integer arithmetic checked: turn it off, or put the shader's code in 'unchecked { }'"
            : string.Empty;

        _diagnostics.Report(DiagnosticDescriptors.CheckedArithmetic, operation.Syntax.GetLocation(), operation.Syntax.ToString(), projectHint);
        return true;
    }

    private bool IsCheckedArithmetic(IOperation operation)
    {
        // A constant is folded by the C# compiler, which reports a checked overflow at compile time.
        if (operation.ConstantValue.HasValue)
        {
            return false;
        }

        switch (operation)
        {
            case IBinaryOperation { IsChecked: true, OperatorMethod: null } binary:
                return binary.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply && IsInteger(binary.Type);

            case IUnaryOperation { IsChecked: true, OperatorMethod: null, OperatorKind: UnaryOperatorKind.Minus } unary:
                return IsInteger(unary.Type);

            case ICompoundAssignmentOperation { IsChecked: true, OperatorMethod: null } compound:
                return compound.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply && IsInteger(compound.Target.Type);

            case IIncrementOrDecrementOperation { IsChecked: true, OperatorMethod: null } increment:
                return IsInteger(increment.Type);

            case IConversionOperation conversion:
                // A user-defined checked conversion (Half's `explicit operator checked int`) throws as well.
                return conversion.OperatorMethod is { Name: "op_CheckedExplicit" } ||
                    (conversion.IsChecked && conversion.OperatorMethod is null && CanOverflow(conversion.Operand.Type, conversion.Type));

            default:
                return false;
        }
    }

    /// <summary>Whether an explicit conversion from <paramref name="source"/> to <paramref name="target"/> can throw in a checked context.</summary>
    private static bool CanOverflow(ITypeSymbol? source, ITypeSymbol? target)
    {
        if (!IsInteger(target) || source is null)
        {
            // To a float or a half nothing overflows: a value out of range becomes an infinity.
            return false;
        }

        if (!IsInteger(source))
        {
            // From a float or a half: NaN, infinities and values out of range throw.
            return source.SpecialType is SpecialType.System_Single or SpecialType.System_Double || source.Name == "Half";
        }

        (long sourceMin, ulong sourceMax) = Range(source.SpecialType);
        (long targetMin, ulong targetMax) = Range(target!.SpecialType);
        return sourceMin < targetMin || sourceMax > targetMax;
    }

    private static (long Min, ulong Max) Range(SpecialType type) => type switch
    {
        SpecialType.System_Byte => (0, byte.MaxValue),
        SpecialType.System_SByte => (sbyte.MinValue, (ulong)sbyte.MaxValue),
        SpecialType.System_Int16 => (short.MinValue, (ulong)short.MaxValue),
        SpecialType.System_UInt16 => (0, ushort.MaxValue),
        SpecialType.System_Int32 => (int.MinValue, int.MaxValue),
        SpecialType.System_UInt32 => (0, uint.MaxValue),
        SpecialType.System_Int64 => (long.MinValue, long.MaxValue),
        SpecialType.System_UInt64 => (0, ulong.MaxValue),
        SpecialType.System_Char => (0, char.MaxValue),
        _ => (long.MinValue, ulong.MaxValue),
    };

    private static bool IsInteger(ITypeSymbol? type) => type?.SpecialType is
        SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Char;

    private static bool InsideCheckedSyntax(SyntaxNode syntax)
    {
        for (SyntaxNode? node = syntax; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case CheckedExpressionSyntax expression:
                    return expression.IsKind(SyntaxKind.CheckedExpression);
                case CheckedStatementSyntax statement:
                    return statement.IsKind(SyntaxKind.CheckedStatement);
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }
}
