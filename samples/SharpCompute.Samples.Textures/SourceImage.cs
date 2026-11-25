// Original SharpCompute sample (MIT).
using System;

namespace SharpCompute.Samples.Textures;

/// <summary>What the format label says about one texture element type.</summary>
/// <param name="Type">The C# element type, <c>T</c> of <c>ReadOnlyTexture2D&lt;T&gt;</c>.</param>
/// <param name="MetalFormat">The Metal pixel format behind it.</param>
/// <param name="Reads">What <c>texture[x, y]</c> returns in a shader.</param>
public sealed record SourceFormat(string Type, string MetalFormat, string Reads);

/// <summary>
/// The picture the sampler explorer magnifies: a 16 × 16 pixel-art tile, generated here so the sample
/// needs no asset files. Its four edges have four different colors and it carries an "F", which looks
/// different mirrored either way, so clamping, repeating and mirroring are easy to tell apart.
/// </summary>
public static class SourceImage
{
    /// <summary>Width and height of the tile, in pixels.</summary>
    public const int Size = 16;

    /// <summary>The eight texture element types, in the order the demo cycles through them.</summary>
    public static SourceFormat[] Formats { get; } =
    [
        new("Rgba32", "RGBA8Unorm", "(R, G, B, A) / 255"),
        new("Rgba32Srgb", "RGBA8Unorm_sRGB", "SRGB DECODED TO LINEAR (R, G, B), A / 255"),
        new("R8", "R8Unorm", "(R / 255, 0, 0, 1)"),
        new("Rg16", "RG8Unorm", "(R / 255, G / 255, 0, 1)"),
        new("Half", "R16Float", "(R, 0, 0, 1)"),
        new("Rgba16Float", "RGBA16Float", "(R, G, B, A)"),
        new("float", "R32Float", "(R, 0, 0, 1)"),
        new("Float4", "RGBA32Float", "(R, G, B, A)"),
    ];

    /// <summary>The tile, row-major from the top-left.</summary>
    public static Rgba32[] Create()
    {
        Rgba32[] pixels = new Rgba32[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                // A two-way gradient: orange at the top-left, violet at the bottom-right.
                float u = x / (Size - 1f), v = y / (Size - 1f);
                pixels[(y * Size) + x] = new Rgba32(Byte(0.95f - (0.55f * v)), Byte(0.55f - (0.35f * u) + (0.1f * v)), Byte(0.2f + (0.6f * u) + (0.2f * v)), 255);
            }
        }

        // One color per edge: clamping smears each edge's own color outwards.
        for (int i = 0; i < Size; i++)
        {
            pixels[i] = new Rgba32(255, 221, 51, 255);                       // top: yellow
            pixels[((Size - 1) * Size) + i] = new Rgba32(51, 204, 102, 255); // bottom: green
        }

        for (int i = 1; i < Size - 1; i++)
        {
            pixels[i * Size] = new Rgba32(230, 57, 70, 255);                 // left: red
            pixels[(i * Size) + Size - 1] = new Rgba32(58, 134, 255, 255);   // right: blue
        }

        // An "F" at twice the font's size with a drop shadow, and a dot in the opposite corner.
        Glyph(pixels, 'F', 4, 4, new Rgba32(20, 20, 40, 255));
        Glyph(pixels, 'F', 3, 3, new Rgba32(255, 255, 255, 255));

        for (int i = 0; i < 4; i++)
        {
            pixels[((11 + (i / 2)) * Size) + 11 + (i % 2)] = new Rgba32(255, 255, 255, 255);
        }

        return pixels;
    }

    /// <summary>The single channel that the one-channel formats store: the tile's brightness.</summary>
    public static byte Luminance(Rgba32 pixel) => (byte)(((pixel.R * 2) + (pixel.G * 5) + pixel.B) / 8);

    private static void Glyph(Rgba32[] pixels, char c, int left, int top, Rgba32 color)
    {
        for (int row = 0; row < PixelFont.GlyphHeight * 2; row++)
        {
            for (int column = 0; column < PixelFont.GlyphWidth * 2; column++)
            {
                if (PixelFont.IsSet(c, column / 2, row / 2))
                {
                    pixels[((top + row) * Size) + left + column] = color;
                }
            }
        }
    }

    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
