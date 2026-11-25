using System;

namespace SharpCompute;

/// <summary>
/// The scalar half-precision implementations behind the <see cref="Half2"/>, <see cref="Half3"/> and
/// <see cref="Half4"/> overloads of <see cref="Gpu"/>, each written to return what an Apple GPU
/// returns for <c>half</c> operands.
/// </summary>
/// <remarks>
/// <para>
/// They are internal on purpose. As public <c>Gpu.Sqrt(Half)</c> overloads they would sit next to
/// <c>Gpu.Sqrt(float)</c>, and <see cref="Half"/> converts implicitly from <c>byte</c> and
/// <c>sbyte</c>: <c>Gpu.Sqrt(2)</c>, which compiles today, would become ambiguous. The vector
/// overloads have no such neighbour, because nothing converts implicitly to a half vector.
/// </para>
/// <para>
/// Measured on an Apple M4 Pro with the runtime's compile options (MSL 3.2, safe math, precise math
/// functions): every function of one argument over all 65,536 halves, the others over 262,144
/// operand sets that include zeros, infinities, NaN, subnormals and operands of nearby exponents.
/// The GPU does IEEE half arithmetic, one rounding to nearest even per operation, and keeps
/// subnormals (it flushes float subnormals, not half ones). Bit for bit the same here: the
/// arithmetic operators, <see cref="Fma"/>, <see cref="Abs"/>, <see cref="Min"/>, <see cref="Max"/>
/// and <see cref="Clamp"/> (except which zero they return for operands that are zeros of both signs,
/// which IEEE leaves open), <see cref="Saturate"/>, <see cref="Sign"/>, the rounding functions,
/// <see cref="Fract"/>, <see cref="Fmod"/>, <see cref="Sqrt"/>, <see cref="Rsqrt"/>,
/// <see cref="Lerp"/>, <see cref="Step"/>, <see cref="Smoothstep"/> and <see cref="CopySign"/>.
/// The transcendental functions are computed in <c>float</c> and rounded to half; the GPU's are
/// within 1 half ULP of that, for every input measured (its <c>tan</c> and <c>atan</c> are off by
/// that one ULP for about 4% and 7% of all halves; the others for a handful).
/// </para>
/// <para>
/// A <c>float</c> has 24 significant bits and a half 11. For <c>+ - * /</c> and <c>sqrt</c> a result
/// rounded to 24 bits and then to 11 is the correctly rounded half (24 ≥ 2 × 11 + 2), which is why
/// <see cref="Half"/>'s own operators, which compute in <c>float</c>, are exact here. A fused
/// multiply-add is not covered by that: <see cref="Fma"/> does its own rounding.
/// </para>
/// </remarks>
internal static class HalfMath
{
    /// <summary>-2, for <c>reflect</c>.</summary>
    public static readonly Half MinusTwo = (Half)(-2f);

    private static readonly Half Zero = (Half)0f;
    private static readonly Half One = (Half)1f;
    private static readonly Half MinusOne = (Half)(-1f);
    private static readonly Half Two = (Half)2f;
    private static readonly Half Three = (Half)3f;

    /// <summary>The largest half below 1 (<c>0x1.ffcp-1</c>), which <c>fract</c> never exceeds.</summary>
    private static readonly Half BelowOne = BitConverter.UInt16BitsToHalf(0x3BFF);

    // ---- fused multiply-add ------------------------------------------------------------------

    /// <summary>
    /// <c>x * y + z</c> rounded once to half (IEEE fusedMultiplyAdd), which is what <c>metal::fma</c>
    /// returns and what the GPU makes of an <c>a * b + c</c> it contracts.
    /// </summary>
    /// <remarks>
    /// The product of two halves has 22 significant bits and is exact in a <c>double</c>, but the sum
    /// with <paramref name="z"/> can need about 80 and is not, and rounding that sum first to 53 bits
    /// and then to 11 can round the wrong way at a tie. So the sum is rounded to odd before it is
    /// rounded to half: its exact error comes from the classic two-sum, and when there is one, the
    /// last bit of the double is forced to 1, which keeps the sum on the right side of every half
    /// tie. A <c>float</c> fma rounded to half does not do: it is off by one ULP for about one
    /// operand set in 50,000.
    /// </remarks>
    public static Half Fma(Half x, Half y, Half z)
    {
        double product = (double)x * (double)y;
        double addend = (double)z;
        double sum = product + addend;

        if (!double.IsFinite(sum) || sum == 0d)
        {
            // Infinity or NaN, or an exact zero: there is nothing to round.
            return (Half)sum;
        }

        double partial = sum - product;
        double error = (product - (sum - partial)) + (addend - partial);

        if (error != 0d)
        {
            long bits = BitConverter.DoubleToInt64Bits(sum);

            if ((bits & 1) == 0)
            {
                // The exact sum lies beyond `sum` (away from zero) or short of it (towards zero); either
                // way the neighbouring double on that side has an odd last bit.
                bits += (error > 0d) == (sum > 0d) ? 1 : -1;
                sum = BitConverter.Int64BitsToDouble(bits);
            }
        }

        return (Half)sum;
    }

    // ---- abs / min / max / clamp -------------------------------------------------------------

    /// <summary>Absolute value: the sign bit cleared, so <c>abs(-0)</c> is <c>+0</c> and a NaN keeps its payload.</summary>
    public static Half Abs(Half x) => BitConverter.UInt16BitsToHalf((ushort)(BitConverter.HalfToUInt16Bits(x) & 0x7FFF));

    /// <summary>Minimum (<c>fmin</c> semantics): if exactly one argument is NaN, returns the other.</summary>
    public static Half Min(Half x, Half y) => (Half)float.MinNumber((float)x, (float)y);

    /// <summary>Maximum (<c>fmax</c> semantics): if exactly one argument is NaN, returns the other.</summary>
    public static Half Max(Half x, Half y) => (Half)float.MaxNumber((float)x, (float)y);

    /// <summary><c>fmin(fmax(x, minValue), maxValue)</c>; a NaN <paramref name="x"/> yields <paramref name="minValue"/>.</summary>
    public static Half Clamp(Half x, Half minValue, Half maxValue) => Min(Max(x, minValue), maxValue);

    /// <summary><c>clamp(x, 0, 1)</c>. NaN yields 0 and <c>-0</c> yields <c>+0</c>.</summary>
    public static Half Saturate(Half x) => Clamp(x, Zero, One);

    /// <summary>1 if <paramref name="x"/> &gt; 0, -1 if &lt; 0, otherwise <c>+0</c> (also for <c>-0</c> and NaN).</summary>
    public static Half Sign(Half x) => x > Zero ? One : x < Zero ? MinusOne : Zero;

    // ---- rounding ----------------------------------------------------------------------------
    // Every integer a half can round to is itself a half, so going through float is exact.

    /// <summary>Largest integer not greater than <paramref name="x"/>.</summary>
    public static Half Floor(Half x) => (Half)MathF.Floor((float)x);

    /// <summary>Smallest integer not less than <paramref name="x"/>.</summary>
    public static Half Ceil(Half x) => (Half)MathF.Ceiling((float)x);

    /// <summary>Nearest integer, halfway cases away from zero; a zero result is always <c>+0</c>.</summary>
    public static Half Round(Half x) => (Half)Gpu.Round((float)x);

    /// <summary>Rounds toward zero; keeps the sign of zero.</summary>
    public static Half Trunc(Half x) => (Half)MathF.Truncate((float)x);

    /// <summary>
    /// <c>min(x - floor(x), 0x1.ffcp-1)</c>, the subtraction rounded to half: tiny negative inputs give the
    /// largest half below 1 (0.99951172), not 1. Infinities yield 0; NaN stays NaN.
    /// </summary>
    public static Half Fract(Half x) => Half.IsInfinity(x) ? Zero : (Half)MathF.Min((float)(x - Floor(x)), (float)BelowOne);

    /// <summary>
    /// C <c>fmod</c>: the exact remainder <c>x - y * trunc(x / y)</c>, which is always a half. It has
    /// the sign of <paramref name="x"/>, except that a zero remainder of a non-zero <paramref name="x"/>
    /// is <c>+0</c> (<c>fmod(-4, 2) == +0</c>, where the float overload returns <c>-0</c>); a zero
    /// <paramref name="x"/> keeps its sign.
    /// </summary>
    public static Half Fmod(Half x, Half y)
    {
        Half remainder = (Half)((float)x % (float)y);
        return remainder == Zero && x != Zero ? Zero : remainder;
    }

    // ---- powers, exponentials, logarithms ----------------------------------------------------

    /// <summary>Correctly rounded square root; <c>sqrt(-0) == -0</c>, negative inputs yield NaN.</summary>
    public static Half Sqrt(Half x) => (Half)MathF.Sqrt((float)x);

    /// <summary><c>1 / sqrt(x)</c>, rounded once. <c>rsqrt(+0) == +inf</c>, <c>rsqrt(-0) == -inf</c>.</summary>
    public static Half Rsqrt(Half x) => (Half)(1.0 / Math.Sqrt((double)x));

    public static Half Pow(Half x, Half y) => (Half)MathF.Pow((float)x, (float)y);

    public static Half Exp(Half x) => (Half)MathF.Exp((float)x);

    public static Half Exp2(Half x) => (Half)float.Exp2((float)x);

    public static Half Exp10(Half x) => (Half)float.Exp10((float)x);

    public static Half Log(Half x) => (Half)MathF.Log((float)x);

    public static Half Log2(Half x) => (Half)MathF.Log2((float)x);

    public static Half Log10(Half x) => (Half)MathF.Log10((float)x);

    // ---- trigonometry ------------------------------------------------------------------------
    // sin, tan and asin of -0 are +0 on the GPU; atan, sinh and tanh keep the sign of a zero.

    public static Half Sin(Half x) => PositiveZero((Half)MathF.Sin((float)x));

    public static Half Cos(Half x) => (Half)MathF.Cos((float)x);

    public static Half Tan(Half x) => PositiveZero((Half)MathF.Tan((float)x));

    public static Half Asin(Half x) => PositiveZero((Half)MathF.Asin((float)x));

    public static Half Acos(Half x) => (Half)MathF.Acos((float)x);

    public static Half Atan(Half x) => (Half)MathF.Atan((float)x);

    public static Half Atan2(Half y, Half x) => (Half)MathF.Atan2((float)y, (float)x);

    public static Half Sinh(Half x) => (Half)MathF.Sinh((float)x);

    public static Half Cosh(Half x) => (Half)MathF.Cosh((float)x);

    public static Half Tanh(Half x) => (Half)MathF.Tanh((float)x);

    private static Half PositiveZero(Half x) => x == Zero ? Zero : x;

    // ---- interpolation -----------------------------------------------------------------------

    /// <summary>
    /// MSL <c>mix</c>, <c>x + (y - x) * a</c>, evaluated as the GPU does: <c>fma(a, y, fma(-a, x, x))</c>
    /// with both steps rounded to half, so <c>a == 0</c> and <c>a == 1</c> return the endpoints exactly.
    /// </summary>
    public static Half Lerp(Half x, Half y, Half a) => Fma(a, y, Fma(-a, x, x));

    /// <summary>1 if <paramref name="x"/> &gt;= <paramref name="edge"/>, otherwise 0 (also 0 when either is NaN).</summary>
    public static Half Step(Half edge, Half x) => x >= edge ? One : Zero;

    /// <summary>
    /// Hermite interpolation: <c>t = clamp((x - edge0) / (edge1 - edge0), 0, 1); t * t * (3 - 2 * t)</c>,
    /// every step rounded to half and none of them fused (measured: the GPU does not contract here).
    /// </summary>
    public static Half Smoothstep(Half edge0, Half edge1, Half x)
    {
        Half t = Clamp((x - edge0) / (edge1 - edge0), Zero, One);
        return t * t * (Three - (Two * t));
    }

    /// <summary>The magnitude of <paramref name="x"/> with the sign bit of <paramref name="y"/>: pure bit manipulation, so NaN payloads and subnormals pass through.</summary>
    public static Half CopySign(Half x, Half y) => BitConverter.UInt16BitsToHalf(
        (ushort)((BitConverter.HalfToUInt16Bits(x) & 0x7FFF) | (BitConverter.HalfToUInt16Bits(y) & 0x8000)));
}
