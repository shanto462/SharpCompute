using System;
using System.Buffers.Binary;
using System.Numerics;

namespace SharpCompute;

/// <summary>
/// GPU math intrinsics. Each method maps to an MSL function via <see cref="MslIntrinsicAttribute"/>
/// and has a CPU implementation with the same semantics, so shader logic can be unit-tested on the CPU.
/// </summary>
/// <remarks>
/// <para>
/// Scalar overloads live here; the componentwise vector overloads and the geometric functions are
/// generated into <c>Gpu.g.cs</c>, and the matrix functions into <c>Gpu.Matrices.g.cs</c>, by
/// <c>tools/gen_vectors.py</c>, which also generates the overloads of the integer intrinsics for
/// <c>byte</c>, <c>sbyte</c>, <c>short</c>, <c>ushort</c>, <c>long</c> and <c>ulong</c> and the rotates
/// into <c>Gpu.Integers.g.cs</c>; the atomics are in <c>Gpu.Atomics.cs</c>. C# names are the
/// PascalCase MSL names, except where .NET or HLSL already has a name for the function:
/// <c>metal::mix</c> is <see cref="Lerp(float, float, float)"/>, <c>popcount</c>, <c>clz</c> and
/// <c>ctz</c> are <see cref="CountBits(uint)"/>, <see cref="LeadingZeroCount(uint)"/> and
/// <see cref="TrailingZeroCount(uint)"/>, <c>isinf</c> is <see cref="IsInfinity(float)"/>, and the
/// unorm8 packing functions are <see cref="PackUnorm4x8(Float4)"/> and <see cref="UnpackUnorm4x8(uint)"/>.
/// </para>
/// <para>
/// The CPU implementations were compared bit for bit against an Apple GPU with the runtime's compile
/// options (MSL 3.2, <c>mathMode = Safe</c>, <c>mathFloatingPointFunctions = Precise</c>) on 550k
/// inputs, including NaN, infinities and signed zeros. Bit-exact: Abs, Sign, Saturate, Floor, Ceil,
/// Round, Trunc, Fract, Fmod, Sqrt, Rsqrt, Step, Smoothstep, Lerp, Length, Distance, and the
/// float/int/uint conversions; Min/Max/Clamp too, except for which zero <c>min(-0, +0)</c> returns
/// (IEEE leaves this unspecified). Dot, Cross, Reflect and Normalize differ only for results near
/// the subnormal range.
/// </para>
/// <para>
/// Also bit-exact, and covered by GPU-vs-CPU tests: the bit reinterpretations (AsFloat, AsInt,
/// AsUInt, which keep NaN payloads and subnormals on the GPU too), CountBits, LeadingZeroCount,
/// TrailingZeroCount, ReverseBits, Fma, CopySign, IsNaN, IsInfinity, IsFinite, PackUnorm4x8 and
/// UnpackUnorm4x8. Log10 and Exp10 agree within a few ULP, like the other transcendentals.
/// </para>
/// <para>
/// Where the hardware differs from the textbook definition and the difference is cheap to reproduce,
/// the CPU copies the hardware: <c>+0</c> from <see cref="Sign(float)"/>, <see cref="Round(float)"/>
/// and <see cref="Atan(float)"/> for zero results, 0 from <see cref="Step(float, float)"/> for NaN,
/// the clamp and infinity handling of <see cref="Fract(float)"/>, and the fma evaluation of
/// <see cref="Lerp(float, float, float)"/>, Dot, Cross and Reflect.
/// </para>
/// <para>
/// Known differences that are not reproduced: Apple GPUs flush float subnormals to zero (inputs and
/// results, also in safe math); pow/exp/log/trigonometric/hyperbolic functions agree within a few
/// ULP only (up to 4 ULP for tan on Apple GPUs).
/// </para>
/// </remarks>
public static partial class Gpu
{
    // ---- abs / min / max / clamp -------------------------------------------------------------

    /// <summary>Absolute value; <c>abs(-0)</c> is <c>+0</c>.</summary>
    [MslIntrinsic("metal::abs")]
    public static float Abs(float x) => MathF.Abs(x);

    /// <summary>Absolute value; wraps for <see cref="int.MinValue"/> (no overflow exception), like MSL.</summary>
    [MslIntrinsic("metal::abs")]
    public static int Abs(int x) => x < 0 ? unchecked(-x) : x;

    /// <summary>Absolute value of an unsigned integer (the identity).</summary>
    [MslIntrinsic("metal::abs")]
    public static uint Abs(uint x) => x;

    /// <summary>Minimum (<c>fmin</c> semantics): if exactly one argument is NaN, returns the other.</summary>
    [MslIntrinsic("metal::min")]
    public static float Min(float x, float y) => float.MinNumber(x, y);

    /// <summary>Minimum.</summary>
    [MslIntrinsic("metal::min")]
    public static int Min(int x, int y) => Math.Min(x, y);

    /// <summary>Minimum.</summary>
    [MslIntrinsic("metal::min")]
    public static uint Min(uint x, uint y) => Math.Min(x, y);

    /// <summary>Maximum (<c>fmax</c> semantics): if exactly one argument is NaN, returns the other.</summary>
    [MslIntrinsic("metal::max")]
    public static float Max(float x, float y) => float.MaxNumber(x, y);

    /// <summary>Maximum.</summary>
    [MslIntrinsic("metal::max")]
    public static int Max(int x, int y) => Math.Max(x, y);

    /// <summary>Maximum.</summary>
    [MslIntrinsic("metal::max")]
    public static uint Max(uint x, uint y) => Math.Max(x, y);

    /// <summary>
    /// <c>fmin(fmax(x, minValue), maxValue)</c>. A NaN <paramref name="x"/> yields
    /// <paramref name="minValue"/>. Does not throw when <paramref name="minValue"/> &gt;
    /// <paramref name="maxValue"/> (undefined in MSL; this returns <paramref name="maxValue"/>).
    /// </summary>
    [MslIntrinsic("metal::clamp")]
    public static float Clamp(float x, float minValue, float maxValue) => Min(Max(x, minValue), maxValue);

    /// <summary><c>min(max(x, minValue), maxValue)</c>; does not throw when the bounds are inverted.</summary>
    [MslIntrinsic("metal::clamp")]
    public static int Clamp(int x, int minValue, int maxValue) => Math.Min(Math.Max(x, minValue), maxValue);

    /// <summary><c>min(max(x, minValue), maxValue)</c>; does not throw when the bounds are inverted.</summary>
    [MslIntrinsic("metal::clamp")]
    public static uint Clamp(uint x, uint minValue, uint maxValue) => Math.Min(Math.Max(x, minValue), maxValue);

    /// <summary><c>clamp(x, 0, 1)</c>. NaN yields 0 and <c>-0</c> yields <c>+0</c>.</summary>
    [MslIntrinsic("metal::saturate")]
    public static float Saturate(float x) => Clamp(x, 0f, 1f);

    /// <summary>1 if <paramref name="x"/> &gt; 0, -1 if &lt; 0, otherwise <c>+0</c> (also for <c>-0</c> and NaN, as on Apple GPUs).</summary>
    [MslIntrinsic("metal::sign")]
    public static float Sign(float x) => x > 0f ? 1f : x < 0f ? -1f : 0f;

    // ---- rounding ----------------------------------------------------------------------------

    /// <summary>Largest integer not greater than <paramref name="x"/>.</summary>
    [MslIntrinsic("metal::floor")]
    public static float Floor(float x) => MathF.Floor(x);

    /// <summary>Smallest integer not less than <paramref name="x"/>.</summary>
    [MslIntrinsic("metal::ceil")]
    public static float Ceil(float x) => MathF.Ceiling(x);

    /// <summary>
    /// Nearest integer, halfway cases away from zero (<c>round(-2.5) == -3</c>). A zero result is
    /// always <c>+0</c> (<c>round(-0.4) == +0</c>), as on Apple GPUs.
    /// </summary>
    [MslIntrinsic("metal::round")]
    public static float Round(float x)
    {
        float r = MathF.Round(x, MidpointRounding.AwayFromZero);
        return r == 0f ? 0f : r;
    }

    /// <summary>Rounds toward zero; keeps the sign of zero (<c>trunc(-0.5) == -0</c>).</summary>
    [MslIntrinsic("metal::trunc")]
    public static float Trunc(float x) => MathF.Truncate(x);

    /// <summary>
    /// <c>min(x - floor(x), 0x1.fffffep-1)</c>: the result is clamped below 1, so tiny negative inputs
    /// give <c>0.99999994</c> (<c>fract(-1e-10)</c>). Infinities yield 0 (as on Apple GPUs); NaN stays NaN.
    /// </summary>
    [MslIntrinsic("metal::fract")]
    public static float Fract(float x) => float.IsInfinity(x) ? 0f : MathF.Min(x - MathF.Floor(x), 0.99999994f);

    /// <summary>
    /// C <c>fmod</c>: the exact remainder <c>x - y * trunc(x / y)</c>, with the sign of
    /// <paramref name="x"/> (<c>fmod(-5.5, 2) == -1.5</c>, <c>fmod(5.5, -2) == 1.5</c>,
    /// <c>fmod(-4, 2) == -0</c>, <c>fmod(5.5, inf) == 5.5</c>). The same as C# <c>%</c> on floats and the
    /// float vector <c>%</c> operators; bit-exact with <c>metal::fmod</c> under precise math.
    /// </summary>
    [MslIntrinsic("metal::fmod")]
    public static float Fmod(float x, float y) => x % y;

    // ---- powers, exponentials, logarithms ----------------------------------------------------

    /// <summary>Correctly rounded square root; <c>sqrt(-0) == -0</c>, negative inputs yield NaN.</summary>
    [MslIntrinsic("metal::sqrt")]
    public static float Sqrt(float x) => MathF.Sqrt(x);

    /// <summary>
    /// <c>1 / sqrt(x)</c>, rounded once (computed in double, as precise on Apple GPUs; a float
    /// <c>1f / MathF.Sqrt(x)</c> is off by 1 ULP for about 10% of inputs). <c>rsqrt(+0) == +inf</c>,
    /// <c>rsqrt(-0) == -inf</c>.
    /// </summary>
    [MslIntrinsic("metal::rsqrt")]
    public static float Rsqrt(float x) => (float)(1.0 / Math.Sqrt(x));

    /// <summary><paramref name="x"/> raised to <paramref name="y"/> (C <c>pow</c>, including negative bases with integral exponents).</summary>
    [MslIntrinsic("metal::pow")]
    public static float Pow(float x, float y) => MathF.Pow(x, y);

    /// <summary>e raised to <paramref name="x"/>.</summary>
    [MslIntrinsic("metal::exp")]
    public static float Exp(float x) => MathF.Exp(x);

    /// <summary>2 raised to <paramref name="x"/>.</summary>
    [MslIntrinsic("metal::exp2")]
    public static float Exp2(float x) => float.Exp2(x);

    /// <summary>Natural logarithm.</summary>
    [MslIntrinsic("metal::log")]
    public static float Log(float x) => MathF.Log(x);

    /// <summary>Base-2 logarithm.</summary>
    [MslIntrinsic("metal::log2")]
    public static float Log2(float x) => MathF.Log2(x);

    /// <summary>Base-10 logarithm.</summary>
    [MslIntrinsic("metal::log10")]
    public static float Log10(float x) => MathF.Log10(x);

    /// <summary>10 raised to <paramref name="x"/>.</summary>
    [MslIntrinsic("metal::exp10")]
    public static float Exp10(float x) => float.Exp10(x);

    // ---- fused multiply-add, sign, classification --------------------------------------------

    /// <summary><c>x * y + z</c> rounded once (IEEE fusedMultiplyAdd); bit-exact with <c>metal::fma</c>.</summary>
    [MslIntrinsic("metal::fma")]
    public static float Fma(float x, float y, float z) => MathF.FusedMultiplyAdd(x, y, z);

    /// <summary>
    /// The magnitude of <paramref name="x"/> with the sign bit of <paramref name="y"/>. Pure bit
    /// manipulation on both sides: the sign of a zero or NaN <paramref name="y"/> counts, a NaN
    /// <paramref name="x"/> keeps its payload, and subnormals pass through unflushed.
    /// </summary>
    [MslIntrinsic("metal::copysign")]
    public static float CopySign(float x, float y) => MathF.CopySign(x, y);

    /// <summary>True when <paramref name="x"/> is a NaN (any payload, either sign).</summary>
    [MslIntrinsic("metal::isnan")]
    public static bool IsNaN(float x) => float.IsNaN(x);

    /// <summary>True when <paramref name="x"/> is positive or negative infinity.</summary>
    [MslIntrinsic("metal::isinf")]
    public static bool IsInfinity(float x) => float.IsInfinity(x);

    /// <summary>True when <paramref name="x"/> is neither infinite nor NaN (zeros and subnormals are finite).</summary>
    [MslIntrinsic("metal::isfinite")]
    public static bool IsFinite(float x) => float.IsFinite(x);

    // ---- trigonometry ------------------------------------------------------------------------

    /// <summary>Sine (radians).</summary>
    [MslIntrinsic("metal::sin")]
    public static float Sin(float x) => MathF.Sin(x);

    /// <summary>Cosine (radians).</summary>
    [MslIntrinsic("metal::cos")]
    public static float Cos(float x) => MathF.Cos(x);

    /// <summary>Tangent (radians).</summary>
    [MslIntrinsic("metal::tan")]
    public static float Tan(float x) => MathF.Tan(x);

    /// <summary>Arc sine; NaN outside [-1, 1].</summary>
    [MslIntrinsic("metal::asin")]
    public static float Asin(float x) => MathF.Asin(x);

    /// <summary>Arc cosine; NaN outside [-1, 1].</summary>
    [MslIntrinsic("metal::acos")]
    public static float Acos(float x) => MathF.Acos(x);

    /// <summary>Arc tangent. A zero result is always <c>+0</c> (<c>atan(-0) == +0</c>), as on Apple GPUs.</summary>
    [MslIntrinsic("metal::atan")]
    public static float Atan(float x)
    {
        float r = MathF.Atan(x);
        return r == 0f ? 0f : r;
    }

    /// <summary>Arc tangent of <c>y / x</c> using the signs of both to pick the quadrant (argument order as in MSL and C).</summary>
    /// <remarks>Signed zeros follow C, also on the GPU: <c>atan2(+0, +0) == +0</c>, <c>atan2(+0, -0) == π</c>, <c>atan2(-0, +0) == -0</c>.</remarks>
    [MslIntrinsic("metal::atan2")]
    public static float Atan2(float y, float x) => MathF.Atan2(y, x);

    /// <summary>Hyperbolic sine.</summary>
    [MslIntrinsic("metal::sinh")]
    public static float Sinh(float x) => MathF.Sinh(x);

    /// <summary>Hyperbolic cosine.</summary>
    [MslIntrinsic("metal::cosh")]
    public static float Cosh(float x) => MathF.Cosh(x);

    /// <summary>Hyperbolic tangent; <c>tanh(±inf) == ±1</c>.</summary>
    [MslIntrinsic("metal::tanh")]
    public static float Tanh(float x) => MathF.Tanh(x);

    // ---- interpolation -----------------------------------------------------------------------

    /// <summary>
    /// Linear blend of <paramref name="x"/> and <paramref name="y"/> (MSL <c>mix</c>):
    /// <c>x + (y - x) * a</c>, evaluated as Apple GPUs do, <c>fma(a, y, fma(-a, x, x))</c>, so
    /// <c>a == 0</c> and <c>a == 1</c> return the endpoints exactly. <paramref name="a"/> is not clamped.
    /// </summary>
    [MslIntrinsic("metal::mix")]
    public static float Lerp(float x, float y, float a) => MathF.FusedMultiplyAdd(a, y, MathF.FusedMultiplyAdd(-a, x, x));

    /// <summary>1 if <paramref name="x"/> &gt;= <paramref name="edge"/>, otherwise 0 (also 0 when either is NaN, as on Apple GPUs).</summary>
    [MslIntrinsic("metal::step")]
    public static float Step(float edge, float x) => x >= edge ? 1f : 0f;

    /// <summary>
    /// Hermite interpolation: <c>t = clamp((x - edge0) / (edge1 - edge0), 0, 1); t * t * (3 - 2 * t)</c>.
    /// Returns 0 at or below <paramref name="edge0"/> and 1 at or above <paramref name="edge1"/>.
    /// </summary>
    [MslIntrinsic("metal::smoothstep")]
    public static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    // ---- bit reinterpretation -----------------------------------------------------------------
    // MSL as_type<T>: the same 32 bits under another type. No conversion happens, so every bit
    // pattern survives a round trip on the GPU too (NaN payloads, -0, subnormals).

    /// <summary>Reinterprets the bits of <paramref name="x"/> as a float (MSL <c>as_type&lt;float&gt;</c>).</summary>
    [MslIntrinsic("as_type<float>")]
    public static float AsFloat(int x) => BitConverter.Int32BitsToSingle(x);

    /// <summary>Reinterprets the bits of <paramref name="x"/> as a float (MSL <c>as_type&lt;float&gt;</c>).</summary>
    [MslIntrinsic("as_type<float>")]
    public static float AsFloat(uint x) => BitConverter.UInt32BitsToSingle(x);

    /// <summary>Reinterprets the bits of <paramref name="x"/> as an int (MSL <c>as_type&lt;int&gt;</c>).</summary>
    [MslIntrinsic("as_type<int>")]
    public static int AsInt(float x) => BitConverter.SingleToInt32Bits(x);

    /// <summary>Reinterprets the bits of <paramref name="x"/> as a uint (MSL <c>as_type&lt;uint&gt;</c>).</summary>
    [MslIntrinsic("as_type<uint>")]
    public static uint AsUInt(float x) => BitConverter.SingleToUInt32Bits(x);

    // ---- bit counting ------------------------------------------------------------------------
    // The result has the type of the argument, as in MSL (BitOperations returns int).

    /// <summary>The number of set bits (MSL <c>popcount</c>).</summary>
    [MslIntrinsic("metal::popcount")]
    public static int CountBits(int x) => BitOperations.PopCount((uint)x);

    /// <summary>The number of set bits (MSL <c>popcount</c>).</summary>
    [MslIntrinsic("metal::popcount")]
    public static uint CountBits(uint x) => (uint)BitOperations.PopCount(x);

    /// <summary>The number of zero bits above the highest set bit; 32 for 0, and 0 for negative values (MSL <c>clz</c>).</summary>
    [MslIntrinsic("metal::clz")]
    public static int LeadingZeroCount(int x) => BitOperations.LeadingZeroCount((uint)x);

    /// <summary>The number of zero bits above the highest set bit; 32 for 0 (MSL <c>clz</c>).</summary>
    [MslIntrinsic("metal::clz")]
    public static uint LeadingZeroCount(uint x) => (uint)BitOperations.LeadingZeroCount(x);

    /// <summary>The number of zero bits below the lowest set bit; 32 for 0 (MSL <c>ctz</c>).</summary>
    [MslIntrinsic("metal::ctz")]
    public static int TrailingZeroCount(int x) => BitOperations.TrailingZeroCount(x);

    /// <summary>The number of zero bits below the lowest set bit; 32 for 0 (MSL <c>ctz</c>).</summary>
    [MslIntrinsic("metal::ctz")]
    public static uint TrailingZeroCount(uint x) => (uint)BitOperations.TrailingZeroCount(x);

    /// <summary>Reverses the order of the 32 bits: bit 0 becomes bit 31 (MSL <c>reverse_bits</c>).</summary>
    [MslIntrinsic("metal::reverse_bits")]
    public static int ReverseBits(int x) => (int)ReverseBits((uint)x);

    /// <summary>Reverses the order of the 32 bits: bit 0 becomes bit 31 (MSL <c>reverse_bits</c>).</summary>
    [MslIntrinsic("metal::reverse_bits")]
    public static uint ReverseBits(uint x)
    {
        // Swap neighbours, pairs and nibbles within each byte, then the bytes.
        x = ((x >> 1) & 0x55555555u) | ((x & 0x55555555u) << 1);
        x = ((x >> 2) & 0x33333333u) | ((x & 0x33333333u) << 2);
        x = ((x >> 4) & 0x0F0F0F0Fu) | ((x & 0x0F0F0F0Fu) << 4);
        return BinaryPrimitives.ReverseEndianness(x);
    }

    // ---- unorm8 packing ----------------------------------------------------------------------

    /// <summary>
    /// Packs four floats into one <c>uint</c> of four unorm8 channels, <c>X</c> in the low byte
    /// (<c>x | y &lt;&lt; 8 | z &lt;&lt; 16 | w &lt;&lt; 24</c>): the layout of an <see cref="Rgba32"/> in memory. This is
    /// how to keep 8-bit colors in a buffer: there are no vectors of bytes, so four channels share a uint.
    /// </summary>
    /// <remarks>
    /// Each channel is converted the way the GPU writes a float to a unorm8 texture: NaN and negative
    /// values give 0, values of 1 and above give 255, and everything between is the integer nearest
    /// to the exact product <c>value × 255</c>. Verified against <c>metal::pack_float_to_unorm4x8</c>
    /// on an Apple GPU at all 255 rounding midpoints and their float neighbours.
    /// </remarks>
    [MslIntrinsic("metal::pack_float_to_unorm4x8")]
    public static uint PackUnorm4x8(Float4 x) =>
        CpuDispatcher.ToUnorm8(x.X) |
        ((uint)CpuDispatcher.ToUnorm8(x.Y) << 8) |
        ((uint)CpuDispatcher.ToUnorm8(x.Z) << 16) |
        ((uint)CpuDispatcher.ToUnorm8(x.W) << 24);

    /// <summary>
    /// Unpacks four unorm8 channels, <c>X</c> from the low byte: each is <c>byte / 255f</c>, correctly
    /// rounded, so 0 gives 0 and 255 gives exactly 1. Bit-exact with <c>metal::unpack_unorm4x8_to_float</c>
    /// for all 256 values, and <c>PackUnorm4x8(UnpackUnorm4x8(x)) == x</c> for every <paramref name="x"/>.
    /// </summary>
    [MslIntrinsic("metal::unpack_unorm4x8_to_float")]
    public static Float4 UnpackUnorm4x8(uint x) => new(
        (x & 0xFF) / 255f,
        ((x >> 8) & 0xFF) / 255f,
        ((x >> 16) & 0xFF) / 255f,
        (x >> 24) / 255f);

    // ---- geometric (non-generated) -----------------------------------------------------------

    /// <summary>Cross product, each component evaluated as <c>fma(a, b, -(c * d))</c> (as on Apple GPUs).</summary>
    [MslIntrinsic("metal::cross")]
    public static Float3 Cross(Float3 x, Float3 y) => new(
        MathF.FusedMultiplyAdd(x.Y, y.Z, -(x.Z * y.Y)),
        MathF.FusedMultiplyAdd(x.Z, y.X, -(x.X * y.Z)),
        MathF.FusedMultiplyAdd(x.X, y.Y, -(x.Y * y.X)));
}
