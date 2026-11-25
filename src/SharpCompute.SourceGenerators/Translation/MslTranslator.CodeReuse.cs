using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// How the struct a method is called on reaches the MSL function that the method becomes.
/// </summary>
internal enum ReceiverMode
{
    /// <summary>No receiver: a static method, a local function, or a method of the shader struct (whose state arrives as hidden inputs).</summary>
    None,

    /// <summary>
    /// <c>T __this</c>, a copy. Used by every method that does not modify its struct, and by one that
    /// does when C# itself would run it on a copy (a temporary, a readonly field, an <c>in</c> parameter).
    /// </summary>
    Value,

    /// <summary><c>thread T&amp; __this</c>: a local, a parameter, or a field of one.</summary>
    Thread,

    /// <summary><c>device T&amp; __this</c>: an element of a <c>ReadWriteBuffer&lt;T&gt;</c>, or a field of one.</summary>
    Device,

    /// <summary><c>threadgroup T&amp; __this</c>: an element of a <c>[GroupShared]</c> array, or a field of one.</summary>
    Threadgroup,
}

/// <summary>
/// Identifies one MSL function. A C# method is usually one function, but a generic method is one per
/// set of type arguments, and a method that modifies its struct is one per address space it is called
/// in: an MSL reference names its address space, so <c>thread Particle&amp;</c> cannot bind to an element
/// of a buffer.
/// </summary>
internal readonly struct HelperKey : IEquatable<HelperKey>
{
    public HelperKey(IMethodSymbol method, ReceiverMode mode, IMethodSymbol? instantiation)
    {
        Method = method;
        Mode = mode;
        Instantiation = instantiation;
    }

    /// <summary>The method: its definition, or the constructed method for a generic one.</summary>
    public IMethodSymbol Method { get; }

    public ReceiverMode Mode { get; }

    /// <summary>
    /// For a local function inside a generic method: the constructed method it was reached from, whose
    /// type arguments its body may name. Null everywhere else.
    /// </summary>
    public IMethodSymbol? Instantiation { get; }

    public bool Equals(HelperKey other) =>
        Mode == other.Mode &&
        SymbolEqualityComparer.Default.Equals(Method, other.Method) &&
        SymbolEqualityComparer.Default.Equals(Instantiation, other.Instantiation);

    public override bool Equals(object? obj) => obj is HelperKey other && Equals(other);

    public override int GetHashCode() =>
        (SymbolEqualityComparer.Default.GetHashCode(Method) * 397) ^ (int)Mode ^ (Instantiation is null ? 0 : SymbolEqualityComparer.Default.GetHashCode(Instantiation));
}

/// <summary>
/// Code a shader shares with other shaders: static methods of any type in the compilation, generic
/// methods (one MSL function per set of type arguments), and the members of user structs, which are
/// methods, computed properties, operators, conversions and constructors. All of them are translated
/// from their C# source like the shader's own helper methods and emitted once per shader that uses them.
/// </summary>
/// <remarks>
/// <para>
/// A member of a user struct becomes a free function whose first parameter is the struct. A member
/// that does not modify the struct takes a copy, which works wherever the struct lives. One that does
/// takes a reference, and is emitted once for each address space it is called in (see
/// <see cref="ReceiverMode"/>); when C# would run it on a copy (a temporary, a readonly field) it takes
/// a copy here too, so the caller's value stays as it was, exactly as in C#.
/// </para>
/// <para>
/// A constructor becomes a function that returns the struct: it starts from the zeroed value, runs
/// the field initializers (or the constructor it chains to), then its body.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    private readonly Dictionary<IMethodSymbol, bool> _writesThis = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> _writesThisInProgress = new(SymbolEqualityComparer.Default);

    /// <summary>The struct a member function was called on, while that function is translated.</summary>
    private sealed class ThisContext
    {
        public ThisContext(IMethodSymbol method, GpuType type, ReceiverMode mode)
        {
            Method = method;
            Type = type;
            Mode = mode;
        }

        public IMethodSymbol Method { get; }

        public GpuType Type { get; }

        /// <summary>
        /// Where <c>__this</c> lives. A constructor's is a local (<see cref="ReceiverMode.Thread"/>);
        /// a copy (<see cref="ReceiverMode.Value"/>) is a local of the function as well.
        /// </summary>
        public ReceiverMode Mode { get; }

        /// <summary>
        /// The symbol that stands for <c>this</c> when the order of evaluation is checked (SCG0021): the
        /// struct's type, which no local or parameter can be.
        /// </summary>
        public ISymbol Symbol => Method.ContainingType;
    }

    private static bool HasSource(ISymbol symbol) => symbol.OriginalDefinition.DeclaringSyntaxReferences.Length > 0;

    /// <summary>The name of a method in messages: its plain name, or what C# calls an accessor, operator or constructor.</summary>
    private static string DisplayName(IMethodSymbol method) =>
        method.MethodKind is MethodKind.Ordinary or MethodKind.LocalFunction ? method.Name : MemberName(method);

    // ---------------------------------------------------------------------------------------------
    // What can be called
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Why a method that is neither an intrinsic nor a method of the shader cannot become an MSL
    /// function, or null when it can: its source must be part of this compilation, and an instance
    /// method needs a receiver that exists on the GPU, which only a user struct is.
    /// </summary>
    private string? UserMethodProblem(IMethodSymbol method)
    {
        IMethodSymbol definition = method.OriginalDefinition;
        INamedTypeSymbol? type = definition.ContainingType;

        if (type is null || !HasSource(definition))
        {
            string assembly = definition.ContainingAssembly?.Name ?? "another assembly";

            return "it is not an [MslIntrinsic] method, a supported System.Math/MathF method, or a method whose source is part of this compilation " +
                $"(it is declared in the referenced assembly '{assembly}', so there is no C# source to translate; " +
                "share the source file between the projects instead)";
        }

        if (type.IsGenericType || type.ContainingType is { IsGenericType: true })
        {
            return $"it is declared in the generic type '{TypeResolver.Display(type)}'; only generic methods are specialized, so move it to a type that is not generic";
        }

        if (definition.IsStatic)
        {
            return null;
        }

        if (type.TypeKind != TypeKind.Struct)
        {
            return $"it is an instance method of '{TypeResolver.Display(type)}', which is not a struct; only static methods and the methods of structs can be called from a shader";
        }

        GpuType? resolved = _types.Resolve(type, out TypeFailure? failure);

        if (resolved is { Kind: GpuTypeKind.Struct })
        {
            return null;
        }

        return resolved is null
            ? $"the struct '{TypeResolver.Display(type)}' it is called on cannot be used in a shader ({failure?.Reason ?? "it has no MSL equivalent"})"
            : $"it is an instance method of the [MslType] type '{TypeResolver.Display(type)}', whose methods must be [MslIntrinsic]";
    }

    /// <summary>True for a property whose getter has a body in source: a computed property, which becomes a function.</summary>
    private bool IsComputedProperty(IPropertySymbol property)
    {
        if (property.OriginalDefinition.GetMethod is not { } getter)
        {
            return false;
        }

        foreach (SyntaxReference reference in getter.DeclaringSyntaxReferences)
        {
            switch (reference.GetSyntax(_cancellationToken))
            {
                case ArrowExpressionClauseSyntax:
                case AccessorDeclarationSyntax { Body: not null }:
                case AccessorDeclarationSyntax { ExpressionBody: not null }:
                    return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="operation"/> is what an assignment, compound assignment or increment stores to.</summary>
    private static bool IsWriteTarget(IOperation operation) => operation.Parent switch
    {
        IAssignmentOperation assignment => ReferenceEquals(assignment.Target, operation),
        IIncrementOrDecrementOperation => true,
        _ => false,
    };

    // ---------------------------------------------------------------------------------------------
    // Generic methods
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The method a call names, with the type arguments of the generic method being translated filled
    /// in: inside <c>Pick&lt;float&gt;</c>, a call of <c>Mix&lt;T&gt;</c> is a call of <c>Mix&lt;float&gt;</c>.
    /// </summary>
    private IMethodSymbol Specialize(IMethodSymbol method)
    {
        if (!method.IsGenericMethod)
        {
            return method.OriginalDefinition;
        }

        ITypeSymbol[] arguments = method.TypeArguments.Select(_types.Substitute).ToArray();
        return method.OriginalDefinition.Construct(arguments);
    }

    /// <summary>
    /// Reports SCG0046 when a type argument of a generic method has no MSL equivalent. Reported at the
    /// call, which chose the type: the declaration only says <c>T</c>.
    /// </summary>
    private bool CheckTypeArguments(IOperation callSite, IMethodSymbol method)
    {
        if (!method.IsGenericMethod)
        {
            return true;
        }

        foreach (ITypeSymbol argument in method.TypeArguments)
        {
            ITypeSymbol actual = _types.Substitute(argument);

            if (actual.TypeKind == TypeKind.Error)
            {
                SawInvalidCode = true;
                return false;
            }

            GpuType? type = _types.Resolve(actual, out TypeFailure? failure);

            if (type is null || type.Kind == GpuTypeKind.Void)
            {
                _diagnostics.Report(
                    DiagnosticDescriptors.UnsupportedTypeArgument,
                    callSite.Syntax.GetLocation(),
                    method.OriginalDefinition.Name,
                    TypeResolver.Display(actual),
                    failure?.Reason ?? TypeResolver.UnsupportedReason(actual));
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyDictionary<ITypeParameterSymbol, ITypeSymbol>? TypeArgumentMap(IMethodSymbol method)
    {
        if (!method.IsGenericMethod)
        {
            return null;
        }

        var map = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        ImmutableArray<ITypeParameterSymbol> parameters = method.OriginalDefinition.TypeParameters;

        for (int i = 0; i < parameters.Length && i < method.TypeArguments.Length; i++)
        {
            map[parameters[i]] = method.TypeArguments[i];
        }

        return map;
    }

    // ---------------------------------------------------------------------------------------------
    // Names
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The preferred MSL name of a function. Methods of the shader keep their name; local functions
    /// are prefixed with the methods that contain them (<c>Execute_Mod</c>); methods of other types
    /// with the type (<c>ShaderMath_Hash</c>, <c>Ray_At</c>, <c>Ray_new</c> for a constructor), so two
    /// types can each have a <c>Hash</c>. A generic method adds its type arguments
    /// (<c>ShaderMath_Pick_float3</c>), and a method that modifies its struct the address space it is
    /// called in, unless that is a local's.
    /// </summary>
    private string HelperName(HelperKey key)
    {
        IMethodSymbol method = key.Method.OriginalDefinition;
        string name = BaseName(method);
        ISymbol? container = method.ContainingSymbol;

        for (; container is IMethodSymbol containingMethod; container = container.ContainingSymbol)
        {
            name = BaseName(containingMethod) + "_" + name;
        }

        if (container is INamedTypeSymbol declaringType && !SymbolEqualityComparer.Default.Equals(declaringType, _shaderType))
        {
            for (INamedTypeSymbol? current = declaringType; current is not null; current = current.ContainingType)
            {
                name = current.Name + "_" + name;
            }
        }

        foreach (ITypeSymbol argument in (key.Instantiation ?? key.Method).TypeArguments)
        {
            name += "_" + (_types.Resolve(argument) is { Kind: GpuTypeKind.Bool or GpuTypeKind.Scalar or GpuTypeKind.Vector or GpuTypeKind.Struct } resolved
                ? resolved.MslName
                : argument.Name);
        }

        return name + key.Mode switch
        {
            ReceiverMode.Device => "_device",
            ReceiverMode.Threadgroup => "_threadgroup",
            ReceiverMode.Value when WritesThis(method) => "_copy",
            _ => string.Empty,
        };
    }

    private static string BaseName(IMethodSymbol method) => method.MethodKind switch
    {
        MethodKind.Constructor => "new",
        MethodKind.PropertyGet => method.AssociatedSymbol is IPropertySymbol { IsIndexer: false } property ? property.Name : "Item",
        _ => method.Name,
    };

    private static string ThisParameter(GpuType type, ReceiverMode mode) => mode switch
    {
        ReceiverMode.Thread => "thread " + type.MslName + "& " + MslNames.This,
        ReceiverMode.Device => "device " + type.MslName + "& " + MslNames.This,
        ReceiverMode.Threadgroup => "threadgroup " + type.MslName + "& " + MslNames.This,
        _ => type.MslName + " " + MslNames.This,
    };

    // ---------------------------------------------------------------------------------------------
    // Does a method modify the struct it is called on?
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// True when an instance method of a struct stores to <c>this</c>: assigns a field (or the whole
    /// struct), passes one by reference, or calls another method that does. What the method says about
    /// itself is not enough: C# only makes it say <c>readonly</c> when the struct is, and most structs
    /// that never change are not declared so. A method that is <c>readonly</c> cannot write, by the
    /// rules of C#.
    /// </summary>
    private bool WritesThis(IMethodSymbol method)
    {
        method = method.OriginalDefinition;

        if (method.IsStatic || method.IsReadOnly || method.MethodKind == MethodKind.Constructor || method.ContainingType is not { TypeKind: TypeKind.Struct })
        {
            return false;
        }

        if (_writesThis.TryGetValue(method, out bool known))
        {
            return known;
        }

        if (!_writesThisInProgress.Add(method))
        {
            // Recursive; reported as SCG0014 when it is translated.
            return false;
        }

        bool result;

        try
        {
            result = GetBody(method) is { } body && WritesThis(body, method);
        }
        finally
        {
            _writesThisInProgress.Remove(method);
        }

        _writesThis[method] = result;
        return result;
    }

    private bool WritesThis(IOperation operation, IMethodSymbol method)
    {
        IOperation? target = operation switch
        {
            IAssignmentOperation assignment => assignment.Target,
            IIncrementOrDecrementOperation increment => increment.Target,
            IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => argument.Value,
            IInvocationOperation { Instance: { } instance } invocation when WritesThis(invocation.TargetMethod) => instance,
            IPropertyReferenceOperation { Instance: { } instance, Property.GetMethod: { } getter } when WritesThis(getter) => instance,
            _ => null,
        };

        if (target is not null && IsRootedAtThis(target, method))
        {
            return true;
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            if (WritesThis(child, method))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True for <c>this</c>, a field of it (at any depth), or a primary-constructor parameter the struct keeps.</summary>
    private static bool IsRootedAtThis(IOperation operation, IMethodSymbol method)
    {
        for (IOperation? current = operation; current is not null;)
        {
            switch (current)
            {
                case IInstanceReferenceOperation:
                    return true;
                case IParameterReferenceOperation parameter:
                    return IsKeptParameter(parameter.Parameter, method.ContainingType);
                case IFieldReferenceOperation field:
                    current = field.Instance;
                    break;
                case IPropertyReferenceOperation property:
                    current = property.Instance;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// True for a primary-constructor parameter of <paramref name="type"/>. Read from a method, it is
    /// state the struct keeps: the compiler stores it in a hidden field.
    /// </summary>
    private static bool IsKeptParameter(IParameterSymbol parameter, INamedTypeSymbol? type) =>
        parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor &&
        SymbolEqualityComparer.Default.Equals(constructor.ContainingType, type);

    /// <summary>The hidden field in which a struct keeps a primary-constructor parameter that its methods read.</summary>
    private static GpuField? KeptParameterField(GpuType type, IParameterSymbol parameter)
    {
        foreach (GpuField field in type.Fields)
        {
            if (TypeResolver.KeptParameterName(field.Symbol) == parameter.Name)
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>
    /// True when calling <paramref name="method"/> stores to the struct it is called on. Only a method
    /// of a user struct can: the shader's state is constant, and the methods of <c>[MslType]</c>
    /// vectors are MSL functions, whatever their C# bodies do.
    /// </summary>
    /// <summary>
    /// True when a call of <paramref name="method"/> on <paramref name="receiver"/> stores to the
    /// receiver itself: the method modifies its struct, and the receiver is a variable rather than
    /// something C# (and the MSL) calls it on a copy of.
    /// </summary>
    private bool MutatesReceiver(IMethodSymbol? method, IOperation receiver) =>
        MutatesReceiver(method) && ReceiverSpace(receiver, receiver, report: false) != ReceiverMode.Value;

    private bool MutatesReceiver(IMethodSymbol? method) =>
        method is { IsStatic: false, ContainingType: { TypeKind: TypeKind.Struct } type } &&
        !SymbolEqualityComparer.Default.Equals(type, _shaderType) &&
        TypeResolver.FindAttribute(type, "MslTypeAttribute") is null &&
        WritesThis(method);

    // ---------------------------------------------------------------------------------------------
    // Where the receiver lives
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// How a method that modifies its struct receives <paramref name="receiver"/>: by reference in the
    /// address space of a variable C# would modify in place, or as a copy where C# copies too (a
    /// temporary, a readonly field, an <c>in</c> parameter, <c>this</c> of a readonly method).
    /// Captured shader state that C# would modify in place is SCG0018: on the GPU it is constant.
    /// </summary>
    /// <param name="report">False to only ask: the evaluation-order check asks before the call is translated.</param>
    private ReceiverMode ReceiverSpace(IOperation receiver, IOperation callSite, bool report = true)
    {
        for (IOperation? current = receiver; current is not null;)
        {
            switch (current)
            {
                // Every local, the variable of a foreach loop included: C# forbids assigning that one,
                // but a method called on it modifies it in place, without a copy.
                case ILocalReferenceOperation:
                    return ReceiverMode.Thread;

                // An element of a local array is a variable of the thread; of a ReadOnlySpan<T>, a read-only one.
                case IArrayElementReferenceOperation element when LocalArrayReference(element.ArrayReference) is not null:
                    return ReceiverMode.Thread;

                case IPropertyReferenceOperation { Property.IsIndexer: true } indexer when LocalArrayReference(indexer.Instance) is { } array:
                    return array.IsReadOnly ? ReceiverMode.Value : ReceiverMode.Thread;

                // An element of an inline array lives where the struct that holds it does.
                case IInlineArrayAccessOperation inlineElement:
                    current = inlineElement.Instance;
                    break;

                case IParameterReferenceOperation parameter when _function.Names.ContainsKey(parameter.Parameter):
                    return parameter.Parameter.RefKind is RefKind.None or RefKind.Ref or RefKind.Out ? ReceiverMode.Thread : ReceiverMode.Value;

                case IParameterReferenceOperation parameter when _function.This is { } self && IsKeptParameter(parameter.Parameter, self.Method.ContainingType):
                    return ThisSpace(self);

                case IParameterReferenceOperation parameter when _captures.TryGet(parameter.Parameter, out _):
                    return CapturedSpace(callSite, parameter.Parameter.Name, isReadOnly: false, report);

                case IInstanceReferenceOperation when _function.This is { } self:
                    return ThisSpace(self);

                case IFieldReferenceOperation { Field.IsStatic: false, Instance: IInstanceReferenceOperation } field when _function.This is null:
                    return CapturedSpace(callSite, field.Field.Name, field.Field.IsReadOnly, report);

                case IFieldReferenceOperation { Field.IsStatic: false, Instance: { } instance } field:
                    // A readonly field is a variable only in a constructor of its own struct.
                    if (field.Field.IsReadOnly && !(_function.IsConstructor && instance is IInstanceReferenceOperation))
                    {
                        return ReceiverMode.Value;
                    }

                    current = instance;
                    break;

                case IPropertyReferenceOperation { Property.IsIndexer: true, Instance.Type: { } bufferType } when TypeResolver.IsBufferType(bufferType, out bool isReadOnly):
                    return isReadOnly ? ReceiverMode.Value : ReceiverMode.Device;

                case IArrayElementReferenceOperation element when GroupSharedReference(element.ArrayReference) is not null:
                    return ReceiverMode.Threadgroup;

                default:
                    return ReceiverMode.Value;
            }
        }

        return ReceiverMode.Value;
    }

    private static ReceiverMode ThisSpace(ThisContext self) =>
        self.Method.IsReadOnly ? ReceiverMode.Value : self.Mode == ReceiverMode.Value ? ReceiverMode.Thread : self.Mode;

    private ReceiverMode CapturedSpace(IOperation callSite, string name, bool isReadOnly, bool report)
    {
        if (report && !isReadOnly && !_shaderType.IsReadOnly)
        {
            // C# would modify the shader's own field; the GPU's copy of it is constant.
            _diagnostics.Report(DiagnosticDescriptors.CapturedStateIsReadOnly, callSite.Syntax.GetLocation(), name);
        }

        return ReceiverMode.Value;
    }

    // ---------------------------------------------------------------------------------------------
    // Calls
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Translates the function for <paramref name="method"/> (on first use) and checks what every call
    /// of one must: that it is not in a program-scope initializer and that its type arguments exist in
    /// MSL. Returns null after reporting why.
    /// </summary>
    private HelperFunction? PrepareHelper(IOperation callSite, IMethodSymbol method, ReceiverMode mode)
    {
        if (_function.IsGlobalInitializer)
        {
            NotConstant(callSite, $"it calls '{DisplayName(method)}', and MSL does not allow function calls in program-scope initializers");
            return null;
        }

        return CheckTypeArguments(callSite, method) ? GetHelper(method, callSite, mode) : null;
    }

    /// <summary>The call itself: the receiver, the arguments, then the kernel inputs the function reads.</summary>
    private Expr HelperCall(HelperFunction helper, string? receiver, IEnumerable<string> arguments)
    {
        // Forward the captured state and kernel built-ins the helper (transitively) reads.
        _function.Inputs.Add(helper.Inputs);

        IEnumerable<string> all = receiver is null ? arguments : new[] { receiver }.Concat(arguments);

        // `::Name` selects our global function even if `metal::` declares one with the same name.
        return Call("::" + helper.Name, all.Concat(helper.Inputs.Arguments()).ToArray());
    }

    /// <summary>
    /// A call with C# arguments: of a method (static, or on <paramref name="receiver"/>), a constructor,
    /// or the getter of a property or indexer.
    /// </summary>
    private Expr UserCall(IOperation callSite, IMethodSymbol method, IOperation? receiver, ImmutableArray<IArgumentOperation> arguments)
    {
        ReceiverMode mode = ReceiverMode.None;

        if (receiver is not null)
        {
            mode = WritesThis(method) ? ReceiverSpace(receiver, callSite) : ReceiverMode.Value;
        }

        // Translate the helper first: if its signature is rejected, the arguments converted to
        // its parameter types would only repeat that diagnostic.
        if (PrepareHelper(callSite, method, mode) is not { } helper)
        {
            return Expr.Error;
        }

        foreach (IArgumentOperation argument in arguments)
        {
            if (argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out } && !IsThreadLocal(argument.Value))
            {
                return Unsupported(argument, $"the by-reference argument '{argument.Syntax}' (only locals and parameters, or their fields, can be passed by reference to a helper)");
            }

            if (argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out } && IsPackedField(argument.Value))
            {
                return Unsupported(argument, $"the by-reference argument '{argument.Syntax}' (a matrix in a struct field is stored packed; copy it to a local, pass that, and assign it back)");
            }
        }

        string? receiverText = receiver is null ? null : Expression(receiver).Text;
        string[] argumentTexts = Arguments(arguments, method, out bool argumentsOk);

        return argumentsOk ? HelperCall(helper, receiverText, argumentTexts) : Expr.Error;
    }

    /// <summary>
    /// An operator or conversion a type declares in source, which is a call in MSL:
    /// <c>a + b</c> → <c>::Complex_op_Addition(a, b)</c>. Returns null (after reporting SCG0016) when
    /// its source is not available.
    /// </summary>
    private Expr? OperatorCall(IOperation callSite, IMethodSymbol method, params IOperation[] operands)
    {
        if (!method.IsStatic || UserMethodProblem(method) is not null)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.UnsupportedMethodCall,
                callSite.Syntax.GetLocation(),
                MemberName(method),
                method.MethodKind == MethodKind.Conversion
                    ? "only conversion operators declared by [MslType] vector types, or by a type whose source is part of this compilation, are supported"
                    : "only operators declared by [MslType] vector types, or by a type whose source is part of this compilation, are supported");
            return null;
        }

        if (PrepareHelper(callSite, method, ReceiverMode.None) is not { } helper)
        {
            return null;
        }

        return HelperCall(helper, receiver: null, operands.Select(o => Expression(o).Text).ToArray());
    }

    /// <summary>True for an operator MSL has itself: one a scalar or <c>[MslType]</c> vector type declares.</summary>
    private bool IsNativeOperator(IMethodSymbol method) => _types.Resolve(method.ContainingType) is { IsNumeric: true };

    /// <summary>
    /// A conversion operator of a user type. The value it converts and the value it returns may each
    /// still need a standard conversion, which C# applies without saying so: <c>Money m = 5;</c> with
    /// an operator from <c>float</c>.
    /// </summary>
    private Expr UserConversion(IConversionOperation operation, IMethodSymbol method, GpuType target)
    {
        if (!method.IsStatic || method.Parameters.Length != 1 || UserMethodProblem(method) is not null)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.UnsupportedMethodCall,
                operation.Syntax.GetLocation(),
                MemberName(method),
                "only conversion operators declared by [MslType] vector types, or by a type whose source is part of this compilation, are supported");
            return Expr.Error;
        }

        GpuType? from = _types.Resolve(method.Parameters[0].Type);
        GpuType? to = _types.Resolve(method.ReturnType);
        bool convertsOperand = !SymbolEqualityComparer.Default.Equals(operation.Operand.Type, method.Parameters[0].Type);
        bool convertsResult = !SymbolEqualityComparer.Default.Equals(operation.Type, method.ReturnType);

        if ((convertsOperand && from is not { Kind: GpuTypeKind.Scalar }) || (convertsResult && (to is not { Kind: GpuTypeKind.Scalar } || target.Kind != GpuTypeKind.Scalar)))
        {
            return Unsupported(
                operation,
                $"a conversion from '{(operation.Operand.Type is { } source ? TypeResolver.Display(source) : "?")}' to '{TypeResolver.Display(operation.Type!)}' through '{MemberName(method)}'");
        }

        if (PrepareHelper(operation, method, ReceiverMode.None) is not { } helper)
        {
            return Expr.Error;
        }

        string operand = Expression(operation.Operand).Text;
        Expr call = HelperCall(helper, receiver: null, new[] { convertsOperand ? Call(from!.MslName, operand).Text : operand });

        return convertsResult ? Call(target.MslName, call.Text) : call;
    }

    /// <summary>
    /// <c>a += b</c> with an operator of a user type: <c>a = ::T_op_Addition(a, b)</c>. That names the
    /// target twice, so its expression must not have effects of its own.
    /// </summary>
    private Expr UserCompoundAssignment(ICompoundAssignmentOperation operation, IMethodSymbol method)
    {
        if (!operation.GetInConversion().IsIdentity || !operation.GetOutConversion().IsIdentity)
        {
            return Unsupported(operation, $"a compound assignment through '{MemberName(method)}' that converts its operands");
        }

        if (HasSideEffects(operation.Target))
        {
            return Unsupported(operation, $"a compound assignment through '{MemberName(method)}' on a target whose expression has side effects");
        }

        if (OperatorCall(operation, method, operation.Target, operation.Value) is not { } call)
        {
            return Expr.Error;
        }

        return new Expr(Expression(operation.Target).Operand(Expr.Unary) + " = " + call.Text, Expr.Assignment);
    }

    // ---------------------------------------------------------------------------------------------
    // Constructors
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the body of the function a constructor becomes: <c>T __this = T{};</c>, the field
    /// initializers or the constructor it chains to, its own statements, and <c>return __this;</c>.
    /// Returns false when it has no source to translate.
    /// </summary>
    private bool ConstructorBody(IMethodSymbol constructor, GpuType type, IOperation callSite)
    {
        INamedTypeSymbol structType = constructor.ContainingType;
        SyntaxNode? declaration = constructor.DeclaringSyntaxReferences.Select(r => r.GetSyntax(_cancellationToken)).FirstOrDefault();

        Writer.Line(type.MslName + " " + MslNames.This + " = " + type.MslName + "{};");

        switch (declaration)
        {
            case TypeDeclarationSyntax:
                // A primary constructor has no body: it stores the parameters the struct keeps, and the
                // positional properties of a record, then runs the initializers (which may read them).
                foreach (IParameterSymbol parameter in constructor.Parameters)
                {
                    GpuField? field = KeptParameterField(type, parameter);

                    if (field is null && structType.IsRecord)
                    {
                        ISymbol? positional = structType.GetMembers(parameter.Name).FirstOrDefault(m =>
                            m is IPropertySymbol && m.DeclaringSyntaxReferences.Any(r => r.GetSyntax(_cancellationToken) is ParameterSyntax));
                        field = positional is null ? null : type.FindField(positional);
                    }

                    if (field is not null && _function.Names.TryGetValue(parameter, out string? name))
                    {
                        StoreField(field, name);
                    }
                }

                FieldInitializers(structType, type);
                break;

            case ConstructorDeclarationSyntax syntax when GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax, _cancellationToken) is IConstructorBodyOperation body:
                if (body.Initializer is { } initializer)
                {
                    // `: this(...)`. The constructor it names runs the field initializers.
                    if (initializer.DescendantsAndSelf().OfType<IInvocationOperation>().FirstOrDefault() is not { TargetMethod.MethodKind: MethodKind.Constructor } chained ||
                        !SymbolEqualityComparer.Default.Equals(chained.TargetMethod.ContainingType, structType))
                    {
                        Unsupported(initializer, "this constructor initializer");
                        return true;
                    }

                    Writer.Line(MslNames.This + " = " + UserCall(chained, chained.TargetMethod, receiver: null, chained.Arguments).Text + ";");
                }
                else
                {
                    FieldInitializers(structType, type);
                }

                if ((body.BlockBody ?? body.ExpressionBody) is { } block)
                {
                    foreach (IOperation statement in block.Operations)
                    {
                        Statement(statement);
                    }
                }

                break;

            default:
                _diagnostics.Report(DiagnosticDescriptors.UnsupportedMethodCall, callSite.Syntax.GetLocation(), MemberName(constructor), "it has no body in source");
                return false;
        }

        Writer.Line("return " + MslNames.This + ";");
        return true;
    }

    /// <summary>
    /// The initializers of a struct's fields and auto-properties (<c>public float Far = 100;</c>), which
    /// C# runs before the body of a constructor that does not chain to another one.
    /// </summary>
    private void FieldInitializers(INamedTypeSymbol structType, GpuType type)
    {
        foreach (ISymbol member in structType.GetMembers())
        {
            if (member.IsStatic || member is not (IFieldSymbol { IsConst: false } or IPropertySymbol) || type.FindField(member) is not { } field)
            {
                continue;
            }

            EqualsValueClauseSyntax? initializer = member.DeclaringSyntaxReferences.Select(r => r.GetSyntax(_cancellationToken)).Select(static s => s switch
            {
                VariableDeclaratorSyntax declarator => declarator.Initializer,
                PropertyDeclarationSyntax property => property.Initializer,
                _ => null,
            }).FirstOrDefault(static i => i is not null);

            if (initializer is not null &&
                GetSemanticModel(initializer.SyntaxTree).GetOperation(initializer, _cancellationToken) is ISymbolInitializerOperation { Value: { } value })
            {
                StoreField(field, FullExpression(value));
            }
        }
    }

    private void StoreField(GpuField field, string value) =>
        Writer.Line(MslNames.This + "." + field.MslName + " = " + (field.Type.NeedsExplicitPacking ? Call(MslNames.Pack, value).Text : value) + ";");
}
