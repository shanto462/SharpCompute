// Original SharpCompute sample (MIT).
using System;

namespace SharpCompute.Samples.NativeInterop.Host;

/// <summary>
/// The samples' 3 × 5 pixel font (digits, capitals, some punctuation), with the brackets,
/// apostrophe and middle dot this demo's help line needs. Each glyph is a 15-bit mask, row by row from the top, bit 0
/// the top-left pixel.
/// </summary>
internal static class PixelFont
{
    /// <summary>Width of a glyph in font pixels; glyphs are drawn one pixel apart.</summary>
    public const int GlyphWidth = 3;

    /// <summary>Height of a glyph in font pixels.</summary>
    public const int GlyphHeight = 5;

    private const string Characters = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:-/.!+(),=<>_[]'·";

    // Five rows of three pixels per character, in the order of Characters.
    private static readonly string[] Rows =
    [
        "...|...|...|...|...", // space
        "###|#.#|#.#|#.#|###", // 0
        ".#.|##.|.#.|.#.|###", // 1
        "##.|..#|.#.|#..|###", // 2
        "##.|..#|.#.|..#|##.", // 3
        "#.#|#.#|###|..#|..#", // 4
        "###|#..|##.|..#|##.", // 5
        ".##|#..|###|#.#|###", // 6
        "###|..#|.#.|.#.|.#.", // 7
        "###|#.#|###|#.#|###", // 8
        "###|#.#|###|..#|##.", // 9
        ".#.|#.#|###|#.#|#.#", // A
        "##.|#.#|##.|#.#|##.", // B
        ".##|#..|#..|#..|.##", // C
        "##.|#.#|#.#|#.#|##.", // D
        "###|#..|##.|#..|###", // E
        "###|#..|##.|#..|#..", // F
        ".##|#..|#.#|#.#|.##", // G
        "#.#|#.#|###|#.#|#.#", // H
        "###|.#.|.#.|.#.|###", // I
        "..#|..#|..#|#.#|.#.", // J
        "#.#|#.#|##.|#.#|#.#", // K
        "#..|#..|#..|#..|###", // L
        "#.#|###|###|#.#|#.#", // M
        "##.|#.#|#.#|#.#|#.#", // N
        ".#.|#.#|#.#|#.#|.#.", // O
        "##.|#.#|##.|#..|#..", // P
        ".#.|#.#|#.#|##.|.##", // Q
        "##.|#.#|##.|#.#|#.#", // R
        ".##|#..|.#.|..#|##.", // S
        "###|.#.|.#.|.#.|.#.", // T
        "#.#|#.#|#.#|#.#|###", // U
        "#.#|#.#|#.#|#.#|.#.", // V
        "#.#|#.#|###|###|#.#", // W
        "#.#|#.#|.#.|#.#|#.#", // X
        "#.#|#.#|.#.|.#.|.#.", // Y
        "###|..#|.#.|#..|###", // Z
        "...|.#.|...|.#.|...", // :
        "...|...|###|...|...", // -
        "..#|..#|.#.|#..|#..", // /
        "...|...|...|...|.#.", // .
        ".#.|.#.|.#.|...|.#.", // !
        "...|.#.|###|.#.|...", // +
        ".#.|#..|#..|#..|.#.", // (
        ".#.|..#|..#|..#|.#.", // )
        "...|...|...|.#.|#..", // ,
        "...|###|...|###|...", // =
        "..#|.#.|#..|.#.|..#", // <
        "#..|.#.|..#|.#.|#..", // >
        "...|...|...|...|###", // _
        "##.|#..|#..|#..|##.", // [
        ".##|..#|..#|..#|.##", // ]
        ".#.|.#.|...|...|...", // '
        "...|...|.#.|...|...", // ·
    ];

    /// <summary>The glyph masks, indexed by <see cref="Index"/>.</summary>
    public static uint[] Masks { get; } = Build();

    /// <summary>The glyph index of <paramref name="c"/> (letters case-insensitive); 0, a blank, for anything unknown.</summary>
    public static int Index(char c) => Math.Max(0, Characters.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal));

    /// <summary>True when the glyph of <paramref name="c"/> has a pixel at (<paramref name="column"/>, <paramref name="row"/>).</summary>
    public static bool IsSet(char c, int column, int row) =>
        (Masks[Index(c)] & (1u << ((row * GlyphWidth) + column))) != 0;

    /// <summary>The width in pixels of <paramref name="text"/> drawn at <paramref name="scale"/>.</summary>
    public static int Measure(string text, int scale) =>
        text.Length == 0 ? 0 : ((text.Length * (GlyphWidth + 1)) - 1) * scale;

    private static uint[] Build()
    {
        uint[] masks = new uint[Characters.Length];

        for (int g = 0; g < Rows.Length; g++)
        {
            string[] rows = Rows[g].Split('|');

            for (int r = 0; r < GlyphHeight; r++)
            {
                for (int c = 0; c < GlyphWidth; c++)
                {
                    if (rows[r][c] == '#')
                    {
                        masks[g] |= 1u << ((r * GlyphWidth) + c);
                    }
                }
            }
        }

        return masks;
    }
}
