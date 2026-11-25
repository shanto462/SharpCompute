using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// The matrix units: <c>WaveMatrix8x8</c> and <c>WaveMatrixHalf8x8</c>, which are Metal's
/// <c>simdgroup_float8x8</c> and <c>simdgroup_half8x8</c>, and their members, which are the functions
/// of <c>&lt;metal_simdgroup_matrix&gt;</c>. Like the SIMD-group operations of <c>Gpu</c> the members
/// are recognized by name and carry hidden call-site parameters that only the CPU reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Expressions.</b> Metal's load and its products return through a reference parameter; in C# they
/// are expressions. They are called through the prelude's <c>__ms_wave_matrix_*</c> helpers, which
/// return the matrix and inline to the plain call. A store returns nothing in either language and is
/// called directly.
/// </para>
/// <para>
/// <b>Address spaces.</b> A load or store takes a pointer: <c>buffer + offset</c> in the
/// <c>device</c> address space for a captured buffer, <c>array + offset</c> in the
/// <c>threadgroup</c> address space for a <c>[GroupShared]</c> array. Both are spelled the same, in
/// the kernel (where a group-shared array is an array, which decays) and in a helper (where it is the
/// pointer the helper was passed); Metal's functions and the prelude's load are overloaded on the two.
/// </para>
/// <para>
/// <b>The offset is added term by term.</b> <c>Load(a, row * n + k, n)</c> becomes
/// <c>a + int(uint(row) * uint(n)) + k</c>, not <c>a + int(uint(row) * uint(n) + uint(k))</c>: the
/// terms of a sum of ints are added to the pointer one at a time. A 32-bit sum that is then widened
/// to an address is opaque to the GPU compiler's addressing of the 64 elements; pointer increments
/// are not: as one widened sum a matrix product measured about 15 % slower (Apple M4 Pro).
/// </para>
/// <para>
/// This is the one place where the generator does not make signed arithmetic wrap as C# does
/// (<c>SignedArithmetic</c>): every term is computed as usual and wraps, but their sum is the
/// pointer's, in 64 bits. The two differ only where the terms add up to more than an int holds, and
/// an index into a buffer cannot depend on that: a sum past int.MaxValue wraps to a negative offset
/// in C#, which is out of range on the CPU too (the meeting throws ArgumentOutOfRangeException), and
/// for C# to get an offset inside the buffer the sum has to wrap all the way round, below
/// int.MinValue or past 2^32. Only a sum of <c>int</c>s at the top of the offset is split; a sum of
/// <c>uint</c>s, where adding a huge value to subtract a small one is an idiom, reaches the offset
/// through a conversion and is left whole.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    private const string WaveMatrixLoad = MslNames.EscapePrefix + "wave_matrix_load";
    private const string WaveMatrixMultiply = MslNames.EscapePrefix + "wave_matrix_multiply";
    private const string WaveMatrixMultiplyAccumulate = MslNames.EscapePrefix + "wave_matrix_multiply_accumulate";

    /// <summary><c>metal::make_filled_simdgroup_matrix&lt;float, 8, 8&gt;</c>, the function that makes a matrix of one value.</summary>
    private static string WaveMatrixFill(GpuType matrix) => "metal::make_filled_simdgroup_matrix<" + matrix.Element!.MslName + ", 8, 8>";

    /// <summary>
    /// <c>default(WaveMatrix8x8)</c> and <c>new WaveMatrix8x8()</c>: the zero matrix, which C# has for
    /// them and MSL has not (a default-constructed <c>simdgroup_matrix</c> is uninitialized).
    /// </summary>
    private Expr WaveMatrixZero(GpuType matrix)
    {
        UsesSimdGroups = true;
        return Call(WaveMatrixFill(matrix), matrix.Element!.MslName == "half" ? "0.0h" : "0.0f");
    }

    /// <summary>
    /// Translates a call of a member of <c>WaveMatrix8x8</c> or <c>WaveMatrixHalf8x8</c>, or returns
    /// null when <paramref name="definition"/> is not one.
    /// </summary>
    private Expr? WaveMatrixCall(IInvocationOperation operation, IMethodSymbol definition)
    {
        if (!TypeResolver.IsWaveMatrixType(definition.ContainingType))
        {
            return null;
        }

        string member = definition.ContainingType.Name + "." + definition.Name;

        if (_function.IsGlobalInitializer)
        {
            return NotConstant(operation, $"'{member}' is an operation of a SIMD group, which only has a value while the shader runs");
        }

        Location location = operation.Syntax.GetLocation();

        // The type the member belongs to is the matrix a static member makes and an instance member works on.
        if (ResolveType(definition.ContainingType, location) is not { Kind: GpuTypeKind.WaveMatrix } matrix ||
            ResolveType(definition.ReturnType, location) is null)
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

        CheckCallSite(operation, definition, callerFile, callerLine, member);

        // The lanes of a SIMD group carry the operation out together: on the CPU they have to meet.
        UsesSimdGroups = true;
        string element = matrix.Element!.MslName;

        switch (definition.Name)
        {
            case "Filled" when definition.IsStatic && arguments.Count == 1:
                return Call(WaveMatrixFill(matrix), Expression(arguments[0].Value).Text);

            case "Diagonal" when definition.IsStatic && arguments.Count == 1:
                // The converting constructor of simdgroup_matrix: the value on the diagonal.
                return Call(matrix.MslName, Expression(arguments[0].Value).Text);

            case "Load" when definition.IsStatic && arguments.Count == 4:
            {
                if (WaveMatrixPointer(operation, member, arguments[0].Value, arguments[1].Value, isStore: false) is not { } pointer)
                {
                    return Expr.Error;
                }

                UsesPrelude = true;
                return Call(WaveMatrixLoad, pointer, WaveMatrixStride(arguments[2].Value), Expression(arguments[3].Value).Text);
            }

            case "Store" when !definition.IsStatic && arguments.Count == 4 && operation.Instance is { } instance:
            {
                if (WaveMatrixPointer(operation, member, arguments[0].Value, arguments[1].Value, isStore: true) is not { } pointer)
                {
                    return Expr.Error;
                }

                // The origin Metal also takes is part of the offset here: `source + offset` with an
                // origin of (0, 0) names the same elements, transposed or not (measured).
                return Call("metal::simdgroup_store", Expression(instance).Text, pointer, WaveMatrixStride(arguments[2].Value), "ulong2(0)", Expression(arguments[3].Value).Text);
            }

            case "Multiply" when definition.IsStatic && arguments.Count == 2:
                UsesPrelude = true;
                return Call(WaveMatrixMultiply + "<" + element + ">", Expression(arguments[0].Value).Text, Expression(arguments[1].Value).Text);

            case "MultiplyAccumulate" when definition.IsStatic && arguments.Count == 3:
                UsesPrelude = true;

                return Call(
                    WaveMatrixMultiplyAccumulate + "<" + element + ">",
                    Expression(arguments[0].Value).Text,
                    Expression(arguments[1].Value).Text,
                    Expression(arguments[2].Value).Text);

            default:
                // A matrix type from another version of SharpCompute, whose members this generator does not know.
                return UnsupportedCall(operation, "this generator does not know this member of the wave matrix; use matching versions of SharpCompute and its source generator");
        }
    }

    /// <summary>Metal takes the elements per row as a <c>ulong</c>; the C# parameter is an <c>int</c>.</summary>
    private string WaveMatrixStride(IOperation stride) => Call("ulong", Expression(stride).Text).Text;

    /// <summary>
    /// The pointer a load or store takes: the buffer or <c>[GroupShared]</c> array plus the offset,
    /// its terms added one by one (see the remarks of this file). Null after reporting SCG0049 when
    /// <paramref name="memory"/> is neither.
    /// </summary>
    private string? WaveMatrixPointer(IInvocationOperation call, string member, IOperation memory, IOperation offset, bool isStore)
    {
        memory = WithoutImplicitConversions(memory)!;
        string name;

        if (GroupSharedReference(memory) is { } sharedField)
        {
            // An array that cannot be used at all was reported where it is declared, or is now.
            if (FindGroupShared(sharedField, memory) is not { } groupShared)
            {
                return null;
            }

            _function.Inputs.AddGroupShared(groupShared);
            name = groupShared.MslName;
        }
        else if (TypeResolver.IsBufferType(memory.Type, out _) && memory is IFieldReferenceOperation or IParameterReferenceOperation)
        {
            // A captured buffer, or a buffer parameter of a helper: a pointer under its own name.
            Expr buffer = Expression(memory);

            if (ReferencesFailedSymbol(memory))
            {
                return null;
            }

            name = buffer.Operand(Expr.Primary);
        }
        else
        {
            if (ContainsInvalid(memory))
            {
                SawInvalidCode = true;
                return null;
            }

            _diagnostics.Report(
                DiagnosticDescriptors.InvalidWaveMatrixMemory,
                memory.Syntax.GetLocation(),
                member,
                memory.Syntax.ToString(),
                memory.Type is IArrayTypeSymbol
                    ? $"it is not a [GroupShared] array of the shader; a wave matrix is {(isStore ? "stored to" : "loaded from")} a captured buffer or a [GroupShared] array"
                    : $"it is not a captured buffer or a [GroupShared] array of the shader, the two kinds of memory a wave matrix is {(isStore ? "stored to" : "loaded from")}");
            return null;
        }

        var pointer = new StringBuilder(name);
        AppendPointerTerms(pointer, offset, negative: false);
        return pointer.ToString();
    }

    /// <summary>
    /// Appends <paramref name="offset"/> to a pointer expression as <c> + term</c> or <c> - term</c>,
    /// once per term of a sum or difference of ints.
    /// </summary>
    private void AppendPointerTerms(StringBuilder pointer, IOperation offset, bool negative)
    {
        if (offset is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Subtract, OperatorMethod: null, IsLifted: false } sum &&
            sum.Type?.SpecialType == SpecialType.System_Int32)
        {
            AppendPointerTerms(pointer, sum.LeftOperand, negative);
            AppendPointerTerms(pointer, sum.RightOperand, negative ^ (sum.OperatorKind == BinaryOperatorKind.Subtract));
            return;
        }

        // A term is written as the right operand of the + or - in front of it, with the parentheses
        // every other sum gets: `a + (row * n) + k`.
        pointer.Append(negative ? " - " : " + ").Append(BinaryOperand(Expression(offset), Expr.Additive, isRight: true));
    }

    /// <summary>
    /// Explains a <c>Load</c> or <c>Store</c> that C# could not bind because the buffer or array has
    /// another element type than the matrix: <c>WaveMatrix8x8.Load(counts, 0, 8)</c> on a
    /// <c>ReadOnlyBuffer&lt;int&gt;</c>. The compiler's own error (CS1503, "cannot convert from
    /// 'ReadOnlyBuffer&lt;int&gt;' to 'ReadOnlyBuffer&lt;float&gt;'") does not say that a matrix has one
    /// element type and what the other one is, so SCG0049 is reported next to it. Nothing is reported
    /// for any other invalid call.
    /// </summary>
    private void ExplainUnboundWaveMatrixCall(IInvalidOperation operation)
    {
        if (operation.Syntax is not InvocationExpressionSyntax { ArgumentList.Arguments: { Count: > 0 } arguments } invocation)
        {
            return;
        }

        SemanticModel model = GetSemanticModel(invocation.SyntaxTree);
        SymbolInfo info = model.GetSymbolInfo(invocation, _cancellationToken);

        if (info.Symbol is not null ||
            info.CandidateSymbols.Length == 0 ||
            !info.CandidateSymbols.All(static c => c is IMethodSymbol { Name: "Load" or "Store" } candidate && TypeResolver.IsWaveMatrixType(candidate.ContainingType)))
        {
            return;
        }

        var method = (IMethodSymbol)info.CandidateSymbols[0];
        bool isStore = method.Name == "Store";

        // The memory is the first parameter of every Load and Store: the first positional argument,
        // or the one named after it.
        string memoryName = isStore ? "destination" : "source";
        ArgumentSyntax memory = arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == memoryName) ?? arguments[0];

        if (memory.NameColon is not null && memory.NameColon.Name.Identifier.ValueText != memoryName)
        {
            return;
        }

        ITypeSymbol? type = model.GetTypeInfo(memory.Expression, _cancellationToken).Type;
        bool isReadOnly = false;

        ITypeSymbol? elementSymbol = type switch
        {
            IArrayTypeSymbol { Rank: 1 } array => array.ElementType,
            INamedTypeSymbol named when TypeResolver.IsBufferType(named, out isReadOnly) => named.TypeArguments[0],
            _ => null,
        };

        if (elementSymbol is null || elementSymbol.TypeKind == TypeKind.Error || _types.Resolve(method.ContainingType) is not { Kind: GpuTypeKind.WaveMatrix } matrix)
        {
            return;
        }

        string own = matrix.Element!.MslName == "half" ? "Half" : "float";
        string other = own == "float" ? "WaveMatrixHalf8x8 for Half" : "WaveMatrix8x8 for float";
        string element = TypeResolver.Display(elementSymbol);
        string reason;

        if (_types.Resolve(elementSymbol) is { Kind: GpuTypeKind.Scalar } resolved && resolved.MslName == matrix.Element.MslName)
        {
            // The element type is right, so what C# rejected is a store to a buffer a shader cannot write.
            if (!(isStore && isReadOnly))
            {
                return;
            }

            reason = $"it is a ReadOnlyBuffer<{element}>, which a shader cannot write; store to a ReadWriteBuffer<{element}> or a [GroupShared] array";
        }
        else
        {
            reason = $"its elements are '{element}', and a {method.ContainingType.Name} is {(isStore ? "stored to" : "loaded from")} '{own}' elements only " +
                $"(use {other} elements; Metal has no matrix of any other element type, and converts none while it loads or stores)";
        }

        _diagnostics.Report(
            DiagnosticDescriptors.InvalidWaveMatrixMemory,
            memory.Expression.GetLocation(),
            method.ContainingType.Name + "." + method.Name,
            memory.Expression.ToString(),
            reason);
    }
}
