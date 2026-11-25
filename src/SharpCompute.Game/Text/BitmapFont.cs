using System;
using System.Collections.Generic;
using System.Globalization;

namespace SharpCompute.Game;

/// <summary>One character of a <see cref="BitmapFont"/>, in the terms of a BMFont glyph table.</summary>
/// <param name="Source">The glyph's image in the atlas; empty for a blank (a space).</param>
/// <param name="OffsetX">Where the image sits to the right of the pen position, in pixels.</param>
/// <param name="OffsetY">Where the image sits below the top of the line.</param>
/// <param name="Advance">How far the pen moves on after the character.</param>
public readonly record struct FontGlyph(AtlasRegion Source, int OffsetX, int OffsetY, int Advance);

/// <summary>A glyph placed by <see cref="BitmapFont.Layout"/>: draw <see cref="Source"/> scaled with its top-left corner at (<see cref="X"/>, <see cref="Y"/>).</summary>
public readonly record struct PlacedGlyph(AtlasRegion Source, int X, int Y);

/// <summary>
/// A font whose glyphs are images in a sprite atlas: a table from characters to
/// <see cref="FontGlyph"/>s, and the layout that turns a string into glyph positions.
/// <see cref="SpriteBatch.DrawText"/> draws the result as tinted, scaled sprites.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where fonts come from.</b> <see cref="PixelFont.Create"/> is the built-in 3 × 5 font.
/// <see cref="FromBitmasks"/> builds a font from one bit mask per glyph (the way tiny pixel fonts are
/// written by hand). <see cref="ParseBMFont"/> reads the text <c>.fnt</c> format of AngelCode BMFont
/// and the many tools that export it. Any other source can pass its own glyph table to the constructor.
/// </para>
/// <para>
/// <b>Missing characters.</b> A character with no glyph is looked up in upper case, then in lower
/// case (so an upper-case-only font still draws "Score"); if that fails too it is drawn as a blank
/// <see cref="BlankAdvance"/> wide. There is no kerning.
/// </para>
/// <para>Immutable, so safe to use from any thread.</para>
/// </remarks>
public sealed class BitmapFont
{
    private readonly Dictionary<char, FontGlyph> _glyphs = [];

    /// <param name="lineHeight">The distance from one line of text to the next, in pixels.</param>
    /// <param name="glyphs">The glyph of each character. A later entry for the same character replaces an earlier one.</param>
    /// <param name="blankAdvance">How far a character without a glyph moves the pen; null uses the space's advance, or half the line height.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lineHeight"/> is not positive, or <paramref name="blankAdvance"/> is negative.</exception>
    public BitmapFont(int lineHeight, IEnumerable<KeyValuePair<char, FontGlyph>> glyphs, int? blankAdvance = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        ArgumentNullException.ThrowIfNull(glyphs);

        foreach ((char c, FontGlyph glyph) in glyphs)
        {
            _glyphs[c] = glyph;
        }

        if (blankAdvance is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(blankAdvance), blankAdvance, "The blank advance cannot be negative.");
        }

        LineHeight = lineHeight;
        BlankAdvance = blankAdvance ?? (_glyphs.TryGetValue(' ', out FontGlyph space) ? space.Advance : Math.Max(1, lineHeight / 2));
    }

    /// <summary>The distance from one line of text to the next, in pixels at scale 1.</summary>
    public int LineHeight { get; }

    /// <summary>How far a character without a glyph moves the pen.</summary>
    public int BlankAdvance { get; }

    /// <summary>The number of characters with a glyph.</summary>
    public int GlyphCount => _glyphs.Count;

    /// <summary>
    /// The glyph drawn for <paramref name="c"/>: its own, else that of its upper or lower case, else
    /// a blank that advances <see cref="BlankAdvance"/>.
    /// </summary>
    public FontGlyph GetGlyph(char c) =>
        _glyphs.TryGetValue(c, out FontGlyph glyph) ||
        _glyphs.TryGetValue(char.ToUpperInvariant(c), out glyph) ||
        _glyphs.TryGetValue(char.ToLowerInvariant(c), out glyph)
            ? glyph
            : new FontGlyph(default, 0, 0, BlankAdvance);

    /// <summary>True when <paramref name="c"/> itself has a glyph (no case folding).</summary>
    public bool HasGlyph(char c) => _glyphs.ContainsKey(c);

    /// <summary>
    /// Lays <paramref name="text"/> out from the top-left corner (<paramref name="x"/>,
    /// <paramref name="y"/>), one <see cref="PlacedGlyph"/> per visible character; <c>'\n'</c> starts
    /// a new line. Enumerate the result with <c>foreach</c>; nothing is allocated.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="x">Left edge of the first line.</param>
    /// <param name="y">Top edge of the first line.</param>
    /// <param name="scale">Whole-number magnification of offsets, advances and line height.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not positive.</exception>
    public TextLayout Layout(ReadOnlySpan<char> text, int x, int y, int scale = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        return new TextLayout(this, text, x, y, scale);
    }

    /// <summary>
    /// The size of <paramref name="text"/> as <see cref="Layout"/> would place it: the width of the
    /// widest line (to the right edge of its last glyph, without the spacing after it) and the
    /// height of all lines.
    /// </summary>
    public (int Width, int Height) Measure(ReadOnlySpan<char> text, int scale = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        int width = 0;
        int pen = 0;
        int lines = 1;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                pen = 0;
                lines++;
                continue;
            }

            FontGlyph glyph = GetGlyph(c);
            width = Math.Max(width, glyph.Source.Width > 0 ? pen + ((glyph.OffsetX + glyph.Source.Width) * scale) : pen + (glyph.Advance * scale));
            pen += glyph.Advance * scale;
        }

        return (width, lines * LineHeight * scale);
    }

    /// <summary>
    /// A fixed-size font from one bit mask per glyph: bit <c>row * glyphWidth + column</c> of a mask
    /// is set where the glyph has a pixel. The glyph images (white, opaque where set) are added to
    /// <paramref name="atlas"/> as a single region, <paramref name="regionName"/>. A zero mask is a blank.
    /// </summary>
    /// <param name="atlas">The atlas the glyph images go into.</param>
    /// <param name="regionName">The name of the font's region in the atlas.</param>
    /// <param name="characters">The character of each mask.</param>
    /// <param name="masks">One mask per character.</param>
    /// <param name="glyphWidth">Glyph width in pixels.</param>
    /// <param name="glyphHeight">Glyph height in pixels; <c>glyphWidth × glyphHeight</c> must fit in 32 bits.</param>
    /// <param name="spacing">Empty pixels between characters (and, doubled, between lines).</param>
    /// <exception cref="ArgumentException">The spans differ in length or are empty, or a glyph has more than 32 pixels or is wider than the atlas.</exception>
    public static BitmapFont FromBitmasks(AtlasBuilder atlas, string regionName, ReadOnlySpan<char> characters, ReadOnlySpan<uint> masks, int glyphWidth, int glyphHeight, int spacing = 1)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(glyphWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(glyphHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(spacing);

        if (characters.Length != masks.Length || masks.IsEmpty)
        {
            throw new ArgumentException($"There must be one mask per character, and at least one ({characters.Length} characters, {masks.Length} masks).", nameof(masks));
        }

        if ((long)glyphWidth * glyphHeight > 32)
        {
            throw new ArgumentException($"A {glyphWidth} × {glyphHeight} glyph does not fit in a 32-bit mask.", nameof(glyphHeight));
        }

        if (glyphWidth > atlas.Width)
        {
            throw new ArgumentException($"A glyph is {glyphWidth} texels wide; the atlas is only {atlas.Width}.", nameof(glyphWidth));
        }

        // One image for the whole font: a grid of glyph cells as wide as the atlas allows.
        int columns = Math.Min(masks.Length, atlas.Width / glyphWidth);
        int rows = (masks.Length + columns - 1) / columns;
        uint[] image = new uint[checked(columns * glyphWidth * rows * glyphHeight)];
        int imageWidth = columns * glyphWidth;

        for (int g = 0; g < masks.Length; g++)
        {
            int cellX = g % columns * glyphWidth;
            int cellY = g / columns * glyphHeight;

            for (int bit = 0; bit < glyphWidth * glyphHeight; bit++)
            {
                if ((masks[g] >> bit & 1u) != 0u)
                {
                    image[((cellY + (bit / glyphWidth)) * imageWidth) + cellX + (bit % glyphWidth)] = 0xFFFFFFFFu;
                }
            }
        }

        AtlasRegion region = atlas.Add(regionName, image, imageWidth, rows * glyphHeight);
        Dictionary<char, FontGlyph> glyphs = [];

        for (int g = 0; g < masks.Length; g++)
        {
            AtlasRegion source = masks[g] == 0u ? default : region.Cell(g, glyphWidth, glyphHeight);
            glyphs[characters[g]] = new FontGlyph(source, 0, 0, glyphWidth + spacing);
        }

        return new BitmapFont(glyphHeight + (2 * spacing), glyphs, glyphWidth + spacing);
    }

    /// <summary>
    /// Reads a font description in the text <c>.fnt</c> format of AngelCode BMFont: the
    /// <c>common lineHeight=…</c> line and one <c>char id=… x=… y=… width=… height=… xoffset=…
    /// yoffset=… xadvance=…</c> line per glyph. <paramref name="page"/> is where the font's page
    /// image was placed in the atlas (the region returned by <see cref="AtlasBuilder.Add"/>).
    /// </summary>
    /// <remarks>Single-page fonts only; kerning pairs and characters outside the Basic Multilingual Plane are ignored.</remarks>
    /// <exception cref="FormatException">
    /// The text has no <c>common</c> line, a <c>char</c> line lacks a field or has a malformed number,
    /// a glyph lies outside <paramref name="page"/>, or a glyph is on a page other than 0.
    /// </exception>
    public static BitmapFont ParseBMFont(string description, AtlasRegion page)
    {
        ArgumentNullException.ThrowIfNull(description);
        Dictionary<char, FontGlyph> glyphs = [];
        Dictionary<string, int> fields = new(StringComparer.Ordinal);
        int lineHeight = 0;

        foreach (ReadOnlySpan<char> rawLine in description.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine.Trim();
            bool isChar = line.StartsWith("char ", StringComparison.Ordinal);

            if (!isChar && !line.StartsWith("common ", StringComparison.Ordinal))
            {
                continue;
            }

            ReadFields(line, fields);

            if (!isChar)
            {
                lineHeight = Field(fields, "lineHeight", line);
                continue;
            }

            int id = Field(fields, "id", line);
            AtlasRegion source = new(page.X + Field(fields, "x", line), page.Y + Field(fields, "y", line), Field(fields, "width", line), Field(fields, "height", line));

            if (fields.TryGetValue("page", out int pageIndex) && pageIndex != 0)
            {
                throw new FormatException($"Character {id} is on page {pageIndex}; only single-page BMFont fonts are supported.");
            }

            if (id is < 0 or > char.MaxValue)
            {
                continue;
            }

            if (source.Width == 0 || source.Height == 0)
            {
                source = default;
            }
            else if (source.Width < 0 || source.Height < 0 || source.X < page.X || source.Y < page.Y || source.X + source.Width > page.X + page.Width || source.Y + source.Height > page.Y + page.Height)
            {
                throw new FormatException($"Character {id} ({source.Width} × {source.Height} at {source.X - page.X}, {source.Y - page.Y}) lies outside the {page.Width} × {page.Height} page.");
            }

            glyphs[(char)id] = new FontGlyph(source, Field(fields, "xoffset", line), Field(fields, "yoffset", line), Field(fields, "xadvance", line));
        }

        if (lineHeight <= 0)
        {
            throw new FormatException("The BMFont description has no 'common' line with a positive lineHeight (is it the text .fnt format?).");
        }

        return new BitmapFont(lineHeight, glyphs);
    }

    /// <summary>Collects the integer <c>key=value</c> pairs of a BMFont line (quoted strings and lists are skipped).</summary>
    private static void ReadFields(ReadOnlySpan<char> line, Dictionary<string, int> fields)
    {
        fields.Clear();

        foreach (Range range in line.Split(' '))
        {
            ReadOnlySpan<char> token = line[range];
            int equals = token.IndexOf('=');

            if (equals > 0 && int.TryParse(token[(equals + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                fields[token[..equals].ToString()] = value;
            }
        }
    }

    private static int Field(Dictionary<string, int> fields, string name, ReadOnlySpan<char> line) =>
        fields.TryGetValue(name, out int value) ? value : throw new FormatException($"BMFont line is missing an integer '{name}' field: \"{line}\".");

    /// <summary>The glyphs of a laid-out text, one at a time: the enumerator behind <see cref="Layout"/>.</summary>
    public ref struct TextLayout
    {
        private readonly BitmapFont _font;
        private readonly ReadOnlySpan<char> _text;
        private readonly int _left;
        private readonly int _scale;
        private int _index;
        private int _penX;
        private int _penY;

        internal TextLayout(BitmapFont font, ReadOnlySpan<char> text, int x, int y, int scale)
        {
            _font = font;
            _text = text;
            _left = x;
            _scale = scale;
            _penX = x;
            _penY = y;
        }

        /// <summary>The glyph <see cref="MoveNext"/> stopped at.</summary>
        public PlacedGlyph Current { get; private set; }

        /// <summary>Makes the layout usable in <c>foreach</c>.</summary>
        public readonly TextLayout GetEnumerator() => this;

        /// <summary>Advances to the next visible glyph, skipping blanks and line breaks.</summary>
        public bool MoveNext()
        {
            while (_index < _text.Length)
            {
                char c = _text[_index++];

                if (c == '\n')
                {
                    _penX = _left;
                    _penY += _font.LineHeight * _scale;
                    continue;
                }

                FontGlyph glyph = _font.GetGlyph(c);
                int x = _penX + (glyph.OffsetX * _scale);
                _penX += glyph.Advance * _scale;

                if (glyph.Source.Width > 0)
                {
                    Current = new PlacedGlyph(glyph.Source, x, _penY + (glyph.OffsetY * _scale));
                    return true;
                }
            }

            return false;
        }
    }
}
