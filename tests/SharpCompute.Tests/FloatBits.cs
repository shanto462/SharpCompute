using System;

namespace SharpCompute.Tests;

/// <summary>
/// Bit-exact comparison of floats and halves in which every NaN equals every NaN. An Apple GPU writes
/// a positive canonical NaN (<c>0x7FC00000</c>, for a half <c>0x7E00</c>) for a NaN it computes,
/// where .NET's <see cref="float.NaN"/> has its sign bit set and a NaN operand can keep its payload;
/// IEEE leaves both open. Everything else stays exact: <c>-0</c> differs from <c>+0</c>, and two
/// numbers one ULP apart differ. Tests that compare GPU results with C# go through this, either with
/// <see cref="Same(float, float)"/> and <see cref="SameBits(uint, uint)"/> and their overloads or by comparing <see cref="Comparable(float)"/>
/// keys, which map every NaN to the canonical one so that arrays of them compare with
/// <c>Assert.Equal</c>.
/// </summary>
/// <remarks>
/// Copies and raw storage (buffer copies, texture bytes) and GPU-against-GPU comparisons stay byte
/// for byte: nothing computes a NaN there, so every bit has to survive.
/// </remarks>
internal static class FloatBits
{
    /// <summary>The NaN an Apple GPU writes for a float.</summary>
    public const uint CanonicalNaN = 0x7FC00000u;

    /// <summary>The NaN an Apple GPU writes for a half.</summary>
    public const ushort CanonicalHalfNaN = 0x7E00;

    public static bool IsNaN(uint bits) => (bits & 0x7FFFFFFFu) > 0x7F800000u;

    public static bool IsNaN(ushort bits) => (bits & 0x7FFF) > 0x7C00;

    // The overloads on bits are named apart from those on values: bits passed to Same would convert
    // to float implicitly and be compared as numbers. These stop that at compile time.
    private const string BitsGoToSameBits = "These are bits: compare them with SameBits.";

    [Obsolete(BitsGoToSameBits, error: true)]
    public static bool Same(uint expected, uint actual) => throw new NotSupportedException(BitsGoToSameBits);

    [Obsolete(BitsGoToSameBits, error: true)]
    public static bool Same(ushort expected, ushort actual) => throw new NotSupportedException(BitsGoToSameBits);

    [Obsolete(BitsGoToSameBits, error: true)]
    public static bool Same(int expected, int actual) => throw new NotSupportedException(BitsGoToSameBits);

    /// <summary>The same float bits, or both NaN.</summary>
    public static bool SameBits(uint expected, uint actual) => expected == actual || (IsNaN(expected) && IsNaN(actual));

    /// <summary>The same half bits, or both NaN.</summary>
    public static bool SameBits(ushort expected, ushort actual) => expected == actual || (IsNaN(expected) && IsNaN(actual));

    /// <summary>The bits of <paramref name="expected"/> are <paramref name="actual"/>, or both are NaN.</summary>
    public static bool SameBits(float expected, uint actual) => SameBits(BitConverter.SingleToUInt32Bits(expected), actual);

    /// <summary>The same bits, or both NaN.</summary>
    public static bool Same(float expected, float actual) => SameBits(BitConverter.SingleToUInt32Bits(expected), BitConverter.SingleToUInt32Bits(actual));

    /// <summary>The same bits, or both NaN.</summary>
    public static bool Same(Half expected, Half actual) => SameBits(BitConverter.HalfToUInt16Bits(expected), BitConverter.HalfToUInt16Bits(actual));

    /// <summary>The bits of <paramref name="value"/>, with every NaN replaced by <see cref="CanonicalNaN"/>.</summary>
    public static uint Comparable(float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        return IsNaN(bits) ? CanonicalNaN : bits;
    }

    /// <summary>The bits of <paramref name="value"/>, with every NaN replaced by <see cref="CanonicalHalfNaN"/>.</summary>
    public static ushort Comparable(Half value)
    {
        ushort bits = BitConverter.HalfToUInt16Bits(value);
        return IsNaN(bits) ? CanonicalHalfNaN : bits;
    }

    /// <summary><see cref="Comparable(float)"/> of every element.</summary>
    public static uint[] Comparable(ReadOnlySpan<float> values)
    {
        uint[] keys = new uint[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            keys[i] = Comparable(values[i]);
        }

        return keys;
    }

    /// <summary><see cref="Comparable(Half)"/> of every element.</summary>
    public static ushort[] Comparable(ReadOnlySpan<Half> values)
    {
        ushort[] keys = new ushort[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            keys[i] = Comparable(values[i]);
        }

        return keys;
    }
}
