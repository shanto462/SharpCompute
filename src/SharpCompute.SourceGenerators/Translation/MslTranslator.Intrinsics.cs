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
    /// MSL atomic functions are overloaded on: <c>device</c> for a buffer element, <c>threadgroup</c>
    /// for an element of a <c>[GroupShared]</c> array. Nothing else in <see cref="AtomicCall"/>
    /// depends on where the location is, except what it records for the descriptor: a device atomic
    /// is shared by the whole dispatch, a threadgroup atomic only by the threads of one group.
    /// </summary>
    private readonly struct AtomicLocation
    {
        public const string Device = "device";
        public const string Threadgroup = "threadgroup";

        public AtomicLocation(string addressSpace, string atomicType, string lvalue, bool isFloatBits = false)
        {
            AddressSpace = addressSpace;
            AtomicType = atomicType;
            LValue = lvalue;
            IsFloatBits = isFloatBits;
        }

        public string AddressSpace { get; }

        /// <summary><c>metal::atomic_int</c>, <c>metal::atomic_uint</c> or <c>metal::atomic&lt;float&gt;</c>.</summary>
        public string AtomicType { get; }

        /// <summary>The MSL lvalue of the element, e.g. <c>counts[bin]</c>.</summary>
        public string LValue { get; }

        /// <summary>
        /// True when the element is a float viewed as <c>metal::atomic_uint</c>: threadgroup memory,
        /// where MSL has no atomic float. The call then goes to the method's
        /// <c>[MslAtomic(ThreadgroupFloat = ...)]</c> function, which works on the bits.
        /// </summary>
        public bool IsFloatBits { get; }

        /// <summary>True for a buffer element, false for an element of threadgroup memory.</summary>
        public bool IsDeviceMemory => AddressSpace == Device;

        /// <summary>
        /// <c>(device metal::atomic_int*)&amp;(counts[bin])</c>, or <c>(threadgroup metal::atomic_int*)&amp;(tile[i])</c>:
        /// the element viewed as an atomic object. The same text is right in a helper, where a buffer is
        /// a <c>device T*</c> parameter and a group-shared array a <c>threadgroup T*</c> one.
        /// </summary>
        public string Pointer => "(" + AddressSpace + " " + AtomicType + "*)&(" + LValue + ")";
    }

    /// <summary>
    /// <c>[MslAtomic(name)]</c>: <c>Gpu.AtomicAdd(ref counts[bin], 1)</c> becomes
    /// <c>metal::atomic_fetch_add_explicit((device metal::atomic_int*)&amp;(counts[bin]), 1, metal::memory_order_relaxed)</c>,
    /// and the same with <c>threadgroup</c> for an element of a <c>[GroupShared]</c> array.
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

        // The function for a float element of threadgroup memory, if the method has one.
        string? threadgroupFloat = attribute.NamedArguments.FirstOrDefault(static a => a.Key == "ThreadgroupFloat").Value.Value as string;

        if (AtomicTarget(target.Value, method, threadgroupFloat is { Length: > 0 }) is not { } location)
        {
            return Expr.Error;
        }

        if (location.IsFloatBits)
        {
            name = threadgroupFloat!;
        }

        UsesAtomics = true;
        UsesDeviceAtomics |= location.IsDeviceMemory;

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
    /// An element of a <c>ReadWriteBuffer&lt;T&gt;</c> with <c>T</c> = int, uint or float qualifies, and
    /// an element of a <c>[GroupShared]</c> array of int or uint. A float element of a
    /// <c>[GroupShared]</c> array qualifies when <paramref name="hasThreadgroupFloat"/> is true: MSL 3.2
    /// has no atomic float in the threadgroup address space, so the element is then viewed as an
    /// <c>atomic_uint</c> for the method's bit-level replacement. A local is private to its thread (and
    /// lives in the <c>thread</c> address space, which has no atomics), captured constants are
    /// read-only, and a field of a struct element is left for later.
    /// </summary>
    private AtomicLocation? AtomicTarget(IOperation target, IMethodSymbol method, bool hasThreadgroupFloat)
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

            if (AtomicType(elementType) is not { } atomicType)
            {
                return InvalidAtomicTarget(target, method, $"MSL has no atomic type for '{TypeResolver.Display(element.Property.Type)}' elements (only for int, uint and float)");
            }

            Expr lvalue = Expression(target);
            return new AtomicLocation(AtomicLocation.Device, atomicType, lvalue.Text);
        }

        if (target is IArrayElementReferenceOperation { Indices.Length: 1 } shared && GroupSharedReference(shared.ArrayReference) is { } sharedField)
        {
            // A field that cannot be used at all (its declaration is invalid, or it belongs to another
            // type) is reported once by FindGroupShared, or was where it is declared.
            if (FindGroupShared(sharedField, shared.ArrayReference) is not { } groupShared)
            {
                return null;
            }

            string display = sharedField.Type is IArrayTypeSymbol { ElementType: { } sharedElement } ? TypeResolver.Display(sharedElement) : groupShared.Element.MslName;

            if (AtomicType(groupShared.Element) is not { } atomicType)
            {
                return InvalidAtomicTarget(target, method, $"MSL has no atomic type for '{display}' elements (only for int, uint and float)");
            }

            // metal_atomic only defines the float atomics for threadgroup memory under
            // __HAVE_ATOMIC_THREADGROUP_FLOAT__, which MSL 3.2 (the version the runtime compiles with)
            // does not set: the direct call would not compile. A method that names a replacement
            // working on the bits (Gpu.AtomicAdd does) gets the element as an atomic_uint instead.
            bool isFloatBits = groupShared.Element.MslName == "float";

            if (isFloatBits && !hasThreadgroupFloat)
            {
                return InvalidAtomicTarget(
                    target,
                    method,
                    "it is a float element of a [GroupShared] array, where MSL 3.2 has no atomic float: only an add is available there (Gpu.AtomicAdd, a compare-and-swap loop over the element's bits)");
            }

            // The element as an lvalue, exactly as a plain read or write of it is translated (which also
            // makes the array an input of the helper this is in): `tile[i]`.
            Expr lvalue = Expression(target);
            return new AtomicLocation(AtomicLocation.Threadgroup, isFloatBits ? "metal::atomic_uint" : atomicType, lvalue.Text, isFloatBits);
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
            IFieldReferenceOperation field when IsGroupSharedElementField(field) => "it is a field of a struct element, and only whole elements can be atomic; keep the value in its own [GroupShared] array of int or uint",
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } when _function.This is not null =>
                "it is a field of the struct this method is called on, and only whole elements of a buffer or of a [GroupShared] array can be atomic",
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } or IParameterReferenceOperation => "captured shader state is read-only on the GPU",
            _ => "it is not an element of a buffer or of a [GroupShared] array",
        };

        return InvalidAtomicTarget(target, method, reason);
    }

    /// <summary>The MSL atomic type that overlays an element of <paramref name="element"/>'s type, or null when there is none.</summary>
    private static string? AtomicType(GpuType element) => element is { Kind: GpuTypeKind.Scalar } ? element.MslName switch
    {
        "int" => "metal::atomic_int",
        "uint" => "metal::atomic_uint",
        "float" => "metal::atomic<float>",
        _ => null,
    } : null;

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

    /// <summary>True for <c>tile[i].Field</c> on a <c>[GroupShared]</c> array (and nested fields of it).</summary>
    private static bool IsGroupSharedElementField(IFieldReferenceOperation field)
    {
        for (IOperation? instance = field.Instance; instance is not null; instance = (instance as IFieldReferenceOperation)?.Instance)
        {
            if (instance is IArrayElementReferenceOperation array && GroupSharedReference(array.ArrayReference) is not null)
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
