using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// Integer division and remainder, emitted through the helpers of <c>MslPrelude.Integers</c>
/// to work around two miscompiles in Apple's GPU compiler. In both, the MSL and the AIR it compiles
/// to are correct; the wrong result comes from the compiler that turns AIR into GPU code.
/// </summary>
/// <remarks>
/// <para>
/// A dropped guard. A division by zero throws in C#, so a shader guards one whose divisor may be
/// zero: <c>t == 0 ? 0 : x % t</c>, or <c>if (t != 0) r = x % t;</c>. The GPU compiler computes the
/// division ahead of its guard and then takes "t is not zero" as known, which it uses to simplify
/// how <c>t</c> was computed: with <c>t = l == C ? 0 : Math.Abs(l)</c> it drops the zero arm, so for
/// <c>l == C</c> both <c>t</c> and the result are wrong (<c>x % Abs(l)</c> instead of 0). Guards
/// around a chain of divisions (<c>t = d == 0 ? 0 : a / d; t == 0 ? 0 : x % t</c>) were dropped in
/// the same way. So every integer <c>/</c> and <c>%</c> whose divisor is not a nonzero constant
/// divides by <c>__ms_nonzero(t)</c>: <c>t</c>, or 1 when <c>t</c> is zero, which leaves the
/// compiler nothing to assume. A division by zero has no C# result to keep: the GPU result was
/// unspecified before and is the dividend (or 0 for <c>%</c>) now. The helper sets the low bit of
/// <c>t</c> when <c>reverse_bits(t) == 0</c>. The test must be one the compilers cannot fold to false
/// under the guard, or the divisor is <c>t</c> again where it is guarded: the front end folds a select
/// on <c>t == 0</c>, and the GPU compiler folded <c>clz(t) &gt;&gt; 5</c> and the top bit of
/// <c>(t - 1) &amp; ~t</c> in some shaders (random shaders, and one specialized by a function
/// constant, with the lanes of a SIMD group dividing by different values). It costs a bit reversal,
/// a compare and an or. A divisor the front end can evaluate, after inlining and folding, is left
/// as it is (<c>__builtin_constant_p</c>, lane by lane for a vector): the reversal would hide the
/// constant from the front end, and the GPU compiler folds a division of two constants wrongly for
/// most even divisors, where the front end folds it right or turns it into a multiplication. Measured on an M4 Pro (median of 14 runs of 4M threads): in a loop that does
/// nothing but divide, 9% of a 32-bit unsigned division, 13% of a signed one and 1% of a 64-bit one;
/// in a 3 x 3 box filter that finds its pixel with <c>i / width</c> and <c>i % width</c>, 1%.
/// </para>
/// <para>
/// A constant dividend near 2^32. A 32-bit unsigned <c>/</c> or <c>%</c> of a constant of
/// 2^32 - 128 or more (one that rounds to 2^32 as a float) by a variable is wrong for some divisors:
/// <c>4294967295u / x</c> is 3579139454 for <c>x = 3</c>. A <c>long</c> division the optimizer can
/// narrow to 32 bits (<c>4294967295L % y</c> with <c>y</c> a widened <c>uint</c>, and the negated
/// constant) goes the same way, as do the lanes of a <c>uint</c> vector. A dividend whose magnitude
/// is in that range and that the generator can evaluate (a C# constant, a <c>static readonly</c>
/// value, a constant vector, or a local with a constant initializer that is never written) is divided
/// by <c>__ms_div_near_max(c, x)</c>, which divides <c>c - x</c> instead: that is not a constant. A
/// dividend that only becomes a constant once the compiler has inlined a helper (a parameter) is not
/// recognized.
/// </para>
/// <para>
/// A program-scope initializer keeps the native operators: it may not call a function, and its
/// operands are constants, which the front end folds before the GPU compiler sees them.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    /// <summary>The smallest magnitude of a dividend that <c>__ms_div_near_max</c> divides: 2^32 - 128.</summary>
    private const ulong NearMaxDividend = 0xFFFFFF80UL;

    /// <summary>
    /// The MSL of an integer <c>a / b</c> or <c>a % b</c> through the helpers of
    /// <c>MslPrelude.Integers</c>, or null when <paramref name="operation"/> is none or keeps
    /// the native operator (a nonzero constant divisor, a program-scope initializer).
    /// </summary>
    private Expr? IntegerDivision(IBinaryOperation operation)
    {
        if (_function.IsGlobalInitializer ||
            operation.OperatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) ||
            !IsNativeArithmetic(operation.OperatorMethod) ||
            _types.Resolve(operation.Type) is not { } type || !IsIntegerOrIntegerVector(type) ||
            IsNonZeroConstant(operation.RightOperand))
        {
            return null;
        }

        bool divide = operation.OperatorKind == BinaryOperatorKind.Divide;

        if (IsNearMaxConstant(operation.LeftOperand, type))
        {
            // The type of the operation names the overload: `4294967295u / v` on a uint vector divides
            // the splat of the constant.
            string helper = divide ? MslNames.DivideNearMax : MslNames.RemainderNearMax;
            UseIntegerHelper(helper, type.MslName);
            return Call(helper, OperandOfType(operation.LeftOperand, type), OperandOfType(operation.RightOperand, type));
        }

        if (_types.Resolve(operation.RightOperand.Type) is not { } divisor)
        {
            return null;
        }

        Expr left = Expression(operation.LeftOperand);
        Expr right = Expression(operation.RightOperand);
        string text = BinaryOperand(left, Expr.Multiplicative, isRight: false) + (divide ? " / " : " % ");

        if (IsNonZeroLiteral(right.Text))
        {
            // A constant only the generator knows, such as the length of a local array.
            return new Expr(text + BinaryOperand(right, Expr.Multiplicative, isRight: true), Expr.Multiplicative);
        }

        // A constant dividend the generator can see divides by __ms_nonzero_c, whose barrier the front
        // end folds when the divisor becomes a constant late (an unrolled loop): the GPU compiler
        // folds a constant divided by a constant it gets through __ms_nonzero's reverse_bits wrongly.
        string nonZero = IsConstantDividend(operation.LeftOperand) ? MslNames.NonZeroOfConstant : MslNames.NonZero;
        UseIntegerHelper(nonZero, divisor.MslName);
        return new Expr(text + Call(nonZero, right.Text).Text, Expr.Multiplicative);
    }

    /// <summary>
    /// True when <paramref name="dividend"/> is a constant the generator can see: a C# constant, one
    /// the folder evaluates (a <c>static readonly</c> value, a constant vector), or a never-written
    /// local initialized with one.
    /// </summary>
    private bool IsConstantDividend(IOperation dividend) =>
        dividend.ConstantValue.HasValue || Folder.TryEvaluate(dividend, out _, out _) || ConstantLocalValue(dividend) is not null;

    /// <summary>
    /// Records that the shader calls the overload of <c>MslPrelude.Integers</c>' <paramref name="helper"/>
    /// for <paramref name="type"/> (an MSL scalar or vector name), and the overloads that one calls.
    /// </summary>
    private void UseIntegerHelper(string helper, string type)
    {
        _integerHelpers.Add(helper + "(" + type);
        string scalar = type.TrimEnd('2', '3', '4');

        if (helper is MslNames.NonZero or MslNames.NonZeroOfConstant && scalar is "char" or "uchar" or "short" or "ushort")
        {
            // The 8- and 16-bit ones divide as int, as MSL promotes them: `__ms_nonzero(int3(x))`.
            UseIntegerHelper(helper, "int" + type.Substring(scalar.Length));
        }
        else if (helper == MslNames.NonZero && scalar != type)
        {
            // A vector one calls the scalar one for each lane.
            _integerHelpers.Add(helper + "(" + scalar);
        }
        else if (helper is MslNames.DivideNearMax or MslNames.RemainderNearMax)
        {
            // The long ones divide the magnitudes as ulong; each divides by a nonzero divisor.
            string unsigned = type == "long" ? "ulong" : type;
            _integerHelpers.Add(helper + "(" + unsigned);
            UseIntegerHelper(MslNames.NonZeroOfConstant, unsigned);
        }
    }

    /// <summary>
    /// The divisor of an integer <c>t /= v</c> or <c>t %= v</c>, <c>__ms_nonzero(v)</c> (or <c>v</c> for a
    /// nonzero literal), or null when
    /// <paramref name="operation"/> is none or keeps the native operator. The target is named once,
    /// as before.
    /// </summary>
    private string? CompoundIntegerDivisor(ICompoundAssignmentOperation operation)
    {
        if (_function.IsGlobalInitializer ||
            operation.OperatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) ||
            !IsNativeArithmetic(operation.OperatorMethod) ||
            _types.Resolve(operation.Target.Type) is not { } target || !IsIntegerOrIntegerVector(target) ||
            _types.Resolve(operation.Value.Type) is not { } value || !IsIntegerOrIntegerVector(value) ||
            IsNonZeroConstant(operation.Value))
        {
            return null;
        }

        string divisor = Expression(operation.Value).Text;

        if (IsNonZeroLiteral(divisor))
        {
            return divisor;
        }

        UseIntegerHelper(MslNames.NonZero, value.MslName);
        return Call(MslNames.NonZero, divisor).Text;
    }

    /// <summary><paramref name="operand"/> as <paramref name="type"/>: converted when its own type is another one.</summary>
    private string OperandOfType(IOperation operand, GpuType type)
    {
        string text = Expression(operand).Text;
        return _types.Resolve(operand.Type)?.MslName == type.MslName ? text : Call(type.MslName, text).Text;
    }

    private static bool IsIntegerOrIntegerVector(GpuType type) =>
        type is { Kind: GpuTypeKind.Scalar or GpuTypeKind.Vector, IsFloating: false, IsBoolean: false } && type.MslName != "bool";

    /// <summary>True for the MSL of a nonzero integer literal: <c>9</c>, <c>64u</c>, <c>(-3)</c>.</summary>
    private static bool IsNonZeroLiteral(string text)
    {
        string digits = text.Trim('(', ')').TrimStart('-').TrimEnd('u', 'U', 'l', 'L');
        return digits.Length > 0 && digits.All(char.IsDigit) && digits.Any(static c => c != '0');
    }

    /// <summary>True when <paramref name="divisor"/> is a compile-time constant with no zero component.</summary>
    private bool IsNonZeroConstant(IOperation divisor)
    {
        if (divisor.ConstantValue is { HasValue: true, Value: { } constant })
        {
            return Magnitude(constant) is not 0;
        }

        return Folder.TryEvaluate(divisor, out FoldedValue? folded, out _) &&
            folded is { Kind: ScalarKind.Int or ScalarKind.UInt } &&
            folded.Components.All(static component => component != 0);
    }

    /// <summary>
    /// True when <paramref name="dividend"/> is a compile-time constant (any component of a vector)
    /// whose magnitude is at least 2^32 - 128 and below 2^32, in an operation of a type the GPU
    /// compiler divides wrongly by it: <c>uint</c>, <c>long</c>, <c>ulong</c> and the <c>uint</c> vectors.
    /// </summary>
    private bool IsNearMaxConstant(IOperation dividend, GpuType type)
    {
        bool vector = type.Kind == GpuTypeKind.Vector;

        if (!(vector ? type.MslName is "uint2" or "uint3" or "uint4" : type.MslName is "uint" or "long" or "ulong"))
        {
            return false;
        }

        if (dividend.ConstantValue is { HasValue: true, Value: { } constant })
        {
            return Magnitude(constant) is >= NearMaxDividend and <= uint.MaxValue;
        }

        if (ConstantLocalValue(dividend) is { } local)
        {
            // `uint m = uint.MaxValue; ... m / x`: the compiler propagates the local's value into the
            // division as it does a constant written there.
            return IsNearMaxConstant(local, type);
        }

        // A static readonly value or a constant vector, which only the generator's folder evaluates
        // (32-bit components).
        return Folder.TryEvaluate(dividend, out FoldedValue? folded, out _) &&
            folded is { Kind: ScalarKind.UInt } &&
            folded.Components.Any(static component => component >= NearMaxDividend);
    }

    /// <summary>
    /// The initializer of the local that <paramref name="operand"/> reads (through conversions), when
    /// it is a constant (to C# or to the generator's folder) and the local is never written after its declaration: no assignment,
    /// compound assignment, <c>++</c>/<c>--</c>, <c>ref</c> or <c>out</c> argument, <c>ref</c> local
    /// or deconstruction targets it. Null otherwise.
    /// </summary>
    private IOperation? ConstantLocalValue(IOperation operand)
    {
        while (operand is IConversionOperation { OperatorMethod: null } conversion)
        {
            operand = conversion.Operand;
        }

        if (operand is not ILocalReferenceOperation { Local: { IsRef: false } local } ||
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(_cancellationToken) is not VariableDeclaratorSyntax { Initializer: not null } declarator ||
            GetSemanticModel(declarator.SyntaxTree).GetOperation(declarator, _cancellationToken) is not IVariableDeclaratorOperation { Initializer.Value: { } value })
        {
            return null;
        }

        if (!(value.ConstantValue.HasValue || Folder.TryEvaluate(value, out _, out _)))
        {
            // Another such local (`uint a = uint.MaxValue; uint b = a;`), or nothing.
            if (ConstantLocalValue(value) is not { } chained)
            {
                return null;
            }

            value = chained;
        }

        // The block the local is declared in is its whole scope.
        SyntaxNode? scope = declarator.FirstAncestorOrSelf<BlockSyntax>() ?? (SyntaxNode?)declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (scope is null || GetSemanticModel(scope.SyntaxTree).GetOperation(scope, _cancellationToken) is not { } body)
        {
            return null;
        }

        foreach (IOperation reference in body.Descendants())
        {
            if (reference is ILocalReferenceOperation other && SymbolEqualityComparer.Default.Equals(other.Local, local) && IsWrite(other))
            {
                return null;
            }
        }

        return value;
    }

    /// <summary>True when <paramref name="reference"/> is written to or taken by reference.</summary>
    private static bool IsWrite(ILocalReferenceOperation reference)
    {
        IOperation child = reference;
        IOperation? parent = reference.Parent;

        // A tuple element of a deconstruction target is written like the target itself, and so is a
        // field, property or swizzle of the local (`v.X = x`, `v.X++`, `F(ref v.X)`).
        while (parent is ITupleOperation ||
               parent is IFieldReferenceOperation field && field.Instance == child ||
               parent is IPropertyReferenceOperation property && property.Instance == child)
        {
            child = parent;
            parent = parent.Parent;
        }

        if (parent is IInvocationOperation invocation && invocation.Instance == child)
        {
            // A method of the local's struct may change it.
            return true;
        }

        return parent switch
        {
            ISimpleAssignmentOperation assignment => assignment.Target == child || assignment.IsRef,
            ICompoundAssignmentOperation compound => compound.Target == child,
            ICoalesceAssignmentOperation coalesce => coalesce.Target == child,
            IDeconstructionAssignmentOperation deconstruction => deconstruction.Target == child,
            IIncrementOrDecrementOperation => true,
            IArgumentOperation argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out or RefKind.RefReadOnlyParameter,
            IVariableInitializerOperation initializer => initializer.Parent is IVariableDeclaratorOperation { Symbol.IsRef: true },
            IAddressOfOperation => true,
            _ => false,
        };
    }

    /// <summary>The magnitude of an integer constant, or null for any other constant.</summary>
    private static ulong? Magnitude(object constant)
    {
        switch (constant)
        {
            case byte or ushort or uint or ulong or char:
                return Convert.ToUInt64(constant, CultureInfo.InvariantCulture);
            case sbyte or short or int or long:
                long value = Convert.ToInt64(constant, CultureInfo.InvariantCulture);
                return value < 0 ? unchecked((ulong)(-(value + 1)) + 1) : (ulong)value;
            default:
                return null;
        }
    }
}
