using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// The SIMD-group operations of <c>SharpCompute.Gpu</c> (HLSL's wave and quad intrinsics) and the Metal
/// functions they map to. Like the texture members they are recognized by name: the C# methods carry
/// hidden call-site parameters and several need more than a function call (a conversion of the vote
/// mask, a lane index narrowed to <c>ushort</c>, a kernel built-in).
/// </summary>
internal static class SimdGroupInfo
{
    internal enum Form
    {
        /// <summary><c>int(built-in)</c>: the lane index and the lane count.</summary>
        Builtin,

        /// <summary><c>f()</c>.</summary>
        NoArguments,

        /// <summary><c>f(vote)</c>, returning a bool.</summary>
        Vote,

        /// <summary><c>uint(vote_t(f(vote)))</c>: the vote mask (64 bits for a SIMD group), narrowed to the lanes there are.</summary>
        Ballot,

        /// <summary><c>f(int(vote))</c>: a count of true votes.</summary>
        Count,

        /// <summary><c>f(value)</c>.</summary>
        Value,

        /// <summary><c>f(value, ushort(lane))</c>: a lane, a delta or a mask.</summary>
        ValueAndLane,

        /// <summary><c>f(value, ushort(N))</c> with a fixed N: the quad reads across an axis.</summary>
        ValueAndFixedLane,

        /// <summary><c>f(value, fill, ushort(delta))</c>: the shuffles that fill the lanes they shift in.</summary>
        ValueFillAndLane,

        /// <summary><c>f(value)</c> returning a bool, with <c>f</c> a prelude helper: the all-equal comparison Metal has no function for.</summary>
        Equal,
    }

    internal readonly struct Operation
    {
        public Operation(Form form, string function, Builtins builtin = Builtins.None, int fixedLane = 0, string voteType = "")
        {
            Form = form;
            Function = function;
            Builtin = builtin;
            FixedLane = fixedLane;
            VoteType = voteType;
        }

        public Form Form { get; }

        /// <summary>The Metal function, qualified so that no user name can hide it.</summary>
        public string Function { get; }

        public Builtins Builtin { get; }

        public int FixedLane { get; }

        /// <summary>For a ballot: the mask type its vote converts to, explicitly and to nothing else.</summary>
        public string VoteType { get; }
    }

    private static readonly Dictionary<string, Operation> Operations = new(System.StringComparer.Ordinal)
    {
        ["WaveGetLaneIndex"] = new(Form.Builtin, string.Empty, Builtins.ThreadIndexInSimdgroup),
        ["WaveGetLaneCount"] = new(Form.Builtin, string.Empty, Builtins.ThreadsPerSimdgroup),
        ["WaveIsFirstLane"] = new(Form.NoArguments, "metal::simd_is_first"),

        ["WaveActiveAnyTrue"] = new(Form.Vote, "metal::simd_any"),
        ["WaveActiveAllTrue"] = new(Form.Vote, "metal::simd_all"),
        ["WaveActiveBallot"] = new(Form.Ballot, "metal::simd_ballot", voteType: "metal::simd_vote::vote_t"),
        ["WaveActiveCountBits"] = new(Form.Count, "metal::simd_sum"),
        ["WavePrefixCountBits"] = new(Form.Count, "metal::simd_prefix_exclusive_sum"),

        ["WaveActiveSum"] = new(Form.Value, "metal::simd_sum"),
        ["WaveActiveProduct"] = new(Form.Value, "metal::simd_product"),
        ["WaveActiveMin"] = new(Form.Value, "metal::simd_min"),
        ["WaveActiveMax"] = new(Form.Value, "metal::simd_max"),
        ["WaveActiveBitAnd"] = new(Form.Value, "metal::simd_and"),
        ["WaveActiveBitOr"] = new(Form.Value, "metal::simd_or"),
        ["WaveActiveBitXor"] = new(Form.Value, "metal::simd_xor"),
        ["WaveActiveAllEqual"] = new(Form.Equal, "__ms_wave_all_equal"),

        ["WavePrefixSum"] = new(Form.Value, "metal::simd_prefix_exclusive_sum"),
        ["WavePrefixProduct"] = new(Form.Value, "metal::simd_prefix_exclusive_product"),
        ["WaveInclusivePrefixSum"] = new(Form.Value, "metal::simd_prefix_inclusive_sum"),
        ["WaveInclusivePrefixProduct"] = new(Form.Value, "metal::simd_prefix_inclusive_product"),

        ["WaveReadLaneAt"] = new(Form.ValueAndLane, "metal::simd_shuffle"),
        ["WaveReadLaneFirst"] = new(Form.Value, "metal::simd_broadcast_first"),
        ["WaveShuffleUp"] = new(Form.ValueAndLane, "metal::simd_shuffle_up"),
        ["WaveShuffleDown"] = new(Form.ValueAndLane, "metal::simd_shuffle_down"),
        ["WaveShuffleXor"] = new(Form.ValueAndLane, "metal::simd_shuffle_xor"),
        ["WaveRotateUp"] = new(Form.ValueAndLane, "metal::simd_shuffle_rotate_up"),
        ["WaveRotateDown"] = new(Form.ValueAndLane, "metal::simd_shuffle_rotate_down"),
        ["WaveShuffleUpAndFill"] = new(Form.ValueFillAndLane, "metal::simd_shuffle_and_fill_up"),
        ["WaveShuffleDownAndFill"] = new(Form.ValueFillAndLane, "metal::simd_shuffle_and_fill_down"),

        ["QuadActiveAnyTrue"] = new(Form.Vote, "metal::quad_any"),
        ["QuadActiveAllTrue"] = new(Form.Vote, "metal::quad_all"),
        ["QuadActiveBallot"] = new(Form.Ballot, "metal::quad_ballot", voteType: "metal::quad_vote::vote_t"),
        ["QuadActiveSum"] = new(Form.Value, "metal::quad_sum"),
        ["QuadActiveProduct"] = new(Form.Value, "metal::quad_product"),
        ["QuadActiveMin"] = new(Form.Value, "metal::quad_min"),
        ["QuadActiveMax"] = new(Form.Value, "metal::quad_max"),
        ["QuadActiveBitAnd"] = new(Form.Value, "metal::quad_and"),
        ["QuadActiveBitOr"] = new(Form.Value, "metal::quad_or"),
        ["QuadActiveBitXor"] = new(Form.Value, "metal::quad_xor"),
        ["QuadPrefixSum"] = new(Form.Value, "metal::quad_prefix_exclusive_sum"),
        ["QuadPrefixProduct"] = new(Form.Value, "metal::quad_prefix_exclusive_product"),
        ["QuadInclusivePrefixSum"] = new(Form.Value, "metal::quad_prefix_inclusive_sum"),
        ["QuadInclusivePrefixProduct"] = new(Form.Value, "metal::quad_prefix_inclusive_product"),

        // A quad's lanes are x + 2 * y, so the neighbour across an axis is one bit away.
        ["QuadReadAcrossX"] = new(Form.ValueAndFixedLane, "metal::quad_shuffle_xor", fixedLane: 1),
        ["QuadReadAcrossY"] = new(Form.ValueAndFixedLane, "metal::quad_shuffle_xor", fixedLane: 2),
        ["QuadReadAcrossDiagonal"] = new(Form.ValueAndFixedLane, "metal::quad_shuffle_xor", fixedLane: 3),
        ["QuadReadLaneAt"] = new(Form.ValueAndLane, "metal::quad_shuffle"),
    };

    /// <summary>The names of every operation, for the tests that check the table against <c>Gpu</c>.</summary>
    public static IEnumerable<string> Names => Operations.Keys;

    public static bool TryGet(string name, out Operation operation) => Operations.TryGetValue(name, out operation);
}

internal sealed partial class MslTranslator
{
    /// <summary>
    /// The SIMD-group call sites translated so far, by method name and the call site the C# compiler
    /// passes to the CPU implementation (the file and line of the call): two that share all three
    /// cannot be told apart when the shader runs on the CPU.
    /// </summary>
    private readonly Dictionary<(string Method, string File, int Line), Location> _simdGroupCallSites = new();

    /// <summary>
    /// Translates a call of one of <c>Gpu</c>'s SIMD-group operations, or returns null when
    /// <paramref name="definition"/> is not one.
    /// </summary>
    private Expr? SimdGroupCall(IInvocationOperation operation, IMethodSymbol definition)
    {
        if (!definition.IsStatic ||
            !TypeResolver.IsSharpComputeType(definition.ContainingType, "Gpu") ||
            !SimdGroupInfo.TryGet(definition.Name, out SimdGroupInfo.Operation simd))
        {
            return null;
        }

        if (_function.IsGlobalInitializer)
        {
            return NotConstant(operation, $"'{MemberName(definition)}' is a SIMD-group operation, which only has a value while the shader runs");
        }

        Location location = operation.Syntax.GetLocation();

        if (ResolveType(definition.ReturnType, location) is not { } returnType)
        {
            return Expr.Error;
        }

        // The parameters in declaration order, without the call site (file and line) that only the CPU reads.
        var arguments = new List<IArgumentOperation>();
        IArgumentOperation? callerFile = null, callerLine = null;

        foreach (IArgumentOperation argument in operation.Arguments.OrderBy(static a => a.Parameter?.Ordinal ?? int.MaxValue))
        {
            if (argument.Parameter is { } parameter && CallSiteAttribute(parameter) is { } attribute)
            {
                if (attribute == "CallerFilePathAttribute")
                {
                    callerFile = argument;
                }
                else
                {
                    callerLine = argument;
                }

                continue;
            }

            arguments.Add(argument);
        }

        CheckCallSite(operation, definition, callerFile, callerLine);

        UsesSimdGroups = true;

        switch (simd.Form)
        {
            case SimdGroupInfo.Form.Builtin when arguments.Count == 0:
                UseBuiltin(simd.Builtin);
                return Call(returnType.MslName, BuiltinInfo.Name(simd.Builtin));

            case SimdGroupInfo.Form.NoArguments when arguments.Count == 0:
                return Call(simd.Function);

            case SimdGroupInfo.Form.Vote when arguments.Count == 1:
                return Call(simd.Function, Expression(arguments[0].Value).Text);

            case SimdGroupInfo.Form.Ballot when arguments.Count == 1:
                // A vote converts only explicitly, and only to its own mask type.
                return Call(returnType.MslName, Call(simd.VoteType, Call(simd.Function, Expression(arguments[0].Value).Text).Text).Text);

            case SimdGroupInfo.Form.Count when arguments.Count == 1:
                return Call(simd.Function, Call(returnType.MslName, Expression(arguments[0].Value).Text).Text);

            case SimdGroupInfo.Form.Value when arguments.Count == 1:
                return Call(simd.Function, SimdGroupValue(arguments[0], returnType));

            case SimdGroupInfo.Form.ValueAndLane when arguments.Count == 2:
                return Call(simd.Function, SimdGroupValue(arguments[0], returnType), Call("ushort", Expression(arguments[1].Value).Text).Text);

            case SimdGroupInfo.Form.ValueAndFixedLane when arguments.Count == 1:
                return Call(simd.Function, SimdGroupValue(arguments[0], returnType), Call("ushort", simd.FixedLane.ToString(CultureInfo.InvariantCulture)).Text);

            case SimdGroupInfo.Form.ValueFillAndLane when arguments.Count == 3:
                return Call(
                    simd.Function,
                    SimdGroupValue(arguments[0], returnType),
                    SimdGroupValue(arguments[1], returnType),
                    Call("ushort", Expression(arguments[2].Value).Text).Text);

            case SimdGroupInfo.Form.Equal when arguments.Count == 1 && ResolveType(arguments[0].Value.Type, location) is { } valueType:
                // A helper of MslPrelude.Source, overloaded per value type; the prelude is emitted when used.
                UsesPrelude = true;
                return Call(simd.Function, SimdGroupValue(arguments[0], valueType));

            default:
                // A Gpu from another version of SharpCompute, whose signature this generator does not know.
                return UnsupportedCall(operation, "this generator does not know this overload of the SIMD-group operation; use matching versions of SharpCompute and its source generator");
        }
    }

    /// <summary>
    /// The value a lane brings to an operation. Metal's functions are templates over the unpacked
    /// vector types, and a template argument does not convert: an element of a buffer, a field of a
    /// struct or a captured constant, which are packed, has to be unpacked explicitly. Locals,
    /// parameters, constructors and calls are unpacked already; anything else gets the conversion,
    /// which costs nothing when it was not needed.
    /// </summary>
    private string SimdGroupValue(IArgumentOperation argument, GpuType type)
    {
        string value = Expression(argument.Value).Text;

        if (type.Kind != GpuTypeKind.Vector)
        {
            return value;
        }

        return argument.Value is ILocalReferenceOperation or IParameterReferenceOperation or IObjectCreationOperation or IInvocationOperation
            ? value
            : Call(type.MslName, value).Text;
    }

    /// <summary>
    /// The name of the caller-info attribute that makes <paramref name="parameter"/> part of the call
    /// site (<c>CallerFilePathAttribute</c> or <c>CallerLineNumberAttribute</c>), or null for an ordinary parameter.
    /// </summary>
    private static string? CallSiteAttribute(IParameterSymbol parameter)
    {
        foreach (AttributeData attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass is
                {
                    Name: "CallerFilePathAttribute" or "CallerLineNumberAttribute",
                    ContainingNamespace: { Name: "CompilerServices", ContainingNamespace: { Name: "Runtime", ContainingNamespace.Name: "System" } },
                } type)
            {
                return type.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Reports SCG0035 when another call of the same operation has the same call site. The file and
    /// line are read from the arguments the C# compiler filled in, so this is exactly what the CPU would see.
    /// </summary>
    private void CheckCallSite(IInvocationOperation operation, IMethodSymbol definition, IArgumentOperation? callerFile, IArgumentOperation? callerLine)
    {
        if (callerLine?.Value.ConstantValue is not { HasValue: true, Value: int line })
        {
            return;
        }

        string file = callerFile?.Value.ConstantValue is { HasValue: true, Value: string path } ? path : string.Empty;
        Location location = operation.Syntax.GetLocation();

        if (!_simdGroupCallSites.TryGetValue((definition.Name, file, line), out Location? first))
        {
            _simdGroupCallSites.Add((definition.Name, file, line), location);
        }
        else if (first != location)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.SimdGroupCallSitesShareLine,
                location,
                definition.ContainingType.Name + "." + definition.Name,
                line.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Translates <c>SimdGroupIds.Index</c> and <c>SimdGroupIds.Count</c>, or returns null for any other property.</summary>
    private Expr? SimdGroupProperty(IPropertyReferenceOperation operation)
    {
        IPropertySymbol property = operation.Property;

        if (!property.IsStatic || !TypeResolver.IsSharpComputeType(property.ContainingType, "SimdGroupIds"))
        {
            return null;
        }

        Builtins builtin = property.Name switch
        {
            "Index" => Builtins.SimdgroupIndexInThreadgroup,
            "Count" => Builtins.SimdgroupsPerThreadgroup,
            _ => Builtins.None,
        };

        if (builtin == Builtins.None)
        {
            return UnsupportedMember(operation, property, "only 'Index' and 'Count' of SimdGroupIds can be used in a shader");
        }

        if (_function.IsGlobalInitializer)
        {
            return NotConstant(operation, $"'{MemberName(property)}' is only known while the shader runs");
        }

        if (ResolveType(property.Type, operation.Syntax.GetLocation()) is not { IsNumeric: true } type)
        {
            return Expr.Error;
        }

        UseBuiltin(builtin);
        UsesSimdGroups = true;
        return Call(type.MslName, BuiltinInfo.Name(builtin));
    }
}
