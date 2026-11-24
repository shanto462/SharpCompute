using System.Runtime.InteropServices;

namespace SharpCompute;

/// <summary>An 8-bit-per-channel RGBA pixel (<c>MTLPixelFormatRGBA8Unorm</c>).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Rgba32(byte r, byte g, byte b, byte a)
{
    public byte R = r;

    public byte G = g;

    public byte B = b;

    public byte A = a;

    public override readonly string ToString() => $"Rgba32({R}, {G}, {B}, {A})";
}
