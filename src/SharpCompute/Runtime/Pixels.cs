using System;
using System.Runtime.InteropServices;

namespace SharpCompute;

/// <summary>An 8-bit single-channel pixel (<c>MTLPixelFormatR8Unorm</c>). Shaders read it as <c>(r, 0, 0, 1)</c>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct R8(byte r)
{
    public byte R = r;

    public override readonly string ToString() => $"R8({R})";
}

/// <summary>
/// An 8-bit-per-channel two-channel pixel (<c>MTLPixelFormatRG8Unorm</c>). Shaders read it as
/// <c>(r, g, 0, 1)</c>. Only <see cref="ReadOnlyTexture2D{T}"/> supports it: Apple GPUs cannot read
/// and write this format in the same shader.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Rg16(byte r, byte g)
{
    public byte R = r;

    public byte G = g;

    public override readonly string ToString() => $"Rg16({R}, {G})";
}

/// <summary>
/// An 8-bit-per-channel RGBA pixel stored in sRGB (<c>MTLPixelFormatRGBA8Unorm_sRGB</c>). Reads and
/// samples return linear values: the GPU (and the CPU implementation) decodes the color channels with
/// the sRGB transfer function, and alpha is linear. Only <see cref="ReadOnlyTexture2D{T}"/> supports it.
/// </summary>
/// <remarks>
/// The CPU decodes with the exact transfer function; the GPU decodes to 12 bits (measured on an
/// Apple M4 Pro), so the same pixel can read up to 1.2e-4 differently on the two.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Rgba32Srgb(byte r, byte g, byte b, byte a)
{
    public byte R = r;

    public byte G = g;

    public byte B = b;

    public byte A = a;

    public override readonly string ToString() => $"Rgba32Srgb({R}, {G}, {B}, {A})";
}

/// <summary>
/// A 16-bit float RGBA pixel (<c>MTLPixelFormatRGBA16Float</c>).
/// </summary>
/// <remarks>
/// <para>
/// A shader's write rounds each channel toward zero to a half, here and in a texture of
/// <see cref="System.Half"/> (<c>R16Float</c>). That is what an Apple M4 Pro was measured to do, not
/// IEEE's round-to-nearest, and the CPU (<c>ForCpu</c>) does the same so that both store the same bits.
/// </para>
/// <para>
/// The pixel is four halves in the order and layout of a <see cref="Half4"/> (8 bytes, 2-byte
/// aligned), and converts to and from one implicitly, without touching a channel. Whole spans
/// reinterpret for free: <c>MemoryMarshal.Cast&lt;Half4, Rgba16Float&gt;(colors)</c> uploads a
/// <see cref="Half4"/> array with <c>CopyFrom</c>, and the other way round after <c>CopyTo</c>. In a
/// shader a texture pixel is always a <see cref="Float4"/>; <c>(Half4)texture[x, y]</c> narrows it,
/// which is exact for this format because every channel it stores is a half already, and
/// <c>texture[x, y] = (Float4)color</c> stores a <see cref="Half4"/> bit for bit for the same reason.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Rgba16Float(Half r, Half g, Half b, Half a)
{
    public Half R = r;

    public Half G = g;

    public Half B = b;

    public Half A = a;

    /// <summary>The pixel as a vector, <c>(R, G, B, A)</c> → <c>(X, Y, Z, W)</c>. No channel is converted.</summary>
    public static implicit operator Half4(Rgba16Float pixel) => new(pixel.R, pixel.G, pixel.B, pixel.A);

    /// <summary>The vector as a pixel, <c>(X, Y, Z, W)</c> → <c>(R, G, B, A)</c>. No component is converted.</summary>
    public static implicit operator Rgba16Float(Half4 value) => new(value.X, value.Y, value.Z, value.W);

    public override readonly string ToString() => $"Rgba16Float({R}, {G}, {B}, {A})";
}
