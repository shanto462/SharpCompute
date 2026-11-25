namespace SharpCompute.Game;

/// <summary>
/// The built-in 3 × 5 pixel font: ASCII 32–95 (digits, capitals, punctuation; lower case is drawn
/// as upper case). Each glyph is a 15-bit mask, bit <c>row * 3 + column</c> set where the pixel is
/// lit. <see cref="Create"/> turns it into a <see cref="BitmapFont"/> whose characters advance four
/// pixels; the raw <see cref="Masks"/> are public for shaders that test the bits directly.
/// </summary>
public static class PixelFont
{
    public const int GlyphWidth = 3;
    public const int GlyphHeight = 5;

    /// <summary>The character of the first mask (a space).</summary>
    public const int First = 32;

    public static readonly uint[] Masks =
    [
        0x0000u, // ' '
        0x2092u, // '!'
        0x002Du, // '"'
        0x5F7Du, // '#'
        0x3DDEu, // '$'
        0x52A5u, // '%'
        0x6AAAu, // '&'
        0x0012u, // "'"
        0x224Au, // '('
        0x2922u, // ')'
        0x0AA8u, // '*'
        0x05D0u, // '+'
        0x1400u, // ','
        0x01C0u, // '-'
        0x2000u, // '.'
        0x12A4u, // '/'
        0x7B6Fu, // '0'
        0x749Au, // '1'
        0x73E7u, // '2'
        0x79A7u, // '3'
        0x49EDu, // '4'
        0x79CFu, // '5'
        0x7BCFu, // '6'
        0x24A7u, // '7'
        0x7BEFu, // '8'
        0x79EFu, // '9'
        0x0410u, // ':'
        0x1410u, // ';'
        0x4454u, // '<'
        0x0E38u, // '='
        0x1511u, // '>'
        0x21A7u, // '?'
        0x636Au, // '@'
        0x5BEFu, // 'A'
        0x3AEBu, // 'B'
        0x724Fu, // 'C'
        0x3B6Bu, // 'D'
        0x72CFu, // 'E'
        0x12CFu, // 'F'
        0x7B4Fu, // 'G'
        0x5BEDu, // 'H'
        0x7497u, // 'I'
        0x7B24u, // 'J'
        0x5AEDu, // 'K'
        0x7249u, // 'L'
        0x5BFDu, // 'M'
        0x5B6Bu, // 'N'
        0x7B6Fu, // 'O'
        0x13EFu, // 'P'
        0x4F6Fu, // 'Q'
        0x5AEBu, // 'R'
        0x79CFu, // 'S'
        0x2497u, // 'T'
        0x7B6Du, // 'U'
        0x2B6Du, // 'V'
        0x5FEDu, // 'W'
        0x5AADu, // 'X'
        0x24ADu, // 'Y'
        0x72A7u, // 'Z'
        0x324Bu, // '['
        0x4889u, // '\\'
        0x6926u, // ']'
        0x002Au, // '^'
        0x7000u, // '_'
    ];

    /// <summary>The glyph index of <paramref name="c"/> (lower case maps to upper case; anything else to a space).</summary>
    public static int Index(char c)
    {
        c = char.ToUpperInvariant(c);
        return c is >= (char)First and < (char)(First + 64) ? c - First : 0;
    }

    /// <summary>
    /// Adds the font's glyphs to <paramref name="atlas"/> (as one region called
    /// <paramref name="regionName"/>) and returns the font. Draw it with a <see cref="SpriteBatch"/>
    /// over the atlas built from the same builder.
    /// </summary>
    public static BitmapFont Create(AtlasBuilder atlas, string regionName = "pixel-font")
    {
        char[] characters = new char[Masks.Length];

        for (int i = 0; i < characters.Length; i++)
        {
            characters[i] = (char)(First + i);
        }

        return BitmapFont.FromBitmasks(atlas, regionName, characters, Masks, GlyphWidth, GlyphHeight);
    }
}
