using System;
using System.Linq;

namespace SharpCompute.Tests;

/// <summary>
/// The comparison the GPU tests use (<see cref="FloatBits"/>): every NaN equals every NaN, and
/// nothing else is loosened. A number one ULP away, the other zero, and an infinity against a NaN
/// all still differ.
/// </summary>
public class FloatBitsTests
{
    private static readonly uint[] FloatNaNs = [0x7FC00000u, 0xFFC00000u, 0x7F800001u, 0xFFFFFFFFu, 0x7FA00001u, 0xFF800001u];

    private static readonly ushort[] HalfNaNs = [0x7E00, 0xFE00, 0x7C01, 0xFFFF, 0x7D01, 0xFC01];

    /// <summary>Every kind of float that is not a NaN, with its neighbours.</summary>
    private static readonly uint[] FloatNumbers =
    [
        0x00000000u, 0x80000000u, 0x00000001u, 0x80000001u, 0x007FFFFFu, 0x00800000u, 0x3F800000u, 0xBF800000u,
        0x3F7FFFFFu, 0x3F800001u, 0x7F7FFFFFu, 0xFF7FFFFFu, 0x7F800000u, 0xFF800000u, 0x12345678u, 0xC0490FDBu,
    ];

    private static readonly ushort[] HalfNumbers = [0x0000, 0x8000, 0x0001, 0x8001, 0x03FF, 0x0400, 0x3C00, 0xBC00, 0x3BFF, 0x3C01, 0x7BFF, 0x7C00, 0xFC00];

    [Fact]
    public void EveryNaN_EqualsEveryNaN_WhateverItsSignAndPayload()
    {
        Assert.All(FloatNaNs, a => Assert.All(FloatNaNs, b => Assert.True(FloatBits.SameBits(a, b), $"0x{a:X8} vs 0x{b:X8}")));
        Assert.All(HalfNaNs, a => Assert.All(HalfNaNs, b => Assert.True(FloatBits.SameBits(a, b), $"0x{a:X4} vs 0x{b:X4}")));
        Assert.True(FloatBits.Same(float.NaN, BitConverter.UInt32BitsToSingle(FloatBits.CanonicalNaN)));
        Assert.True(FloatBits.Same(Half.NaN, BitConverter.UInt16BitsToHalf(FloatBits.CanonicalHalfNaN)));
        Assert.True(FloatBits.SameBits(float.NaN, FloatBits.CanonicalNaN));

        // .NET's NaN has its sign bit set; the canonical key is the GPU's positive one.
        Assert.Equal(0xFFC00000u, BitConverter.SingleToUInt32Bits(float.NaN));
        Assert.Equal(FloatBits.CanonicalNaN, FloatBits.Comparable(float.NaN));
        Assert.Equal(FloatBits.CanonicalHalfNaN, FloatBits.Comparable(Half.NaN));
        Assert.Equal(FloatBits.Comparable([float.NaN, -0f, 1f]), FloatBits.Comparable([BitConverter.UInt32BitsToSingle(0x7FA00001u), -0f, 1f]));
    }

    [Fact]
    public void Numbers_StayBitExact_ZerosAndOneUlpApartIncluded()
    {
        foreach (uint a in FloatNumbers)
        {
            Assert.True(FloatBits.SameBits(a, a));
            Assert.Equal(a, FloatBits.Comparable(BitConverter.UInt32BitsToSingle(a)));
            Assert.All(FloatNaNs, nan => Assert.False(FloatBits.SameBits(a, nan), $"0x{a:X8} vs NaN 0x{nan:X8}"));
            Assert.All(FloatNumbers.Where(b => b != a), b => Assert.False(FloatBits.SameBits(a, b), $"0x{a:X8} vs 0x{b:X8}"));

            // One ULP up and down (the last bit, which a NaN-insensitive comparison must not lose).
            Assert.False(FloatBits.SameBits(a, a ^ 1u));
            Assert.False(FloatBits.Same(BitConverter.UInt32BitsToSingle(a), BitConverter.UInt32BitsToSingle(a ^ 1u)));
        }

        foreach (ushort a in HalfNumbers)
        {
            Assert.True(FloatBits.SameBits(a, a));
            Assert.Equal(a, FloatBits.Comparable(BitConverter.UInt16BitsToHalf(a)));
            Assert.All(HalfNaNs, nan => Assert.False(FloatBits.SameBits(a, nan), $"0x{a:X4} vs NaN 0x{nan:X4}"));
            Assert.False(FloatBits.SameBits(a, (ushort)(a ^ 1)));
            Assert.False(FloatBits.Same(BitConverter.UInt16BitsToHalf(a), BitConverter.UInt16BitsToHalf((ushort)(a ^ 1))));
        }

        Assert.False(FloatBits.Same(0f, -0f));
        Assert.False(FloatBits.Same((Half)0f, Half.NegativeZero));
        Assert.False(FloatBits.Same(float.PositiveInfinity, float.NaN));
        Assert.False(FloatBits.SameBits(0f, 0x80000000u));
        Assert.NotEqual(FloatBits.Comparable([0f, 1f]), FloatBits.Comparable([-0f, 1f]));
        Assert.NotEqual(FloatBits.Comparable([1f]), FloatBits.Comparable([MathF.BitIncrement(1f)]));
    }
}
