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
    ThreadsPerThreadgroup = 16,
    ThreadgroupsPerGrid = 32,
    ThreadIndexInThreadgroup = 64,

    // The SIMD-group built-ins, which are scalars (see BuiltinInfo.Type).
    ThreadIndexInSimdgroup = 128,
    ThreadsPerSimdgroup = 256,
    SimdgroupIndexInThreadgroup = 512,
    SimdgroupsPerThreadgroup = 1024,
}

internal static class BuiltinInfo
{
    public static readonly Builtins[] All =
    {
        Builtins.ThreadPositionInGrid,
        Builtins.ThreadsPerGrid,
        Builtins.ThreadPositionInThreadgroup,
        Builtins.ThreadgroupPositionInGrid,
        Builtins.ThreadsPerThreadgroup,
        Builtins.ThreadgroupsPerGrid,
        Builtins.ThreadIndexInThreadgroup,
        Builtins.ThreadIndexInSimdgroup,
        Builtins.ThreadsPerSimdgroup,
        Builtins.SimdgroupIndexInThreadgroup,
        Builtins.SimdgroupsPerThreadgroup,
    };

    /// <summary>The SIMD-group built-ins: scalars, and the same in every part of a windowed dispatch (windows are threadgroup aligned).</summary>
    public const Builtins SimdGroup =
        Builtins.ThreadIndexInSimdgroup | Builtins.ThreadsPerSimdgroup | Builtins.SimdgroupIndexInThreadgroup | Builtins.SimdgroupsPerThreadgroup;

    public static string Name(Builtins builtin) => builtin switch
    {
        Builtins.ThreadPositionInGrid => "__tid",
        Builtins.ThreadsPerGrid => "__grid",
        Builtins.ThreadPositionInThreadgroup => "__gtid",
        Builtins.ThreadsPerThreadgroup => "__gsize",
        Builtins.ThreadgroupsPerGrid => "__groups",
        Builtins.ThreadIndexInThreadgroup => "__gindex",
        Builtins.ThreadIndexInSimdgroup => "__lane",
        Builtins.ThreadsPerSimdgroup => "__lanes",
        Builtins.SimdgroupIndexInThreadgroup => "__sgid",
        Builtins.SimdgroupsPerThreadgroup => "__sgroups",
        _ => "__gid",
    };

    /// <summary>
    /// The MSL type of the built-in's kernel argument: a <c>uint3</c>, except for the flat index in the
    /// threadgroup and the SIMD-group built-ins, which are scalars.
    /// </summary>
    public static string Type(Builtins builtin) =>
        builtin == Builtins.ThreadIndexInThreadgroup || (builtin & SimdGroup) != 0 ? "uint" : "uint3";

    /// <summary>
    /// The built-ins a partial-grid (windowed) dispatch has to rebase: the thread and threadgroup
    /// positions get the window's offset, the grid size is the full grid rather than the part, and
    /// the threadgroup count gets the threadgroups that lie before the window. The position, index
    /// and group size within a threadgroup need nothing, because windows are threadgroup aligned.
    /// </summary>
    public const Builtins Windowed =
        Builtins.ThreadPositionInGrid | Builtins.ThreadsPerGrid | Builtins.ThreadgroupPositionInGrid | Builtins.ThreadgroupsPerGrid;

    /// <summary>The name of the raw Metal argument of a windowed built-in, which the kernel prologue rebases into <see cref="Name"/>.</summary>
    public static string RawName(Builtins builtin) => Name(builtin) + "_in";

    /// <summary>
    /// The <c>__GridWindow</c> member added to a windowed built-in, or null for the grid size, which is
    /// replaced. The threadgroup count of the whole grid is the part's count plus the threadgroups
    /// before it: a window starts on a threadgroup boundary and runs to the end of the grid, so
    /// <c>ceil(part / size) + offset / size = ceil(grid / size)</c> along the cut axis, and the offset
    /// is zero along the others.
    /// </summary>
    public static string? WindowOffset(Builtins builtin) => builtin switch
    {
        Builtins.ThreadPositionInGrid => "offset",
        Builtins.ThreadgroupPositionInGrid or Builtins.ThreadgroupsPerGrid => "groupOffset",
        _ => null,
    };

    public static string Attribute(Builtins builtin) => builtin switch
    {
        Builtins.ThreadPositionInGrid => "thread_position_in_grid",
        Builtins.ThreadsPerGrid => "threads_per_grid",
        Builtins.ThreadPositionInThreadgroup => "thread_position_in_threadgroup",
        Builtins.ThreadsPerThreadgroup => "threads_per_threadgroup",
        Builtins.ThreadgroupsPerGrid => "threadgroups_per_grid",
        Builtins.ThreadIndexInThreadgroup => "thread_index_in_threadgroup",
        Builtins.ThreadIndexInSimdgroup => "thread_index_in_simdgroup",
        Builtins.ThreadsPerSimdgroup => "threads_per_simdgroup",
        Builtins.SimdgroupIndexInThreadgroup => "simdgroup_index_in_threadgroup",
        Builtins.SimdgroupsPerThreadgroup => "simdgroups_per_threadgroup",
        _ => "threadgroup_position_in_grid",
    };
}

/// <summary>
/// A <c>[GroupShared] static readonly T[] name = new T[N];</c> field of the shader struct: threadgroup
/// memory, declared as <c>threadgroup T name[N];</c> in the kernel.
/// </summary>
internal sealed class GroupSharedField
{
    public GroupSharedField(IFieldSymbol symbol, GpuType element, int length, string mslName)
    {
        Symbol = symbol;
        Element = element;
        Length = length;
        MslName = mslName;
    }

    public IFieldSymbol Symbol { get; }

    public GpuType Element { get; }

    /// <summary>The number of elements, a compile-time constant.</summary>
    public int Length { get; }

    /// <summary>The name of the array in the kernel, and of the pointer to it that helpers receive.</summary>
    public string MslName { get; }

    /// <summary>
    /// Bytes of threadgroup memory. The element is declared with its boundary (packed) type, as in a
    /// buffer, so this is the size of the C# array's elements too.
    /// </summary>
    public long ByteLength => (long)Length * Element.Size;

    /// <summary>The declaration inside the kernel.</summary>
    public string Declaration =>
        "threadgroup " + Element.BoundaryName + " " + MslName + "[" + Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "];";

    /// <summary>The hidden parameter through which a helper reaches the kernel's array.</summary>
    public string Parameter => "threadgroup " + Element.BoundaryName + "* " + MslName;
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

    /// <summary>The kernel parameter name (buffers and textures) or the member name inside <c>__Constants</c>.</summary>
    public string MslName { get; }

    public bool IsBuffer => Type.Kind == GpuTypeKind.Buffer;

    public bool IsTexture => Type.Kind == GpuTypeKind.Texture;

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

    /// <summary>
    /// The names of the captured buffers and textures (kernel parameters) and of the group-shared
    /// arrays (locals of the kernel): helpers receive all of them under these names, so locals must
    /// not shadow them.
    /// </summary>
    public HashSet<string> BufferNames { get; } = new(StringComparer.Ordinal);

    /// <summary>Allocates the names in <see cref="BufferNames"/>, which share one MSL scope.</summary>
    public NameAllocator ResourceNames { get; } = new();

    public IEnumerable<Capture> Buffers => All.Where(static c => c.IsBuffer);

    public IEnumerable<Capture> Textures => All.Where(static c => c.IsTexture);

    /// <summary>The captures stored in the constants block: everything that is not bound on its own.</summary>
    public IEnumerable<Capture> Constants => All.Where(static c => !c.Type.IsResource);

    public void Add(Capture capture)
    {
        All.Add(capture);
        _bySymbol[capture.Symbol] = capture;

        if (capture.Type.IsResource)
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

    /// <summary>The translated body. Final once the kernel is translated: until then it may hold the placeholders of texture fences.</summary>
    public string Body { get; set; }

    /// <summary>Kernel inputs used by this function or anything it calls; passed as trailing hidden parameters.</summary>
    public HiddenInputs Inputs { get; }
}

/// <summary>
/// The kernel parameters a function reads besides its own parameters: captured buffers and textures
/// (both in <see cref="Buffers"/>), group-shared arrays (which are locals of the kernel), the constants
/// block, and built-ins. A helper receives them as trailing hidden parameters with the kernel's names,
/// in this order, so callers forward them unchanged.
/// </summary>
internal sealed class HiddenInputs
{
    private readonly HashSet<Capture> _bufferSet = new();
    private readonly HashSet<GroupSharedField> _groupSharedSet = new();

    public List<Capture> Buffers { get; } = new();

    /// <summary>The group-shared arrays this function (or anything it calls) reads or writes.</summary>
    public List<GroupSharedField> GroupShared { get; } = new();

    public bool UsesConstants { get; set; }

    public Builtins Builtins { get; set; }

    public void AddBuffer(Capture buffer)
    {
        if (_bufferSet.Add(buffer))
        {
            Buffers.Add(buffer);
        }
    }

    public void AddGroupShared(GroupSharedField field)
    {
        if (_groupSharedSet.Add(field))
        {
            GroupShared.Add(field);
        }
    }

    public void Add(HiddenInputs other)
    {
        foreach (Capture buffer in other.Buffers)
        {
            AddBuffer(buffer);
        }

        foreach (GroupSharedField field in other.GroupShared)
        {
            AddGroupShared(field);
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

        foreach (GroupSharedField field in GroupShared)
        {
            yield return field.Parameter;
        }

        if (UsesConstants)
        {
            yield return "constant " + MslNames.ConstantsStruct + "& " + MslNames.Constants;
        }

        foreach (Builtins builtin in BuiltinInfo.All)
        {
            if ((Builtins & builtin) != 0)
            {
                yield return BuiltinInfo.Type(builtin) + " " + BuiltinInfo.Name(builtin);
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

        foreach (GroupSharedField field in GroupShared)
        {
            // The kernel's array decays to the pointer a helper takes; a helper forwards its pointer.
            yield return field.MslName;
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

/// <summary>
/// The six samplers of <c>SharpCompute.Sampler</c>. Each one a shader uses becomes a program-scope
/// <c>constexpr sampler</c>, so nothing is allocated or bound at run time.
/// </summary>
internal static class SamplerInfo
{
    /// <summary>The static properties of <c>Sampler</c>; the index identifies a sampler.</summary>
    private static readonly string[] Properties = { "PointClamp", "PointRepeat", "PointMirror", "LinearClamp", "LinearRepeat", "LinearMirror" };

    private static readonly string[] Addresses = { "clamp", "repeat", "mirror" };

    private static readonly string[] MslAddresses = { "clamp_to_edge", "repeat", "mirrored_repeat" };

    public static int Count => Properties.Length;

    /// <summary>The index of the <c>Sampler</c> property called <paramref name="property"/>, or -1.</summary>
    public static int IndexOf(string property) => Array.IndexOf(Properties, property);

    public static string Name(int index) =>
        MslNames.SamplerPrefix + (index < 3 ? "point" : "linear") + "_" + Addresses[index % 3];

    public static string Declaration(int index) =>
        $"constexpr sampler {Name(index)}(coord::normalized, filter::{(index < 3 ? "nearest" : "linear")}, address::{MslAddresses[index % 3]});";
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
    private readonly Dictionary<HelperKey, HelperFunction?> _helpers = new();
    private readonly List<HelperFunction> _helperOrder = new();
    private readonly List<IMethodSymbol> _callStack = new();
    private readonly HashSet<ISymbol> _failedSymbols = new(SymbolEqualityComparer.Default);
    private readonly bool _isPixelShader;
    private readonly Dictionary<IFieldSymbol, GlobalConstant?> _globals = new(SymbolEqualityComparer.Default);
    private readonly List<GlobalConstant> _globalOrder = new();
    private readonly List<IFieldSymbol> _globalsInProgress = new();
    private readonly bool[] _samplersUsed = new bool[SamplerInfo.Count];
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

    /// <summary>The declarations of the samplers the shader uses, in the order of <c>Sampler</c>'s properties.</summary>
    public IReadOnlyList<string> Samplers =>
        Enumerable.Range(0, SamplerInfo.Count).Where(i => _samplersUsed[i]).Select(SamplerInfo.Declaration).ToList();

    /// <summary>True when a <c>__ms_*</c> prelude helper is called, so the MSL needs <c>MslPrelude.Source</c>.</summary>
    public bool UsesPrelude { get; private set; }

    /// <summary>
    /// True when a lane-by-lane sum or difference of integer vectors is called (<c>__ms_add</c>,
    /// <c>__ms_sub</c> and their by-reference forms), so the MSL needs <c>MslPrelude.VectorArithmetic</c>.
    /// </summary>
    public bool UsesVectorArithmetic { get; private set; }

    /// <summary>
    /// True when an integer division or remainder goes through <c>__ms_nonzero</c>,
    /// <c>__ms_div_near_max</c> or <c>__ms_rem_near_max</c>, so the MSL needs <c>MslPrelude.IntegerDivision</c>.
    /// </summary>
    public bool UsesIntegerDivision { get; private set; }

    /// <summary>
    /// True when an <c>[MslAtomic]</c> call was translated, in the kernel or in a helper it reaches
    /// (helpers are only translated when called), on a buffer element or on threadgroup memory.
    /// </summary>
    public bool UsesAtomics { get; private set; }

    /// <summary>
    /// True when one of those calls is on a buffer element (<c>device</c> memory), which every thread
    /// of the dispatch shares. Published on the descriptor so that the runtime never divides such a
    /// shader between the CPU and the GPU; atomics on threadgroup memory stay within one group, which
    /// runs on one device, and do not set it.
    /// </summary>
    public bool UsesDeviceAtomics { get; private set; }

    /// <summary>Every local and parameter name that was emitted, used to detect clashes with struct names.</summary>
    public HashSet<string> LocalNames { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the kernel (or a helper it calls) uses a SIMD-group operation or built-in: its lanes
    /// compute results together, which the descriptor reports to the runtime.
    /// </summary>
    public bool UsesSimdGroups { get; private set; }

    /// <summary>Set when the user's code contains compile errors; the generator then stays quiet.</summary>
    public bool SawInvalidCode { get; private set; }

    private CodeWriter Writer => _function.Writer;

    /// <summary>
    /// Translates the body of <c>Execute</c>. Returns null when it has no body in source.
    /// <paramref name="groupShared"/> receives the group-shared arrays the kernel (or a helper it calls)
    /// uses, in declaration order: the kernel declares exactly these.
    /// </summary>
    public string? TranslateKernel(IMethodSymbol execute, out Builtins builtins, out IReadOnlyList<GroupSharedField> groupShared)
    {
        builtins = Builtins.None;
        groupShared = Array.Empty<GroupSharedField>();
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
        HiddenInputs inputs = _function.Inputs;
        groupShared = _groupShared.Where(inputs.GroupShared.Contains).ToList();

        // Everything the shader runs has been translated, so it is now known which textures are read:
        // settle the fences after the writes, here and in the helpers.
        foreach (HelperFunction helper in _helperOrder)
        {
            helper.Body = ResolveFences(helper.Body);
        }

        return ResolveFences(_function.Writer.ToString());
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
            SyntaxNode syntax = reference.GetSyntax(_cancellationToken);

            if (syntax is LocalFunctionStatementSyntax localFunction)
            {
                if (GetSemanticModel(localFunction.SyntaxTree).GetOperation(localFunction, _cancellationToken) is ILocalFunctionOperation operation)
                {
                    return operation.Body ?? operation.IgnoredBody;
                }

                continue;
            }

            // A method, operator or conversion; an accessor with a body; or the `=> value` of a property.
            SyntaxNode? bodySyntax = syntax switch
            {
                BaseMethodDeclarationSyntax declaration => (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody,
                AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
                ArrowExpressionClauseSyntax arrow => arrow,
                _ => null,
            };

            if (bodySyntax is null)
            {
                continue;
            }

            SemanticModel model = GetSemanticModel(syntax.SyntaxTree);

            switch (model.GetOperation(syntax, _cancellationToken))
            {
                case IMethodBodyBaseOperation methodBody:
                    return methodBody.BlockBody ?? methodBody.ExpressionBody;
                case IBlockOperation block:
                    return block;
            }

            if (model.GetOperation(bodySyntax, _cancellationToken) is IBlockOperation bodyBlock)
            {
                return bodyBlock;
            }
        }

        return null;
    }

    /// <summary>
    /// The MSL function for <paramref name="method"/>, translated on first use. <paramref name="mode"/>
    /// says how a member of a user struct receives the struct (see <see cref="ReceiverMode"/>); it is
    /// <see cref="ReceiverMode.None"/> for everything else.
    /// </summary>
    private HelperFunction? GetHelper(IMethodSymbol method, IOperation callSite, ReceiverMode mode = ReceiverMode.None)
    {
        method = Specialize(method);

        // A local function may name the type parameters of the generic method around it, so it is
        // one function per instantiation of that method.
        IMethodSymbol? instantiation = method.MethodKind == MethodKind.LocalFunction ? _function.Instantiation : null;
        var key = new HelperKey(method, mode, instantiation);

        if (_helpers.TryGetValue(key, out HelperFunction? existing))
        {
            return existing;
        }

        int index = _callStack.FindIndex(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, method.OriginalDefinition));

        if (index >= 0)
        {
            string cycle = string.Join(" -> ", _callStack.Skip(index).Select(DisplayName).Concat(new[] { DisplayName(method) }));
            _diagnostics.Report(DiagnosticDescriptors.RecursiveHelperMethod, callSite.Syntax.GetLocation(), DisplayName(method), cycle);
            return null;
        }

        FunctionContext caller = _function;
        IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol>? callerTypeArguments = _types.TypeArguments;

        _function = new FunctionContext(FunctionKind.Helper)
        {
            Instantiation = method.MethodKind == MethodKind.LocalFunction ? instantiation : (method.IsGenericMethod ? method : null),
        };

        if (method.MethodKind != MethodKind.LocalFunction)
        {
            // Inside the function, `T` means the type argument of this instantiation.
            _types.TypeArguments = TypeArgumentMap(method);
        }

        _callStack.Add(method);
        HelperFunction? helper = null;

        try
        {
            helper = TranslateHelper(key, callSite);
        }
        finally
        {
            _callStack.RemoveAt(_callStack.Count - 1);
            _function = caller;
            _types.TypeArguments = callerTypeArguments;
        }

        _helpers[key] = helper;

        if (helper is not null)
        {
            _helperOrder.Add(helper);
        }

        return helper;
    }

    private HelperFunction? TranslateHelper(HelperKey key, IOperation callSite)
    {
        // The parameters and the body are those of the definition; for a generic method the type
        // resolver replaces its type parameters with the arguments of this instantiation.
        IMethodSymbol method = key.Method.OriginalDefinition;
        bool isConstructor = method.MethodKind == MethodKind.Constructor;
        SyntaxNode? declaration = (method.PartialImplementationPart ?? method).DeclaringSyntaxReferences
            .Select(r => r.GetSyntax(_cancellationToken))
            .FirstOrDefault();
        TypeSyntax? returnTypeSyntax = declaration switch
        {
            MethodDeclarationSyntax m => m.ReturnType,
            LocalFunctionStatementSyntax f => f.ReturnType,
            OperatorDeclarationSyntax o => o.ReturnType,
            ConversionOperatorDeclarationSyntax c => c.Type,
            AccessorDeclarationSyntax { Parent.Parent: BasePropertyDeclarationSyntax property } => property.Type,
            ArrowExpressionClauseSyntax { Parent: BasePropertyDeclarationSyntax property } => property.Type,
            _ => null,
        };

        Location fallback = callSite.Syntax.GetLocation();
        bool failed = false;

        // A constructor becomes a function that returns the struct it builds.
        ITypeSymbol returnSymbol = isConstructor ? method.ContainingType : method.ReturnType;
        GpuType? returnType = ReturnsLocalArray(method, returnTypeSyntax?.GetLocation() ?? fallback)
            ? null
            : _types.Resolve(returnSymbol, _diagnostics, returnTypeSyntax?.GetLocation() ?? fallback);

        if (_function.ReturnsLocalArray)
        {
            failed = true;
        }
        else if (returnType is null || returnType.IsResource)
        {
            if (returnType is { Kind: GpuTypeKind.Buffer })
            {
                _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, returnTypeSyntax?.GetLocation() ?? fallback, "a helper method that returns a buffer");
            }
            else if (returnType is not null)
            {
                _diagnostics.Report(DiagnosticDescriptors.InvalidTextureHelperSignature, returnTypeSyntax?.GetLocation() ?? fallback, method.Name, "returns a texture");
            }

            failed = true;
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            _diagnostics.Report(DiagnosticDescriptors.UnsupportedSyntax, returnTypeSyntax?.GetLocation() ?? fallback, "a ref return");
            failed = true;
        }

        var parameters = new List<string>();

        if (key.Mode != ReceiverMode.None || isConstructor)
        {
            // A member of a user struct: the struct is `__this`, the first parameter (or, in a
            // constructor, the local that is returned).
            if (_types.Resolve(method.ContainingType) is not { Kind: GpuTypeKind.Struct } self)
            {
                return null;
            }

            _function.This = new ThisContext(method, self, isConstructor ? ReceiverMode.Thread : key.Mode);
            _function.IsConstructor = isConstructor;

            if (!isConstructor)
            {
                parameters.Add(ThisParameter(self, key.Mode));
            }
        }

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

            if (LocalArrayParameter(parameter, parameterName, parameterSyntax, location, ref failed) is { } arrayParameters)
            {
                // `Span<float> values` -> `thread float* values, int __len_values` (see MslTranslator.LocalArrays.cs).
                parameters.AddRange(arrayParameters);
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

                if (parameterType.Kind == GpuTypeKind.Texture)
                {
                    _diagnostics.Report(
                        DiagnosticDescriptors.InvalidTextureHelperSignature,
                        location,
                        method.Name,
                        $"takes the texture parameter '{parameter.Name}' by '{RefKindText(parameter.RefKind)}'");
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

        if (isConstructor)
        {
            if (!ConstructorBody(method, _function.This!.Type, callSite))
            {
                return null;
            }
        }
        else
        {
            IBlockOperation? body = GetBody(method);

            if (body is null)
            {
                _diagnostics.Report(DiagnosticDescriptors.UnsupportedMethodCall, fallback, DisplayName(method), "it has no body in source");
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
        }

        if (failed)
        {
            return null;
        }

        HiddenInputs inputs = _function.Inputs;
        parameters.AddRange(inputs.Parameters());

        string name = _globalNames.Allocate(MslNames.Escape(HelperName(key)));
        string signature = "static " + returnType!.MslName + " " + name + "(" + string.Join(", ", parameters) + ")";

        return new HelperFunction(name, signature, _function.Writer.ToString(), inputs);
    }

    private static string RefKindText(RefKind kind) => kind == RefKind.Out ? "out" : "ref";

    /// <summary>Assigns the MSL name of a local or parameter in the current function.</summary>
    private string DeclareLocal(ISymbol symbol)
    {
        string name = MslNames.Escape(symbol.Name);

        // A local may legally share its name with a captured buffer or texture (a kernel parameter in
        // MSL, and a hidden parameter of helpers that use it) or with a user struct type; rename it so
        // that it does not shadow them.
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

        /// <summary>For a member of a user struct: the struct it was called on, which is <c>__this</c> in the function.</summary>
        public ThisContext? This { get; set; }

        /// <summary>True in the function a constructor of a user struct becomes, which returns <c>__this</c>.</summary>
        public bool IsConstructor { get; set; }

        /// <summary>The constructed generic method whose type arguments are in effect, if any.</summary>
        public IMethodSymbol? Instantiation { get; set; }

        /// <summary>For <see cref="FunctionKind.GlobalInitializer"/>: the field being initialized.</summary>
        public IFieldSymbol? Global { get; set; }

        public Dictionary<ISymbol, string> Names { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>Locals of inline <c>out T x</c> declarations, already declared before their statement.</summary>
        public HashSet<ILocalSymbol> HoistedLocals { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>The local arrays of this function: its array locals, and its array parameters if it is a helper.</summary>
        public Dictionary<ISymbol, LocalArray> LocalArrays { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>Set for a helper whose return type is a local array type, which was reported at the signature.</summary>
        public bool ReturnsLocalArray { get; set; }

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
