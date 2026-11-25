using System;
using System.Linq;
using System.Reflection;
using static SharpCompute.Vectors.Tests.HalfAssert;

namespace SharpCompute.Vectors.Tests;

/// <summary>
/// The CPU semantics of <see cref="Half2"/>, <see cref="Half3"/> and <see cref="Half4"/>: IEEE half
/// arithmetic, one rounding to nearest even per operation, and the intrinsics as an Apple GPU
/// evaluates them for halves. The bit patterns that look arbitrary are ones an Apple M4 Pro
/// returned; the GPU-vs-CPU tests in SharpCompute.Tests compare whole dispatches.
/// </summary>
public class HalfVectorTests
{
    private static readonly Half Ulp = H(1.0 / 1024);       // 2^-10: the spacing of halves in [1, 2)
    private static readonly Half HalfUlp = H(1.0 / 2048);   // 2^-11
    private static readonly Half MinSubnormal = Bits(0x0001); // 2^-24
    private static readonly Half Max = Bits(0x7BFF);        // 65504
    private static readonly Half NegativeZero = Bits(0x8000);

    private static Half2 V(double x, double y) => new(H(x), H(y));

    private static Half3 V(double x, double y, double z) => new(H(x), H(y), H(z));

    private static Half4 V(double x, double y, double z, double w) => new(H(x), H(y), H(z), H(w));

    private static void Same(Half2 expected, Half2 actual) => FloatAssert.SameVector(expected, actual);

    private static void Same(Half3 expected, Half3 actual) => FloatAssert.SameVector(expected, actual);

    private static void Same(Half4 expected, Half4 actual) => FloatAssert.SameVector(expected, actual);

    // ---- arithmetic ---------------------------------------------------------------------------

    [Fact]
    public void AdditionRoundsOnceToNearestEven()
    {
        // 1 + 2^-11 lies exactly between 1 and 1 + 2^-10: the tie goes to the even one, 1.
        // (1 + 2^-10) + 2^-11 lies between 1 + 2^-10 and 1 + 2^-9: the even one is 1 + 2^-9.
        Same(V(1, 1 + (2.0 / 1024)), new Half2(H(1), H(1) + Ulp) + HalfUlp);
        Same(V(1, 1 + (2.0 / 1024)), HalfUlp + new Half2(H(1), H(1) + Ulp));
        Same(V(1, 1), new Half2(H(1)) + new Half2(H(1e-5), H(-1e-5)));
    }

    [Fact]
    public void MultiplicationAndDivisionRoundOnce()
    {
        // (1 + 2^-10)^2 = 1 + 2^-9 + 2^-20: the last term is far below the spacing.
        Same(V(1 + (2.0 / 1024), 6.25), new Half2(H(1) + Ulp, H(2.5)) * new Half2(H(1) + Ulp, H(2.5)));
        Same(new Half2(Bits(0x3555), Bits(0x3955)), new Half2(H(1), H(2)) / H(3)); // 1/3 and 2/3
        Same(new Half2(Bits(0x3555), H(0.25)), H(1) / V(3, 4));
        Same(V(-3, 6), V(1, -2) * H(-3));
    }

    [Fact]
    public void SubnormalsAreKept_NotFlushedToZero()
    {
        // An Apple GPU flushes float subnormals but not half ones, so neither does the CPU.
        Same(new Half2(Bits(0x0002), Bits(0x0200)), new Half2(MinSubnormal, Bits(0x0400)) * new Half2(H(2), H(0.5)));
        Same(new Half2(Bits(0x0003), Bits(0x8001)), new Half2(Bits(0x0002), NegativeZero) + new Half2(MinSubnormal, -MinSubnormal));
        HalfAssert.Same(H(0), (new Half2(MinSubnormal) * H(0.5)).X); // 2^-25: the tie goes to the even one, zero
    }

    [Fact]
    public void OverflowGoesToInfinityFromTheTieUp()
    {
        // The next half after 65504 would be 65536: 65520 is the tie, and goes to the even "65536", infinity.
        Same(new Half2(Max, Half.PositiveInfinity), new Half2(Max) + V(8, 16));
        Same(new Half2(Half.NegativeInfinity, Half.PositiveInfinity), V(-300, 300) * H(300));
    }

    [Fact]
    public void NegationFlipsTheSignBit()
    {
        Same(new Half3(NegativeZero, H(0), H(-1)), -new Half3(H(0), NegativeZero, H(1)));
        Assert.True(Half.IsNaN((-new Half2(Half.NaN)).X));
    }

    [Fact]
    public void RemainderIsFmod_WithPositiveZeroForANonZeroDividend()
    {
        Same(V(1.5, -1.5, 1.5, -1.5), V(5.5, -5.5, 5.5, -5.5) % V(2, 2, -2, -2));

        // Where the float vectors (and C#'s %) give -0 for -4 % 2, an Apple GPU gives +0 for halves.
        Same(V(0, 0), V(-4, 4) % H(2));
        FloatAssert.SameVector(new Float2(-0f, 0f), new Float2(-4, 4) % 2f);

        // A zero dividend keeps its sign; infinity as divisor returns the dividend; a zero divisor is NaN.
        Same(new Half2(NegativeZero, H(0)), new Half2(NegativeZero, H(0)) % H(3));
        Same(new Half3(Half.NaN, Half.NaN, H(1)), new Half3(H(1), Half.PositiveInfinity, H(1)) % new Half3(H(0), H(1), Half.PositiveInfinity));
        Same(V(1, 0.5), H(7) % V(3, 1.625));

        // The remainder is exact, also between very different magnitudes: 65504 = 21834 * 3 + 2.
        HalfAssert.Same(H(2), (new Half2(Max) % H(3)).X);
    }

    [Fact]
    public void CompoundAssignmentUsesTheOperators()
    {
        Half3 v = V(1, 2, 3);
        v *= H(2);
        v += new Half3(H(1));
        v.XY -= V(3, 5);
        Same(V(0, 0, 7), v);
    }

    // ---- conversions --------------------------------------------------------------------------

    [Fact]
    public void FloatToHalfRoundsToNearestEven()
    {
        float tie = 1f + (1f / 2048);          // exactly between 1 and 1 + 2^-10
        float tieUp = 1f + (3f / 2048);        // exactly between 1 + 2^-10 and 1 + 2^-9
        Same(new Half4(H(1), H(1) + Ulp + Ulp, H(1) + Ulp, H(1)), (Half4)new Float4(tie, tieUp, MathF.BitIncrement(tie), MathF.BitDecrement(tie)));

        // 65520 is the tie between the largest half and "65536": infinity. Just below it is 65504.
        Same(new Half4(Max, Half.PositiveInfinity, Half.NegativeInfinity, Half.PositiveInfinity), (Half4)new Float4(MathF.BitDecrement(65520f), 65520f, -1e10f, float.PositiveInfinity));

        // 2^-25 is the tie between zero and the smallest subnormal: zero. Anything above it rounds up.
        float tiny = MathF.ScaleB(1f, -25);
        Same(new Half4(H(0), MinSubnormal, NegativeZero, -MinSubnormal), (Half4)new Float4(tiny, MathF.BitIncrement(tiny), -tiny, -MathF.BitIncrement(tiny)));

        Half2 special = (Half2)new Float2(float.NaN, -0f);
        Assert.True(Half.IsNaN(special.X));
        HalfAssert.Same(NegativeZero, special.Y);
    }

    [Fact]
    public void HalfToFloatIsExact()
    {
        FloatAssert.SameVector(new Float4(MathF.ScaleB(1f, -24), 65504f, -0f, 0.333251953125f), (Float4)new Half4(MinSubnormal, Max, NegativeZero, Bits(0x3555)));
        FloatAssert.SameVector(new Float2(float.PositiveInfinity, float.NaN), (Float2)new Half2(Half.PositiveInfinity, Half.NaN));

        // Every half survives the round trip through float, bit for bit.
        for (int bits = 0; bits <= ushort.MaxValue; bits++)
        {
            Half h = Bits((ushort)bits);
            HalfAssert.Same(h, ((Half2)(Float2)new Half2(h)).Y);
        }
    }

    [Fact]
    public void HalfToIntegerTruncatesSaturatesAndMapsNaNToZero()
    {
        Assert.Equal(new Int4(1, -1, 0, 0), (Int4)new Half4(H(1.9), H(-1.9), Half.NaN, NegativeZero));
        Assert.Equal(new Int3(int.MaxValue, int.MinValue, 65504), (Int3)new Half3(Half.PositiveInfinity, Half.NegativeInfinity, Max));
        Assert.Equal(new UInt4(0, uint.MaxValue, 2, 0), (UInt4)new Half4(H(-1), Half.PositiveInfinity, H(2.99), Half.NaN));

        checked
        {
            Assert.Equal(new Int2(int.MaxValue, -7), (Int2)new Half2(Half.PositiveInfinity, H(-7.5)));
            Assert.Equal(new UInt2(0, 3), (UInt2)new Half2(Half.NegativeInfinity, H(3.5)));
        }
    }

    [Fact]
    public void IntegerToHalfRoundsToNearestEven_AndOverflowsToInfinity()
    {
        // Above 2048 halves are 2 apart: 2049 ties to 2048 (even), 2051 to 2052.
        Same(V(2048, 2052, -2048, 7), (Half4)new Int4(2049, 2051, -2049, 7));
        Same(new Half3(Max, Half.PositiveInfinity, Half.NegativeInfinity), (Half3)new Int3(65519, 65520, int.MinValue));
        Same(new Half3(H(2048), Half.PositiveInfinity, H(0)), (Half3)new UInt3(2049, uint.MaxValue, 0));
    }

    // ---- intrinsics: bit for bit what an Apple GPU returns ---------------------------------------

    [Theory]
    [InlineData(0x30A0, 0xF4B0, 0x06A4, 0xE96B)] // 0.1445 * -19200 + 0.0001: a float fma rounded to half gives 0xE96C
    [InlineData(0xE42D, 0xCE00, 0x916B, 0x7643)] // a float fma rounded to half gives 0x7644
    [InlineData(0x984A, 0x1376, 0x9C10, 0x9C11)] // a float fma rounded to half gives 0x9C10
    [InlineData(0x3C00, 0x3C00, 0x3C00, 0x4000)] // 1 * 1 + 1
    [InlineData(0x7BFF, 0x4000, 0xFBFF, 0x7BFF)] // 65504 * 2 - 65504: no intermediate overflow
    [InlineData(0x7C00, 0x0000, 0x3C00, 0x7E00)] // inf * 0: NaN
    public void FmaRoundsOnce_ToHalf(int x, int y, int z, int expected)
    {
        Half hx = Bits((ushort)x), hy = Bits((ushort)y), hz = Bits((ushort)z);
        HalfAssert.Same(Bits((ushort)expected), Gpu.Fma(new Half2(hx), new Half2(hy), new Half2(hz)).X);
    }

    [Fact]
    public void FmaIsNotAFloatFmaRoundedToHalf()
    {
        // The reason Fma has its own rounding: 24 bits and then 11 can round twice, the wrong way.
        Half x = Bits(0x30A0), y = Bits(0xF4B0), z = Bits(0x06A4);
        Half twice = (Half)MathF.FusedMultiplyAdd((float)x, (float)y, (float)z);
        Assert.Equal(0xE96C, BitsOf(twice));
        Assert.Equal(0xE96B, BitsOf(Gpu.Fma(new Half2(x), new Half2(y), new Half2(z)).X));

        // And it is not a product rounded to half followed by a sum: (1 + 2^-10)^2 - 1 keeps its 2^-20.
        Half a = H(1) + Ulp;
        HalfAssert.Same(H((2.0 / 1024) + (1.0 / 1048576)), Gpu.Fma(new Half2(a), new Half2(a), new Half2(H(-1))).Y);
        HalfAssert.Same(H(2.0 / 1024), ((a * a) + H(-1)));
    }

    [Fact]
    public void AbsMinMaxClampSaturateSign()
    {
        Same(new Half3(H(0), H(2), Half.PositiveInfinity), Gpu.Abs(new Half3(NegativeZero, H(-2), Half.NegativeInfinity)));
        Same(V(1, -2, 3), Gpu.Min(new Half3(H(1), H(5), Half.NaN), new Half3(Half.NaN, H(-2), H(3))));
        Same(V(1, 5, 3), Gpu.Max(new Half3(H(1), H(5), Half.NaN), new Half3(Half.NaN, H(-2), H(3))));
        Same(V(2, 2, 9), Gpu.Max(V(1, 2, 9), H(2)));
        Same(V(0, 0.25, 1, 1), Gpu.Clamp(V(-1, 0.25, 1, 9), H(0), H(1)));
        Same(V(0, 5, 10), Gpu.Clamp(V(-4, 5, 99), new Half3(H(0)), new Half3(H(10))));
        HalfAssert.Same(H(2), Gpu.Clamp(new Half2(Half.NaN), H(2), H(3)).X); // NaN yields the lower bound
        Same(V(0, 0, 0.5, 1), Gpu.Saturate(new Half4(Half.NaN, NegativeZero, H(0.5), H(7))));
        Same(V(1, -1, 0, 0), Gpu.Sign(new Half4(MinSubnormal, H(-3), NegativeZero, Half.NaN)));
    }

    [Fact]
    public void Rounding()
    {
        Same(V(2, -3, 1023, -1), Gpu.Floor(V(2.5, -2.5, 1023.5, -0.25)));
        Same(V(3, -2, 1024, 1), Gpu.Ceil(V(2.5, -2.5, 1023.5, 0.25)));
        Same(V(3, -3, 0, 2), Gpu.Round(V(2.5, -2.5, -0.4, 1.5)));       // away from zero; a zero result is +0
        Same(new Half3(H(2), H(-2), NegativeZero), Gpu.Trunc(V(2.9, -2.9, -0.5))); // keeps the sign of zero
        Same(new Half2(Half.PositiveInfinity, Half.NegativeInfinity), Gpu.Floor(new Half2(Half.PositiveInfinity, Half.NegativeInfinity)));
    }

    [Fact]
    public void FractStaysBelowOne()
    {
        Same(V(0.75, 0.25, 0), Gpu.Fract(V(2.75, -2.75, 5)));

        // A tiny negative input: x - floor(x) rounds to 1, and the result is clamped to the largest half below 1.
        Same(new Half3(Bits(0x3BFF), MinSubnormal, H(0)), Gpu.Fract(new Half3(-MinSubnormal, MinSubnormal, NegativeZero)));
        Same(V(0, 0), Gpu.Fract(new Half2(Half.PositiveInfinity, Half.NegativeInfinity)));
        Assert.True(Half.IsNaN(Gpu.Fract(new Half2(Half.NaN)).X));
    }

    [Fact]
    public void FmodMatchesTheRemainderOperator()
    {
        Same(V(1.5, 0), Gpu.Fmod(V(5.5, -4), V(2, 2)));
        Same(V(5.5, -4) % V(2, 2), Gpu.Fmod(V(5.5, -4), V(2, 2)));
    }

    [Fact]
    public void SqrtAndRsqrt()
    {
        // sqrt(2^-24) = 2^-12, sqrt(65504) = 255.875; -0 keeps its sign, negatives are NaN.
        Same(new Half4(Bits(0x0C00), Bits(0x5BFF), NegativeZero, H(1.5)), Gpu.Sqrt(new Half4(MinSubnormal, Max, NegativeZero, H(2.25))));
        Assert.True(Half.IsNaN(Gpu.Sqrt(new Half2(H(-1))).X));

        Same(new Half4(Bits(0x6C00), Bits(0x1C00), Half.PositiveInfinity, Half.NegativeInfinity), Gpu.Rsqrt(new Half4(MinSubnormal, Max, H(0), NegativeZero)));
        Same(V(0.5, 0.25), Gpu.Rsqrt(V(4, 16)));
        Same(new Half2(Bits(0x39A8), H(0)), Gpu.Rsqrt(new Half2(H(2), Half.PositiveInfinity))); // 1/sqrt(2) = 0.70703125 as a half
    }

    [Fact]
    public void ExponentialsAndLogarithms_ExactResults()
    {
        // Results that are halves exactly; in between, the functions are float functions rounded to half.
        Same(V(1, 8, 0.5), Gpu.Exp2(V(0, 3, -1)));
        Same(V(1, 100, 1000), Gpu.Exp10(V(0, 2, 3)));
        Same(V(3, -2, 10), Gpu.Log2(V(8, 0.25, 1024)));
        Same(V(2, 0), Gpu.Log10(V(100, 1)));
        Same(V(1024, 0.125, 9), Gpu.Pow(V(2, 2, -3), V(10, -3, 2)));
        Same(V(1, 4, 9), Gpu.Pow(V(1, 2, 3), H(2)));
        Same(new Half3(H(1), Half.PositiveInfinity, H(0)), Gpu.Exp(new Half3(H(0), Half.PositiveInfinity, Half.NegativeInfinity)));
        Same(new Half2(Half.NegativeInfinity, H(0)), Gpu.Log(V(0, 1)));
        Same(new Half2(Bits(0x4170), Bits(0x35E3)), Gpu.Exp(V(1, -1))); // e and 1/e to 11 bits
    }

    [Fact]
    public void Trigonometry_SignedZerosAsOnTheGpu()
    {
        // sin, tan and asin of -0 are +0 on an Apple GPU; atan, sinh and tanh keep the sign.
        var zero = new Half2(NegativeZero, H(0));
        Same(V(0, 0), Gpu.Sin(zero));
        Same(V(0, 0), Gpu.Tan(zero));
        Same(V(0, 0), Gpu.Asin(zero));
        Same(zero, Gpu.Atan(zero));
        Same(zero, Gpu.Sinh(zero));
        Same(zero, Gpu.Tanh(zero));
        Same(V(1, 1), Gpu.Cos(zero));
        Same(V(1, 1), Gpu.Cosh(zero));

        Same(new Half2(Bits(0x3E48), Bits(0x4248)), Gpu.Acos(V(0, -1))); // pi/2 and pi to 11 bits
        Same(new Half2(Bits(0x3E48), Bits(0xBE48)), Gpu.Atan(new Half2(Half.PositiveInfinity, Half.NegativeInfinity)));
        Same(new Half2(Bits(0x3E48), Bits(0x4248)), Gpu.Atan2(V(1, 0), V(0, -1)));
        Same(V(1, -1), Gpu.Tanh(new Half2(Half.PositiveInfinity, Half.NegativeInfinity)));
        Assert.True(Half.IsNaN(Gpu.Sin(new Half2(Half.PositiveInfinity)).X));
    }

    [Fact]
    public void LerpStepSmoothstepCopySign()
    {
        // fma(a, y, fma(-a, x, x)): the endpoints come back exactly.
        Half x = Bits(0x3555), y = Bits(0x4A37);
        Same(new Half2(x, y), Gpu.Lerp(new Half2(x), new Half2(y), V(0, 1)));
        Same(V(3, 1), Gpu.Lerp(V(2, 0), V(4, 2), H(0.5)));
        Assert.True(Half.IsNaN(Gpu.Lerp(new Half2(Bits(0x0013)), new Half2(Bits(0x1362)), Half.NegativeInfinity).X)); // as on the GPU

        Same(V(0, 1, 1, 0), Gpu.Step(H(0.5), new Half4(H(0.25), H(0.5), H(0.75), Half.NaN)));
        Same(V(0, 0.5, 1, 1), Gpu.Smoothstep(H(0), H(1), V(-1, 0.5, 1, 7)));
        Same(V(0.15625, 0.84375), Gpu.Smoothstep(V(0, 0), V(4, 4), V(1, 3))); // t = 0.25 and 0.75: t * t * (3 - 2t)
        Same(new Half3(H(-2), H(2), NegativeZero), Gpu.CopySign(V(2, -2, 0), new Half3(NegativeZero, H(0), H(-1))));
    }

    [Fact]
    public void DotAndCrossAreHalfFmaChains()
    {
        Same(new Half2(H(11), H(32)), new Half2(Gpu.Dot(V(1, 2), V(3, 4)), Gpu.Dot(V(1, 2, 3), V(4, 5, 6))));
        HalfAssert.Same(H(70), Gpu.Dot(V(1, 2, 3, 4), V(5, 6, 7, 8)));

        // (1 + 2^-6)^2 = 1 + 2^-5 + 2^-12 is not a half; the fma keeps the 2^-12 that a rounded product loses.
        Half a = H(1 + (1.0 / 64));
        Half kept = H((1.0 / 32) + (1.0 / 4096));
        HalfAssert.Same(kept, Gpu.Dot(new Half2(H(1), a), new Half2(H(-1), a)));
        HalfAssert.Same(kept, Gpu.Dot(new Half3(H(1), H(0), a), new Half3(H(-1), H(0), a)));
        HalfAssert.Same(kept, Gpu.Dot(new Half4(H(1), H(0), H(0), a), new Half4(H(-1), H(0), H(0), a)));
        HalfAssert.Same(H(1.0 / 32), (H(1) * H(-1)) + (a * a)); // a plain sum loses it

        Same(V(0, 0, 1), Gpu.Cross(V(1, 0, 0), V(0, 1, 0)));
        Same(V(-3, 6, -3), Gpu.Cross(V(1, 2, 3), V(4, 5, 6)));
        HalfAssert.Same(kept, Gpu.Cross(new Half3(H(0), a, H(1)), new Half3(H(0), H(1), a)).X);
    }

    [Fact]
    public void LengthIsNotRescaled_ItOverflowsAndUnderflowsWithTheSumOfSquares()
    {
        HalfAssert.Same(H(5), Gpu.Length(V(3, -4)));
        HalfAssert.Same(H(3), Gpu.Length(V(1, 2, 2)));
        HalfAssert.Same(H(2), Gpu.Length(V(1, -1, 1, -1)));
        HalfAssert.Same(H(5), Gpu.Distance(V(1, 1, 1), V(4, 5, 1)));

        // 300^2 does not fit in a half: infinity, where the float overload answers 300. And 2^-13 squared
        // is below the smallest subnormal: 0. Both are what an Apple GPU returns.
        HalfAssert.Same(Half.PositiveInfinity, Gpu.Length(V(300, 0)));
        FloatAssert.Same(300f, Gpu.Length(new Float2(300, 0)));
        HalfAssert.Same(H(0), Gpu.Length(new Half2(H(1.0 / 8192), H(0))));
        HalfAssert.Same(Half.PositiveInfinity, Gpu.Length(new Half3(Bits(0x0405), Bits(0x422C), Bits(0x7BC4)))); // measured
        HalfAssert.Same(H(0), Gpu.Length(new Half2(Bits(0x0002), Bits(0x8189))));                               // measured
        HalfAssert.Same(Half.PositiveInfinity, Gpu.Distance(V(200, 0), V(-200, 0)));
    }

    [Fact]
    public void NormalizeIsXTimesRsqrtOfDot()
    {
        Half fifth = H(0.2); // rsqrt(25) rounded to half: 0.199951171875
        Same(new Half3(H(3) * fifth, H(0), H(4) * fifth), Gpu.Normalize(V(3, 0, 4)));
        Same(V(-1, 0), Gpu.Normalize(V(-2, 0)));
        Same(V(0.5, 0.5, 0.5, 0.5), Gpu.Normalize(V(1, 1, 1, 1)));

        // A zero vector is 0 * inf: NaN, where the float overload returns the vector. As on the GPU.
        Half3 zero = Gpu.Normalize(new Half3(H(0), NegativeZero, H(0)));
        Assert.True(Half.IsNaN(zero.X) && Half.IsNaN(zero.Y) && Half.IsNaN(zero.Z));
        FloatAssert.SameVector(new Float3(0, -0f, 0), Gpu.Normalize(new Float3(0, -0f, 0)));
    }

    [Fact]
    public void Reflect()
    {
        Same(V(1, 1, 0), Gpu.Reflect(V(1, -1, 0), V(0, 1, 0)));
        Same(V(-2, 3), Gpu.Reflect(V(2, 3), V(1, 0)));
        Same(V(0, 0, 0, -5), Gpu.Reflect(V(0, 0, 0, 5), V(0, 0, 0, 1)));
    }

    // ---- the generated overloads as a whole -------------------------------------------------------

    private static readonly string[] NotComponentwise =
    [
        "Dot", "Cross", "Length", "Distance", "Normalize", "Reflect", "IsNaN", "IsInfinity", "IsFinite", "Equal", "NotEqual", "Select",
    ];

    private static MethodInfo[] ComponentwiseHalfOverloads() =>
        typeof(Gpu).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !NotComponentwise.Contains(m.Name) && VectorTypes.HalfVectors.Contains(m.ReturnType))
            .Where(m => !m.Name.StartsWith("Wave", StringComparison.Ordinal) && !m.Name.StartsWith("Quad", StringComparison.Ordinal)) // those need the lanes of a dispatch
            .ToArray();

    /// <summary>
    /// Every componentwise half overload gives each component the result its own inputs have: the
    /// same call on vectors that repeat that component's inputs returns it in every component.
    /// </summary>
    [Fact]
    public void ComponentwiseOverloadsTreatEveryComponentAlike()
    {
        MethodInfo[] methods = ComponentwiseHalfOverloads();
        Assert.Equal((38 * 3) + (7 * 3), methods.Length); // 38 functions, 7 of them with a scalar form, three widths

        foreach (MethodInfo m in methods)
        {
            Type vector = m.ReturnType;
            ParameterInfo[] ps = m.GetParameters();
            Assert.All(ps, p => Assert.True(p.ParameterType == vector || p.ParameterType == typeof(Half), $"{m}: parameter {p.Name}"));

            object[] args = ps.Select((p, i) => p.ParameterType == vector ? VectorTypes.SampleVector(vector, i) : VectorTypes.Sample(typeof(Half), i, 1)).ToArray();
            object result = m.Invoke(null, args)!;

            for (int c = 0; c < VectorTypes.Width(vector); c++)
            {
                object[] repeated = args.Select(a => a.GetType() == vector ? VectorTypes.Make(vector, Enumerable.Repeat(VectorTypes.Get(a, c), VectorTypes.Width(vector))) : a).ToArray();
                object expected = VectorTypes.Get(m.Invoke(null, repeated)!, 0);
                object actual = VectorTypes.Get(result, c);
                Assert.True(VectorTypes.SameValue(expected, actual), $"{m} component {c}: expected {VectorTypes.Format(expected)}, got {VectorTypes.Format(actual)}");
            }
        }
    }

    /// <summary>
    /// Every half overload maps to the MSL function its float overload maps to, and computes that
    /// function: within one half ULP of the float result rounded to half (the half formulas of
    /// <c>fma</c>, <c>mix</c>, <c>smoothstep</c> and <c>fract</c> round differently, never further).
    /// </summary>
    [Fact]
    public void HalfOverloadsComputeTheFunctionOfTheirFloatOverload()
    {
        foreach (MethodInfo m in ComponentwiseHalfOverloads())
        {
            Type vector = m.ReturnType;
            int n = VectorTypes.Width(vector);
            Type floatVector = VectorTypes.Vector(typeof(float), n);
            ParameterInfo[] ps = m.GetParameters();
            Type[] floatSignature = ps.Select(p => p.ParameterType == vector ? floatVector : typeof(float)).ToArray();
            MethodInfo floatOverload = typeof(Gpu).GetMethod(m.Name, floatSignature) ?? throw new InvalidOperationException($"{m}: no float overload");
            Assert.Equal(floatOverload.GetCustomAttribute<MslIntrinsicAttribute>()!.Name, m.GetCustomAttribute<MslIntrinsicAttribute>()!.Name);

            object[] args = ps.Select((p, i) => p.ParameterType == vector ? VectorTypes.SampleVector(vector, i) : VectorTypes.Sample(typeof(Half), i, 1)).ToArray();
            object[] floatArgs = args.Select(a => a is Half h ? (float)h : VectorTypes.Make(floatVector, VectorTypes.Get(a).Select(c => (object)(float)(Half)c))).ToArray();
            object[] result = VectorTypes.Get(m.Invoke(null, args)!);
            object[] reference = VectorTypes.Get(floatOverload.Invoke(null, floatArgs)!);

            for (int c = 0; c < n; c++)
            {
                Half expected = (Half)(float)reference[c];
                Half actual = (Half)result[c];
                Assert.True(
                    Half.IsNaN(expected) ? Half.IsNaN(actual) : UlpDistance(expected, actual) <= 1,
                    $"{m} component {c}: float overload gives {VectorTypes.Format(expected)}, half overload {VectorTypes.Format(actual)}");
            }
        }
    }

    /// <summary>The number of halves between two finite halves (0 for +0 and -0).</summary>
    private static int UlpDistance(Half a, Half b)
    {
        static int Ordinal(Half h)
        {
            ushort bits = BitsOf(h);
            return (bits & 0x8000) != 0 ? -(bits & 0x7FFF) : bits;
        }

        return Math.Abs(Ordinal(a) - Ordinal(b));
    }

    [Fact]
    public void RadiansAndDegrees_AreOneHalfMultiplicationByTheRoundedConstant()
    {
        // pi / 180 and 180 / pi rounded to half: 0.017456055 and 57.28125.
        Half radiansPerDegree = Bits(0x2478), degreesPerRadian = Bits(0x5329);
        HalfAssert.Same(radiansPerDegree, (Half)0.017453292519943295f);
        HalfAssert.Same(degreesPerRadian, (Half)57.29577951308232f);
        HalfAssert.Same(radiansPerDegree, Gpu.Radians(H(1)));
        HalfAssert.Same(degreesPerRadian, Gpu.Degrees(H(1)));

        foreach (double x in new[] { 0, -0.0, 1, -45, 90, 180, 0.3333, 1000, 3000, double.PositiveInfinity, double.NaN })
        {
            Half h = H(x);
            HalfAssert.Same(h * radiansPerDegree, Gpu.Radians(h));
            HalfAssert.Same(h * degreesPerRadian, Gpu.Degrees(h));
        }

        // 180 degrees: pi to the 11 bits of a half, and back within a degree's worth of the constant's error.
        HalfAssert.Same(Bits(0x4249), Gpu.Radians(H(180))); // 3.1425781; pi rounded to half is 0x4248
        HalfAssert.Same(NegativeZero, Gpu.Radians(NegativeZero));
        HalfAssert.Same(Half.PositiveInfinity, Gpu.Degrees(H(2000))); // 114,562 does not fit in a half

        // The vector overloads are the scalar one per component.
        Same(new Half3(Gpu.Radians(H(30)), Gpu.Radians(H(60)), Gpu.Radians(H(-90))), Gpu.Radians(V(30, 60, -90)));
        Same(new Half2(Gpu.Degrees(H(1)), Gpu.Degrees(H(-2))), Gpu.Degrees(V(1, -2)));
        Same(new Half4(Gpu.Degrees(H(0.5)), Gpu.Degrees(H(0)), Gpu.Degrees(H(3)), Gpu.Degrees(H(-1))), Gpu.Degrees(V(0.5, 0, 3, -1)));
    }

    [Fact]
    public void TheBitsOfAHalf_ReinterpretWithoutConversion()
    {
        Assert.Equal(0x3C00, Gpu.AsUShort(H(1)));
        Assert.Equal(unchecked((short)0xBC00), Gpu.AsShort(H(-1)));
        HalfAssert.Same(H(-2), Gpu.AsHalf((ushort)0xC000));
        HalfAssert.Same(H(0.5), Gpu.AsHalf((short)0x3800));
        Assert.True(Gpu.AsShort(NegativeZero) < 0); // the sign bit is the sign of the short

        // Every bit pattern survives, NaN payloads included, and they are BitConverter's.
        for (int bits = 0; bits <= ushort.MaxValue; bits++)
        {
            ushort u = (ushort)bits;
            short s = unchecked((short)u);
            Assert.Equal(u, Gpu.AsUShort(Gpu.AsHalf(u)));
            Assert.Equal(s, Gpu.AsShort(Gpu.AsHalf(s)));
            Assert.Equal(u, BitConverter.HalfToUInt16Bits(Gpu.AsHalf(s)));
            Assert.Equal(s, BitConverter.HalfToInt16Bits(Gpu.AsHalf(u)));
        }
    }

    [Fact]
    public void MixedOverloadsBroadcastTheScalar()
    {
        Same(Gpu.Min(V(1, 5), new Half2(H(2))), Gpu.Min(V(1, 5), H(2)));
        Same(Gpu.Pow(V(2, 3), new Half2(H(3))), Gpu.Pow(V(2, 3), H(3)));
        Same(Gpu.Lerp(V(2, 0), V(4, 2), new Half2(H(0.25))), Gpu.Lerp(V(2, 0), V(4, 2), H(0.25)));
        Same(Gpu.Step(new Half2(H(0.5)), V(0.25, 0.75)), Gpu.Step(H(0.5), V(0.25, 0.75)));
        Same(Gpu.Smoothstep(new Half2(H(0)), new Half2(H(2)), V(0.5, 1.5)), Gpu.Smoothstep(H(0), H(2), V(0.5, 1.5)));
    }

    [Fact]
    public void DefaultEqualsComparesComponents()
    {
        Assert.True(V(1, 2).Equals(V(1, 2)));
        Assert.False(V(1, 2).Equals(V(2, 1)));
        Assert.Equal(V(4, 5, 6).GetHashCode(), V(4, 5, 6).GetHashCode());
        Assert.Equal(default(Half4), new Half4(H(0)));
    }
}
