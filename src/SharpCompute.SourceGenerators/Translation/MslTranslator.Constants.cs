using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// <c>static readonly</c> and <c>const</c> fields of the shader struct (or a type containing it) are
/// lowered to MSL program-scope constants: <c>constant float3 Color = float3(1.0f, 0.5f, 0.0f);</c>.
/// MSL rejects any function call in such an initializer ("cannot have global constructors"), so
/// calls to <c>[MslIntrinsic]</c> functions are evaluated here and replaced by their value; anything
/// else that is not a constructor, an operator, a literal, or another such constant is SCG0024.
/// </summary>
internal sealed partial class MslTranslator
{
    private ConstantFolder? _folder;

    private ConstantFolder Folder => _folder ??= new ConstantFolder(_types, _shaderType, GetSemanticModel, _cancellationToken);

    private bool IsShaderOrContainingType(INamedTypeSymbol? type)
    {
        for (INamedTypeSymbol? current = _shaderType; current is not null; current = current.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True for a static readonly field that can be lowered to a program-scope constant: one of the
    /// shader (or a type containing it), or of any other type whose source is part of this compilation.
    /// </summary>
    private bool IsGlobalSource(IFieldSymbol field) =>
        IsShaderOrContainingType(field.ContainingType) || (HasSource(field) && field.ContainingType is { IsGenericType: false });

    /// <summary>Returns the program-scope constant for <paramref name="field"/>, translating it on first use.</summary>
    private GlobalConstant? GlobalReference(IOperation reference, IFieldSymbol field)
    {
        field = field.OriginalDefinition;

        if (_globals.TryGetValue(field, out GlobalConstant? existing))
        {
            return existing;
        }

        if (_globalsInProgress.Contains(field, SymbolEqualityComparer.Default))
        {
            ReportNonConstant(reference.Syntax.GetLocation(), field, "its initializer depends on itself");
            return null;
        }

        _globalsInProgress.Add(field);
        GlobalConstant? global;

        try
        {
            global = TranslateGlobal(field, reference);
        }
        finally
        {
            _globalsInProgress.RemoveAt(_globalsInProgress.Count - 1);
        }

        _globals[field] = global;

        if (global is not null)
        {
            _globalOrder.Add(global);
        }

        return global;
    }

    private GlobalConstant? TranslateGlobal(IFieldSymbol field, IOperation reference)
    {
        var declarator = field.DeclaringSyntaxReferences.Select(r => r.GetSyntax(_cancellationToken)).OfType<VariableDeclaratorSyntax>().FirstOrDefault();
        Location declaration = declarator?.Identifier.GetLocation() ?? reference.Syntax.GetLocation();
        GpuType? type = _types.Resolve(field.Type, out TypeFailure? failure);

        if (type is null || type.Kind is not (GpuTypeKind.Scalar or GpuTypeKind.Bool or GpuTypeKind.Vector))
        {
            if (field.Type.TypeKind == TypeKind.Error)
            {
                SawInvalidCode = true;
                return null;
            }

            string reason = failure?.Reason ?? $"its type '{TypeResolver.Display(field.Type)}' is not a scalar, vector, or matrix type";
            ReportNonConstant(declaration, field, reason);
            return null;
        }

        string initializer;

        if (field.IsConst)
        {
            if (field.ConstantValue is null)
            {
                SawInvalidCode = true;
                return null;
            }

            Expr constant = Constant(field.ConstantValue, type, reference);
            initializer = constant.Text;
        }
        else
        {
            if (declarator?.Initializer is not { } equalsValue ||
                GetSemanticModel(equalsValue.SyntaxTree).GetOperation(equalsValue, _cancellationToken) is not IFieldInitializerOperation { Value: { } value })
            {
                ReportNonConstant(declaration, field, "it has no initializer (static constructors are not translated)");
                return null;
            }

            if (FindStaticConstructorWrite(field) is { } write)
            {
                _diagnostics.Report(DiagnosticDescriptors.StaticFieldAssignedInStaticConstructor, write.GetLocation(), field.ContainingType.Name + "." + field.Name);
                return null;
            }

            FunctionContext caller = _function;
            _function = new FunctionContext(FunctionKind.GlobalInitializer) { Global = field };
            int errorsBefore = _diagnostics.ErrorCount;

            try
            {
                initializer = Expression(value).Text;
            }
            finally
            {
                _function = caller;
            }

            if (_diagnostics.ErrorCount != errorsBefore)
            {
                return null;
            }
        }

        // A constant of another type carries the type's name, like its methods do: two classes of
        // helpers can each have a `Scale`.
        string preferred = IsShaderOrContainingType(field.ContainingType) ? field.Name : field.ContainingType.Name + "_" + field.Name;
        string name = _globalNames.Allocate(MslNames.Escape(preferred));
        return new GlobalConstant(name, type.MslName, initializer);
    }

    /// <summary>
    /// Static constructors are not translated, so a <c>static readonly</c> field that one reassigns
    /// would be lowered from its initializer and the GPU would read a different value than C#
    /// (SCG0026). Returns the first write to <paramref name="field"/> in a static constructor of its
    /// type: an assignment, a compound assignment, an increment, or a <c>ref</c>/<c>out</c> argument.
    /// </summary>
    private SyntaxNode? FindStaticConstructorWrite(IFieldSymbol field)
    {
        foreach (IMethodSymbol constructor in field.ContainingType.StaticConstructors)
        {
            foreach (SyntaxReference reference in constructor.DeclaringSyntaxReferences)
            {
                SyntaxNode syntax = reference.GetSyntax(_cancellationToken);

                if (GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax, _cancellationToken) is { } body &&
                    FindFieldWrite(body, field) is { } write)
                {
                    return write;
                }
            }
        }

        return null;
    }

    private static SyntaxNode? FindFieldWrite(IOperation operation, IFieldSymbol field)
    {
        IOperation? target = operation switch
        {
            IAssignmentOperation assignment => assignment.Target,
            IIncrementOrDecrementOperation increment => increment.Target,
            IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => argument.Value,
            _ => null,
        };

        if (target is not null && WritesField(target, field))
        {
            return target.Syntax;
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            if (FindFieldWrite(child, field) is { } write)
            {
                return write;
            }
        }

        return null;
    }

    /// <summary>True when <paramref name="target"/> is the field itself or a tuple that contains it.</summary>
    private static bool WritesField(IOperation target, IFieldSymbol field) => target switch
    {
        IFieldReferenceOperation reference => SymbolEqualityComparer.Default.Equals(reference.Field.OriginalDefinition, field),
        ITupleOperation tuple => tuple.Elements.Any(e => WritesField(e, field)),
        IConversionOperation conversion => WritesField(conversion.Operand, field),
        _ => false,
    };

    /// <summary>Evaluates a call in a program-scope initializer and returns its value as a literal.</summary>
    private Expr FoldCall(IInvocationOperation operation)
    {
        if (ContainsInvalid(operation))
        {
            SawInvalidCode = true;
            return Expr.Error;
        }

        if (!Folder.TryEvaluate(operation, out FoldedValue? value, out string? reason))
        {
            return NotConstant(operation, reason!);
        }

        return Literal(value!);
    }

    /// <summary>Reports that part of a program-scope initializer cannot be evaluated at compile time.</summary>
    private Expr NotConstant(IOperation operation, string reason)
    {
        if (_function.Global is { } global)
        {
            ReportNonConstant(operation.Syntax.GetLocation(), global, reason);
        }

        return Expr.Error;
    }

    private void ReportNonConstant(Location location, IFieldSymbol field, string reason) =>
        _diagnostics.Report(DiagnosticDescriptors.NonConstantStaticInitializer, location, field.ContainingType.Name + "." + field.Name, reason);

    /// <summary>Formats a folded value as an MSL literal (<c>1.5f</c>, <c>float3(...)</c>, <c>float2x2(...)</c>).</summary>
    private static Expr Literal(FoldedValue value)
    {
        if (value.Components.Length == 1 && value.Type.Kind is GpuTypeKind.Scalar or GpuTypeKind.Bool)
        {
            return ScalarLiteral(value.Kind, value.Components[0]);
        }

        return Call(value.Type.MslName, value.Components.Select(c => ScalarLiteral(value.Kind, c).Text).ToArray());
    }

    private static Expr ScalarLiteral(ScalarKind kind, double value) => kind switch
    {
        ScalarKind.Float => FloatLiteral((float)value),
        ScalarKind.Int => Int32Literal((int)value),
        ScalarKind.UInt => new Expr(((uint)value).ToString(CultureInfo.InvariantCulture) + "u", Expr.Primary),
        _ => new Expr(value != 0 ? "true" : "false", Expr.Primary),
    };

    /// <summary>The binding strength of an <c>[MslBinaryOperator]</c> operator, for parenthesizing its operands.</summary>
    private static bool TryGetOperatorPrecedence(string op, out int precedence)
    {
        precedence = op switch
        {
            "*" or "/" or "%" => Expr.Multiplicative,
            "+" or "-" => Expr.Additive,
            "<<" or ">>" => Expr.Shift,
            "<" or "<=" or ">" or ">=" => Expr.Relational,
            "==" or "!=" => Expr.Equality,
            "&" => Expr.BitwiseAnd,
            "^" => Expr.BitwiseXor,
            "|" => Expr.BitwiseOr,
            "&&" => Expr.LogicalAnd,
            "||" => Expr.LogicalOr,
            _ => 0,
        };

        return precedence != 0;
    }

    // ---------------------------------------------------------------------------------------------
    // Local functions
    // ---------------------------------------------------------------------------------------------

    private readonly Dictionary<IMethodSymbol, bool> _localFunctionChecks = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// A local function is hoisted to a separate MSL function, so it cannot read the locals and
    /// parameters of the method around it (SCG0025). Reading captured shader state is fine: that is
    /// forwarded like it is for instance helper methods. Returns false (and records the function as
    /// failed) when it captures.
    /// </summary>
    private bool CheckLocalFunction(IMethodSymbol localFunction, IOperation? body)
    {
        localFunction = localFunction.OriginalDefinition;

        if (_localFunctionChecks.TryGetValue(localFunction, out bool ok))
        {
            return ok;
        }

        ok = true;

        if (body is not null && FindCapture(body, localFunction) is { } capture)
        {
            var syntax = localFunction.DeclaringSyntaxReferences.Select(r => r.GetSyntax(_cancellationToken)).OfType<LocalFunctionStatementSyntax>().FirstOrDefault();
            _diagnostics.Report(DiagnosticDescriptors.CapturingLocalFunction, syntax?.Identifier.GetLocation() ?? capture.Syntax.GetLocation(), localFunction.Name, capture.Name);
            _failedSymbols.Add(localFunction);
            ok = false;
        }

        _localFunctionChecks[localFunction] = ok;
        return ok;
    }

    private (string Name, SyntaxNode Syntax)? FindCapture(IOperation operation, IMethodSymbol localFunction)
    {
        ISymbol? symbol = operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter when !_captures.TryGet(parameter.Parameter, out _) && !_captures.IsFailed(parameter.Parameter) => parameter.Parameter,
            _ => null,
        };

        if (symbol is not null && !IsDeclaredWithin(symbol, localFunction))
        {
            return (symbol.Name, operation.Syntax);
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            if (FindCapture(child, localFunction) is { } capture)
            {
                return capture;
            }
        }

        return null;
    }

    private static bool IsDeclaredWithin(ISymbol symbol, IMethodSymbol method)
    {
        for (ISymbol? container = symbol.ContainingSymbol; container is IMethodSymbol; container = container.ContainingSymbol)
        {
            if (SymbolEqualityComparer.Default.Equals(container, method))
            {
                return true;
            }
        }

        return false;
    }
}
