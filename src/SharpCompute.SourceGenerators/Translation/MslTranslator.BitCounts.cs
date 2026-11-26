using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators;

/// <summary>
/// The bit counts that are emitted as calls of <c>MslPrelude.Integers</c>' <c>__ms_popcount</c> and
/// <c>__ms_clz</c> instead of <c>metal::popcount</c> and <c>metal::clz</c>, to work around a
/// miscompile in Apple's GPU compiler (the MSL, and the AIR it compiles to, are correct).
/// </summary>
/// <remarks>
/// <para>
/// The bug. The compiler miscounts a value whose upper bits it treats as an extension of a narrower
/// one. The popcount or clz of a 32-bit value that holds a sign-extended 16-bit one counts it as if
/// it were zero-extended: <c>Gpu.LeadingZeroCount((uint)(short)i)</c> is 16 for a negative short,
/// and so is the clz of the remainder of two <c>sbyte</c>s widened to <c>int</c>, which the optimizer
/// computes in 16 bits. No 16-bit type need be involved: <c>(x &lt;&lt; 16) &gt;&gt; 16</c> is a
/// sign extension too. The 64-bit popcount counts its low half the same way. And the popcount of a
/// <c>short</c> or <c>ushort</c> that the optimizer computes in a wider register, such as
/// <c>b ? s : t</c>, counts the bits above its 16: <c>Gpu.CountBits</c> of the short -2 is 31. The
/// 64-bit counts go wrong in the same way: the clz of <c>(long)s</c> is 16 for a negative short.
/// </para>
/// <para>
/// The workaround. Which values end up sign-extended is up to the optimizer, so every count goes
/// through the helpers: a popcount (of any width, scalar or vector) counts the bits of
/// <c>reverse_bits(x)</c>, which has the same number of them, which the compiler computes in full,
/// and which shifts out whatever is above the bits of an 8- or 16-bit value (a popcount of the value
/// zero-extended to 32 bits is not enough: the compiler drops that extension of a short computed by
/// a branch in a wider register); and a 32- or 64-bit clz is
/// selected on the sign, as the clz of a negative value is 0 and that of any other is right. A
/// rotate by half the width would also make the compiler see every bit, but it folds
/// <c>popcount(rotate(x))</c> back to <c>popcount(x)</c>. The 8- and 16-bit clz, ctz and the bit reversals were not affected. Measured on
/// an M4 Pro, in a loop that does nothing but count (four instructions an iteration), the reversal
/// adds 27% to a popcount and the select 9% to a clz: one instruction each.
/// </para>
/// </remarks>
internal sealed partial class MslTranslator
{
    /// <summary>
    /// The MSL function a bit-count intrinsic of <paramref name="method"/> is emitted as:
    /// <paramref name="intrinsic"/>, or the helper of <c>MslPrelude.Integers</c> for <c>metal::popcount</c> and for the
    /// 32- and 64-bit <c>metal::clz</c> (see the remarks on this class).
    /// </summary>
    private string BitCountFunction(string intrinsic, IMethodSymbol method)
    {
        if (method.Parameters.Length != 1 || _types.Resolve(method.Parameters[0].Type) is not { } type)
        {
            return intrinsic;
        }

        string? helper = intrinsic switch
        {
            "metal::popcount" when IsBitCountedType(type) => MslNames.PopCount,
            "metal::clz" when type.MslName is "int" or "uint" or "long" or "ulong" or "int2" or "int3" or "int4" or "uint2" or "uint3" or "uint4" => MslNames.LeadingZeroCount,
            _ => null,
        };

        if (helper is null)
        {
            return intrinsic;
        }

        UseIntegerHelper(helper, type.MslName);
        return helper;
    }

    /// <summary>The types <c>__ms_popcount</c> has an overload for: every integer scalar, and the <c>int</c> and <c>uint</c> vectors.</summary>
    private static bool IsBitCountedType(GpuType type) =>
        type.IsIntegerScalar && !type.IsBoolean && type.MslName != "bool" ||
        type.MslName is "int2" or "int3" or "int4" or "uint2" or "uint3" or "uint4";
}
