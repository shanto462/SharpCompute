using System;

namespace SharpCompute.Vectors.Tests;

/// <summary>
/// CPU semantics of the scalar intrinsics, including edge cases. Expected values follow the MSL
/// spec and, where noted, what an Apple GPU returns (MSL 3.2, safe math).
/// </summary>
public class ScalarIntrinsicTests
{
    private const float NaN = float.NaN;
    private const float Inf = float.PositiveInfinity;
    private const float NegZero = -0f;

    [Fact]
    public void NegativeZeroConstantIsNegative() => Assert.True(float.IsNegative(NegZero));

    [Theory]
    [InlineData(NegZero)]
    public void InlineDataPreservesNegativeZero(float x) => Assert.True(float.IsNegative(x));

    [Theory]
    [InlineData(-1.5f, 1.5f)]
    [InlineData(2f, 2f)]
    [InlineData(NegZero, 0f)]
    [InlineData(-Inf, Inf)]
    [InlineData(NaN, NaN)]
    public void Abs(float x, float expected) => FloatAssert.Same(expected, Gpu.Abs(x));

    [Fact]
    public void AbsIntegers()
    {
        Assert.Equal(5, Gpu.Abs(-5));
        Assert.Equal(int.MinValue, Gpu.Abs(int.MinValue)); // wraps like MSL, no OverflowException
        Assert.Equal(7u, Gpu.Abs(7u));
    }

    [Theory]
    [InlineData(1f, 2f, 1f, 2f)]
    [InlineData(NaN, 1f, 1f, 1f)] // fmin/fmax: a single NaN is ignored
    [InlineData(1f, NaN, 1f, 1f)]
    [InlineData(NaN, NaN, NaN, NaN)]
    [InlineData(-Inf, 0f, -Inf, 0f)]
    [InlineData(-3f, -3f, -3f, -3f)]
    public void MinMax(float x, float y, float min, float max)
    {
        FloatAssert.Same(min, Gpu.Min(x, y));
        FloatAssert.Same(max, Gpu.Max(x, y));
    }

    [Fact]
    public void MinMaxIntegers()
    {
        Assert.Equal(-3, Gpu.Min(-3, 2));
        Assert.Equal(2, Gpu.Max(-3, 2));
        Assert.Equal(0u, Gpu.Min(0u, uint.MaxValue));
        Assert.Equal(uint.MaxValue, Gpu.Max(0u, uint.MaxValue));
    }

    [Theory]
    [InlineData(0.5f, 0f, 1f, 0.5f)]
    [InlineData(-1f, 0f, 1f, 0f)]
    [InlineData(2f, 0f, 1f, 1f)]
    [InlineData(NaN, 0f, 1f, 0f)] // fmin(fmax(NaN, lo), hi) == lo
    [InlineData(NegZero, 0f, 1f, 0f)]
    [InlineData(5f, 1f, 0f, 0f)] // inverted bounds: no throw, returns max
    [InlineData(-Inf, -1f, 1f, -1f)]
    public void Clamp(float x, float lo, float hi, float expected) => FloatAssert.Same(expected, Gpu.Clamp(x, lo, hi));

    [Fact]
    public void ClampIntegers()
    {
        Assert.Equal(3, Gpu.Clamp(5, 0, 3));
        Assert.Equal(0, Gpu.Clamp(-5, 0, 3));
        Assert.Equal(0, Gpu.Clamp(5, 3, 0)); // Math.Clamp would throw
        Assert.Equal(10u, Gpu.Clamp(20u, 1u, 10u));
        Assert.Equal(1u, Gpu.Clamp(0u, 1u, 10u));
    }

    [Theory]
    [InlineData(0.25f, 0.25f)]
    [InlineData(1.5f, 1f)]
    [InlineData(-0.5f, 0f)]
    [InlineData(NegZero, 0f)]
    [InlineData(NaN, 0f)]
    [InlineData(Inf, 1f)]
    [InlineData(-Inf, 0f)]
    public void Saturate(float x, float expected) => FloatAssert.Same(expected, Gpu.Saturate(x));

    [Theory]
    [InlineData(3f, 1f)]
    [InlineData(-2f, -1f)]
    [InlineData(float.Epsilon, 1f)]
    [InlineData(-1e-30f, -1f)]
    [InlineData(0f, 0f)]
    [InlineData(NaN, 0f)]
    [InlineData(Inf, 1f)]
    [InlineData(-Inf, -1f)]
    public void Sign(float x, float expected) => FloatAssert.Same(expected, Gpu.Sign(x));

    [Fact]
    public void SignOfNegativeZeroIsPositiveZero() => FloatAssert.Same(0f, Gpu.Sign(NegZero)); // as on Apple GPUs

    [Theory]
    [InlineData(2.7f, 2f)]
    [InlineData(-0.5f, -1f)]
    [InlineData(NegZero, NegZero)]
    [InlineData(-Inf, -Inf)]
    [InlineData(NaN, NaN)]
    public void Floor(float x, float expected) => FloatAssert.Same(expected, Gpu.Floor(x));

    [Theory]
    [InlineData(0.2f, 1f)]
    [InlineData(-0.5f, NegZero)]
    [InlineData(NegZero, NegZero)]
    [InlineData(Inf, Inf)]
    [InlineData(NaN, NaN)]
    public void Ceil(float x, float expected) => FloatAssert.Same(expected, Gpu.Ceil(x));

    [Theory]
    [InlineData(0.5f, 1f)] // halfway cases away from zero
    [InlineData(-0.5f, -1f)]
    [InlineData(1.5f, 2f)]
    [InlineData(2.5f, 3f)]
    [InlineData(-2.5f, -3f)]
    [InlineData(5.5f, 6f)]
    [InlineData(0.49999997f, 0f)] // largest float below 0.5: no add-0.5 rounding bug
    [InlineData(2.4f, 2f)]
    [InlineData(-0.4f, 0f)] // zero results are +0 on Apple GPUs
    [InlineData(NegZero, 0f)]
    [InlineData(-Inf, -Inf)]
    [InlineData(NaN, NaN)]
    public void Round(float x, float expected) => FloatAssert.Same(expected, Gpu.Round(x));

    [Theory]
    [InlineData(2.7f, 2f)]
    [InlineData(-2.7f, -2f)]
    [InlineData(-0.5f, NegZero)]
    [InlineData(Inf, Inf)]
    [InlineData(NaN, NaN)]
    public void Trunc(float x, float expected) => FloatAssert.Same(expected, Gpu.Trunc(x));

    [Theory]
    [InlineData(1.25f, 0.25f)]
    [InlineData(-1.25f, 0.75f)]
    [InlineData(3f, 0f)]
    [InlineData(NegZero, 0f)]
    [InlineData(-1e-10f, 0.99999994f)] // clamped below 1 (x - floor(x) would round to 1)
    [InlineData(-5e-8f, 0.99999994f)]
    [InlineData(Inf, 0f)] // GPU returns 0 for infinities
    [InlineData(-Inf, 0f)]
    [InlineData(NaN, NaN)]
    public void Fract(float x, float expected) => FloatAssert.Same(expected, Gpu.Fract(x));

    [Theory]
    [InlineData(5.5f, 2f, 1.5f)]
    [InlineData(-5.5f, 2f, -1.5f)] // sign of the dividend
    [InlineData(5.5f, -2f, 1.5f)]
    [InlineData(-5.5f, -2f, -1.5f)]
    [InlineData(-4f, 2f, NegZero)] // C fmod; also the GPU under precise math
    [InlineData(NegZero, 1f, NegZero)]
    [InlineData(7f, 7f, 0f)]
    [InlineData(1f, 0f, NaN)]
    [InlineData(Inf, 1f, NaN)]
    [InlineData(5.5f, Inf, 5.5f)]
    [InlineData(NaN, 1f, NaN)]
    [InlineData(1e7f, 3f, 1f)] // exact even for large quotients
    public void Fmod(float x, float y, float expected) => FloatAssert.Same(expected, Gpu.Fmod(x, y));

    [Fact]
    public void FmodIsCSharpRemainder()
    {
        foreach (float x in new[] { 5.5f, -5.5f, -4f, 2f, 1e7f + 1, -0f, 3e-30f })
        {
            foreach (float y in new[] { 2f, -2f, 1e-10f, 0.3f, Inf, -7f })
            {
                FloatAssert.Same(x % y, Gpu.Fmod(x, y));
            }
        }
    }

    [Theory]
    [InlineData(4f, 2f)]
    [InlineData(NegZero, NegZero)]
    [InlineData(-1f, NaN)]
    [InlineData(Inf, Inf)]
    [InlineData(NaN, NaN)]
    public void Sqrt(float x, float expected) => FloatAssert.Same(expected, Gpu.Sqrt(x));

    [Theory]
    [InlineData(4f, 0.5f)]
    [InlineData(0.25f, 2f)]
    [InlineData(0f, Inf)]
    [InlineData(NegZero, -Inf)]
    [InlineData(-1f, NaN)]
    [InlineData(Inf, 0f)]
    public void Rsqrt(float x, float expected) => FloatAssert.Same(expected, Gpu.Rsqrt(x));

    [Fact]
    public void RsqrtRoundsOnce()
    {
        // 1f / MathF.Sqrt(x) rounds twice and is 1 ULP off for some inputs; Gpu.Rsqrt rounds once.
        float x = 3f;
        float once = (float)(1.0 / Math.Sqrt(x));
        FloatAssert.Same(once, Gpu.Rsqrt(x));
        int differing = 0;
        for (int i = 1; i <= 1000; i++)
        {
            float v = i * 0.37f;
            FloatAssert.Same((float)(1.0 / Math.Sqrt(v)), Gpu.Rsqrt(v));
            differing += 1f / MathF.Sqrt(v) != Gpu.Rsqrt(v) ? 1 : 0;
        }

        Assert.True(differing > 0, "expected the float formula to differ somewhere");
    }

    [Theory]
    [InlineData(2f, 10f, 1024f)]
    [InlineData(-2f, 3f, -8f)] // negative base, integral exponent
    [InlineData(-2f, 0.5f, NaN)]
    [InlineData(4f, 0.5f, 2f)]
    [InlineData(0f, 0f, 1f)]
    [InlineData(NaN, 0f, 1f)]
    [InlineData(1f, NaN, 1f)]
    [InlineData(0f, -1f, Inf)]
    [InlineData(-2f, -Inf, 0f)]
    public void Pow(float x, float y, float expected) => FloatAssert.Same(expected, Gpu.Pow(x, y));

    [Fact]
    public void ExponentialsAndLogarithms()
    {
        FloatAssert.Same(1f, Gpu.Exp(0f));
        FloatAssert.Near(MathF.E, Gpu.Exp(1f));
        FloatAssert.Same(0f, Gpu.Exp(-Inf));
        FloatAssert.Same(Inf, Gpu.Exp(Inf));
        FloatAssert.Same(1024f, Gpu.Exp2(10f));
        FloatAssert.Same(0.5f, Gpu.Exp2(-1f));
        FloatAssert.Same(0f, Gpu.Exp2(-Inf));
        FloatAssert.Same(0f, Gpu.Log(1f));
        FloatAssert.Near(1f, Gpu.Log(MathF.E));
        FloatAssert.Same(-Inf, Gpu.Log(0f));
        FloatAssert.Same(NaN, Gpu.Log(-1f));
        FloatAssert.Same(3f, Gpu.Log2(8f));
        FloatAssert.Same(-Inf, Gpu.Log2(NegZero));
        FloatAssert.Same(NaN, Gpu.Log2(-1f));
        FloatAssert.Same(Inf, Gpu.Log2(Inf));
    }

    [Fact]
    public void Trigonometry()
    {
        const float Pi = MathF.PI;
        FloatAssert.Near(1f, Gpu.Sin(Pi / 2));
        FloatAssert.Same(NegZero, Gpu.Sin(NegZero));
        FloatAssert.Same(1f, Gpu.Cos(0f));
        FloatAssert.Near(1f, Gpu.Tan(Pi / 4));
        FloatAssert.Near(Pi / 2, Gpu.Asin(1f));
        FloatAssert.Same(NaN, Gpu.Asin(2f));
        FloatAssert.Near(Pi, Gpu.Acos(-1f));
        FloatAssert.Same(NaN, Gpu.Acos(-1.5f));
        FloatAssert.Near(Pi / 2, Gpu.Atan(Inf));
        FloatAssert.Same(0f, Gpu.Atan(NegZero)); // +0, as on Apple GPUs
        FloatAssert.Same(NaN, Gpu.Sin(Inf));
    }

    [Theory]
    [InlineData(1f, -1f, 3 * MathF.PI / 4)] // (y, x) argument order
    [InlineData(-1f, -1f, -3 * MathF.PI / 4)]
    [InlineData(1f, 0f, MathF.PI / 2)]
    [InlineData(0f, -1f, MathF.PI)]
    [InlineData(NegZero, -1f, -MathF.PI)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(Inf, 1f, MathF.PI / 2)]
    public void Atan2(float y, float x, float expected) => FloatAssert.Near(expected, Gpu.Atan2(y, x));

    [Fact]
    public void Atan2OfSignedZerosFollowsC()
    {
        FloatAssert.Same(0f, Gpu.Atan2(0f, 0f));
        FloatAssert.Near(MathF.PI, Gpu.Atan2(0f, NegZero));
        FloatAssert.Same(NegZero, Gpu.Atan2(NegZero, 0f));
        FloatAssert.Near(-MathF.PI, Gpu.Atan2(NegZero, NegZero));
    }

    [Fact]
    public void Hyperbolic()
    {
        FloatAssert.Same(0f, Gpu.Sinh(0f));
        FloatAssert.Near(1.1752012f, Gpu.Sinh(1f));
        FloatAssert.Same(1f, Gpu.Cosh(0f));
        FloatAssert.Same(Inf, Gpu.Cosh(-Inf));
        FloatAssert.Near(0.7615942f, Gpu.Tanh(1f));
        FloatAssert.Same(1f, Gpu.Tanh(Inf));
        FloatAssert.Same(1f, Gpu.Tanh(100f));
        FloatAssert.Same(-1f, Gpu.Tanh(-Inf));
        FloatAssert.Same(NaN, Gpu.Tanh(NaN));
    }

    [Theory]
    [InlineData(2f, 4f, 0.5f, 3f)]
    [InlineData(2f, 4f, 0f, 2f)]
    [InlineData(2f, 4f, 1f, 4f)]
    [InlineData(2f, 4f, 1.5f, 5f)] // a is not clamped
    [InlineData(2f, 4f, -1f, 0f)]
    [InlineData(1e8f, 1f, 1f, 1f)] // endpoint-exact (x + (y - x) * a would give 0 here)
    [InlineData(1f, 1e8f, 1f, 1e8f)]
    [InlineData(2f, 4f, Inf, NaN)] // matches the GPU
    [InlineData(-Inf, 1f, 0.75f, NaN)] // matches the GPU (x * (1 - a) + y * a would give -inf)
    [InlineData(2f, 4f, NaN, NaN)]
    public void Lerp(float x, float y, float a, float expected) => FloatAssert.Same(expected, Gpu.Lerp(x, y, a));

    [Theory]
    [InlineData(0.5f, 0.5f, 1f)]
    [InlineData(0.5f, 0.49f, 0f)]
    [InlineData(0.5f, 2f, 1f)]
    [InlineData(0f, NegZero, 1f)]
    [InlineData(-Inf, -Inf, 1f)]
    [InlineData(0.5f, NaN, 0f)] // the GPU returns 0 when either argument is NaN
    [InlineData(NaN, 0.5f, 0f)]
    public void Step(float edge, float x, float expected) => FloatAssert.Same(expected, Gpu.Step(edge, x));

    [Theory]
    [InlineData(0f, 1f, 0.5f, 0.5f)]
    [InlineData(0f, 1f, 0.25f, 0.15625f)]
    [InlineData(0f, 1f, 0.75f, 0.84375f)]
    [InlineData(0f, 1f, -1f, 0f)] // t is clamped
    [InlineData(0f, 1f, 2f, 1f)]
    [InlineData(2f, 4f, 3f, 0.5f)]
    [InlineData(1f, 0f, 0.25f, 0.84375f)] // reversed edges follow the formula
    [InlineData(0f, 1f, NaN, 0f)]
    [InlineData(0f, 0f, 1f, 1f)]
    [InlineData(0f, 0f, -1f, 0f)]
    public void Smoothstep(float e0, float e1, float x, float expected) => FloatAssert.Same(expected, Gpu.Smoothstep(e0, e1, x));
}
