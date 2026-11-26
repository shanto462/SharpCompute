using System;

namespace SharpCompute.Differential.Tests;

/// <summary>
/// The helpers of the model shaders: what an Apple GPU does that the C# of a case does not say.
/// The model shaders run with <c>ForCpu</c> only, so these run as C#; they are written in the shader
/// language anyway, because the generator translates every shader of the project.
/// </summary>
/// <remarks>All members are pure functions of their arguments and so thread-safe.</remarks>
public static class GpuModel
{
    /// <summary>A subnormal float as the GPU's float arithmetic reads and writes it: a zero of the same sign.</summary>
    public static float Flush(float value)
    {
        uint bits = Gpu.AsUInt(value);
        return (bits & 0x7F800000u) == 0 ? Gpu.AsFloat(bits & 0x80000000u) : value;
    }

    public static Float2 Flush(Float2 value) => new(Flush(value.X), Flush(value.Y));

    public static Float3 Flush(Float3 value) => new(Flush(value.X), Flush(value.Y), Flush(value.Z));

    public static Float4 Flush(Float4 value) => new(Flush(value.X), Flush(value.Y), Flush(value.Z), Flush(value.W));

    // The smallest normal float is 2^-126. A result that is exactly ±2^-126 after rounding is flushed
    // by the GPU when its exact value is below 2^-126 in magnitude: it decides tininess before
    // rounding. The three functions below decide that from the operands, exactly and in floats
    // (the shader language has no double). Each scales the operands by a power of two, which is
    // exact, to where a float has its full precision and the rounding error of a product is itself a
    // float (Gpu.Fma(a, b, -(a * b)) is that error). Overflow of a scaled value can only happen where
    // the result is not ±2^-126, and then the scaled values are not used.

    /// <summary>
    /// The product <paramref name="result"/> = <c>a * b</c> as the GPU gives it: flushed when it is
    /// subnormal, and when it is ±2^-126 and <c>a * b</c> is exactly below that.
    /// </summary>
    public static float FlushProduct(float result, float a, float b)
    {
        // The operand of the smaller magnitude is at most 2^-63, so 2^30 times it does not overflow.
        bool first = Gpu.Abs(a) <= Gpu.Abs(b);
        float small = (first ? a : b) * 1073741824f;
        float large = first ? b : a;
        float product = small * large;
        float error = Gpu.Fma(small, large, -product);

        return IsSmallestNormal(result) && IsBelow(product, error, Gpu.AsFloat(0x0F800000u)) ? Zero(result) : Flush(result);
    }

    public static Float2 FlushProduct(Float2 result, Float2 a, Float2 b) => new(FlushProduct(result.X, a.X, b.X), FlushProduct(result.Y, a.Y, b.Y));

    public static Float3 FlushProduct(Float3 result, Float3 a, Float3 b) => new(FlushProduct(result.X, a.X, b.X), FlushProduct(result.Y, a.Y, b.Y), FlushProduct(result.Z, a.Z, b.Z));

    public static Float4 FlushProduct(Float4 result, Float4 a, Float4 b) =>
        new(FlushProduct(result.X, a.X, b.X), FlushProduct(result.Y, a.Y, b.Y), FlushProduct(result.Z, a.Z, b.Z), FlushProduct(result.W, a.W, b.W));

    /// <summary>
    /// The quotient <paramref name="result"/> = <c>a / b</c> as the GPU gives it: flushed when it is
    /// subnormal, and when it is ±2^-126 and <c>a / b</c> is exactly below that, which is
    /// <c>|a| &lt; 2^-126 |b|</c>. Both sides are exact as <c>2^64 |a|</c> and <c>2^-62 |b|</c>: for
    /// such a quotient <c>|a|</c> is at most 4 and <c>|b|</c> at least 2^-23.
    /// </summary>
    public static float FlushQuotient(float result, float a, float b) =>
        IsSmallestNormal(result) && Gpu.Abs(a) * 18446744073709551616f < Gpu.Abs(b) * Gpu.AsFloat(0x20800000u) ? Zero(result) : Flush(result);

    public static Float2 FlushQuotient(Float2 result, Float2 a, Float2 b) => new(FlushQuotient(result.X, a.X, b.X), FlushQuotient(result.Y, a.Y, b.Y));

    public static Float3 FlushQuotient(Float3 result, Float3 a, Float3 b) => new(FlushQuotient(result.X, a.X, b.X), FlushQuotient(result.Y, a.Y, b.Y), FlushQuotient(result.Z, a.Z, b.Z));

    public static Float4 FlushQuotient(Float4 result, Float4 a, Float4 b) =>
        new(FlushQuotient(result.X, a.X, b.X), FlushQuotient(result.Y, a.Y, b.Y), FlushQuotient(result.Z, a.Z, b.Z), FlushQuotient(result.W, a.W, b.W));

    /// <summary>
    /// The fused multiply-add <paramref name="result"/> = <c>a * b + c</c> as the GPU gives it:
    /// flushed when it is subnormal, and when it is ±2^-126 and <c>a * b + c</c> is exactly below that.
    /// </summary>
    /// <remarks>
    /// A sum of ±2^-126 leaves <c>c</c> and <c>a * b</c> at most 2^-77 in magnitude (the product has
    /// 48 bits), so everything is scaled by 2^60, to a sum of ±2^-66. The error of the scaled fused
    /// multiply-add is computed exactly (Boldo and Muller's ErrFma: the product as two floats, two
    /// exact sums, and the difference from the rounded result); its sign says on which side of
    /// ±2^-66 the exact sum is. Where the scaled product is below 2^-100 its own error is not a
    /// float, but then <c>c</c> is ±2^-126 itself and the sign of the product decides.
    /// </remarks>
    public static float FlushFma(float result, float a, float b, float c)
    {
        bool first = Gpu.Abs(a) <= Gpu.Abs(b);
        float small = (first ? a : b) * 1152921504606846976f;
        float large = first ? b : a;
        float addend = c * 1152921504606846976f;
        float sum = Gpu.Fma(small, large, addend);

        // small * large = high + low, exactly.
        float high = small * large;
        float low = Gpu.Fma(small, large, -high);

        // addend + low = s1 + e1 and high + s1 = s2 + e2, exactly (two-sums).
        float s1 = addend + low;
        float t1 = s1 - addend;
        float e1 = (addend - (s1 - t1)) + (low - t1);
        float s2 = high + s1;
        float t2 = s2 - high;
        float e2 = (high - (s2 - t2)) + (s1 - t2);

        // The exact sum minus the rounded one; its first float has its sign.
        float error = ((s2 - sum) + e2) + e1;
        float productSign = small == 0f || large == 0f ? 0f : ((small > 0f) == (large > 0f) ? 1f : -1f);

        return IsSmallestNormal(result) && IsBelow(sum, Gpu.Abs(high) < Gpu.AsFloat(0x0D800000u) ? productSign : error, Gpu.AsFloat(0x1E800000u))
            ? Zero(result)
            : Flush(result);
    }

    public static Float2 FlushFma(Float2 result, Float2 a, Float2 b, Float2 c) => new(FlushFma(result.X, a.X, b.X, c.X), FlushFma(result.Y, a.Y, b.Y, c.Y));

    public static Float3 FlushFma(Float3 result, Float3 a, Float3 b, Float3 c) =>
        new(FlushFma(result.X, a.X, b.X, c.X), FlushFma(result.Y, a.Y, b.Y, c.Y), FlushFma(result.Z, a.Z, b.Z, c.Z));

    public static Float4 FlushFma(Float4 result, Float4 a, Float4 b, Float4 c) =>
        new(FlushFma(result.X, a.X, b.X, c.X), FlushFma(result.Y, a.Y, b.Y, c.Y), FlushFma(result.Z, a.Z, b.Z, c.Z), FlushFma(result.W, a.W, b.W, c.W));

    /// <summary>True for ±2^-126, the smallest normal float.</summary>
    private static bool IsSmallestNormal(float value) => (Gpu.AsUInt(value) & 0x7FFFFFFFu) == 0x00800000u;

    /// <summary>The zero of the sign of <paramref name="value"/>.</summary>
    private static float Zero(float value) => Gpu.AsFloat(Gpu.AsUInt(value) & 0x80000000u);

    /// <summary>
    /// Whether an exact value is below <paramref name="bound"/> in magnitude, given the float it
    /// rounds to and the sign of what the rounding left out (<paramref name="error"/>, exact value
    /// minus <paramref name="rounded"/>): it is when the float is below the bound, or is the bound
    /// and was rounded away from zero.
    /// </summary>
    private static bool IsBelow(float rounded, float error, float bound) =>
        Gpu.Abs(rounded) < bound || (Gpu.Abs(rounded) == bound && (rounded > 0f ? error < 0f : error > 0f));

    /// <summary>
    /// A half fused multiply-add, rounded once: what the GPU makes of <c>a * b + c</c> on halves.
    /// There is no scalar overload of <see cref="Gpu.Fma(Half2, Half2, Half2)"/>, so it goes through one.
    /// </summary>
    public static Half Fma(Half a, Half b, Half c) => Gpu.Fma(new Half2(a), new Half2(b), new Half2(c)).X;
}
