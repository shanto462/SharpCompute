// Original SharpCompute sample (MIT).
using System;
using System.Threading.Tasks;

namespace SharpCompute.Samples.Imaging;

/// <summary>
/// Conversions between the filters' premultiplied <see cref="Float4"/> pixels and 8-bit RGBA:
/// premultiplied bytes for CoreGraphics, straight (unpremultiplied) <see cref="Rgba32"/> for PNG.
/// </summary>
public static class PixelConversion
{
    private const int ParallelThreshold = 1 << 16;

    /// <summary>Premultiplied RGBA8 bytes (CoreGraphics' <c>kCGImageAlphaPremultipliedLast</c>) to premultiplied floats.</summary>
    public static Float4[] FromPremultipliedRgba8(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % 4 != 0)
        {
            throw new ArgumentException("RGBA8 data must be a whole number of 4-byte pixels.", nameof(bytes));
        }

        Float4[] pixels = new Float4[bytes.Length / 4];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Unpack(bytes.Slice(i * 4, 4));
        }

        return pixels;
    }

    /// <summary>Like <see cref="FromPremultipliedRgba8(ReadOnlySpan{byte})"/>, in parallel, into an existing array of <c>bytes.Length / 4</c> pixels.</summary>
    public static void FromPremultipliedRgba8(byte[] bytes, Float4[] pixels)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(pixels);

        if (bytes.Length != pixels.Length * 4)
        {
            throw new ArgumentException($"Expected {pixels.Length * 4} bytes for {pixels.Length} pixels, got {bytes.Length}.", nameof(bytes));
        }

        For(pixels.Length, i => pixels[i] = Unpack(bytes.AsSpan(i * 4, 4)));
    }

    private static Float4 Unpack(ReadOnlySpan<byte> p) => new(p[0] / 255f, p[1] / 255f, p[2] / 255f, p[3] / 255f);

    /// <summary>Straight-alpha RGBA8 (PNG's encoding) to premultiplied floats.</summary>
    public static Float4[] FromStraightRgba32(ReadOnlySpan<Rgba32> source)
    {
        Float4[] pixels = new Float4[source.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            Rgba32 p = source[i];
            float a = p.A / 255f;
            pixels[i] = new Float4(p.R / 255f * a, p.G / 255f * a, p.B / 255f * a, a);
        }

        return pixels;
    }

    /// <summary>Premultiplied floats to premultiplied RGBA8 bytes, rounding to nearest.</summary>
    public static byte[] ToPremultipliedRgba8(Float4[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        byte[] bytes = new byte[pixels.Length * 4];
        ToPremultipliedRgba8(pixels, bytes);
        return bytes;
    }

    /// <summary>Like <see cref="ToPremultipliedRgba8(Float4[])"/>, into an existing array of <c>4 * pixels.Length</c> bytes.</summary>
    public static void ToPremultipliedRgba8(Float4[] pixels, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length != pixels.Length * 4)
        {
            throw new ArgumentException($"Expected {pixels.Length * 4} bytes for {pixels.Length} pixels, got {bytes.Length}.", nameof(bytes));
        }

        For(pixels.Length, i =>
        {
            Float4 p = pixels[i];
            float a = Gpu.Saturate(p.W);
            int b = i * 4;
            bytes[b] = Quantize(Math.Min(p.X, a));
            bytes[b + 1] = Quantize(Math.Min(p.Y, a));
            bytes[b + 2] = Quantize(Math.Min(p.Z, a));
            bytes[b + 3] = Quantize(a);
        });
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
