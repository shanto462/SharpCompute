using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// Calls that need more than <c>name(arguments)</c>: atomic operations, which take a pointer to
/// atomic storage, and the <c>System.BitConverter</c> / <c>System.Numerics.BitOperations</c>
/// methods, whose MSL equivalents differ in result type or argument form.
/// </summary>
internal sealed partial class MslTranslator
{
    // ---------------------------------------------------------------------------------------------
    // Atomics
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Where an atomic operation's location lives. The address space decides the pointer type the
    /// MSL atomic functions are overloaded on; today the only atomic storage is a buffer element
    /// (<c>device</c>). Threadgroup memory slots in here as a second producer of this value, with
    /// <c>threadgroup</c> as its address space: nothing else in <see cref="AtomicCall"/> depends on
    /// where the location is.
    /// </summary>
    private readonly struct AtomicLocation
    {
        public AtomicLocation(string addressSpace, string atomicType, string lvalue)
        {
            AddressSpace = addressSpace;
            AtomicType = atomicType;
            LValue = lvalue;
        }

        public string AddressSpace { get; }

        /// <summary><c>metal::atomic_int</c>, <c>metal::atomic_uint</c> or <c>metal::atomic&lt;float&gt;</c>.</summary>
        public string AtomicType { get; }

        /// <summary>The MSL lvalue of the element, e.g. <c>counts[bin]</c>.</summary>
        public string LValue { get; }

        /// <summary><c>(device metal::atomic_int*)&amp;(counts[bin])</c>: the element viewed as an atomic object.</summary>
        public string Pointer => "(" + AddressSpace + " " + AtomicType + "*)&(" + LValue + ")";
    }

    /// <summary>
    /// <c>[MslAtomic(name)]</c>: <c>Gpu.AtomicAdd(ref counts[bin], 1)</c> becomes
    /// <c>metal::atomic_fetch_add_explicit((device metal::atomic_int*)&amp;(counts[bin]), 1, metal::memory_order_relaxed)</c>.
    /// The location (the first, <c>ref</c> parameter) must be atomic storage, or it is SCG0034.
    /// </summary>
    private Expr AtomicCall(IInvocationOperation operation, AttributeData attribute)
    {
        IMethodSymbol method = operation.TargetMethod;

        if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not string { Length: > 0 } name)
        {
            return UnsupportedCall(operation, "its [MslAtomic] attribute does not name an MSL function");
        }

        if (!method.IsStatic || method.Parameters.Length == 0 || method.Parameters[0].RefKind != RefKind.Ref)
        {
            return UnsupportedCall(operation, "[MslAtomic] methods must be static and take the location as their first parameter, by 'ref'");
        }

        if (ResolveType(method.ReturnType, operation.Syntax.GetLocation()) is null)
        {
            return Expr.Error;
        }

        IArgumentOperation? target = null;
        var values = new List<(int Ordinal, IArgumentOperation Argument)>();

        foreach (IArgumentOperation argument in operation.Arguments)
        {
            if (argument.Parameter is not { } parameter || argument.ArgumentKind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection)
            {
                return Unsupported(argument, "a 'params' argument");
            }

            if (parameter.Ordinal == 0)
            {
                target = argument;
            }
            else if (parameter.RefKind != RefKind.None)
            {
                return UnsupportedCall(operation, "only the location of an [MslAtomic] method can be passed by reference");
            }
            else
            {
                values.Add((parameter.Ordinal, argument));
            }
        }

        if (target is null)
        {
            return Expr.Error;
        }

        if (AtomicTarget(target.Value, method) is not { } location)
        {
            return Expr.Error;
        }

        UsesAtomics = true;

        var arguments = new List<string> { location.Pointer };
        arguments.AddRange(values.OrderBy(static v => v.Ordinal).Select(v => Expression(v.Argument.Value).Text));

        if (name.EndsWith("_explicit", System.StringComparison.Ordinal))
        {
            // The `_explicit` functions take a memory order; relaxed is the only one MSL defines.
            arguments.Add("metal::memory_order_relaxed");
        }

        if (name.StartsWith(MslNames.EscapePrefix, System.StringComparison.Ordinal))
        {
            UsesPrelude = true;
        }

        return Call(name, arguments.ToArray());
    }

    /// <summary>
    /// Resolves the location argument of an atomic operation, or reports SCG0034 and returns null.
    /// Only an element of a <c>ReadWriteBuffer&lt;T&gt;</c> with <c>T</c> = int, uint or float qualifies:
    /// a local is private to its thread (and lives in the <c>thread</c> address space, which has no
    /// atomics), captured constants are read-only, and a field of a struct element is left for later.
    /// </summary>
    private AtomicLocation? AtomicTarget(IOperation target, IMethodSymbol method)
    {
        if (target is IPropertyReferenceOperation { Property.IsIndexer: true, Arguments.Length: 1, Instance: { } buffer } element &&
            TypeResolver.IsBufferType(buffer.Type, out bool isReadOnly))
        {
            if (isReadOnly)
            {
                // Only reachable through a `ref readonly` location parameter; `ref` is a C# error (below).
                return InvalidAtomicTarget(target, method, "it is an element of a ReadOnlyBuffer<T>, which a shader cannot write");
            }

            // An unsupported element type was reported where the buffer was captured.
            if (_types.Resolve(element.Property.Type) is not { } elementType)
            {
                return ReferencesFailedSymbol(target)
                    ? null
                    : InvalidAtomicTarget(target, method, $"its type '{TypeResolver.Display(element.Property.Type)}' has no MSL equivalent");
            }

            string? atomicType = elementType is { Kind: GpuTypeKind.Scalar } ? elementType.MslName switch
            {
                "int" => "metal::atomic_int",
                "uint" => "metal::atomic_uint",
                "float" => "metal::atomic<float>",
                _ => null,
            } : null;

            if (atomicType is null)
            {
                return InvalidAtomicTarget(target, method, $"MSL has no atomic type for '{TypeResolver.Display(element.Property.Type)}' elements (only for int, uint and float)");
            }

            Expr lvalue = Expression(target);
            return new AtomicLocation("device", atomicType, lvalue.Text);
        }

        if (ContainsInvalid(target))
        {
            // Not valid C# to begin with (say, a property passed by ref); the compiler reports it. The
            // one case worth explaining is `ref` to an element of a ReadOnlyBuffer<T> (CS8329, which
            // only says that a readonly variable cannot be passed by ref).
            SawInvalidCode = true;

            return target is IInvalidOperation { ChildOperations: { Count: 1 } children } &&
                children.First() is IPropertyReferenceOperation { Property.IsIndexer: true, Instance.Type: { } bufferType } &&
                TypeResolver.IsBufferType(bufferType, out bool readOnlyElement) && readOnlyElement
                    ? InvalidAtomicTarget(target, method, "it is an element of a ReadOnlyBuffer<T>, which a shader cannot write")
                    : null;
        }

        string reason = target switch
        {
            _ when IsThreadLocal(target) => "it is a local, which is private to one GPU thread and never shared",
            IFieldReferenceOperation field when IsBufferElementField(field) => "it is a field of a struct element, and only whole elements can be atomic; keep the value in its own buffer of int or uint",

            // TODO: atomics on threadgroup memory. An int, uint or float element of a [GroupShared]
            // array could be returned above as an AtomicLocation in the "threadgroup" address space.
            IArrayElementReferenceOperation array when GroupSharedReference(array.ArrayReference) is not null =>
                "it is an element of a [GroupShared] array, and atomics on threadgroup memory are not supported yet; within a group, share it across a barrier instead",
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } or IParameterReferenceOperation => "captured shader state is read-only on the GPU",
            _ => "it is not an element of a buffer",
        };

        return InvalidAtomicTarget(target, method, reason);
    }

    private AtomicLocation? InvalidAtomicTarget(IOperation target, IMethodSymbol method, string reason)
    {
        _diagnostics.Report(DiagnosticDescriptors.InvalidAtomicTarget, target.Syntax.GetLocation(), target.Syntax.ToString(), method.Name, reason);
        return null;
    }

    /// <summary>True for <c>buffer[i].Field</c> (and nested fields of it).</summary>
    private static bool IsBufferElementField(IFieldReferenceOperation field)
    {
        for (IOperation? instance = field.Instance; instance is not null; instance = (instance as IFieldReferenceOperation)?.Instance)
        {
            if (instance is IPropertyReferenceOperation { Property.IsIndexer: true, Instance.Type: { } type } && TypeResolver.IsBufferType(type, out _))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // System.BitConverter and System.Numerics.BitOperations
    // ---------------------------------------------------------------------------------------------

    private static bool IsBitHelperType(INamedTypeSymbol? type) => type switch
    {
        { Name: "BitConverter", ContainingType: null, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } => true,
        { Name: "BitOperations", ContainingType: null, ContainingNamespace: { Name: "Numerics", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } => true,
        _ => false,
    };

    /// <summary>
    /// The 32-bit members of <c>BitConverter</c> and <c>BitOperations</c> whose semantics MSL matches:
    /// bit casts are <c>as_type</c>; the counts return the argument's type in MSL and <c>int</c> in C#
    /// (and agree on 32 for a zero argument); a rotate count is taken modulo 32 in C#, which is made
    /// explicit, and a right rotate is a left rotate by the negated count.
    /// </summary>
    private Expr BitCall(IInvocationOperation operation)
    {
        IMethodSymbol method = operation.TargetMethod;
        Location location = operation.Syntax.GetLocation();

        // Report 64-bit overloads (PopCount(ulong), DoubleToInt64Bits) as the unsupported type they use.
        if (ResolveType(method.ReturnType, location) is not { } returnType)
        {
            return Expr.Error;
        }

        var types = new List<string>();

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                return UnsupportedCall(operation, "it has no MSL equivalent");
            }

            if (ResolveType(parameter.Type, location) is not { } parameterType)
            {
                return Expr.Error;
            }

            types.Add(parameterType.Kind == GpuTypeKind.Scalar ? parameterType.MslName : "?");
        }

        string signature = method.Name + "(" + string.Join(", ", types) + ")";
        string[] arguments = Arguments(operation.Arguments, method, out bool ok);

        if (!ok)
        {
            return Expr.Error;
        }

        switch (method.ContainingType.Name, signature)
        {
            case ("BitConverter", "SingleToInt32Bits(float)"):
            case ("BitConverter", "SingleToUInt32Bits(float)"):
            case ("BitConverter", "Int32BitsToSingle(int)"):
            case ("BitConverter", "UInt32BitsToSingle(uint)"):
                return Call("as_type<" + returnType.MslName + ">", arguments);

            case ("BitOperations", "PopCount(uint)"):
                return Call("int", "metal::popcount(" + arguments[0] + ")");

            case ("BitOperations", "LeadingZeroCount(uint)"):
                return Call("int", "metal::clz(" + arguments[0] + ")");

            case ("BitOperations", "TrailingZeroCount(int)"):
            case ("BitOperations", "TrailingZeroCount(uint)"):
                return Call("int", "metal::ctz(" + arguments[0] + ")");

            case ("BitOperations", "RotateLeft(uint, int)"):
                return Call("metal::rotate", arguments[0], "uint(" + arguments[1] + ") & 31u");

            case ("BitOperations", "RotateRight(uint, int)"):
                // Unsigned negation: `-count` would overflow for int.MinValue, which is undefined in MSL.
                return Call("metal::rotate", arguments[0], "(0u - uint(" + arguments[1] + ")) & 31u");

            default:
                return UnsupportedCall(operation, "it has no MSL equivalent");
        }
    }
}
