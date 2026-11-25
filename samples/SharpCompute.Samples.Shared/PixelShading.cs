// Original SharpCompute sample (MIT).
namespace SharpCompute.Samples.Shared;

/// <summary>
/// Shader code that Glowfall and Pulsewake have in common: the lattice hash their procedural art is
/// built on, the RGBA8 packing of the frame they render, and the test that draws their HUD glyphs.
/// Each game's shaders call these like their own helper methods; the generator translates a method
/// to MSL in every shader that uses it.
/// </summary>
/// <remarks>
/// This file is compiled into both games (each project links it, see their <c>.csproj</c>) rather
/// than into a library they reference: a shader's helpers are translated from their C# source, so
/// the source has to be part of the compilation that contains the shader. Nothing here keeps state,
/// so the methods are as good on the CPU (<c>ForCpu</c>) as on the GPU.
/// </remarks>
internal static class PixelShading
{
    /// <summary>The width and height of a HUD glyph, in font pixels: one bit each, row by row from bit 0.</summary>
    public const int GlyphWidth = 3;
    public const int GlyphHeight = 5;

    /// <summary>
    /// A hash of an integer lattice point to 0..1, the same for the same point and seed on every run
    /// and on both the GPU and the CPU: integer arithmetic only, until the last step.
    /// </summary>
    public static float Hash(int x, int y, uint seed)
    {
        uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u) + (seed * 2246822519u);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (float)(h & 0xFFFFFFu) / 16777215f;
    }

    /// <summary>The color of an RGBA8 pixel (red in the low byte), without its alpha.</summary>
    public static Float3 UnpackRgb(uint pixel) =>
        new((float)(pixel & 0xFFu) / 255f, (float)((pixel >> 8) & 0xFFu) / 255f, (float)((pixel >> 16) & 0xFFu) / 255f);

    /// <summary>A color as an opaque RGBA8 pixel (red in the low byte): clamped to 0..1 and rounded to the nearest step.</summary>
    public static uint PackRgb(Float3 color)
    {
        color = Gpu.Saturate(color);
        uint red = (uint)((color.X * 255f) + 0.5f);
        uint green = (uint)((color.Y * 255f) + 0.5f);
        uint blue = (uint)((color.Z * 255f) + 0.5f);
        return red | (green << 8) | (blue << 16) | 0xFF000000u;
    }

    /// <summary>
    /// True when the pixel at (<paramref name="gx"/>, <paramref name="gy"/>) from a glyph's top-left
    /// corner is one of its lit pixels, with the glyph drawn at <paramref name="scale"/> times its
    /// size. The font is only read for pixels inside the glyph's box.
    /// </summary>
    public static bool GlyphCovers(ReadOnlyBuffer<uint> font, int glyph, int gx, int gy, int scale) =>
        gx >= 0 && gy >= 0 && gx < GlyphWidth * scale && gy < GlyphHeight * scale &&
        ((font[glyph] >> (((gy / scale) * GlyphWidth) + (gx / scale))) & 1u) != 0u;
}
