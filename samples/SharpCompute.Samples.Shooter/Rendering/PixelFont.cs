// Original SharpCompute sample (MIT).
using System;

namespace SharpCompute.Samples.Shooter.Rendering;

/// <summary>
/// A 3 × 5 pixel font for the HUD: digits, capitals and a little punctuation. Each glyph is a
/// 15-bit mask, row by row from the top, bit 0 the top-left pixel; <see cref="Composite"/> tests one
/// bit per pixel.
/// </summary>
public static class PixelFont
{
    private const string Characters = " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:-/.!+";

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
    ];

    /// <summary>The glyph masks, indexed by <see cref="Index"/>.</summary>
    public static uint[] Masks { get; } = Build();

    /// <summary>The glyph index of <paramref name="c"/> (letters case-insensitive); 0, a blank, for anything unknown.</summary>
    public static int Index(char c) => Math.Max(0, Characters.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal));

    private static uint[] Build()
    {
        uint[] masks = new uint[Characters.Length];

        for (int g = 0; g < Rows.Length; g++)
        {
            string[] rows = Rows[g].Split('|');

            for (int r = 0; r < 5; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (rows[r][c] == '#')
                    {
                        masks[g] |= 1u << ((r * 3) + c);
                    }
                }
            }
        }

        return masks;
    }
}
