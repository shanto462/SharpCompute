using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>Kernel built-in inputs, emitted as kernel parameters only when used.</summary>
[Flags]
internal enum Builtins
{
    None = 0,
    ThreadPositionInGrid = 1,
    ThreadsPerGrid = 2,
    ThreadPositionInThreadgroup = 4,
    ThreadgroupPositionInGrid = 8,
}

internal static class BuiltinInfo
{
    public static readonly Builtins[] All =
    {
        Builtins.ThreadPositionInGrid,
        Builtins.ThreadsPerGrid,
        Builtins.ThreadPositionInThreadgroup,
        Builtins.ThreadgroupPositionInGrid,
    };

    public static string Name(Builtins builtin) => builtin switch
    {
        Builtins.ThreadPositionInGrid => "__tid",
        Builtins.ThreadsPerGrid => "__grid",
        Builtins.ThreadPositionInThreadgroup => "__gtid",
        _ => "__gid",
    };

    /// <summary>
    /// The built-ins a partial-grid (windowed) dispatch has to rebase: the thread and threadgroup
    /// positions get the window's offset, and the grid size is the full grid rather than the part.
    /// The position within a threadgroup needs nothing, because windows are threadgroup aligned.
    /// </summary>
    public const Builtins Windowed = Builtins.ThreadPositionInGrid | Builtins.ThreadsPerGrid | Builtins.ThreadgroupPositionInGrid;

    /// <summary>The name of the raw Metal argument of a windowed built-in, which the kernel prologue rebases into <see cref="Name"/>.</summary>
    public static string RawName(Builtins builtin) => Name(builtin) + "_in";

    /// <summary>The <c>__GridWindow</c> member added to a windowed built-in, or null for the grid size, which is replaced.</summary>
    public static string? WindowOffset(Builtins builtin) => builtin switch
    {
        Builtins.ThreadPositionInGrid => "offset",
        Builtins.ThreadgroupPositionInGrid => "groupOffset",
        _ => null,
    };

    public static string Attribute(Builtins builtin) => builtin switch
    {
        Builtins.ThreadPositionInGrid => "thread_position_in_grid",
        Builtins.ThreadsPerGrid => "threads_per_grid",
        Builtins.ThreadPositionInThreadgroup => "thread_position_in_threadgroup",
        _ => "threadgroup_position_in_grid",
    };
}

/// <summary>A captured field or primary-constructor parameter of the shader struct.</summary>
internal sealed class Capture
{
    public Capture(ISymbol symbol, GpuType type, string mslName)
    {
        Symbol = symbol;
        Type = type;
        MslName = mslName;
    }

    public ISymbol Symbol { get; }

    public GpuType Type { get; }

    /// <summary>The kernel parameter name (buffers) or the member name inside <c>__Constants</c>.</summary>
    public string MslName { get; }

    public bool IsBuffer => Type.Kind == GpuTypeKind.Buffer;

    public bool IsParameter => Symbol is IParameterSymbol;

    /// <summary>Set by the translator when <c>.Length</c> is read, which adds a hidden constant.</summary>
    public bool LengthUsed { get; set; }

    public string LengthName => MslNames.LengthPrefix + MslNames.Sanitize(Symbol.Name);
}

internal sealed class CaptureSet
{
    private readonly Dictionary<ISymbol, Capture> _bySymbol = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> _failed = new(SymbolEqualityComparer.Default);

    public List<Capture> All { get; } = new();

    public HashSet<string> BufferNames { get; } = new(StringComparer.Ordinal);

    public IEnumerable<Capture> Buffers => All.Where(static c => c.IsBuffer);

    public IEnumerable<Capture> Constants => All.Where(static c => !c.IsBuffer);

    public void Add(Capture capture)
    {
        All.Add(capture);
        _bySymbol[capture.Symbol] = capture;

        if (capture.IsBuffer)
        {
            BufferNames.Add(capture.MslName);
        }
    }

    /// <summary>Records state that could not be captured, so that uses of it are not reported twice.</summary>
    public void AddFailed(ISymbol symbol) => _failed.Add(symbol);

    public bool TryGet(ISymbol symbol, out Capture capture) => _bySymbol.TryGetValue(symbol, out capture!);

    public bool IsFailed(ISymbol symbol) => _failed.Contains(symbol);
}

/// <summary>A helper method or local function translated to an MSL function.</summary>
internal sealed class HelperFunction
{
    public HelperFunction(string name, string signature, string body, HiddenInputs inputs)
    {
        Name = name;
        Signature = signature;
        Body = body;
        Inputs = inputs;
    }

    public string Name { get; }

    /// <summary>The declaration without a trailing <c>;</c> or body.</summary>
    public string Signature { get; }

    public string Body { get; }

    /// <summary>Kernel inputs used by this function or anything it calls; passed as trailing hidden parameters.</summary>
    public HiddenInputs Inputs { get; }
}

/// <summary>
/// The kernel parameters a function reads besides its own parameters: captured buffers, the
/// constants block, and built-ins. A helper receives them as trailing hidden parameters with the
/// kernel's names, in this order, so callers forward them unchanged.
/// </summary>
internal sealed class HiddenInputs
{
    private readonly HashSet<Capture> _bufferSet = new();

    public List<Capture> Buffers { get; } = new();

    public bool UsesConstants { get; set; }

    public Builtins Builtins { get; set; }

    public void AddBuffer(Capture buffer)
    {
        if (_bufferSet.Add(buffer))
        {
            Buffers.Add(buffer);
        }
    }

    public void Add(HiddenInputs other)
    {
        foreach (Capture buffer in other.Buffers)
        {
            AddBuffer(buffer);
        }

        UsesConstants |= other.UsesConstants;
        Builtins |= other.Builtins;
    }

    /// <summary>The hidden parameter declarations, in forwarding order.</summary>
    public IEnumerable<string> Parameters()
    {
        foreach (Capture buffer in Buffers)
        {
            yield return buffer.Type.ParameterTypeName + " " + buffer.MslName;
        }

        if (UsesConstants)
        {
            yield return "constant " + MslNames.ConstantsStruct + "& " + MslNames.Constants;
        }

        foreach (Builtins builtin in BuiltinInfo.All)
        {
            if ((Builtins & builtin) != 0)
            {
                yield return "uint3 " + BuiltinInfo.Name(builtin);
            }
        }
    }

    /// <summary>The hidden arguments, matching <see cref="Parameters"/>.</summary>
    public IEnumerable<string> Arguments()
    {
        foreach (Capture buffer in Buffers)
        {
            yield return buffer.MslName;
        }

        if (UsesConstants)
        {
            yield return MslNames.Constants;
        }

        foreach (Builtins builtin in BuiltinInfo.All)
        {
            if ((Builtins & builtin) != 0)
            {
                yield return BuiltinInfo.Name(builtin);
            }
        }
    }
}

/// <summary>A <c>static readonly</c> or <c>const</c> field lowered to an MSL program-scope constant.</summary>
internal sealed class GlobalConstant
{
    public GlobalConstant(string name, string type, string initializer)
    {
        Name = name;
        Type = type;
        Initializer = initializer;
    }

    public string Name { get; }

    public string Type { get; }

    public string Initializer { get; }
}

/// <summary>
/// Translates method bodies to MSL by walking Roslyn's <see cref="IOperation"/> tree, which makes
/// implicit conversions, constant values, named/optional arguments, and operator methods explicit.
/// </summary>
internal sealed partial class MslTranslator
{
    private readonly SemanticModel _semanticModel;
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _shaderType;
    private readonly TypeResolver _types;
    private readonly CaptureSet _captures;
    private readonly DiagnosticBag _diagnostics;
    private readonly NameAllocator _globalNames;
    private readonly HashSet<string> _reservedLocalNames;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModels = new();
    private readonly Dictionary<IMethodSymbol, HelperFunction?> _helpers = new(SymbolEqualityComparer.Default);
    private readonly List<HelperFunction> _helperOrder = new();
    private readonly List<IMethodSymbol> _callStack = new();
    private readonly HashSet<ISymbol> _failedSymbols = new(SymbolEqualityComparer.Default);
    private readonly bool _isPixelShader;
    private readonly Dictionary<IFieldSymbol, GlobalConstant?> _globals = new(SymbolEqualityComparer.Default);
    private readonly List<GlobalConstant> _globalOrder = new();
    private readonly List<IFieldSymbol> _globalsInProgress = new();
    private FunctionContext _function = new(FunctionKind.Kernel);

    public MslTranslator(
        SemanticModel semanticModel,
        INamedTypeSymbol shaderType,
        TypeResolver types,
        CaptureSet captures,
        DiagnosticBag diagnostics,
        NameAllocator globalNames,
        HashSet<string> reservedLocalNames,
        bool isPixelShader,
        CancellationToken cancellationToken)
    {
        _isPixelShader = isPixelShader;
        _semanticModel = semanticModel;
        _compilation = semanticModel.Compilation;
        _shaderType = shaderType;
        _types = types;
        _captures = captures;
        _diagnostics = diagnostics;
        _globalNames = globalNames;
        _reservedLocalNames = reservedLocalNames;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Helper functions reachable from the kernel, callees before callers.</summary>
    public IReadOnlyList<HelperFunction> Helpers => _helperOrder;

    /// <summary>Program-scope constants used by the shader, dependencies first.</summary>
    public IReadOnlyList<GlobalConstant> Globals => _globalOrder;

    /// <summary>True when a <c>__ms_*</c> prelude helper is called, so the MSL needs <c>MslPrelude.Source</c>.</summary>
    public bool UsesPrelude { get; private set; }

    /// <summary>Every local and parameter name that was emitted, used to detect clashes with struct names.</summary>
    public HashSet<string> LocalNames { get; } = new(StringComparer.Ordinal);

    /// <summary>Set when the user's code contains compile errors; the generator then stays quiet.</summary>
    public bool SawInvalidCode { get; private set; }

    private CodeWriter Writer => _function.Writer;

    /// <summary>Translates the body of <c>Execute</c>. Returns null when it has no body in source.</summary>
    public string? TranslateKernel(IMethodSymbol execute, out Builtins builtins)
    {
        builtins = Builtins.None;
        _function = new FunctionContext(FunctionKind.Kernel);

        if (GetBody(execute) is not { } body)
        {
            return null;
        }

        foreach (IOperation statement in body.Operations)
        {
            Statement(statement);
        }

        builtins = _function.Inputs.Builtins;
        return _function.Writer.ToString();
    }

    private SemanticModel GetSemanticModel(SyntaxTree tree)
    {
        if (tree == _semanticModel.SyntaxTree)
        {
            return _semanticModel;
        }

        if (!_semanticModels.TryGetValue(tree, out SemanticModel? model))
        {
            model = _compilation.GetSemanticModel(tree);
            _semanticModels.Add(tree, model);
        }

        return model;
    }

    private IBlockOperation? GetBody(IMethodSymbol method)
    {
        method = method.PartialImplementationPart ?? method;

        foreach (SyntaxReference reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(_cancellationToken) is LocalFunctionStatementSyntax localFunction)
            {
                if (GetSemanticModel(localFunction.SyntaxTree).GetOperation(localFunction, _cancellationToken) is ILocalFunctionOperation operation)
                {
                    return operation.Body ?? operation.IgnoredBody;
                }

                continue;
            }

            if (reference.GetSyntax(_cancellationToken) is not BaseMethodDeclarationSyntax declaration ||
                (declaration.Body is null && declaration.ExpressionBody is null))
            {
                continue;
            }

            SemanticModel model = GetSemanticModel(declaration.SyntaxTree);

            if (model.GetOperation(declaration, _cancellationToken) is IMethodBodyOperation methodBody)
            {
                return methodBody.BlockBody ?? methodBody.ExpressionBody;
            }

            SyntaxNode bodySyntax = (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody!;

            if (model.GetOperation(bodySyntax, _cancellationToken) is IBlockOperation block)
            {
                return block;
            }
        }

        return null;
    }

    private HelperFunction? GetHelper(IMethodSymbol method, IOperation callSite)
    {
        method = method.OriginalDefinition;

        if (_helpers.TryGetValue(method, out HelperFunction? existing))
        {
            return existing;
        }

        int index = _callStack.FindIndex(m => SymbolEqualityComparer.Default.Equals(m, method));

        if (index >= 0)
        {
            string cycle = string.Join(" -> ", _callStack.Skip(index).Select(static m => m.Name).Concat(new[] { method.Name }));
            _diagnostics.Report(DiagnosticDescriptors.RecursiveHelperMethod, callSite.Syntax.GetLocation(), method.Name, cycle);
            return null;
        }

        FunctionContext caller = _function;
        _function = new FunctionContext(FunctionKind.Helper);
        _callStack.Add(method);
        HelperFunction? helper = null;

        try
        {
            helper = TranslateHelper(method, callSite);
        }
        finally
        {
            _callStack.RemoveAt(_callStack.Count - 1);
            _function = caller;
        }

        _helpers[method] = helper;

        if (helper is not null)
        {
            _helperOrder.Add(helper);
        }

        return helper;
    }

    private HelperFunction? TranslateHelper(IMethodSymbol method, IOperation callSite)
    {
        SyntaxNode? declaration = (method.PartialImplementationPart ?? method).DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(_cancellationToken))
            .FirstOrDefault(static s => s is MethodDeclarationSyntax or LocalFunctionStatementSyntax);
        TypeSyntax? returnTypeSyntax = declaration switch
        {
            MethodDeclarationSyntax m => m.ReturnType,
            LocalFunctionStatementSyntax f => f.ReturnType,
            _ => null,
        };

        Location fallback = callSite.Syntax.GetLocation();
        bool failed = false;

        GpuType? returnType = _types.Resolve(method.ReturnType, _diagnostics, returnTypeSyntax?.GetLocation() ?? fallback);

        if (returnType is null || returnType.Kind == GpuTypeKind.Buffer)
        {
            if (returnType is not null)
            {
                _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, returnTypeSyntax?.GetLocation() ?? fallback, "a helper method that returns a buffer");
            }

            failed = true;
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, returnTypeSyntax?.GetLocation() ?? fallback, "a ref return");
            failed = true;
        }

        var parameters = new List<string>();

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            var parameterSyntax = parameter.DeclaringSyntaxReferences.Select(r => r.GetSyntax(_cancellationToken)).OfType<ParameterSyntax>().FirstOrDefault();
            Location location = parameterSyntax?.GetLocation() ?? fallback;

            // Declare the name even when the parameter is rejected, so that the body still translates
            // and reports its own problems (but not this one again).
            string parameterName = DeclareLocal(parameter);

            if (parameter.IsParams)
            {
                _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, location, $"the 'params' parameter '{parameter.Name}'");
                _failedSymbols.Add(parameter);
                failed = true;
                continue;
            }

            GpuType? parameterType = _types.Resolve(parameter.Type, _diagnostics, parameterSyntax?.Type?.GetLocation() ?? location);

            if (parameterType is null || parameterType.Kind == GpuTypeKind.Void)
            {
                _failedSymbols.Add(parameter);
                failed = true;
                continue;
            }

            if (parameter.RefKind is RefKind.Ref or RefKind.Out)
            {
                if (parameterType.Kind == GpuTypeKind.Buffer)
                {
                    _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, location, $"the '{RefKindText(parameter.RefKind)}' buffer parameter '{parameter.Name}'");
                    _failedSymbols.Add(parameter);
                    failed = true;
                    continue;
                }

                // A reference to a local of the caller. Call sites only pass locals and parameters,
                // which live in the thread address space.
                parameters.Add("thread " + parameterType.MslName + "& " + parameterName);
                continue;
            }

            // `in` parameters are read-only, so passing them by value is equivalent. (A `const T&` would
            // need an address space, and `const thread T&` cannot bind to a buffer element.)
            parameters.Add(parameterType.ParameterTypeName + " " + parameterName);
        }

        IBlockOperation? body = GetBody(method);

        if (body is null)
        {
            _diagnostics.Report(DiagnosticDescriptors.UnsupportedMethodCall, fallback, method.Name, "it has no body in source");
            return null;
        }

        if (method.MethodKind == MethodKind.LocalFunction && !CheckLocalFunction(method, body))
        {
            return null;
        }

        foreach (IOperation statement in body.Operations)
        {
            Statement(statement);
        }

        if (failed)
        {
            return null;
        }

        HiddenInputs inputs = _function.Inputs;
        parameters.AddRange(inputs.Parameters());

        string name = _globalNames.Allocate(MslNames.Escape(HelperName(method)));
        string signature = "static " + returnType!.MslName + " " + name + "(" + string.Join(", ", parameters) + ")";

        return new HelperFunction(name, signature, _function.Writer.ToString(), inputs);
    }

    /// <summary>
    /// The preferred MSL name of a helper. Local functions are hoisted to program scope, so their name
    /// is prefixed with the methods that contain them (<c>Execute_Mod</c>, <c>Box_SDBox</c>).
    /// </summary>
    private static string HelperName(IMethodSymbol method)
    {
        string name = method.Name;

        for (ISymbol? container = method.ContainingSymbol; container is IMethodSymbol containingMethod; container = container.ContainingSymbol)
        {
            name = containingMethod.Name + "_" + name;
        }

        return name;
    }

    private static string RefKindText(RefKind kind) => kind == RefKind.Out ? "out" : "ref";

    /// <summary>Assigns the MSL name of a local or parameter in the current function.</summary>
    private string DeclareLocal(ISymbol symbol)
    {
        string name = MslNames.Escape(symbol.Name);

        // A local may legally share its name with a captured buffer (a kernel parameter in MSL, and a
        // hidden parameter of helpers that use it) or with a user struct type; rename it so that it
        // does not shadow them.
        if (_reservedLocalNames.Contains(name) || _captures.BufferNames.Contains(name))
        {
            name = MslNames.LocalPrefix + MslNames.Sanitize(symbol.Name);
        }

        _function.Names[symbol] = name;
        LocalNames.Add(name);
        return name;
    }

    private void UseBuiltin(Builtins builtin) => _function.Inputs.Builtins |= builtin;

    private Expr Unsupported(IOperation operation, string construct)
    {
        if (ContainsInvalid(operation))
        {
            // The C# compiler already reports this code; a second, vaguer diagnostic would not help.
            SawInvalidCode = true;
            return Expr.Error;
        }

        _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, DiagnosticLocation(operation), construct);
        return Expr.Error;
    }

    /// <summary>
    /// True when the operation reads a local, parameter, or captured field that was already rejected.
    /// Its declaration carries the diagnostic; repeating it at every use would bury the cause.
    /// </summary>
    private bool ReferencesFailedSymbol(IOperation operation)
    {
        ISymbol? symbol = operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IFieldReferenceOperation field => field.Field,
            _ => null,
        };

        if (symbol is not null && (_failedSymbols.Contains(symbol) || _captures.IsFailed(symbol)))
        {
            return true;
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            if (ReferencesFailedSymbol(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInvalid(IOperation operation)
    {
        if (operation is IInvalidOperation || operation.Type?.TypeKind == TypeKind.Error)
        {
            return true;
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            if (ContainsInvalid(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Statements are reported at their first token so the squiggle does not cover a whole block.</summary>
    private static Location DiagnosticLocation(IOperation operation)
    {
        SyntaxNode syntax = operation.Syntax;

        return syntax switch
        {
            LocalFunctionStatementSyntax localFunction => localFunction.Identifier.GetLocation(),
            StatementSyntax or CatchClauseSyntax or SwitchSectionSyntax => syntax.GetFirstToken().GetLocation(),
            AnonymousFunctionExpressionSyntax or QueryExpressionSyntax or SwitchExpressionSyntax => FirstLine(syntax),
            _ => syntax.GetLocation(),
        };
    }

    private static Location FirstLine(SyntaxNode syntax)
    {
        Location location = syntax.GetLocation();
        FileLinePositionSpan span = location.GetLineSpan();

        if (span.StartLinePosition.Line == span.EndLinePosition.Line)
        {
            return location;
        }

        var line = syntax.SyntaxTree.GetText().Lines[span.StartLinePosition.Line];
        return Location.Create(syntax.SyntaxTree, Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(syntax.SpanStart, line.End));
    }

    /// <summary>Names constructs that have no MSL equivalent; returns null for everything else.</summary>
    private static string? DescribeUnsupported(IOperation operation)
    {
        switch (operation.Kind)
        {
            case OperationKind.AnonymousFunction:
                return operation.Syntax is AnonymousMethodExpressionSyntax ? "an anonymous method" : "a lambda expression";
            case OperationKind.DelegateCreation:
                return ((IDelegateCreationOperation)operation).Target is IAnonymousFunctionOperation
                    ? (operation.Syntax is AnonymousMethodExpressionSyntax ? "an anonymous method" : "a lambda expression")
                    : "a delegate creation";
            case OperationKind.MethodReference:
                return "a method group";
            case OperationKind.TranslatedQuery:
                return "a LINQ query expression";
            case OperationKind.ObjectCreation when operation.Type is { IsReferenceType: true } type:
                return $"'new' of the class '{TypeResolver.Display(type)}'";
            case OperationKind.AnonymousObjectCreation:
                return "an anonymous object creation";
            case OperationKind.ArrayCreation:
                return "an array creation";
            case OperationKind.ArrayElementReference:
                return "an array element access";
            case OperationKind.InterpolatedString:
                return "an interpolated string";
            case OperationKind.Literal when operation.Type?.SpecialType == SpecialType.System_String:
                return "a string literal";
            case OperationKind.Literal when operation.ConstantValue is { HasValue: true, Value: null }:
                return "a null literal";
            case OperationKind.IsType:
                return "an 'is' expression";
            case OperationKind.IsPattern:
                return "pattern matching";
            case OperationKind.SwitchExpression:
                return "a switch expression";
            case OperationKind.Await:
                return "an await expression";
            case OperationKind.TypeOf:
                return "a typeof expression";
            case OperationKind.NameOf:
                return "a nameof expression";
            case OperationKind.Throw:
                return operation.Syntax is ThrowStatementSyntax ? "a throw statement" : "a throw expression";
            case OperationKind.Coalesce:
                return "the '??' operator";
            case OperationKind.CoalesceAssignment:
                return "the '??=' operator";
            case OperationKind.ConditionalAccess:
                return "the null-conditional operator";
            case OperationKind.Tuple:
                return "a tuple expression";
            case OperationKind.DeconstructionAssignment:
                return "a deconstruction";
            case OperationKind.DeclarationExpression:
                return "an inline variable declaration";
            case OperationKind.AddressOf:
                return "the address-of operator";
            case OperationKind.Range:
                return "a range expression";
            case OperationKind.With:
                return "a 'with' expression";
            case OperationKind.DynamicInvocation:
            case OperationKind.DynamicMemberReference:
            case OperationKind.DynamicObjectCreation:
            case OperationKind.DynamicIndexerAccess:
                return "a dynamic operation";
            case OperationKind.InstanceReference:
                return "'this' used as a value";
            case OperationKind.Try:
                return ((ITryOperation)operation).Catches.Length > 0 ? "a try/catch statement" : "a try/finally statement";
            case OperationKind.Using:
            case OperationKind.UsingDeclaration:
                return "a using statement";
            case OperationKind.Lock:
                return "a lock statement";
            case OperationKind.Loop when ((ILoopOperation)operation).LoopKind == LoopKind.ForEach:
                return "a foreach loop";
            case OperationKind.Labeled:
                return "a labeled statement";
            case OperationKind.Branch when ((IBranchOperation)operation).BranchKind == BranchKind.GoTo:
                return "a goto statement";
            case OperationKind.YieldReturn:
            case OperationKind.YieldBreak:
                return "a yield statement";
            default:
                return null;
        }
    }

    /// <summary>Turns a syntax kind such as <c>StackAllocArrayCreationExpression</c> into readable words.</summary>
    private static string Humanize(SyntaxNode syntax)
    {
        string kind = syntax.Kind().ToString();
        var builder = new StringBuilder(kind.Length + 8);

        for (int i = 0; i < kind.Length; i++)
        {
            if (i > 0 && char.IsUpper(kind[i]))
            {
                builder.Append(' ');
            }

            builder.Append(char.ToLowerInvariant(kind[i]));
        }

        return "a " + builder;
    }

    private enum FunctionKind
    {
        /// <summary>The body of <c>Execute</c>.</summary>
        Kernel,

        /// <summary>A helper method or hoisted local function.</summary>
        Helper,

        /// <summary>The initializer of a program-scope constant, which must not call functions.</summary>
        GlobalInitializer,
    }

    private sealed class FunctionContext
    {
        public FunctionContext(FunctionKind kind) => Kind = kind;

        public FunctionKind Kind { get; }

        public bool IsKernel => Kind == FunctionKind.Kernel;

        public bool IsGlobalInitializer => Kind == FunctionKind.GlobalInitializer;

        /// <summary>For <see cref="FunctionKind.GlobalInitializer"/>: the field being initialized.</summary>
        public IFieldSymbol? Global { get; set; }

        public Dictionary<ISymbol, string> Names { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>Locals of inline <c>out T x</c> declarations, already declared before their statement.</summary>
        public HashSet<ILocalSymbol> HoistedLocals { get; } = new(SymbolEqualityComparer.Default);

        public HiddenInputs Inputs { get; } = new();

        public CodeWriter Writer { get; } = new(indent: 1);
    }

    /// <summary>A translated expression plus the binding strength needed to parenthesize it correctly.</summary>
    private readonly struct Expr
    {
        public const int Assignment = 1;
        public const int Conditional = 2;
        public const int LogicalOr = 3;
        public const int LogicalAnd = 4;
        public const int BitwiseOr = 5;
        public const int BitwiseXor = 6;
        public const int BitwiseAnd = 7;
        public const int Equality = 8;
        public const int Relational = 9;
        public const int Shift = 10;
        public const int Additive = 11;
        public const int Multiplicative = 12;
        public const int Unary = 13;
        public const int Primary = 14;

        public static readonly Expr Error = new("0 /* error */", Primary);

        public Expr(string text, int precedence, bool parenthesized = false)
        {
            Text = text;
            Precedence = precedence;
            Parenthesized = parenthesized;
        }

        /// <summary>The expression without surrounding parentheses; valid wherever a full expression is expected.</summary>
        public string Text { get; }

        public int Precedence { get; }

        /// <summary>True when the C# source wrapped this expression in parentheses, which we then preserve.</summary>
        public bool Parenthesized { get; }

        public Expr Parenthesize() => new(Text, Precedence, parenthesized: true);

        /// <summary>The text to use as an operand of an operator that binds with <paramref name="minimum"/> strength.</summary>
        public string Operand(int minimum) => Parenthesized || Precedence < minimum ? "(" + Text + ")" : Text;
    }
}
