using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>The C# shapes of a local array.</summary>
internal enum LocalArrayKind
{
    /// <summary>Not an array or span type.</summary>
    None,

    /// <summary><c>T[]</c>.</summary>
    Array,

    /// <summary><c>System.Span&lt;T&gt;</c>.</summary>
    Span,

    /// <summary><c>System.ReadOnlySpan&lt;T&gt;</c>, which becomes a <c>const</c> MSL array.</summary>
    ReadOnlySpan,

    /// <summary><c>T[,]</c> and higher ranks, which are rejected.</summary>
    MultiDimensional,

    /// <summary><c>T[][]</c> or a span of arrays, which are rejected.</summary>
    Jagged,
}

/// <summary>
/// A local array of the function being translated: a local declared with <c>stackalloc</c>, an array
/// creation or a collection expression (a fixed-size MSL array in the thread address space), or an
/// array parameter of a helper (a pointer to the caller's array plus its length).
/// </summary>
internal sealed class LocalArray
{
    public LocalArray(ISymbol symbol, GpuType element, string mslName, int length, bool isReadOnly, string? lengthName = null)
    {
        Symbol = symbol;
        Element = element;
        MslName = mslName;
        Length = length;
        IsReadOnly = isReadOnly;
        LengthName = lengthName;
    }

    public ISymbol Symbol { get; }

    /// <summary>The element type; local arrays hold the native MSL type (<c>float3</c>, not <c>packed_float3</c>).</summary>
    public GpuType Element { get; }

    /// <summary>The name of the array, or of the pointer a helper receives.</summary>
    public string MslName { get; }

    /// <summary>The constant length of a local, or -1 for a parameter, whose length is <see cref="LengthName"/>.</summary>
    public int Length { get; }

    /// <summary>For a parameter: the hidden <c>int</c> parameter holding its length.</summary>
    public string? LengthName { get; }

    /// <summary>True for a <c>ReadOnlySpan&lt;T&gt;</c>: a <c>const</c> array, or a <c>thread const T*</c> parameter.</summary>
    public bool IsReadOnly { get; }
}

/// <summary>
/// Fixed-size local arrays: <c>Span&lt;float&gt; taps = stackalloc float[9];</c>, <c>float[] w = new float[9];</c>,
/// <c>ReadOnlySpan&lt;float&gt; w = [1, 2, 1];</c> and <c>[InlineArray(N)]</c> structs.
/// </summary>
/// <remarks>
/// <para>
/// Array and span locals become MSL arrays in the thread address space, <c>float taps[9] = {};</c>,
/// and <c>ReadOnlySpan&lt;T&gt;</c> ones <c>const</c> arrays. Without an initializer the elements are
/// zeroed, as C# zeroes <c>new T[n]</c> and (without <c>[SkipLocalsInit]</c>) <c>stackalloc</c>, so the
/// GPU and <c>ForCpu</c> start from the same values. The length must be a compile-time constant.
/// </para>
/// <para>
/// A C# array or span is a reference to its elements; an MSL array is the elements. The two only
/// agree while each array has exactly one name, so a local array can be indexed (read, written,
/// compound-assigned, incremented, passed by <c>ref</c>), its <c>Length</c> read (a constant),
/// looped over with <c>foreach</c> (lowered to an index loop), and passed to a helper method, which
/// receives <c>thread T*</c> plus an <c>int</c> length: a helper works for every length and forwards
/// both to the helpers it calls, and writes through the pointer reach the caller's array, as they do
/// through a span in C#. Anything that would give the elements a second name or let them outlive the
/// function (copying to another local, reassigning, returning, slicing, converting) is SCG0039.
/// </para>
/// <para>
/// An <c>[InlineArray(N)]</c> struct is a value type in C# too, so it is an ordinary struct in MSL
/// whose only member is the array (see <c>TypeResolver.CreateInlineArray</c>): it is copied, passed
/// and returned by value and may cross the CPU/GPU boundary. Its elements are <c>f[i]</c>, and
/// <c>foreach</c> works on it as on the other local arrays.
/// </para>
/// <para>
/// Nothing is bounds-checked on the GPU: an index outside the array reads or writes whatever lies
/// there. <c>ForCpu</c> runs the C#, which throws <c>IndexOutOfRangeException</c>, so running a shader
/// on the CPU is the way to find such a bug.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    /// <summary>
    /// Above this many bytes of one local array, SCG0038 warns. An Apple GPU thread keeps small arrays in
    /// registers and spills larger ones to memory, which is correct but slow: on an M4 Pro, a kernel that
    /// fills and reads back its array costs about 0.09 ns per element (all threads together) at 1 KB and
    /// 0.55 ns at 16 KB, and Metal refuses to create the pipeline once a thread needs 256 KB of stack.
    /// </summary>
    public const int MaxLocalArrayBytes = 16384;

    private const string Reference =
        "a local array lives in the registers of one GPU thread and is not a reference, so it cannot be copied to another variable, compared, returned or stored; index it, read its Length, loop over it with foreach, or pass it to a helper method";

    private const string Members =
        "of a local array, only the indexer, 'Length' and foreach can be used in a shader, and it can be passed to a helper method of the shader; the methods of Span<T> and arrays (Slice, CopyTo, Fill, ...) are not available";

    private const string Creation =
        "a local array can only be created as the initializer of a local variable, as in 'Span<float> taps = stackalloc float[9];', 'float[] weights = new float[9];' or 'ReadOnlySpan<float> weights = [1, 2, 1];'";

    private const string InlineArrayToSpan =
        "an inline array cannot be converted to a span in a shader; index it directly, or pass the inline array itself to a helper parameter of its own type";

    public static bool IsLocalArrayType(ITypeSymbol? type) => ClassifyArrayType(type, out _) != LocalArrayKind.None;

    /// <summary>Recognizes <c>T[]</c>, <c>Span&lt;T&gt;</c> and <c>ReadOnlySpan&lt;T&gt;</c> (and the ranks and nestings that are rejected).</summary>
    public static LocalArrayKind ClassifyArrayType(ITypeSymbol? type, out ITypeSymbol? element)
    {
        element = null;

        switch (type)
        {
            case IArrayTypeSymbol array:
                element = array.ElementType;

                if (!array.IsSZArray)
                {
                    return LocalArrayKind.MultiDimensional;
                }

                return array.ElementType is IArrayTypeSymbol ? LocalArrayKind.Jagged : LocalArrayKind.Array;

            case INamedTypeSymbol { Arity: 1, Name: "Span" or "ReadOnlySpan", ContainingType: null, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } span:
                element = span.TypeArguments[0];

                if (element is IArrayTypeSymbol)
                {
                    return LocalArrayKind.Jagged;
                }

                return span.Name == "Span" ? LocalArrayKind.Span : LocalArrayKind.ReadOnlySpan;

            default:
                return LocalArrayKind.None;
        }
    }

    /// <summary>
    /// The local array <paramref name="operation"/> names: a local or parameter of the current function
    /// registered as one, seen through implicit conversions (<c>T[]</c> → <c>Span&lt;T&gt;</c> → <c>ReadOnlySpan&lt;T&gt;</c>).
    /// </summary>
    private LocalArray? LocalArrayReference(IOperation? operation)
    {
        ISymbol? symbol = WithoutImplicitConversions(operation) switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null,
        };

        return symbol is not null && _function.LocalArrays.TryGetValue(symbol, out LocalArray? array) ? array : null;
    }

    /// <summary>True for a reference to a local array whose declaration was already reported.</summary>
    private bool IsFailedLocalArray(IOperation? operation) =>
        WithoutImplicitConversions(operation) is { } inner && IsLocalArrayType(inner.Type) && ReferencesFailedSymbol(inner);

    private static bool IsStackAlloc(IOperation operation) =>
        operation is { Kind: OperationKind.None, Syntax: StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax };

    private static bool IsArrayCreation(IOperation operation) =>
        operation is IArrayCreationOperation || IsStackAlloc(operation) ||
        (operation is ICollectionExpressionOperation collection && IsLocalArrayType(collection.Type));

    /// <summary>
    /// True for a call that becomes an MSL function: a local function, a method of the shader, or a
    /// method of another type that is translated from source (a shared static helper, a method of a
    /// user struct). The methods of <c>Span&lt;T&gt;</c> and arrays themselves have no source here.
    /// </summary>
    private bool IsHelperCall(IInvocationOperation invocation) =>
        invocation.TargetMethod.MethodKind == MethodKind.LocalFunction ||
        SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, _shaderType) ||
        (invocation.TargetMethod.MethodKind == MethodKind.Ordinary && UserMethodProblem(invocation.TargetMethod) is null);

    /// <summary>Reports SCG0039 for <paramref name="operation"/>, unless the C# compiler already reports it.</summary>
    private Expr LocalArrayMisuse(IOperation operation, string reason)
    {
        if (ContainsInvalid(operation))
        {
            SawInvalidCode = true;
            return Expr.Error;
        }

        _diagnostics.Report(DiagnosticDescriptors.InvalidLocalArrayUse, operation.Syntax.GetLocation(), operation.Syntax.ToString(), reason);
        return Expr.Error;
    }

    private static Expr LengthOf(LocalArray array) =>
        array.LengthName is { } name ? new Expr(name, Expr.Primary) : Int32Literal(array.Length);

    // ---------------------------------------------------------------------------------------------
    // Declarations and parameters
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>Span&lt;float&gt; taps = stackalloc float[9];</c> → <c>float taps[9] = {};</c>, and with elements
    /// (<c>stackalloc float[] { 1, 2, 1 }</c>, <c>new float[] { … }</c>, <c>[1, 2, 1]</c>)
    /// <c>float taps[3] = { 1.0f, 2.0f, 1.0f };</c>. C++ evaluates the elements of a braced list in
    /// order, as C# does.
    /// </summary>
    private LocalDeclaration DeclareLocalArray(IVariableDeclaratorOperation declarator, IVariableDeclarationOperation declaration, ILocalSymbol local, Location typeLocation)
    {
        string name = DeclareLocal(local);
        Location nameLocation = declarator.Syntax is VariableDeclaratorSyntax syntax ? syntax.Identifier.GetLocation() : typeLocation;
        LocalArrayKind kind = ClassifyArrayType(local.Type, out ITypeSymbol? elementSymbol);

        if (kind is LocalArrayKind.MultiDimensional or LocalArrayKind.Jagged)
        {
            return Failed(DiagnosticDescriptors.InvalidLocalArray, typeLocation, ShapeReason(kind, local.Type));
        }

        IOperation? initializer = declarator.GetVariableInitializer()?.Value ?? declaration.Initializer?.Value;

        if (initializer is null)
        {
            return Failed(
                DiagnosticDescriptors.InvalidLocalArray,
                nameLocation,
                "it has no initializer; create the array where it is declared, as in 'Span<float> taps = stackalloc float[9];', 'float[] weights = new float[9];' or 'ReadOnlySpan<float> weights = [1, 2, 1];'");
        }

        if (ContainsInvalid(initializer))
        {
            SawInvalidCode = true;
            return Failed();
        }

        IOperation value = WithoutImplicitConversions(initializer)!;
        IOperation? lengthOperation = null;
        IReadOnlyList<IOperation>? elements = null;

        switch (value)
        {
            case IArrayCreationOperation creation:
                lengthOperation = creation.DimensionSizes[0];
                elements = creation.Initializer?.ElementValues;
                break;

            case ICollectionExpressionOperation collection:
                if (collection.Elements.FirstOrDefault(static e => e is ISpreadOperation) is { } spread)
                {
                    return Failed(
                        DiagnosticDescriptors.InvalidLocalArray,
                        spread.Syntax.GetLocation(),
                        $"its collection expression spreads '{spread.Syntax}' into it, whose length is not known when the shader is compiled; list the elements");
                }

                elements = collection.Elements;
                break;

            case { } stackAlloc when IsStackAlloc(stackAlloc):
                // The first child is the length (implicit when there are elements), the rest the elements.
                List<IOperation> children = stackAlloc.ChildOperations.ToList();
                bool hasElements = stackAlloc.Syntax is ImplicitStackAllocArrayCreationExpressionSyntax ||
                    stackAlloc.Syntax is StackAllocArrayCreationExpressionSyntax { Initializer: not null };

                lengthOperation = children[0];
                elements = hasElements ? children.Skip(1).ToList() : null;
                break;

            default:
                if (LocalArrayReference(value) is not null)
                {
                    LocalArrayMisuse(value, "a local array cannot be copied or aliased: in C# both names would refer to the same elements, which a GPU thread's array cannot express; use the original");
                    return Failed();
                }

                if (GroupSharedReference(value) is not null)
                {
                    // Threadgroup memory used as a value has a diagnostic of its own.
                    Expression(initializer);
                    return Failed();
                }

                if (IsFailedLocalArray(value) || LocalArrayExpression(initializer) is not null)
                {
                    // Reported already, or now: a slice, a range, an inline array converted to a span...
                    return Failed();
                }

                return Failed(
                    DiagnosticDescriptors.InvalidLocalArray,
                    initializer.Syntax.GetLocation(),
                    $"its initializer '{initializer.Syntax}' is not a stackalloc, an array creation ('new T[N]') or a collection expression ('[a, b, c]')");
        }

        if (ResolveArrayElement(elementSymbol!, typeLocation, local.Name) is not { } element)
        {
            return Failed();
        }

        long length;

        if (lengthOperation is null)
        {
            length = elements!.Count;
        }
        else if (lengthOperation.ConstantValue is not { HasValue: true, Value: { } constant } || !TryGetLength(constant, out length))
        {
            return Failed(
                DiagnosticDescriptors.LocalArrayLengthNotConstant,
                lengthOperation.Syntax.GetLocation(),
                $"'{lengthOperation.Syntax}' is not a compile-time constant; use a literal, a const, or an expression of those, because a GPU thread's arrays are sized when the shader is compiled");
        }

        if (length is < 1 or > int.MaxValue)
        {
            return Failed(
                DiagnosticDescriptors.LocalArrayLengthNotConstant,
                (lengthOperation is { IsImplicit: false } ? lengthOperation.Syntax : value.Syntax).GetLocation(),
                length == 0 ? "an empty array has no MSL equivalent" : $"the length {length.ToString(CultureInfo.InvariantCulture)} is out of range");
        }

        CheckLocalArraySize(local, length * LocalSize(element), nameLocation);

        string initializerText = elements is null ? "{}" : "{ " + string.Join(", ", elements.Select(FullExpression)) + " }";
        bool isReadOnly = kind == LocalArrayKind.ReadOnlySpan;

        _function.LocalArrays[local] = new LocalArray(local, element, name, (int)length, isReadOnly);
        return new LocalDeclaration(isReadOnly, element.MslName, name, initializerText, "[" + length.ToString(CultureInfo.InvariantCulture) + "]");

        LocalDeclaration Failed(DiagnosticDescriptor? descriptor = null, Location? location = null, string? reason = null)
        {
            if (descriptor is not null)
            {
                _diagnostics.Report(descriptor, location, local.Name, reason!);
            }

            _failedSymbols.Add(local);
            return new LocalDeclaration(isConst: false, "int", name, initializer: null);
        }
    }

    /// <summary>SCG0038 for a local array (or a local of an inline array type) larger than <see cref="MaxLocalArrayBytes"/>.</summary>
    private void CheckLocalArraySize(ILocalSymbol local, long bytes, Location location)
    {
        if (bytes > MaxLocalArrayBytes)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.LocalArrayTooLarge,
                location,
                local.Name,
                bytes.ToString("N0", CultureInfo.InvariantCulture),
                MaxLocalArrayBytes.ToString("N0", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>SCG0038 for a local whose type is an inline array.</summary>
    private void CheckInlineArrayLocal(IVariableDeclaratorOperation declarator, ILocalSymbol local, GpuType? type, Location typeLocation)
    {
        if (type is { IsInlineArray: true, Element: { } element })
        {
            Location location = declarator.Syntax is VariableDeclaratorSyntax syntax ? syntax.Identifier.GetLocation() : typeLocation;
            CheckLocalArraySize(local, type.ArrayLength * LocalSize(element), location);
        }
    }

    private static string ShapeReason(LocalArrayKind kind, ITypeSymbol type) => kind == LocalArrayKind.MultiDimensional
        ? $"'{TypeResolver.Display(type)}' is a multi-dimensional array; use a one-dimensional array and index it as [y * width + x]"
        : $"'{TypeResolver.Display(type)}' is an array of arrays; use a one-dimensional array and index it as [i * length + j]";

    /// <summary>The element type of a local array, or null after reporting why it cannot be one.</summary>
    private GpuType? ResolveArrayElement(ITypeSymbol elementSymbol, Location location, string name)
    {
        if (elementSymbol.TypeKind == TypeKind.Error)
        {
            SawInvalidCode = true;
            return null;
        }

        GpuType? element = _types.Resolve(elementSymbol, out TypeFailure? failure);

        if (TypeResolver.IsStructFailure(failure))
        {
            // The user struct is the real culprit: report its own diagnostic.
            _diagnostics.Report(failure!.Descriptor, location, failure.Arguments);
            return null;
        }

        if (TypeResolver.WaveMatrixStorage(elementSymbol, $"the element of the local array '{name}'") is { } matrixElement)
        {
            // An array is memory, which a matrix has no layout in; and MSL's `{}` would leave the
            // matrices of `new WaveMatrix8x8[4]` uninitialized where C# has zero matrices.
            _diagnostics.Report(matrixElement.Descriptor, location, matrixElement.Arguments);
            return null;
        }

        if (element is null || element.Kind is GpuTypeKind.Void or GpuTypeKind.Buffer or GpuTypeKind.Texture)
        {
            string reason = failure?.Reason ?? "buffers and textures are kernel arguments, not values; capture them in the shader instead";
            _diagnostics.Report(DiagnosticDescriptors.InvalidLocalArray, location, name, $"its element type '{TypeResolver.Display(elementSymbol)}' is not supported ({reason})");
            return null;
        }

        return element;
    }

    /// <summary>
    /// The bytes an element takes in a thread's MSL array: the C# size, except that 3-component vectors
    /// and matrix columns are padded to 4 components in MSL.
    /// </summary>
    private static long LocalSize(GpuType element) =>
        element.Kind == GpuTypeKind.Vector && element.MslName.EndsWith("3", System.StringComparison.Ordinal) ? element.Size / 3 * 4 : element.Size;

    /// <summary>
    /// A <c>T[]</c>, <c>Span&lt;T&gt;</c> or <c>ReadOnlySpan&lt;T&gt;</c> parameter of a helper becomes
    /// <c>thread T* name, int __len_name</c> (<c>thread const T*</c> for a read-only span). Returns null
    /// when the parameter is no such type, and an empty list (with <paramref name="failed"/> set) after
    /// reporting why it cannot be one.
    /// </summary>
    private List<string>? LocalArrayParameter(IParameterSymbol parameter, string name, ParameterSyntax? syntax, Location location, ref bool failed)
    {
        LocalArrayKind kind = ClassifyArrayType(parameter.Type, out ITypeSymbol? elementSymbol);

        if (kind == LocalArrayKind.None)
        {
            return null;
        }

        Location typeLocation = syntax?.Type?.GetLocation() ?? location;

        if (kind is LocalArrayKind.MultiDimensional or LocalArrayKind.Jagged)
        {
            _diagnostics.Report(DiagnosticDescriptors.InvalidLocalArray, typeLocation, parameter.Name, ShapeReason(kind, parameter.Type));
        }
        else if (parameter.RefKind != RefKind.None)
        {
            _diagnostics.Report(
                DiagnosticDescriptors.InvalidLocalArrayUse,
                location,
                parameter.Name,
                "a local array parameter cannot be 'ref', 'out' or 'in': the helper receives the caller's elements and can write them already (unless it is a ReadOnlySpan<T>); remove the modifier");
        }
        else if (ResolveArrayElement(elementSymbol!, typeLocation, parameter.Name) is { } element)
        {
            bool isReadOnly = kind == LocalArrayKind.ReadOnlySpan;
            string lengthName = MslNames.ArrayLengthPrefix + name;

            _function.LocalArrays[parameter] = new LocalArray(parameter, element, name, length: -1, isReadOnly, lengthName);
            return new List<string> { "thread " + (isReadOnly ? "const " : string.Empty) + element.MslName + "* " + name, "int " + lengthName };
        }

        _failedSymbols.Add(parameter);
        failed = true;
        return new List<string>();
    }

    /// <summary>Reports a helper whose return type is an array or a span (SCG0039); false for any other return type.</summary>
    private bool ReturnsLocalArray(IMethodSymbol method, Location location)
    {
        if (!IsLocalArrayType(method.ReturnType))
        {
            return false;
        }

        _function.ReturnsLocalArray = true;
        _diagnostics.Report(
            DiagnosticDescriptors.InvalidLocalArrayUse,
            location,
            method.Name,
            $"a helper method cannot return a local array ('{TypeResolver.Display(method.ReturnType)}'), which would live in the helper after it returned; declare the array in the caller and pass it to the helper, which can fill it");
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Uses
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Element accesses of local arrays and inline arrays, and every other expression that involves a
    /// local array in a way MSL cannot express. Null for everything else.
    /// </summary>
    private Expr? LocalArrayExpression(IOperation operation)
    {
        switch (operation)
        {
            case IArrayElementReferenceOperation element:
                if (LocalArrayReference(element.ArrayReference) is { } array)
                {
                    return new Expr(array.MslName + "[" + Expression(element.Indices[0]).Text + "]", Expr.Primary);
                }

                if (IsFailedLocalArray(element.ArrayReference))
                {
                    return Expr.Error;
                }

                // Not one local array (`(c ? a : b)[i]`, a helper's result, a creation): say what it is.
                return LocalArrayExpression(element.ArrayReference) ?? ReportArrayValue(element.ArrayReference);

            case ISimpleAssignmentOperation assignment when IsLocalArrayType(assignment.Target.Type):
                return ReferencesFailedSymbol(assignment.Target)
                    ? Expr.Error
                    : LocalArrayMisuse(
                        assignment.Target,
                        "a local array cannot be assigned after its declaration: it is the elements themselves, not a reference that could be pointed at others; declare another local array, or copy the elements one by one");

            case IBinaryOperation binary when (LocalArrayReference(binary.LeftOperand) ?? LocalArrayReference(binary.RightOperand)) is not null:
                return LocalArrayMisuse(LocalArrayReference(binary.LeftOperand) is not null ? WithoutImplicitConversions(binary.LeftOperand)! : WithoutImplicitConversions(binary.RightOperand)!, Reference);

            case IInlineArrayAccessOperation inlineElement:
                return InlineArrayElement(inlineElement);

            case IImplicitIndexerReferenceOperation indexer when LocalArrayReference(indexer.Instance) is not null || _types.Resolve(indexer.Instance.Type) is { IsInlineArray: true }:
                return LocalArrayMisuse(
                    operation,
                    "ranges and indices from the end ('^') of a local array are not supported; index it with an int, and pass the whole array and an offset to a helper method that works on part of it");

            case IInvocationOperation invocation when !IsHelperCall(invocation) &&
                (LocalArrayReference(invocation.Instance) is not null || invocation.Arguments.Any(a => LocalArrayReference(a.Value) is not null)):
                return LocalArrayMisuse(operation, Members);

            case IConversionOperation conversion when IsLocalArrayType(conversion.Type) && _types.Resolve(conversion.Operand.Type) is { IsInlineArray: true }:
                return LocalArrayMisuse(operation, InlineArrayToSpan);
        }

        IOperation inner = WithoutImplicitConversions(operation)!;

        if (IsArrayCreation(inner))
        {
            return LocalArrayMisuse(inner, Creation);
        }

        if (IsFailedLocalArray(inner))
        {
            return Expr.Error;
        }

        // The array itself, or something that yields one of several (`c ? a : b`).
        if (IsLocalArrayType(inner.Type) && inner.DescendantsAndSelf().Any(d => LocalArrayReference(d) is not null))
        {
            return LocalArrayMisuse(inner, Reference);
        }

        return null;
    }

    /// <summary>
    /// An array or span that is not a local array of this function, used where one is expected.
    /// Translating it reports what it is (a helper that returns an array, a slice...); if that found
    /// nothing to say, SCG0039 says it here.
    /// </summary>
    private Expr ReportArrayValue(IOperation operation)
    {
        int errors = _diagnostics.ErrorCount;
        Expression(operation);

        if (_diagnostics.ErrorCount == errors && !SawInvalidCode)
        {
            LocalArrayMisuse(operation, Reference);
        }

        return Expr.Error;
    }

    /// <summary>
    /// <c>f[i]</c> on an <c>[InlineArray]</c> struct → <c>f.e[i]</c>. The elements are boundary storage
    /// (as struct fields are), so a matrix element is unpacked where read and packed where stored.
    /// </summary>
    private Expr InlineArrayElement(IInlineArrayAccessOperation operation)
    {
        if (ReferencesFailedSymbol(operation.Instance))
        {
            return Expr.Error;
        }

        if (ResolveType(operation.Instance.Type, operation.Instance.Syntax.GetLocation()) is not { IsInlineArray: true } array)
        {
            return Expr.Error;
        }

        if (operation.Argument.Type?.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_UInt32))
        {
            return LocalArrayMisuse(operation, "ranges and indices from the end ('^') of an inline array are not supported; index it with an int");
        }

        GpuField field = array.Fields[0];
        string instance = Expression(operation.Instance).Operand(Expr.Primary);
        string index = Expression(operation.Argument).Text;

        return BoundaryRead(operation, field.Type, new Expr(instance + "." + field.MslName + "[" + index + "]", Expr.Primary));
    }

    /// <summary><c>span[i]</c>, <c>span.Length</c> and <c>array.Length</c> of a local array; null when the instance is no array or span.</summary>
    private Expr? LocalArrayProperty(IPropertyReferenceOperation operation)
    {
        if (operation.Instance is not { } instance || !IsLocalArrayType(WithoutImplicitConversions(instance)!.Type))
        {
            return null;
        }

        if (LocalArrayReference(instance) is not { } array)
        {
            if (IsFailedLocalArray(instance))
            {
                return Expr.Error;
            }

            // Not a local array (a [GroupShared] field is handled before this).
            return ReportArrayValue(instance);
        }

        IPropertySymbol property = operation.Property;

        if (property.IsIndexer && operation.Arguments.Length == 1)
        {
            return new Expr(array.MslName + "[" + Expression(operation.Arguments[0].Value).Text + "]", Expr.Primary);
        }

        if (property.Name == "Length" && !property.IsIndexer)
        {
            return LengthOf(array);
        }

        return UnsupportedMember(operation, property, Members);
    }

    /// <summary>
    /// The arguments for a local array parameter of a helper: the caller's array (or pointer) and its
    /// length, as one comma-separated text. Anything else is SCG0039.
    /// </summary>
    private string LocalArrayArgument(IArgumentOperation argument, ref bool ok)
    {
        IOperation value = argument.Value;

        if (LocalArrayReference(value) is { } array)
        {
            return array.MslName + ", " + LengthOf(array).Text;
        }

        ok = false;

        if (ContainsInvalid(value))
        {
            SawInvalidCode = true;
        }
        else if (!ReferencesFailedSymbol(value))
        {
            string reason = value is IConversionOperation { Operand.Type: var source } && _types.Resolve(source) is { IsInlineArray: true }
                ? InlineArrayToSpan
                : "a Span<T>, ReadOnlySpan<T> or T[] parameter of a helper method takes a local array of the caller (a stackalloc, array or span local, or such a parameter) as it is; it cannot be created, sliced or converted in the call";

            _diagnostics.Report(DiagnosticDescriptors.InvalidLocalArrayUse, value.Syntax.GetLocation(), value.Syntax.ToString(), reason);
        }

        return Expr.Error.Text;
    }

    /// <summary>
    /// True for an element of a local array of this function (<c>ref taps[i]</c>), which lives in the
    /// thread address space and can bind to a <c>thread T&amp;</c> parameter, like a local.
    /// </summary>
    private bool IsThreadLocalArrayElement(IOperation operation) => operation switch
    {
        IArrayElementReferenceOperation element => LocalArrayReference(element.ArrayReference) is not null,
        IPropertyReferenceOperation { Property.IsIndexer: true } indexer => LocalArrayReference(indexer.Instance) is { IsReadOnly: false },

        // An inline array holds the boundary type: only where that is the native type can it bind.
        IInlineArrayAccessOperation element => IsThreadLocal(element.Instance) && _types.Resolve(element.Type) is { } type && type.BoundaryName == type.MslName,
        _ => false,
    };

    /// <summary>The local array a helper argument passes, and whether the helper may write it; null for any other argument.</summary>
    private ISymbol? LocalArrayPassedToHelper(IArgumentOperation argument, out bool writable)
    {
        writable = false;

        if (argument.Parameter is not { RefKind: RefKind.None } parameter || LocalArrayReference(argument.Value) is not { } array)
        {
            return null;
        }

        LocalArrayKind kind = ClassifyArrayType(parameter.Type, out _);
        writable = kind is LocalArrayKind.Array or LocalArrayKind.Span;
        return kind == LocalArrayKind.None ? null : array.Symbol;
    }

    // ---------------------------------------------------------------------------------------------
    // foreach
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>foreach (float t in taps) body</c> over a local array or an inline array →
    /// <c>for (int __i_t = 0; __i_t &lt; 9; __i_t++) { float t = taps[__i_t]; body }</c>. The element is
    /// read at the start of each iteration, as C# does through the span or array it iterates, and a
    /// <c>ref</c> loop variable over a local array becomes a <c>thread T&amp;</c>. Any other collection is SCG0012.
    /// </summary>
    private void ForEach(IForEachLoopOperation operation)
    {
        IOperation collection = WithoutImplicitConversions(operation.Collection)!;
        LocalArray? array = LocalArrayReference(collection);
        GpuType? inlineArray = array is null && _types.Resolve(collection.Type) is { IsInlineArray: true } inline ? inline : null;

        if (array is null && inlineArray is null)
        {
            if (!ReferencesFailedSymbol(collection))
            {
                Unsupported(operation, "a foreach loop over anything but a local array (a stackalloc, array or span local, an array parameter of a helper, or an [InlineArray] struct)");
            }

            return;
        }

        if (operation.LoopControlVariable is not IVariableDeclaratorOperation { Symbol: { } variable })
        {
            Unsupported(operation, "a foreach loop that deconstructs its elements");
            return;
        }

        GpuType element = array?.Element ?? inlineArray!.Element!;
        GpuType? variableType = _types.Resolve(variable.Type);

        if (!ReferenceEquals(variableType, element))
        {
            ITypeSymbol? elementSymbol = inlineArray?.Fields[0].Symbol.Type;

            if (elementSymbol is null)
            {
                ClassifyArrayType(collection.Type, out elementSymbol);
            }

            Unsupported(operation, $"a foreach loop whose variable is not of the element type ('{TypeResolver.Display(variable.Type)}' over elements of type '{TypeResolver.Display(elementSymbol!)}')");
            return;
        }

        if (inlineArray is not null && variable.RefKind != RefKind.None)
        {
            Unsupported(operation, "a 'ref' foreach loop over an inline array (index the inline array instead)");
            return;
        }

        if (inlineArray is not null && HasSideEffects(collection))
        {
            Unsupported(operation, "a foreach loop over an inline array computed with side effects (assign it to a local first)");
            return;
        }

        string index = MslNames.ForEachIndexPrefix + MslNames.Sanitize(variable.Name);
        string length = array is not null ? LengthOf(array).Text : inlineArray!.ArrayLength.ToString(CultureInfo.InvariantCulture);
        string elementText;

        if (array is not null)
        {
            elementText = array.MslName + "[" + index + "]";
        }
        else
        {
            GpuField field = inlineArray!.Fields[0];
            elementText = BoundaryRead(operation, field.Type, new Expr(Expression(collection).Operand(Expr.Primary) + "." + field.MslName + "[" + index + "]", Expr.Primary)).Text;
        }

        string declarationType = variable.RefKind switch
        {
            RefKind.Ref => "thread " + element.MslName + "&",
            RefKind.RefReadOnly or RefKind.In => "thread const " + element.MslName + "&",
            _ => element.MslName,
        };

        Writer.Line($"for (int {index} = 0; {index} < {length}; {index}++)");
        Writer.Line("{");
        Writer.Indent++;
        Writer.Line(declarationType + " " + DeclareLocal(variable) + " = " + elementText + ";");

        // The body shares the loop variable's scope, so its statements go straight into the loop.
        IEnumerable<IOperation> body = operation.Body is IBlockOperation block ? block.Operations : new[] { operation.Body };

        foreach (IOperation statement in body)
        {
            Statement(statement);
        }

        Writer.Indent--;
        Writer.Line("}");
    }
}
