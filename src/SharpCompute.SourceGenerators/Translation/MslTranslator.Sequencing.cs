using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// C# evaluates operands strictly left to right, so <c>a[i++] + a[i++]</c> and <c>F(i++, i)</c> are
/// well defined. MSL 3.2 is C++14, where the same expressions modify and read <c>i</c> without a
/// defined order (undefined behavior, clang's <c>-Wunsequenced</c>). Rather than emit MSL that may
/// compute something else, such expressions are rejected with SCG0021.
/// </summary>
internal sealed partial class MslTranslator
{
    /// <summary>Translates a full expression (a statement, initializer, or condition) after checking its evaluation order.</summary>
    private string FullExpression(IOperation operation)
    {
        ISymbol? conflict = null;
        Analyze(operation, ref conflict);

        if (conflict is not null && !ContainsInvalid(operation))
        {
            _diagnostics.Report(DiagnosticDescriptors.UnsequencedModification, operation.Syntax.GetLocation(), conflict.Name);
        }

        return Expression(operation).Text;
    }

    /// <summary>
    /// Translates the condition of an if/while/do/for. An assignment used as a condition gets a second
    /// pair of parentheses, which is how C++ compilers are told that <c>=</c> was meant.
    /// </summary>
    private string Condition(IOperation operation)
    {
        string text = FullExpression(operation);

        return operation is IAssignmentOperation ? "(" + text + ")" : text;
    }

    /// <summary>The locals and parameters an expression reads and writes while it is evaluated.</summary>
    private sealed class Effects
    {
        public static readonly Effects None = new();

        public HashSet<ISymbol> Reads { get; } = new(SymbolEqualityComparer.Default);

        public HashSet<ISymbol> Writes { get; } = new(SymbolEqualityComparer.Default);

        public bool IsEmpty => Reads.Count == 0 && Writes.Count == 0;

        public void Add(Effects other)
        {
            Reads.UnionWith(other.Reads);
            Writes.UnionWith(other.Writes);
        }
    }

    /// <summary>Merges the effects of two operands whose relative evaluation order MSL leaves open.</summary>
    private static Effects Unsequenced(Effects left, Effects right, ref ISymbol? conflict)
    {
        if (left.IsEmpty)
        {
            return right;
        }

        if (right.IsEmpty)
        {
            return left;
        }

        conflict ??= left.Writes.FirstOrDefault(s => right.Reads.Contains(s) || right.Writes.Contains(s))
            ?? right.Writes.FirstOrDefault(left.Reads.Contains);

        var result = new Effects();
        result.Add(left);
        result.Add(right);
        return result;
    }

    private static Effects Sequenced(Effects first, Effects second)
    {
        if (first.IsEmpty)
        {
            return second;
        }

        if (second.IsEmpty)
        {
            return first;
        }

        var result = new Effects();
        result.Add(first);
        result.Add(second);
        return result;
    }

    private Effects Analyze(IOperation? operation, ref ISymbol? conflict)
    {
        switch (operation)
        {
            case null:
                return Effects.None;

            case ILocalReferenceOperation local:
                return Read(local.Local);

            case IParameterReferenceOperation parameter when _function.Names.ContainsKey(parameter.Parameter):
                return Read(parameter.Parameter);

            case ISimpleAssignmentOperation assignment:
                return Store(assignment.Target, assignment.Value, readsTarget: false, ref conflict);

            case ICompoundAssignmentOperation compound:
                return Store(compound.Target, compound.Value, readsTarget: true, ref conflict);

            case IIncrementOrDecrementOperation increment:
                return Store(increment.Target, value: null, readsTarget: true, ref conflict);

            // `&&`, `||`, and `?:` evaluate their first operand completely before the rest.
            case IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr } logical:
                return Sequenced(Analyze(logical.LeftOperand, ref conflict), Analyze(logical.RightOperand, ref conflict));

            case IConditionalOperation conditional:
                return Sequenced(
                    Analyze(conditional.Condition, ref conflict),
                    Sequenced(Analyze(conditional.WhenTrue, ref conflict), Analyze(conditional.WhenFalse, ref conflict)));

            case IInvocationOperation invocation:
                return Call(invocation, ref conflict);

            default:
                Effects result = Effects.None;

                foreach (IOperation child in operation.ChildOperations)
                {
                    result = Unsequenced(result, Analyze(child, ref conflict), ref conflict);
                }

                return result;
        }
    }

    /// <summary>
    /// The arguments of a call are evaluated in no particular order. Variables passed as <c>ref</c>/<c>out</c>
    /// are written (and, for <c>ref</c>, read) by the callee, which runs after all arguments are
    /// evaluated in C# and in MSL alike; so they conflict with the rest of the enclosing expression
    /// (<c>F(out x) + x</c>), but not with the call's own arguments (<c>F(out x, x)</c>).
    /// </summary>
    private Effects Call(IInvocationOperation invocation, ref ISymbol? conflict)
    {
        Effects arguments = Analyze(invocation.Instance, ref conflict);
        var callee = new Effects();

        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            if (argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out } parameter)
            {
                arguments = Unsequenced(arguments, TargetEffects(argument.Value, out ISymbol? stored, ref conflict), ref conflict);

                if (stored is not null)
                {
                    callee.Writes.Add(stored);

                    if (parameter.RefKind == RefKind.Ref)
                    {
                        callee.Reads.Add(stored);
                    }
                }

                continue;
            }

            arguments = Unsequenced(arguments, Analyze(argument, ref conflict), ref conflict);
        }

        return Sequenced(arguments, callee);
    }

    private static Effects Read(ISymbol symbol)
    {
        var effects = new Effects();
        effects.Reads.Add(symbol);
        return effects;
    }

    /// <summary>
    /// Effects of writing to <paramref name="target"/>. The operands are evaluated in no particular
    /// order; the store itself happens after both, so it only conflicts with other writes.
    /// </summary>
    private Effects Store(IOperation target, IOperation? value, bool readsTarget, ref ISymbol? conflict)
    {
        Effects targetEffects = TargetEffects(target, out ISymbol? stored, ref conflict);
        Effects operands = Unsequenced(targetEffects, Analyze(value, ref conflict), ref conflict);

        if (stored is null)
        {
            return operands;
        }

        if (operands.Writes.Contains(stored))
        {
            conflict ??= stored;
        }

        var result = new Effects();
        result.Add(operands);
        result.Writes.Add(stored);

        if (readsTarget)
        {
            result.Reads.Add(stored);
        }

        return result;
    }

    /// <summary>
    /// Evaluating an assignment target does not read the variable being stored to (<c>v</c> in
    /// <c>v.X = 1</c>), but it does evaluate any index expressions (<c>i++</c> in <c>buffer[i++] = 1</c>).
    /// </summary>
    private Effects TargetEffects(IOperation target, out ISymbol? stored, ref ISymbol? conflict)
    {
        stored = null;

        switch (target)
        {
            case ILocalReferenceOperation local:
                stored = local.Local;
                return Effects.None;

            case IParameterReferenceOperation parameter when _function.Names.ContainsKey(parameter.Parameter):
                stored = parameter.Parameter;
                return Effects.None;

            case IDeclarationExpressionOperation { Expression: ILocalReferenceOperation declared }:
                // `out float x`: a new variable that the call writes.
                stored = declared.Local;
                return Effects.None;

            case IFieldReferenceOperation { Instance: { } instance }:
                return TargetEffects(instance, out stored, ref conflict);

            case IPropertyReferenceOperation { Instance: { } instance } property:
                Effects result = TargetEffects(instance, out stored, ref conflict);

                foreach (IArgumentOperation argument in property.Arguments)
                {
                    result = Unsequenced(result, Analyze(argument, ref conflict), ref conflict);
                }

                return result;

            default:
                return Analyze(target, ref conflict);
        }
    }
}
