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
/// The CPU decodes with the exact transfer function; Apple GPUs decode to 12 bits, so the same pixel
/// can read up to 1.2e-4 differently on the two.
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
/// A 16-bit float RGBA pixel (<c>MTLPixelFormatRGBA16Float</c>). A shader's write rounds each channel
/// toward zero to the nearest half, as Apple GPUs do (and as <see cref="System.Half"/> textures do).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Rgba16Float(Half r, Half g, Half b, Half a)
{
    public Half R = r;

    public Half G = g;

    public Half B = b;

    public Half A = a;

    public override readonly string ToString() => $"Rgba16Float({R}, {G}, {B}, {A})";
}
