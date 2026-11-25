// Original SharpCompute sample (MIT).
using System;
using System.Threading.Tasks;

namespace SharpCompute.Samples.Imaging;

/// <summary>
/// Conversions between the filters' premultiplied <see cref="Float4"/> pixels and straight-alpha
/// (unpremultiplied) <see cref="Rgba32"/>, what PNG stores and what SharpCompute.Imaging loads and saves.
/// </summary>
public static class PixelConversion
{
    private const int ParallelThreshold = 1 << 16;

    /// <summary>Straight-alpha RGBA8 (PNG's encoding) to premultiplied floats.</summary>
    public static Float4[] FromStraightRgba32(ReadOnlySpan<Rgba32> source)
    {
        Float4[] pixels = new Float4[source.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = FromStraight(source[i]);
        }

        return pixels;
    }

    private static Float4 FromStraight(Rgba32 p)
    {
        float a = p.A / 255f;
        return new Float4(p.R / 255f * a, p.G / 255f * a, p.B / 255f * a, a);
    }

    /// <summary>Like <see cref="FromStraightRgba32(ReadOnlySpan{Rgba32})"/>, in parallel, into an existing array of the same length.</summary>
    public static void FromStraightRgba32(Rgba32[] source, Float4[] pixels)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pixels);

        if (source.Length != pixels.Length)
        {
            throw new ArgumentException($"Expected {pixels.Length} pixels, got {source.Length}.", nameof(source));
        }

        For(pixels.Length, i => pixels[i] = FromStraight(source[i]));
    }

    /// <summary>Premultiplied floats to straight-alpha <see cref="Rgba32"/> (what PNG stores), rounding to nearest.</summary>
    public static Rgba32[] ToStraightRgba32(Float4[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        Rgba32[] result = new Rgba32[pixels.Length];
        ToStraightRgba32(pixels, result);
        return result;
    }

    /// <summary>Like <see cref="ToStraightRgba32(Float4[])"/>, into an existing array of the same length.</summary>
    public static void ToStraightRgba32(Float4[] pixels, Rgba32[] result)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Length != pixels.Length)
        {
            throw new ArgumentException($"Expected {pixels.Length} pixels, got {result.Length}.", nameof(result));
        }

        For(pixels.Length, i => result[i] = ToStraight(pixels[i]));
    }

    public static Rgba32 ToStraight(Float4 p)
    {
        float a = Gpu.Saturate(p.W);
        byte alpha = Quantize(a);

        if (alpha == 0)
        {
            return default;
        }

        return new Rgba32(Quantize(p.X / a), Quantize(p.Y / a), Quantize(p.Z / a), alpha);
    }

    /// <summary>Maps 0..1 to 0..255, clamping and rounding to nearest (NaN → 0).</summary>
    public static byte Quantize(float value) => float.IsNaN(value) ? (byte)0 : (byte)((Gpu.Saturate(value) * 255f) + 0.5f);

    private static void For(int count, Action<int> body)
    {
        if (count < ParallelThreshold)
        {
            for (int i = 0; i < count; i++)
            {
                body(i);
            }

            return;
        }

        const int chunk = 4096;
        Parallel.For(0, (count + chunk - 1) / chunk, c =>
        {
            for (int i = c * chunk, end = Math.Min(i + chunk, count); i < end; i++)
            {
                body(i);
            }
        });
    }
}
