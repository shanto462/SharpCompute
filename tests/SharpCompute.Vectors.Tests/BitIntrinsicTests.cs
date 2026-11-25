using System;
using System.Linq;
using System.Numerics;

namespace SharpCompute.Vectors.Tests;

/// <summary>
/// CPU semantics of the bit, classification and packing intrinsics, including the edge patterns
/// (0, all ones, the sign bit, NaN payloads, signed zeros, infinities, subnormals). The same inputs
/// are compared against the GPU in the runtime tests.
/// </summary>
public class BitIntrinsicTests
{
    private const float NaN = float.NaN;
    private const float Inf = float.PositiveInfinity;
    private const float NegZero = -0f;

    public static readonly uint[] Patterns =
    [
        0u, 1u, 2u, 3u, 0x80000000u, 0xFFFFFFFFu, 0x7FFFFFFFu, 0x00FF00FFu, 0xAAAAAAAAu, 0x55555555u,
        0x12345678u, 0x00010000u, 0x7F800000u, 0xFF800000u, 0x7FC00000u, 0xFFC12345u, 0x00000100u, 0x3F800000u,
    ];

    // ---- bit reinterpretation ---------------------------------------------------------------------

    [Fact]
    public void Reinterpretation_KeepsEveryBit()
    {
        foreach (uint bits in Patterns)
        {
            // NaN payloads, the sign of zero and subnormals all survive both directions.
            Assert.Equal(bits, Gpu.AsUInt(Gpu.AsFloat(bits)));
            Assert.Equal((int)bits, Gpu.AsInt(Gpu.AsFloat((int)bits)));
            Assert.Equal(bits, BitConverter.SingleToUInt32Bits(Gpu.AsFloat(bits)));
            Assert.Equal(Gpu.AsUInt(Gpu.AsFloat(bits)), (uint)Gpu.AsInt(Gpu.AsFloat(bits)));
        }

        Assert.Equal(0x3F800000, Gpu.AsInt(1f));
        Assert.Equal(0x80000000u, Gpu.AsUInt(NegZero));
        Assert.Equal(unchecked((int)0x80000000), Gpu.AsInt(NegZero));
        Assert.Equal(0x7F800000u, Gpu.AsUInt(Inf));
        Assert.Equal(1f, Gpu.AsFloat(0x3F800000));
        Assert.Equal(-2f, Gpu.AsFloat(0xC0000000u));
        Assert.Equal(float.Epsilon, Gpu.AsFloat(1));
        Assert.True(float.IsNaN(Gpu.AsFloat(-1)));
    }

    [Fact]
    public void Reinterpretation_OfVectors_IsComponentwise()
    {
        Assert.Equal(new Int2(0x3F800000, unchecked((int)0xC0000000)), Gpu.AsInt(new Float2(1, -2)));
        Assert.Equal(new Int3(0, int.MinValue, 0x7F800000), Gpu.AsInt(new Float3(0f, NegZero, Inf)));
        Assert.Equal(new Int4(0x3F000000, 0x40000000, 0x40400000, 0x40800000), Gpu.AsInt(new Float4(0.5f, 2, 3, 4)));
        Assert.Equal(new UInt2(0x3F800000u, 0xC0000000u), Gpu.AsUInt(new Float2(1, -2)));
        Assert.Equal(new UInt3(0u, 0x80000000u, 0xFF800000u), Gpu.AsUInt(new Float3(0f, NegZero, -Inf)));
        Assert.Equal(new UInt4(0x3F000000u, 0x40000000u, 0x40400000u, 0x40800000u), Gpu.AsUInt(new Float4(0.5f, 2, 3, 4)));
        FloatAssert.SameVector(new Float2(1, -2), Gpu.AsFloat(new Int2(0x3F800000, unchecked((int)0xC0000000))));
        FloatAssert.SameVector(new Float3(0f, NegZero, Inf), Gpu.AsFloat(new UInt3(0u, 0x80000000u, 0x7F800000u)));
        FloatAssert.SameVector(new Float4(0.5f, 2, 3, 4), Gpu.AsFloat(new Int4(0x3F000000, 0x40000000, 0x40400000, 0x40800000)));
        FloatAssert.SameVector(new Float4(0.5f, 2, 3, 4), Gpu.AsFloat(new UInt4(0x3F000000u, 0x40000000u, 0x40400000u, 0x40800000u)));
        FloatAssert.SameVector(new Float3(1, 2, 3), Gpu.AsFloat(Gpu.AsInt(new Float3(1, 2, 3))));
        FloatAssert.SameVector(new Float2(1, 2), Gpu.AsFloat(Gpu.AsUInt(new Float2(1, 2))));
    }

    // ---- counts and reversal ----------------------------------------------------------------------

    [Theory]
    [InlineData(0u, 0u, 32u, 32u, 0u)]
    [InlineData(1u, 1u, 31u, 0u, 0x80000000u)]
    [InlineData(0x80000000u, 1u, 0u, 31u, 1u)]
    [InlineData(0xFFFFFFFFu, 32u, 0u, 0u, 0xFFFFFFFFu)]
    [InlineData(0x00FF00FFu, 16u, 8u, 0u, 0xFF00FF00u)]
    [InlineData(0x00010000u, 1u, 15u, 16u, 0x00008000u)]
    [InlineData(0x12345678u, 13u, 3u, 3u, 0x1E6A2C48u)]
    [InlineData(6u, 2u, 29u, 1u, 0x60000000u)]
    public void CountsAndReversal(uint x, uint popcount, uint leading, uint trailing, uint reversed)
    {
        Assert.Equal(popcount, Gpu.CountBits(x));
        Assert.Equal(leading, Gpu.LeadingZeroCount(x));
        Assert.Equal(trailing, Gpu.TrailingZeroCount(x));
        Assert.Equal(reversed, Gpu.ReverseBits(x));

        // The int overloads see the same bits and return int, as in MSL.
        Assert.Equal((int)popcount, Gpu.CountBits((int)x));
        Assert.Equal((int)leading, Gpu.LeadingZeroCount((int)x));
        Assert.Equal((int)trailing, Gpu.TrailingZeroCount((int)x));
        Assert.Equal((int)reversed, Gpu.ReverseBits((int)x));
    }

    [Fact]
    public void CountsAgreeWithBitOperations_AndReversalWithTheDefinition()
    {
        var random = new Random(7);
        foreach (uint x in Patterns.Concat(Enumerable.Range(0, 2000).Select(_ => (uint)random.NextInt64(0, 1L << 32))))
        {
            Assert.Equal((uint)BitOperations.PopCount(x), Gpu.CountBits(x));
            Assert.Equal((uint)BitOperations.LeadingZeroCount(x), Gpu.LeadingZeroCount(x));
            Assert.Equal((uint)BitOperations.TrailingZeroCount(x), Gpu.TrailingZeroCount(x));

            uint reversed = 0;
            for (int bit = 0; bit < 32; bit++)
            {
                reversed |= ((x >> bit) & 1u) << (31 - bit);
            }

            Assert.Equal(reversed, Gpu.ReverseBits(x));
            Assert.Equal(x, Gpu.ReverseBits(Gpu.ReverseBits(x)));
            Assert.Equal(Gpu.LeadingZeroCount(x), Gpu.TrailingZeroCount(Gpu.ReverseBits(x)));
        }
    }

    [Fact]
    public void CountsOfVectors_AreComponentwise()
    {
        Assert.Equal(new UInt3(0, 32, 16), Gpu.CountBits(new UInt3(0, 0xFFFFFFFFu, 0x00FF00FFu)));
        Assert.Equal(new Int2(32, 1), Gpu.CountBits(new Int2(-1, int.MinValue)));
        Assert.Equal(new UInt4(32, 31, 0, 8), Gpu.LeadingZeroCount(new UInt4(0, 1, 0x80000000u, 0x00FF00FFu)));
        Assert.Equal(new Int3(32, 0, 0), Gpu.LeadingZeroCount(new Int3(0, -1, int.MinValue)));
        Assert.Equal(new UInt2(32, 16), Gpu.TrailingZeroCount(new UInt2(0, 0x00010000u)));
        Assert.Equal(new Int4(32, 0, 31, 3), Gpu.TrailingZeroCount(new Int4(0, -1, int.MinValue, 8)));
        Assert.Equal(new UInt2(0x80000000u, 1u), Gpu.ReverseBits(new UInt2(1u, 0x80000000u)));
        Assert.Equal(new Int3(int.MinValue, 1, -1), Gpu.ReverseBits(new Int3(1, int.MinValue, -1)));
    }

    // ---- fma, copysign, classification, log10, exp10 ----------------------------------------------

    [Fact]
    public void Fma_RoundsOnce()
    {
        // (1 + 2^-12)^2 - 1 = 2^-11 + 2^-24: the product alone would lose the 2^-24.
        float a = 1f + MathF.ScaleB(1f, -12);
        Assert.Equal(MathF.ScaleB(1f, -11) + MathF.ScaleB(1f, -24), Gpu.Fma(a, a, -1f));
        Assert.NotEqual(Gpu.Fma(a, a, -1f), (a * a) - 1f);

        FloatAssert.Same(7f, Gpu.Fma(2f, 3f, 1f));
        FloatAssert.Same(NaN, Gpu.Fma(Inf, 0f, 1f));
        FloatAssert.Same(NaN, Gpu.Fma(Inf, 1f, -Inf));
        FloatAssert.Same(Inf, Gpu.Fma(Inf, 1f, 1f));
        FloatAssert.Same(NaN, Gpu.Fma(NaN, 1f, 1f));
        FloatAssert.Same(0f, Gpu.Fma(NegZero, 1f, 0f));
        FloatAssert.Same(NegZero, Gpu.Fma(NegZero, 1f, NegZero));
        FloatAssert.SameVector(new Float3(7, 0, -1), Gpu.Fma(new Float3(2, 0, 1), new Float3(3, 5, -2), new Float3(1, 0, 1)));
    }

    [Theory]
    [InlineData(1.5f, 2f, 1.5f)]
    [InlineData(1.5f, -2f, -1.5f)]
    [InlineData(-1.5f, 2f, 1.5f)]
    [InlineData(1.5f, NegZero, -1.5f)] // the sign bit of a zero counts
    [InlineData(-1.5f, 0f, 1.5f)]
    [InlineData(0f, -1f, NegZero)]
    [InlineData(NegZero, 1f, 0f)]
    [InlineData(Inf, -1f, -Inf)]
    [InlineData(float.Epsilon, -1f, -float.Epsilon)] // subnormals pass through
    public void CopySign(float x, float y, float expected) => FloatAssert.Same(expected, Gpu.CopySign(x, y));

    [Fact]
    public void CopySign_UsesTheSignBitOfNaN_AndKeepsThePayload()
    {
        float negativeNaN = Gpu.AsFloat(0xFFC12345u);
        FloatAssert.Same(-2f, Gpu.CopySign(2f, negativeNaN));
        FloatAssert.Same(2f, Gpu.CopySign(-2f, Gpu.AsFloat(0x7FC00000u)));
        Assert.Equal(0x7FC12345u, Gpu.AsUInt(Gpu.CopySign(negativeNaN, 1f)));
        Assert.Equal(0xFFC12345u, Gpu.AsUInt(Gpu.CopySign(Gpu.AsFloat(0x7FC12345u), -1f)));
        FloatAssert.SameVector(new Float2(-1, 2), Gpu.CopySign(new Float2(1, -2), new Float2(-5, 5)));
    }

    [Theory]
    [InlineData(NegZero, false, false, true)]
    [InlineData(1.5f, false, false, true)]
    [InlineData(float.MaxValue, false, false, true)]
    [InlineData(float.Epsilon, false, false, true)]
    [InlineData(Inf, false, true, false)]
    [InlineData(-Inf, false, true, false)]
    [InlineData(NaN, true, false, false)]
    public void Classification(float x, bool nan, bool infinity, bool finite)
    {
        Assert.Equal(nan, Gpu.IsNaN(x));
        Assert.Equal(infinity, Gpu.IsInfinity(x));
        Assert.Equal(finite, Gpu.IsFinite(x));
    }

    [Fact]
    public void Classification_OfEveryNaNPayload()
    {
        foreach (uint bits in new[] { 0x7FC00000u, 0xFFC00000u, 0x7F800001u, 0xFFFFFFFFu, 0x7FC12345u })
        {
            float x = Gpu.AsFloat(bits);
            Assert.True(Gpu.IsNaN(x));
            Assert.False(Gpu.IsInfinity(x));
            Assert.False(Gpu.IsFinite(x));
        }
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(10f, 1f)]
    [InlineData(1000f, 3f)]
    [InlineData(0.01f, -2f)]
    [InlineData(NegZero, -Inf)]
    [InlineData(-1f, NaN)]
    [InlineData(Inf, Inf)]
    [InlineData(NaN, NaN)]
    public void Log10(float x, float expected)
    {
        if (float.IsFinite(expected))
        {
            FloatAssert.Near(expected, Gpu.Log10(x));
        }
        else
        {
            FloatAssert.Same(expected, Gpu.Log10(x));
        }
    }

    [Theory]
    [InlineData(NegZero, 1f)]
    [InlineData(1f, 10f)]
    [InlineData(3f, 1000f)]
    [InlineData(-2f, 0.01f)]
    [InlineData(Inf, Inf)]
    [InlineData(-Inf, 0f)]
    [InlineData(100f, Inf)] // overflow
    [InlineData(NaN, NaN)]
    public void Exp10(float x, float expected)
    {
        if (float.IsFinite(expected) && expected != 0f)
        {
            FloatAssert.Near(expected, Gpu.Exp10(x), 1e-5f);
        }
        else
        {
            FloatAssert.Same(expected, Gpu.Exp10(x));
        }
    }

    [Fact]
    public void Log10AndExp10_OfVectors()
    {
        // (+0 next to the -0 of the theories above, which xUnit would call a duplicate row.)
        Assert.True(Gpu.IsFinite(0f) && !Gpu.IsNaN(0f) && !Gpu.IsInfinity(0f));
        FloatAssert.Same(-Inf, Gpu.Log10(0f));
        FloatAssert.Same(1f, Gpu.Exp10(0f));

        FloatAssert.SameVector(new Float3(Gpu.Log10(2f), Gpu.Log10(100f), Gpu.Log10(0.5f)), Gpu.Log10(new Float3(2, 100, 0.5f)));
        FloatAssert.SameVector(new Float2(Gpu.Exp10(2f), Gpu.Exp10(-0.5f)), Gpu.Exp10(new Float2(2, -0.5f)));
        FloatAssert.Near(2.5f, Gpu.Log10(Gpu.Exp10(2.5f)));
    }

    // ---- unorm8 packing ---------------------------------------------------------------------------

    [Fact]
    public void Pack_PutsXInTheLowByte_LikeAnRgba32InMemory()
    {
        Assert.Equal(0x000000FFu, Gpu.PackUnorm4x8(new Float4(1, 0, 0, 0)));
        Assert.Equal(0x0000FF00u, Gpu.PackUnorm4x8(new Float4(0, 1, 0, 0)));
        Assert.Equal(0x00FF0000u, Gpu.PackUnorm4x8(new Float4(0, 0, 1, 0)));
        Assert.Equal(0xFF000000u, Gpu.PackUnorm4x8(new Float4(0, 0, 0, 1)));
        Assert.Equal(0x80FF4000u, Gpu.PackUnorm4x8(new Float4(0f, 0.25f, 1f, 0.5f)));
        Assert.Equal(0xFFFFFFFFu, Gpu.PackUnorm4x8(new Float4(1)));
        Assert.Equal(0u, Gpu.PackUnorm4x8(default));
    }

    [Theory]
    [InlineData(NaN, 0)]
    [InlineData(-1f, 0)]
    [InlineData(NegZero, 0)]
    [InlineData(-Inf, 0)]
    [InlineData(Inf, 255)]
    [InlineData(2f, 255)]
    [InlineData(1f, 255)]
    [InlineData(0.5f, 128)] // 127.5 rounds up
    [InlineData(0.25f, 64)] // 63.75
    [InlineData(0.001f, 0)] // 0.255
    [InlineData(0.002f, 1)] // 0.51
    [InlineData(0.998f, 254)] // 254.49
    [InlineData(0.99804f, 255)] // 254.5002
    public void Pack_ClampsAndRoundsToNearest(float value, byte expected)
    {
        Assert.Equal(expected, Gpu.PackUnorm4x8(new Float4(value, 0, 0, 0)));
        Assert.Equal((uint)expected << 24, Gpu.PackUnorm4x8(new Float4(0, 0, 0, value)));
    }

    [Fact]
    public void Pack_RoundsTheExactProduct_AtEveryMidpoint()
    {
        // The float nearest to (k + 0.5) / 255 lies just above or just below the midpoint, so the
        // exact product decides, not a rounded float product. Same rule the GPU applies.
        for (int k = 0; k < 255; k++)
        {
            float mid = (k + 0.5f) / 255f;
            foreach (float value in new[] { MathF.BitDecrement(mid), mid, MathF.BitIncrement(mid) })
            {
                uint expected = (uint)Math.Floor(((double)value * 255.0) + 0.5);
                Assert.Equal(expected, Gpu.PackUnorm4x8(new Float4(value, 0, 0, 0)));
            }
        }
    }

    [Fact]
    public void Unpack_IsByteOver255_AndRoundTripsEveryValue()
    {
        for (uint b = 0; b < 256; b++)
        {
            Float4 unpacked = Gpu.UnpackUnorm4x8(b | ((255 - b) << 8) | (b << 16) | ((255 - b) << 24));
            FloatAssert.Same(b / 255f, unpacked.X);
            FloatAssert.Same((255 - b) / 255f, unpacked.Y);
            FloatAssert.Same(b / 255f, unpacked.Z);
            FloatAssert.Same((255 - b) / 255f, unpacked.W);
        }

        FloatAssert.SameVector(new Float4(1, 0, 0, 1), Gpu.UnpackUnorm4x8(0xFF0000FFu));

        var random = new Random(11);
        foreach (uint x in Patterns.Concat(Enumerable.Range(0, 2000).Select(_ => (uint)random.NextInt64(0, 1L << 32))))
        {
            Assert.Equal(x, Gpu.PackUnorm4x8(Gpu.UnpackUnorm4x8(x)));
        }
    }
}
