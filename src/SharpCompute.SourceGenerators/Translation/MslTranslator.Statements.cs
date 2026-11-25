using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

internal sealed partial class MslTranslator
{
    private void Statement(IOperation operation)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        switch (operation)
        {
            case IInvalidOperation:
                SawInvalidCode = true;
                break;

            case IBlockOperation block:
                Writer.Line("{");
                Writer.Indent++;

                foreach (IOperation statement in block.Operations)
                {
                    Statement(statement);
                }

                Writer.Indent--;
                Writer.Line("}");
                break;

            case IVariableDeclarationGroupOperation declarations:
                HoistDeclarations(declarations);

                foreach (LocalDeclaration declaration in Declarations(declarations))
                {
                    Writer.Line(declaration.ToString() + ";");
                }

                break;

            case IExpressionStatementOperation expression:
                HoistDeclarations(expression.Operation);
                string text = FullExpression(expression.Operation);
                string? fence = TakeTrailingFence(ref text);

                Writer.Line(text + ";");

                if (fence is not null)
                {
                    // A texture write: its fence, if it turns out to need one, is the next statement.
                    Writer.Line(fence);
                }

                break;

            case IConditionalOperation conditional:
                // C# scopes an `out` variable of an if condition to the enclosing block (and forbids
                // reusing its name further down the else-if chain), so declaring it before the if is exact.
                for (IOperation? branch = conditional; branch is IConditionalOperation { Syntax: IfStatementSyntax } ifStatement; branch = ifStatement.WhenFalse)
                {
                    HoistDeclarations(ifStatement.Condition);
                }

                If(conditional, isElseIf: false);
                break;

            case IForLoopOperation forLoop:
                For(forLoop);
                break;

            case IWhileLoopOperation whileLoop:
                While(whileLoop);
                break;

            case IForEachLoopOperation forEach:
                // Only over local arrays, as an index loop (see MslTranslator.LocalArrays.cs).
                ForEach(forEach);
                break;

            case IBranchOperation { BranchKind: BranchKind.Break }:
                Writer.Line("break;");
                break;

            case IBranchOperation { BranchKind: BranchKind.Continue }:
                Writer.Line("continue;");
                break;

            case IReturnOperation { Kind: OperationKind.Return, ReturnedValue: { } value } when _function.IsKernel && _isPixelShader:
                // A pixel shader's Execute returns the color: write it to the output texture instead.
                HoistDeclarations(value);
                UseBuiltin(Builtins.ThreadPositionInGrid);
                Writer.Line(MslNames.Output + ".write(" + FullExpression(value) + ", " + BuiltinInfo.Name(Builtins.ThreadPositionInGrid) + ".xy);");
                Writer.Line("return;");
                break;

            case IReturnOperation { Kind: OperationKind.Return, ReturnedValue: not null } when _function.ReturnsLocalArray:
                // The helper returns a local array, which its signature was reported for; the value would only repeat that.
                Writer.Line("return;");
                break;

            case IReturnOperation { Kind: OperationKind.Return, ReturnedValue: null } when _function.IsConstructor:
                // A constructor's function returns the struct it has built so far.
                Writer.Line("return " + MslNames.This + ";");
                break;

            case IReturnOperation { Kind: OperationKind.Return } returnOperation:
                HoistDeclarations(returnOperation.ReturnedValue);
                Writer.Line(returnOperation.ReturnedValue is null ? "return;" : "return " + FullExpression(returnOperation.ReturnedValue) + ";");
                break;

            case ILocalFunctionOperation localFunction:
                // Hoisted to a program-scope function when it is called; nothing to emit here.
                CheckLocalFunction(localFunction.Symbol, localFunction.Body ?? localFunction.IgnoredBody);
                break;

            case ISwitchOperation switchOperation:
                Switch(switchOperation);
                break;

            case IEmptyOperation:
                break;

            default:
                Unsupported(operation, DescribeUnsupported(operation) ?? Humanize(operation.Syntax));
                break;
        }
    }

    /// <summary>
    /// Declares the variables of inline <c>out T x</c> arguments (<c>F(p, out Float2 angle)</c>) on their
    /// own lines before the statement, so that the call can pass them by reference. Only used for
    /// statements that are evaluated once, in the scope the C# variable belongs to; elsewhere an inline
    /// declaration remains SCG0012.
    /// </summary>
    private void HoistDeclarations(IOperation? operation)
    {
        if (operation is null)
        {
            return;
        }

        foreach (IOperation node in operation.DescendantsAndSelf())
        {
            if (node is not IDeclarationExpressionOperation { Expression: ILocalReferenceOperation { Local: { } local } } declaration ||
                _function.HoistedLocals.Contains(local) ||
                node.Parent is not IArgumentOperation { Parameter.RefKind: RefKind.Out })
            {
                continue;
            }

            if (_types.Resolve(local.Type, _diagnostics, declaration.Syntax.GetLocation()) is not { Kind: not GpuTypeKind.Buffer } type)
            {
                _failedSymbols.Add(local);
                continue;
            }

            Writer.Line(type.MslName + " " + DeclareLocal(local) + ";");
            _function.HoistedLocals.Add(local);
        }
    }

    /// <summary>Writes the body of a control-flow statement, always inside braces.</summary>
    private void Body(IOperation operation)
    {
        if (operation is IBlockOperation)
        {
            Statement(operation);
            return;
        }

        Writer.Line("{");
        Writer.Indent++;
        Statement(operation);
        Writer.Indent--;
        Writer.Line("}");
    }

    private void If(IConditionalOperation operation, bool isElseIf)
    {
        Writer.Line((isElseIf ? "else if (" : "if (") + Condition(operation.Condition) + ")");
        Body(operation.WhenTrue);

        if (operation.WhenFalse is IConditionalOperation { Syntax: IfStatementSyntax } elseIf)
        {
            If(elseIf, isElseIf: true);
        }
        else if (operation.WhenFalse is not null)
        {
            Writer.Line("else");
            Body(operation.WhenFalse);
        }
    }

    private void For(IForLoopOperation operation)
    {
        var initializers = new List<string>();

        foreach (IOperation before in operation.Before)
        {
            switch (before)
            {
                case IVariableDeclarationGroupOperation declarations:
                    // All declarators of a for-initializer share one type: `int i = 0, j = 1`.
                    List<LocalDeclaration> locals = Declarations(declarations);

                    for (int i = 0; i < locals.Count; i++)
                    {
                        initializers.Add(i == 0 ? locals[i].ToString() : locals[i].ToString(includeType: false));
                    }

                    break;

                case IExpressionStatementOperation expression:
                    initializers.Add(FullExpression(expression.Operation));
                    break;

                case IInvalidOperation:
                    SawInvalidCode = true;
                    break;

                default:
                    Unsupported(before, DescribeUnsupported(before) ?? Humanize(before.Syntax));
                    break;
            }
        }

        string condition = operation.Condition is null ? string.Empty : Condition(operation.Condition);
        var increments = new List<string>();

        foreach (IOperation bottom in operation.AtLoopBottom)
        {
            if (bottom is IExpressionStatementOperation expression)
            {
                increments.Add(FullExpression(expression.Operation));
            }
            else if (bottom is IInvalidOperation)
            {
                SawInvalidCode = true;
            }
            else
            {
                Unsupported(bottom, DescribeUnsupported(bottom) ?? Humanize(bottom.Syntax));
            }
        }

        Writer.Line("for (" + string.Join(", ", initializers) + "; " + condition + "; " + string.Join(", ", increments) + ")");
        Body(operation.Body);
    }

    private void While(IWhileLoopOperation operation)
    {
        string condition = operation.Condition is null ? "true" : Condition(operation.Condition);

        if (operation.ConditionIsTop)
        {
            Writer.Line("while (" + condition + ")");
            Body(operation.Body);
        }
        else
        {
            Writer.Line("do");
            Body(operation.Body);
            Writer.Line("while (" + condition + ");");
        }
    }

    private void Switch(ISwitchOperation operation)
    {
        Writer.Line("switch (" + FullExpression(operation.Value) + ")");
        Writer.Line("{");
        Writer.Indent++;

        foreach (ISwitchCaseOperation section in operation.Cases)
        {
            foreach (ICaseClauseOperation clause in section.Clauses)
            {
                switch (clause)
                {
                    case ISingleValueCaseClauseOperation singleValue:
                        Writer.Line("case " + FullExpression(singleValue.Value) + ":");
                        break;
                    case IDefaultCaseClauseOperation:
                        Writer.Line("default:");
                        break;
                    default:
                        _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, clause.Syntax.GetLocation(), "a pattern-based switch case (only constant 'case' labels are supported)");
                        break;
                }
            }

            // Each section gets its own scope: C++ forbids jumping over an initialization into a later case.
            Writer.Line("{");
            Writer.Indent++;

            foreach (IOperation statement in section.Body)
            {
                Statement(statement);
            }

            Writer.Indent--;
            Writer.Line("}");
        }

        Writer.Indent--;
        Writer.Line("}");
    }

    private List<LocalDeclaration> Declarations(IVariableDeclarationGroupOperation group)
    {
        var result = new List<LocalDeclaration>();

        foreach (IVariableDeclarationOperation declaration in group.Declarations)
        {
            foreach (IVariableDeclaratorOperation declarator in declaration.Declarators)
            {
                ILocalSymbol local = declarator.Symbol;
                Location location = declarator.Syntax is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Type: { } typeSyntax } }
                    ? typeSyntax.GetLocation()
                    : declarator.Syntax.GetLocation();

                if (local.RefKind != RefKind.None)
                {
                    _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, location, $"the ref local '{local.Name}'");
                    continue;
                }

                if (IsLocalArrayType(local.Type))
                {
                    // `Span<float> taps = stackalloc float[9];` -> `float taps[9] = {};` (see MslTranslator.LocalArrays.cs).
                    result.Add(DeclareLocalArray(declarator, declaration, local, location));
                    continue;
                }

                // Translate the initializer first: if it already explains what is wrong (e.g. a lambda),
                // an additional "unsupported type" diagnostic for the local would only be noise.
                int errorsBefore = _diagnostics.ErrorCount;
                IOperation? initializer = declarator.GetVariableInitializer()?.Value ?? declaration.Initializer?.Value;
                string? initializerText = initializer is null ? null : FullExpression(initializer);

                GpuType? type = _diagnostics.ErrorCount == errorsBefore
                    ? _types.Resolve(local.Type, _diagnostics, location)
                    : _types.Resolve(local.Type);

                if (type is { Kind: GpuTypeKind.Buffer })
                {
                    _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, location, $"the buffer-typed local '{local.Name}' (use the captured buffer directly)");
                    type = null;
                }

                if (type is null)
                {
                    _failedSymbols.Add(local);
                }

                CheckInlineArrayLocal(declarator, local, type, location);
                result.Add(new LocalDeclaration(local.IsConst, type?.MslName ?? "int", DeclareLocal(local), initializerText));
            }
        }

        return result;
    }

    private readonly struct LocalDeclaration
    {
        private readonly bool _isConst;
        private readonly string _type;
        private readonly string _name;
        private readonly string? _initializer;
        private readonly string _declaratorSuffix;

        /// <param name="declaratorSuffix">Written after the name: the <c>[N]</c> of a local array, which C++ puts in the declarator.</param>
        public LocalDeclaration(bool isConst, string type, string name, string? initializer, string declaratorSuffix = "")
        {
            _isConst = isConst;
            _type = type;
            _name = name;
            _initializer = initializer;
            _declaratorSuffix = declaratorSuffix;
        }

        public override string ToString() => ToString(includeType: true);

        public string ToString(bool includeType) =>
            (includeType ? (_isConst ? "const " : string.Empty) + _type + " " : string.Empty) +
            _name +
            _declaratorSuffix +
            (_initializer is null ? string.Empty : " = " + _initializer);
    }
}
