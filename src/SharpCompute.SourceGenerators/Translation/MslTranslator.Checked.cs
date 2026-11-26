using System.Collections.Generic;
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
//
// Where the context comes from decides how it is reported. `checked(...)` and `checked { }` are
// deliberate, and each operation in them is reported where it is written. The project-wide switch
// makes every integer operation checked, so it is reported once per shader, on the shader's
// declaration, with the count and the first operation (ShaderAnalyzer reports it).
internal sealed partial class MslTranslator
{
    // The operations that are checked only because of the project's CheckForOverflowUnderflow. They
    // are reported once for the shader (see ProjectCheckedCount), not one by one: the switch makes
    // every integer operation checked, and a diagnostic per operation buries the one fix that matters.
    // Keyed by syntax node, since a helper can be translated more than once (once per receiver mode).
    private readonly HashSet<SyntaxNode> _projectChecked = new();
    private SyntaxNode? _firstProjectChecked;

    /// <summary>
    /// How many operations of the shader, in the kernel and in the helpers it reaches, are checked
    /// only because the project enables <c>CheckForOverflowUnderflow</c>. Valid after <see cref="TranslateKernel"/>.
    /// </summary>
    public int ProjectCheckedCount => _projectChecked.Count;

    /// <summary>The first of those operations to be translated, or null when there are none.</summary>
    public SyntaxNode? FirstProjectChecked => _firstProjectChecked;

    /// <summary>
    /// Reports <paramref name="operation"/> when it is checked arithmetic, and returns whether it was
    /// reported here (the caller then stops translating it).
    /// Explicit <c>checked(...)</c> and <c>checked { }</c> are reported where they are written, since
    /// they are deliberate; what the project switch alone makes checked is collected for one
    /// diagnostic on the shader.
    /// </summary>
    private bool ReportIfChecked(IOperation operation)
    {
        if (!IsCheckedArithmetic(operation))
        {
            return false;
        }

        if (_compilation.Options.CheckOverflow && !InsideCheckedSyntax(operation.Syntax))
        {
            if (_projectChecked.Add(operation.Syntax))
            {
                _firstProjectChecked ??= operation.Syntax;
            }

            // Translation goes on, so that the operands, and the helpers they call, are counted too.
            // The shader's diagnostic is an error, so none of this MSL is used.
            return false;
        }

        string text = operation.Syntax.ToString();
        _diagnostics.Report(
            DiagnosticDescriptors.CheckedArithmetic,
            operation.Syntax.GetLocation(),
            $"'{text}' is checked arithmetic",
            $"Write it unchecked, as 'unchecked({text})' or inside 'unchecked {{ }}', which wraps on both, or test the operands before the operation");
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
