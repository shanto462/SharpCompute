using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCompute.Game.Tests;

/// <summary>Bitmap fonts: glyph tables, layout, measuring, the built-in pixel font and BMFont descriptions.</summary>
public sealed class BitmapFontTests
{
    private static readonly Rgba32 White = new(255, 255, 255, 255);

    private static List<PlacedGlyph> Place(BitmapFont font, string text, int x, int y, int scale = 1)
    {
        List<PlacedGlyph> placed = [];

        foreach (PlacedGlyph glyph in font.Layout(text, x, y, scale))
        {
            placed.Add(glyph);
        }

        return placed;
    }

    [Fact]
    public void PixelFont_CoversAscii32To95_AndFoldsCase()
    {
        Assert.Equal(64, PixelFont.Masks.Length);
        Assert.Equal(0u, PixelFont.Masks[PixelFont.Index(' ')]);

        for (char c = '!'; c <= '_'; c++)
        {
            Assert.Equal(c - 32, PixelFont.Index(c));
            Assert.NotEqual(0u, PixelFont.Masks[PixelFont.Index(c)]);
            Assert.True(PixelFont.Masks[PixelFont.Index(c)] < 1u << 15, $"'{c}' has bits outside 3 × 5.");
        }

        Assert.Equal(PixelFont.Index('G'), PixelFont.Index('g'));
        Assert.Equal(0, PixelFont.Index('§'));
        Assert.Equal(0, PixelFont.Index('~'));

        // Bit row * 3 + column: a plus sign is the middle column and the middle row.
        Assert.Equal(Mask("...", ".#.", "###", ".#.", "..."), PixelFont.Masks[PixelFont.Index('+')]);
        Assert.Equal(Mask("#..", "#..", "#..", "#..", "###"), PixelFont.Masks[PixelFont.Index('L')]);
        Assert.Equal(Mask("...", "...", "...", "...", "###"), PixelFont.Masks[PixelFont.Index('_')]);
    }

    [Fact]
    public void PixelFont_PutsEveryGlyphInTheAtlas_AsWhitePixelsWhereTheMaskIsSet()
    {
        AtlasBuilder builder = new(64);
        builder.Add("something-else", new uint[35], 7, 5);
        BitmapFont font = PixelFont.Create(builder);
        uint[] atlas = builder.ToPixels();

        Assert.Equal(64, font.GlyphCount);
        Assert.Equal(7, font.LineHeight);
        Assert.Equal(4, font.BlankAdvance);
        Assert.True(builder.Regions.ContainsKey("pixel-font"));

        for (char c = ' '; c <= '_'; c++)
        {
            FontGlyph glyph = font.GetGlyph(c);
            uint mask = PixelFont.Masks[PixelFont.Index(c)];
            Assert.Equal(4, glyph.Advance);

            if (mask == 0)
            {
                Assert.Equal(default, glyph.Source);
                continue;
            }

            Assert.Equal((3, 5), (glyph.Source.Width, glyph.Source.Height));

            for (int bit = 0; bit < 15; bit++)
            {
                uint texel = atlas[((glyph.Source.Y + (bit / 3)) * builder.Width) + glyph.Source.X + (bit % 3)];
                Assert.Equal((mask >> bit & 1) != 0 ? 0xFFFFFFFFu : 0u, texel);
            }
        }
    }

    [Fact]
    public void Layout_PlacesGlyphsAlongThePen_SkippingBlanks()
    {
        BitmapFont font = PixelFont.Create(new AtlasBuilder());
        List<PlacedGlyph> placed = Place(font, "A B!", 10, 20);

        Assert.Equal(3, placed.Count);
        Assert.Equal([(10, 20), (18, 20), (22, 20)], placed.Select(static g => (g.X, g.Y)));
        Assert.Equal(font.GetGlyph('A').Source, placed[0].Source);
        Assert.Equal(font.GetGlyph('B').Source, placed[1].Source);
        Assert.Equal(font.GetGlyph('!').Source, placed[2].Source);
    }

    [Fact]
    public void Layout_ScalesAdvancesAndLineHeight_AndBreaksAtNewlines()
    {
        BitmapFont font = PixelFont.Create(new AtlasBuilder());
        List<PlacedGlyph> placed = Place(font, "AB\nC\n\nD", 5, 7, scale: 3);

        Assert.Equal([(5, 7), (17, 7), (5, 28), (5, 70)], placed.Select(static g => (g.X, g.Y)));
        Assert.Empty(Place(font, "", 0, 0));
        Assert.Empty(Place(font, "   \n ", 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => font.Layout("A", 0, 0, 0));
    }

    [Fact]
    public void Layout_FoldsCase_AndTreatsUnknownCharactersAsBlanks()
    {
        BitmapFont font = PixelFont.Create(new AtlasBuilder());

        Assert.Equal(font.GetGlyph('Q'), font.GetGlyph('q'));
        Assert.True(font.HasGlyph('Q'));
        Assert.False(font.HasGlyph('q'));
        Assert.Equal(new FontGlyph(default, 0, 0, 4), font.GetGlyph('§'));

        // "a§b": the unknown character still takes its four pixels.
        Assert.Equal([0, 8], Place(font, "a§b", 0, 0).Select(static g => g.X));
    }

    [Fact]
    public void Measure_IsTheInkWidthOfTheWidestLine_AndTheHeightOfAllLines()
    {
        BitmapFont font = PixelFont.Create(new AtlasBuilder());

        Assert.Equal((0, 7), font.Measure(""));
        Assert.Equal((3, 7), font.Measure("A"));
        Assert.Equal((11, 7), font.Measure("ABC"));               // 3 × 4 minus the trailing gap
        Assert.Equal((44, 28), font.Measure("ABC", scale: 4));
        Assert.Equal((19, 14), font.Measure("AB\nHELLO"));
        Assert.Equal((8, 7), font.Measure("A "));                 // a trailing blank counts its advance
        Assert.Throws<ArgumentOutOfRangeException>(() => font.Measure("A", -1));

        // The classic centering sum: (width − length × 4 × scale + scale) / 2.
        Assert.Equal(6 * 4 * 2 - 2, font.Measure("PAUSED", 2).Width);
    }

    [Fact]
    public void AGlyphTable_SetsOffsetsAndAdvances()
    {
        Dictionary<char, FontGlyph> table = new()
        {
            ['a'] = new FontGlyph(new AtlasRegion(0, 0, 6, 8), 1, 2, 8),
            ['g'] = new FontGlyph(new AtlasRegion(6, 0, 6, 10), 0, 4, 7),
            [' '] = new FontGlyph(default, 0, 0, 5),
        };
        BitmapFont font = new(lineHeight: 14, table);

        Assert.Equal(5, font.BlankAdvance);          // the space's advance
        Assert.Equal(table['a'], font.GetGlyph('A'));     // a lower-case-only font draws capitals too
        Assert.Equal(
            [new PlacedGlyph(table['a'].Source, 101, 52), new PlacedGlyph(table['g'].Source, 113, 54), new PlacedGlyph(table['a'].Source, 101, 66)],
            Place(font, "a g\na", 100, 50));
        Assert.Equal([(102, 54), (126, 58)], Place(font, "a g", 100, 50, 2).Select(static g => (g.X, g.Y)));
        Assert.Equal((19, 14), font.Measure("a g"));      // 8 + 5 + (0 + 6)

        Assert.Equal(7, new BitmapFont(14, []).BlankAdvance);            // no space: half the line height
        Assert.Equal(3, new BitmapFont(14, table, blankAdvance: 3).BlankAdvance);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitmapFont(0, table));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BitmapFont(14, table, -1));
        Assert.Throws<ArgumentNullException>(() => new BitmapFont(14, null!));
    }

    [Fact]
    public void FromBitmasks_WrapsGlyphsIntoRows_WhenTheAtlasIsNarrow()
    {
        // Five 4 × 2 glyphs in an atlas 9 texels wide: two per row, three rows.
        AtlasBuilder builder = new(9, padding: 0);
        uint[] masks = [0b1111_1111, 0b0000_1111, 0, 0b1000_0001, 0b0110_0110];
        BitmapFont font = BitmapFont.FromBitmasks(builder, "tiny", "abcde", masks, glyphWidth: 4, glyphHeight: 2, spacing: 2);

        Assert.Equal(new AtlasRegion(0, 0, 8, 6), builder.Regions["tiny"]);
        Assert.Equal(new AtlasRegion(0, 0, 4, 2), font.GetGlyph('a').Source);
        Assert.Equal(new AtlasRegion(4, 0, 4, 2), font.GetGlyph('b').Source);
        Assert.Equal(default, font.GetGlyph('c').Source);
        Assert.Equal(new AtlasRegion(4, 2, 4, 2), font.GetGlyph('d').Source);
        Assert.Equal(new AtlasRegion(0, 4, 4, 2), font.GetGlyph('e').Source);
        Assert.Equal(6, font.GetGlyph('c').Advance);
        Assert.Equal(6, font.LineHeight);

        uint[] atlas = builder.ToPixels();
        const uint on = 0xFFFFFFFFu;
        // 'd' (bits 0 and 7) in the cell at (4, 2): its top-left and bottom-right texels.
        Assert.Equal([on, 0u, 0u, 0u], atlas[((2 * 9) + 4)..((2 * 9) + 8)]);
        Assert.Equal([0u, 0u, 0u, on], atlas[((3 * 9) + 4)..((3 * 9) + 8)]);
    }

    [Fact]
    public void FromBitmasks_ValidatesItsArguments()
    {
        AtlasBuilder builder = new(16);

        Assert.Throws<ArgumentException>(() => BitmapFont.FromBitmasks(builder, "f", "ab", [1u], 3, 5));
        Assert.Throws<ArgumentException>(() => BitmapFont.FromBitmasks(builder, "f", "", [], 3, 5));
        Assert.Throws<ArgumentException>(() => BitmapFont.FromBitmasks(builder, "f", "a", [1u], 6, 6));       // 36 bits
        Assert.Throws<ArgumentException>(() => BitmapFont.FromBitmasks(builder, "f", "a", [1u], 17, 1));      // wider than the atlas
        Assert.Throws<ArgumentOutOfRangeException>(() => BitmapFont.FromBitmasks(builder, "f", "a", [1u], 0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitmapFont.FromBitmasks(builder, "f", "a", [1u], 3, 5, -1));
        Assert.Throws<ArgumentNullException>(() => BitmapFont.FromBitmasks(null!, "f", "a", [1u], 3, 5));
        Assert.Empty(builder.Regions);
    }

    [Fact]
    public void ParseBMFont_ReadsTheGlyphTable_OffsetByThePage()
    {
        const string description = """
            info face="Test" size=16 bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=1 aa=1 padding=0,0,0,0 spacing=1,1
            common lineHeight=18 base=14 scaleW=64 scaleH=32 pages=1 packed=0
            page id=0 file="test_0.png"
            chars count=4
            char id=32   x=0     y=0     width=0     height=0     xoffset=0     yoffset=14    xadvance=5     page=0  chnl=15
            char id=65   x=2     y=3     width=10    height=12    xoffset=-1    yoffset=2     xadvance=11    page=0  chnl=15
            char id=103  x=14    y=3     width=8     height=13    xoffset=1     yoffset=5     xadvance=9     page=0  chnl=15
            char id=128512 x=0 y=0 width=4 height=4 xoffset=0 yoffset=0 xadvance=4 page=0 chnl=15
            kernings count=1
            kerning first=65 second=103 amount=-1
            """;

        BitmapFont font = BitmapFont.ParseBMFont(description, new AtlasRegion(100, 40, 64, 32));

        Assert.Equal(18, font.LineHeight);
        Assert.Equal(3, font.GlyphCount);        // the emoji is outside the BMP
        Assert.Equal(5, font.BlankAdvance);
        Assert.Equal(new FontGlyph(new AtlasRegion(102, 43, 10, 12), -1, 2, 11), font.GetGlyph('A'));
        Assert.Equal(new FontGlyph(new AtlasRegion(114, 43, 8, 13), 1, 5, 9), font.GetGlyph('g'));
        Assert.Equal(new FontGlyph(default, 0, 14, 5), font.GetGlyph(' '));
        Assert.Equal(font.GetGlyph('A'), font.GetGlyph('a'));

        Assert.Equal(
            [new PlacedGlyph(new AtlasRegion(102, 43, 10, 12), -1, 2), new PlacedGlyph(new AtlasRegion(114, 43, 8, 13), 17, 5)],
            Place(font, "A g", 0, 0));
    }

    [Theory]
    [InlineData("char id=65 x=0 y=0 width=4 height=4 xoffset=0 yoffset=0 xadvance=5", "common")]               // no common line
    [InlineData("common lineHeight=0\nchar id=65 x=0 y=0 width=4 height=4 xoffset=0 yoffset=0 xadvance=5", "lineHeight")]
    [InlineData("common lineHeight=10\nchar id=65 x=0 y=0 width=4 height=4 xoffset=0 yoffset=0", "xadvance")]   // a missing field
    [InlineData("common lineHeight=10\nchar id=65 x=zero y=0 width=4 height=4 xoffset=0 yoffset=0 xadvance=5", "'x'")]
    [InlineData("common lineHeight=10\nchar id=65 x=30 y=0 width=4 height=4 xoffset=0 yoffset=0 xadvance=5", "outside")]
    [InlineData("common lineHeight=10\nchar id=65 x=0 y=0 width=-4 height=4 xoffset=0 yoffset=0 xadvance=5", "outside")]
    [InlineData("common lineHeight=10\nchar id=65 x=0 y=0 width=4 height=4 xoffset=0 yoffset=0 xadvance=5 page=1", "page")]
    [InlineData("<?xml version=\"1.0\"?><font></font>", "common")]
    public void ParseBMFont_RejectsMalformedDescriptions(string description, string expectedInMessage)
    {
        FormatException e = Assert.Throws<FormatException>(() => BitmapFont.ParseBMFont(description, new AtlasRegion(0, 0, 32, 32)));
        Assert.Contains(expectedInMessage, e.Message, StringComparison.Ordinal);
    }

    [MetalFact]
    public void DrawText_DrawsTheGlyphPixels_InColor_AtScale()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        AtlasBuilder builder = new();
        BitmapFont font = PixelFont.Create(builder);
        using SpriteAtlas atlas = builder.Build(device);
        using SpriteBatch batch = new(device, atlas, 64, 32);
        using FrameBuffer frame = new(device, 64, 32);
        Rgba32 background = new(10, 20, 30, 255), ink = new(250, 180, 40, 255);

        batch.Begin();
        Assert.Equal(2, batch.DrawText(font, "L +", 3, 4, ink));
        Assert.Equal(1, batch.DrawText(font, "t", 20, 10, White, scale: 3));

        using (ComputeContext context = device.CreateComputeContext())
        {
            batch.Record(context, frame, background);
        }

        // "L" at (3, 4) and "+" two advances on, at (11, 4), pixel for pixel.
        AssertGlyph(frame, 'L', 3, 4, 1, ink, background);
        AssertGlyph(frame, '+', 11, 4, 1, ink, background);
        Assert.Equal(background, frame[7, 6]);       // the blank between them

        // A lower-case "t" drawn as "T", every font pixel a 3 × 3 block.
        AssertGlyph(frame, 'T', 20, 10, 3, White, background);
    }

    private static void AssertGlyph(FrameBuffer frame, char c, int x, int y, int scale, Rgba32 ink, Rgba32 background)
    {
        uint mask = PixelFont.Masks[PixelFont.Index(c)];

        for (int py = 0; py < PixelFont.GlyphHeight * scale; py++)
        {
            for (int px = 0; px < PixelFont.GlyphWidth * scale; px++)
            {
                bool lit = (mask >> (((py / scale) * 3) + (px / scale)) & 1) != 0;
                Assert.Equal(lit ? ink : background, frame[x + px, y + py]);
            }
        }
    }

    private static uint Mask(params ReadOnlySpan<string> rows)
    {
        uint mask = 0;

        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                mask |= rows[r][c] == '#' ? 1u << ((r * 3) + c) : 0u;
            }
        }

        return mask;
    }
}
