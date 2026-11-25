using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// C# evaluates operands strictly left to right, so <c>a[i++] + a[i++]</c> and <c>F(i++, i)</c> are
/// well defined. MSL 3.2 is C++14, where the same expressions modify and read <c>i</c> without a
/// defined order (undefined behavior, clang's <c>-Wunsequenced</c>). Rather than emit MSL that may
/// compute something else, such expressions are rejected with SCG0021.
/// </summary>
/// <remarks>
/// <para>
/// Besides locals, parameters and <c>this</c>, the check tracks <b>resources</b>: each captured buffer
/// (or buffer view) and texture, each <c>[GroupShared]</c> array, and each buffer or texture parameter
/// of a helper is one variable. Reading an element reads the resource and writing one (an assignment,
/// <c>++</c>, a <c>ref</c> argument such as the location of a <c>Gpu.Atomic*</c> call, a method that
/// modifies a struct element) writes it, whatever the indices: any two elements of one resource are
/// treated as possibly the same element. Two different resources are treated as distinct, although a
/// buffer bound under two names can be the same memory; that is for the bind-time aliasing check to
/// catch, not for this one.
/// </para>
/// <para>
/// A helper's effects on resources are its own accesses plus those of the helpers it calls (static
/// helpers, struct methods, properties, operators and constructors, instance helpers of the shader),
/// worked out from their source once per method; the resources it receives as parameters are mapped
/// to the caller's arguments. Inside a call they are <i>call effects</i>, which differ from direct
/// ones in one way: a function body is never interleaved with the expression around it (its side
/// effects are indeterminately sequenced, not unsequenced), and Metal's compiler evaluates the
/// operands of an operator and the arguments of a call left to right, as C# does. So
/// <c>data[i] + Bump(i)</c> and <c>F(data[i], Bump(i))</c> give the C# result on the GPU and are not
/// reported. An assignment is where the orders differ: MSL evaluates the value, calls included, before
/// the target (its indices, and for <c>+=</c> and the like the read of the target), while C# goes left
/// to right. A resource that one side writes and the other reads or writes is therefore reported,
/// direct or not: <c>data[0] += Bump(0)</c>, <c>data[data[8]] = Bump(8)</c>, <c>data[k] += data[k] = 5</c>.
/// The store itself comes last in both languages, so <c>data[0] = Bump(0)</c> is fine.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    private readonly Dictionary<IMethodSymbol, ResourceUse> _resourceUse = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> _resourceUseInProgress = new(SymbolEqualityComparer.Default);

    /// <summary>For a conflict on a resource found at an assignment: how to split it, or null.</summary>
    private string? _conflictExample;

    /// <summary>Translates a full expression (a statement, initializer, or condition) after checking its evaluation order.</summary>
    private string FullExpression(IOperation operation)
    {
        ISymbol? conflict = null;
        _conflictExample = null;
        Analyze(operation, ref conflict);

        if (conflict is not null && !ContainsInvalid(operation))
        {
            // In a member of a user struct, the struct itself is tracked under its type (see ThisContext).
            string name = conflict is INamedTypeSymbol ? "this" : conflict.Name;
            string advice = IsResource(conflict)
                ? $"may evaluate it in another order: '{name}' is GPU memory, and any two of its elements are treated as the same one. Do the write in a statement of its own and keep its result in a local{(_conflictExample is null ? string.Empty : ", as in '" + _conflictExample + "'")}."
                : "leaves the order undefined. Split the expression into separate statements.";

            _diagnostics.Report(DiagnosticDescriptors.UnsequencedModification, operation.Syntax.GetLocation(), name, advice);
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

    /// <summary>
    /// The variables (locals, parameters, <c>this</c>, resources) an expression reads and writes while it
    /// is evaluated. <see cref="Reads"/> and <see cref="Writes"/> are its own accesses;
    /// <see cref="CallReads"/> and <see cref="CallWrites"/> are the accesses to resources inside the
    /// functions it calls, which only conflict across an assignment (see the remarks of the class).
    /// Never modify <see cref="None"/>.
    /// </summary>
    private sealed class Effects
    {
        public static readonly Effects None = new();

        public HashSet<ISymbol> Reads { get; } = new(SymbolEqualityComparer.Default);

        public HashSet<ISymbol> Writes { get; } = new(SymbolEqualityComparer.Default);

        public HashSet<ISymbol> CallReads { get; } = new(SymbolEqualityComparer.Default);

        public HashSet<ISymbol> CallWrites { get; } = new(SymbolEqualityComparer.Default);

        public bool IsEmpty => Reads.Count == 0 && Writes.Count == 0 && CallReads.Count == 0 && CallWrites.Count == 0;

        /// <summary>True when the expression reads or writes <paramref name="symbol"/> in any way.</summary>
        public bool Uses(ISymbol symbol) =>
            Reads.Contains(symbol) || Writes.Contains(symbol) || CallReads.Contains(symbol) || CallWrites.Contains(symbol);

        /// <summary>The first variable this expression writes, directly or in a call, that <paramref name="other"/> uses.</summary>
        public ISymbol? FirstWriteUsedBy(Effects other) =>
            Writes.FirstOrDefault(other.Uses) ?? CallWrites.FirstOrDefault(other.Uses);

        public void Add(Effects other)
        {
            Reads.UnionWith(other.Reads);
            Writes.UnionWith(other.Writes);
            CallReads.UnionWith(other.CallReads);
            CallWrites.UnionWith(other.CallWrites);
        }
    }

    /// <summary>The resources a helper reads and writes, its parameters included, while it runs.</summary>
    private sealed class ResourceUse
    {
        public static readonly ResourceUse None = new();

        public HashSet<ISymbol> Reads { get; } = new(SymbolEqualityComparer.Default);

        public HashSet<ISymbol> Writes { get; } = new(SymbolEqualityComparer.Default);
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
        Effects effects = AnalyzeOperation(operation, ref conflict);

        // A call of a function with a body in source adds what that body does to resources. Its own
        // accesses happen after its arguments are evaluated, so they are sequenced after them.
        return operation is not null && CallEffects(operation) is { } call ? Sequenced(effects, call) : effects;
    }

    private Effects AnalyzeOperation(IOperation? operation, ref ISymbol? conflict)
    {
        switch (operation)
        {
            case null:
                return Effects.None;

            // A buffer, texture or [GroupShared] array named on its own (`data.Length`, an argument)
            // is no access to its memory: its elements are, below.
            case not null when ResourceKey(operation) is not null:
                return Effects.None;

            case IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } instance } element when ResourceKey(instance) is { } resource:
                Effects elementRead = Read(resource);

                foreach (IArgumentOperation argument in element.Arguments)
                {
                    elementRead = Unsequenced(elementRead, Analyze(argument, ref conflict), ref conflict);
                }

                return elementRead;

            case IArrayElementReferenceOperation element when ResourceKey(element.ArrayReference) is { } resource:
                Effects sharedRead = Read(resource);

                foreach (IOperation index in element.Indices)
                {
                    sharedRead = Unsequenced(sharedRead, Analyze(index, ref conflict), ref conflict);
                }

                return sharedRead;

            case ILocalReferenceOperation local:
                return Read(local.Local);

            case IParameterReferenceOperation parameter when _function.Names.ContainsKey(parameter.Parameter):
                return Read(parameter.Parameter);

            // In a member of a user struct, `this` is a variable like any other: `X++ + X`.
            case IInstanceReferenceOperation when _function.This is { } self:
                return Read(self.Symbol);

            case IParameterReferenceOperation parameter when _function.This is { } self && IsKeptParameter(parameter.Parameter, self.Method.ContainingType):
                return Read(self.Symbol);

            // A getter that modifies its struct is a call that writes the struct.
            case IPropertyReferenceOperation { Instance: { } receiver } property when MutatesReceiver(property.Property.GetMethod, receiver):
                return MutatingCall(receiver, property.Arguments, ref conflict);

            case ISimpleAssignmentOperation assignment:
                return Store(assignment, assignment.Target, assignment.Value, readsTarget: false, ref conflict);

            case ICompoundAssignmentOperation compound:
                return Store(compound, compound.Target, compound.Value, readsTarget: true, ref conflict);

            case IIncrementOrDecrementOperation increment:
                return Store(increment, increment.Target, value: null, readsTarget: true, ref conflict);

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
        if (invocation.Instance is { } receiver && MutatesReceiver(invocation.TargetMethod, receiver))
        {
            return MutatingCall(receiver, invocation.Arguments, ref conflict);
        }

        var callee = new Effects();

        if (invocation.Instance is { } instance && ResourceKey(instance) is { } texture)
        {
            // `texture.Sample(sampler, uv)` reads the texture.
            callee.Reads.Add(texture);
        }

        return Call(Analyze(invocation.Instance, ref conflict), callee, invocation.Arguments, ref conflict);
    }

    /// <summary>
    /// A call of a method that modifies the struct it is called on: the struct is read and written by
    /// the callee, like a variable passed by <c>ref</c> (<c>v.Normalize() + v.X</c> is a conflict).
    /// </summary>
    private Effects MutatingCall(IOperation receiver, System.Collections.Immutable.ImmutableArray<IArgumentOperation> arguments, ref ISymbol? conflict)
    {
        Effects target = TargetEffects(receiver, out ISymbol? stored, ref conflict);
        var callee = new Effects();

        if (stored is not null)
        {
            // `particles[i].Step(dt)` writes the buffer from inside the method.
            bool inCall = IsResource(stored);
            (inCall ? callee.CallWrites : callee.Writes).Add(stored);
            (inCall ? callee.CallReads : callee.Reads).Add(stored);
        }

        return Call(target, callee, arguments, ref conflict);
    }

    private Effects Call(Effects arguments, Effects callee, System.Collections.Immutable.ImmutableArray<IArgumentOperation> invocationArguments, ref ISymbol? conflict)
    {

        foreach (IArgumentOperation argument in invocationArguments)
        {
            if (LocalArrayPassedToHelper(argument, out bool writable) is { } array)
            {
                // The helper receives a pointer to the caller's elements: like a `ref` argument, it may
                // read and (unless the parameter is a ReadOnlySpan) write them after the arguments.
                arguments = Unsequenced(arguments, Analyze(argument, ref conflict), ref conflict);
                callee.Reads.Add(array);

                if (writable)
                {
                    callee.Writes.Add(array);
                }

                continue;
            }

            if (argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out } parameter)
            {
                arguments = Unsequenced(arguments, TargetEffects(argument.Value, out ISymbol? stored, ref conflict), ref conflict);

                if (stored is not null)
                {
                    // An element of a resource passed by reference (the location of a Gpu.Atomic* call) is
                    // written by the callee, like a local; but from inside a function, a call effect.
                    bool inCall = IsResource(stored);
                    (inCall ? callee.CallWrites : callee.Writes).Add(stored);

                    if (parameter.RefKind == RefKind.Ref)
                    {
                        (inCall ? callee.CallReads : callee.Reads).Add(stored);
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
    /// <remarks>
    /// For a resource, the order is known: MSL evaluates the value first, then the target (and reads it,
    /// for a compound assignment or <c>++</c>), where C# evaluates the target first. A resource that one
    /// side writes, directly or in a call, and the other uses is a conflict. Writes in the value do not
    /// conflict with the store to a resource, which comes last in both languages (as the GPU measures
    /// show: <c>data[k] += data[k] = 5</c> stores 5 + 5).
    /// </remarks>
    private Effects Store(IOperation site, IOperation target, IOperation? value, bool readsTarget, ref ISymbol? conflict)
    {
        Effects targetEffects = TargetEffects(target, out ISymbol? stored, ref conflict);
        Effects valueEffects = Analyze(value, ref conflict);
        Effects operands = Unsequenced(targetEffects, valueEffects, ref conflict);
        bool isResource = stored is not null && IsResource(stored);

        if (conflict is null)
        {
            Effects targetSide = isResource && readsTarget ? Sequenced(targetEffects, Read(stored!)) : targetEffects;

            if (valueEffects.FirstWriteUsedBy(targetSide) is { } written)
            {
                conflict = written;

                // `data[0] += Bump(0)` -> `var t = Bump(0); data[0] += t;`: the write happens first, as it does on the GPU.
                _conflictExample = site.Syntax is AssignmentExpressionSyntax assignment
                    ? $"var t = {assignment.Right}; {assignment.Left} {assignment.OperatorToken.Text} t;"
                    : null;
            }
            else
            {
                conflict = targetSide.FirstWriteUsedBy(valueEffects);
            }
        }

        if (stored is null)
        {
            return operands;
        }

        if (!isResource && operands.Writes.Contains(stored))
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
            case not null when ResourceKey(target) is { } resource:
                // Writing an element of a buffer, texture or [GroupShared] array writes the resource.
                stored = resource;
                return Effects.None;

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

            case IInstanceReferenceOperation when _function.This is { } self:
                stored = self.Symbol;
                return Effects.None;

            case IParameterReferenceOperation parameter when _function.This is { } self && IsKeptParameter(parameter.Parameter, self.Method.ContainingType):
                stored = self.Symbol;
                return Effects.None;

            case IFieldReferenceOperation { Instance: { } instance }:
                return TargetEffects(instance, out stored, ref conflict);

            case IArrayElementReferenceOperation element:
                // An element of a local array is part of the array variable, like a field of a struct.
                Effects elementEffects = TargetEffects(element.ArrayReference, out stored, ref conflict);

                foreach (IOperation index in element.Indices)
                {
                    elementEffects = Unsequenced(elementEffects, Analyze(index, ref conflict), ref conflict);
                }

                return elementEffects;

            case IInlineArrayAccessOperation inlineElement:
                return Unsequenced(TargetEffects(inlineElement.Instance, out stored, ref conflict), Analyze(inlineElement.Argument, ref conflict), ref conflict);

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

    // ---------------------------------------------------------------------------------------------
    // Resources: buffers, textures and [GroupShared] arrays
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The resource <paramref name="operation"/> names, or null: a captured buffer or texture (its
    /// field or primary-constructor parameter), a <c>[GroupShared]</c> array, or a buffer or texture
    /// parameter or local of a helper. Captured state of any other type is not a resource.
    /// </summary>
    private ISymbol? ResourceKey(IOperation? operation)
    {
        switch (WithoutImplicitConversions(operation))
        {
            case IFieldReferenceOperation { Field: { } field } when IsGroupShared(field):
                return field.OriginalDefinition;

            case IFieldReferenceOperation { Field: { } field } when _captures.TryGet(field, out Capture capture):
                return capture.Type.IsResource ? capture.Symbol : null;

            case IParameterReferenceOperation { Parameter: { } parameter } when _captures.TryGet(parameter, out Capture capture):
                return capture.Type.IsResource ? capture.Symbol : null;

            case IParameterReferenceOperation { Parameter: { } parameter } when IsResourceType(parameter.Type):
                return parameter.OriginalDefinition;

            case ILocalReferenceOperation { Local: { } local } when IsResourceType(local.Type):
                return local;

            default:
                return null;
        }
    }

    /// <summary>True when <paramref name="symbol"/> is one that <see cref="ResourceKey"/> returns.</summary>
    private bool IsResource(ISymbol symbol) => symbol switch
    {
        IFieldSymbol field => IsGroupShared(field) || (_captures.TryGet(field, out Capture capture) && capture.Type.IsResource),
        IParameterSymbol parameter => _captures.TryGet(parameter, out Capture capture) ? capture.Type.IsResource : IsResourceType(parameter.Type),
        ILocalSymbol local => IsResourceType(local.Type),
        _ => false,
    };

    private static bool IsResourceType(ITypeSymbol type) => TypeResolver.IsBufferType(type, out _) || TypeResolver.IsTextureType(type, out _);

    /// <summary>The resource whose element <paramref name="target"/> is (<c>data[i].Field</c>, <c>tile[i]</c>), or null.</summary>
    private ISymbol? ResourceRoot(IOperation? target)
    {
        for (IOperation? current = target; current is not null;)
        {
            if (ResourceKey(current) is { } resource)
            {
                return resource;
            }

            current = current switch
            {
                IFieldReferenceOperation field => field.Instance,
                IPropertyReferenceOperation property => property.Instance,
                IArrayElementReferenceOperation element => element.ArrayReference,
                IInlineArrayAccessOperation element => element.Instance,
                IConversionOperation { IsImplicit: true } conversion => conversion.Operand,
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// The function <paramref name="operation"/> calls, and the operand passed for each of its
    /// parameters by ordinal: a method, local function, property getter or indexer, constructor, or a
    /// user-defined operator or conversion. Null for anything else.
    /// </summary>
    private static IMethodSymbol? Callee(IOperation operation, List<(int Ordinal, IOperation Value)> arguments)
    {
        switch (operation)
        {
            case IInvocationOperation invocation:
                AddArguments(invocation.Arguments, arguments);
                return invocation.TargetMethod;

            case IPropertyReferenceOperation property:
                AddArguments(property.Arguments, arguments);
                return property.Property.GetMethod;

            case IObjectCreationOperation creation:
                AddArguments(creation.Arguments, arguments);
                return creation.Constructor;

            case IBinaryOperation { OperatorMethod: { } method } binary:
                arguments.Add((0, binary.LeftOperand));
                arguments.Add((1, binary.RightOperand));
                return method;

            case ICompoundAssignmentOperation { OperatorMethod: { } method } compound:
                arguments.Add((0, compound.Target));
                arguments.Add((1, compound.Value));
                return method;

            case IUnaryOperation { OperatorMethod: { } method } unary:
                arguments.Add((0, unary.Operand));
                return method;

            case IIncrementOrDecrementOperation { OperatorMethod: { } method } increment:
                arguments.Add((0, increment.Target));
                return method;

            case IConversionOperation { OperatorMethod: { } method } conversion:
                arguments.Add((0, conversion.Operand));
                return method;

            default:
                return null;
        }
    }

    private static void AddArguments(System.Collections.Immutable.ImmutableArray<IArgumentOperation> source, List<(int Ordinal, IOperation Value)> arguments)
    {
        foreach (IArgumentOperation argument in source)
        {
            if (argument.Parameter is { } parameter)
            {
                arguments.Add((parameter.Ordinal, argument.Value));
            }
        }
    }

    /// <summary>
    /// What the function <paramref name="operation"/> calls does to resources, with its resource
    /// parameters replaced by the caller's arguments; null when it does nothing to them.
    /// </summary>
    private Effects? CallEffects(IOperation operation)
    {
        var arguments = new List<(int Ordinal, IOperation Value)>();

        if (Callee(operation, arguments) is not { } method || !HasSource(method))
        {
            return null;
        }

        IMethodSymbol definition = (method.ReducedFrom ?? method).OriginalDefinition;
        ResourceUse use = ResourceUseOf(definition);

        if (use.Reads.Count == 0 && use.Writes.Count == 0)
        {
            return null;
        }

        var effects = new Effects();
        Map(use.Reads, effects.CallReads);
        Map(use.Writes, effects.CallWrites);
        return effects.IsEmpty ? null : effects;

        void Map(HashSet<ISymbol> resources, HashSet<ISymbol> into)
        {
            foreach (ISymbol resource in resources)
            {
                if (MapToCaller(resource, definition, arguments) is { } mapped)
                {
                    into.Add(mapped);
                }
            }
        }
    }

    /// <summary>A resource of <paramref name="callee"/> as the caller sees it: a parameter becomes the resource passed for it.</summary>
    private ISymbol? MapToCaller(ISymbol resource, IMethodSymbol callee, List<(int Ordinal, IOperation Value)> arguments)
    {
        if (resource is not IParameterSymbol parameter || !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, callee))
        {
            return resource;
        }

        foreach ((int ordinal, IOperation value) in arguments)
        {
            if (ordinal == parameter.Ordinal)
            {
                return ResourceKey(value);
            }
        }

        return null;
    }

    /// <summary>
    /// The resources <paramref name="method"/> (a definition with a body in source) reads and writes,
    /// including through the functions it calls. Worked out from the source once per method, before
    /// or after it is translated, like <see cref="WritesThis(IMethodSymbol)"/>.
    /// </summary>
    private ResourceUse ResourceUseOf(IMethodSymbol method)
    {
        if (_resourceUse.TryGetValue(method, out ResourceUse? known))
        {
            return known;
        }

        if (!_resourceUseInProgress.Add(method))
        {
            // Recursive; reported as SCG0014 when it is translated.
            return ResourceUse.None;
        }

        var use = new ResourceUse();

        try
        {
            if (GetBody(method) is { } body)
            {
                CollectResourceUse(body, use);
            }
        }
        finally
        {
            _resourceUseInProgress.Remove(method);
        }

        _resourceUse[method] = use;
        return use;
    }

    private void CollectResourceUse(IOperation operation, ResourceUse use)
    {
        switch (operation)
        {
            case IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } instance } when ResourceKey(instance) is { } resource:
                use.Reads.Add(resource);
                break;

            case IArrayElementReferenceOperation element when ResourceKey(element.ArrayReference) is { } resource:
                use.Reads.Add(resource);
                break;

            case IInvocationOperation { Instance: { } instance } when ResourceKey(instance) is { } texture:
                use.Reads.Add(texture);
                break;
        }

        IOperation? written = operation switch
        {
            IAssignmentOperation assignment => assignment.Target,
            IIncrementOrDecrementOperation increment => increment.Target,
            IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => argument.Value,
            IInvocationOperation { Instance: { } instance } invocation when WritesThis(invocation.TargetMethod) => instance,
            IPropertyReferenceOperation { Instance: { } instance, Property.GetMethod: { } getter } when WritesThis(getter) => instance,
            _ => null,
        };

        if (ResourceRoot(written) is { } target)
        {
            use.Writes.Add(target);
        }

        if (CallEffects(operation) is { } call)
        {
            use.Reads.UnionWith(call.CallReads);
            use.Writes.UnionWith(call.CallWrites);
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            CollectResourceUse(child, use);
        }
    }
}
