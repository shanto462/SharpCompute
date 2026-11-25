using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCompute.Game.Tests;

/// <summary>Atlas packing: every image lands intact, nothing overlaps, and regions say where.</summary>
public sealed class AtlasBuilderTests
{
    /// <summary>An image whose every texel is <paramref name="id"/> in the high bytes and its own index in the low ones.</summary>
    private static uint[] Image(int width, int height, uint id)
    {
        uint[] pixels = new uint[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (id << 16) | (uint)i | 0xFF000000u;
        }

        return pixels;
    }

    [Fact]
    public void Add_PlacesImagesLeftToRight_ThenStartsANewShelf()
    {
        AtlasBuilder builder = new(width: 32, padding: 0);

        Assert.Equal(new AtlasRegion(0, 0, 16, 8), builder.Add("a", Image(16, 8, 1), 16, 8));
        Assert.Equal(new AtlasRegion(16, 0, 10, 12), builder.Add("b", Image(10, 12, 2), 10, 12));
        // 26 + 8 > 32: a new shelf below the tallest image of the first (12).
        Assert.Equal(new AtlasRegion(0, 12, 8, 4), builder.Add("c", Image(8, 4, 3), 8, 4));
        Assert.Equal(new AtlasRegion(8, 12, 24, 1), builder.Add("d", Image(24, 1, 4), 24, 1));

        Assert.Equal(32, builder.Width);
        Assert.Equal(16, builder.Height);
        Assert.Equal(4, builder.Regions.Count);
        Assert.Equal(new AtlasRegion(16, 0, 10, 12), builder.Regions["b"]);
    }

    [Fact]
    public void Padding_SeparatesImages_OnBothAxes()
    {
        AtlasBuilder builder = new(width: 20, padding: 2);

        Assert.Equal(new AtlasRegion(0, 0, 8, 8), builder.Add("a", Image(8, 8, 1), 8, 8));
        Assert.Equal(new AtlasRegion(10, 0, 8, 5), builder.Add("b", Image(8, 5, 2), 8, 5));
        Assert.Equal(new AtlasRegion(0, 10, 20, 3), builder.Add("c", Image(20, 3, 3), 20, 3));
        Assert.Equal(13, builder.Height);
    }

    [Theory]
    [InlineData(64, 0, 1)]
    [InlineData(64, 1, 2)]
    [InlineData(100, 3, 3)]
    [InlineData(37, 2, 4)]
    public void ManyImages_NeverOverlap_AndKeepTheirPixels(int atlasWidth, int padding, int seed)
    {
        AtlasBuilder builder = new(atlasWidth, padding);
        Random random = new(seed);
        List<(AtlasRegion Region, uint[] Pixels)> placed = [];

        for (uint id = 1; id <= 60; id++)
        {
            int w = random.Next(1, 30), h = random.Next(1, 20);
            uint[] pixels = Image(w, h, id);
            AtlasRegion region = builder.Add($"image{id}", pixels, w, h);
            Assert.Equal((w, h), (region.Width, region.Height));
            placed.Add((region, pixels));
        }

        uint[] atlas = builder.ToPixels();
        Assert.Equal(builder.Width * builder.Height, atlas.Length);
        int[] owners = new int[atlas.Length];

        foreach ((AtlasRegion r, uint[] pixels) in placed)
        {
            Assert.True(SpriteAtlas.Contains(r, builder.Width, builder.Height));

            for (int y = 0; y < r.Height; y++)
            {
                for (int x = 0; x < r.Width; x++)
                {
                    int at = ((r.Y + y) * builder.Width) + r.X + x;
                    Assert.Equal(pixels[(y * r.Width) + x], atlas[at]);
                    owners[at]++;
                }
            }
        }

        // No texel belongs to two images, and everything outside an image is transparent.
        Assert.DoesNotContain(owners, static n => n > 1);
        Assert.All(Enumerable.Range(0, atlas.Length).Where(i => owners[i] == 0), i => Assert.Equal(0u, atlas[i]));

        // With padding, no two regions touch.
        foreach ((AtlasRegion a, _) in placed)
        {
            foreach ((AtlasRegion b, _) in placed)
            {
                if (a != b)
                {
                    bool apart = a.X + a.Width + padding <= b.X || b.X + b.Width + padding <= a.X ||
                                 a.Y + a.Height + padding <= b.Y || b.Y + b.Height + padding <= a.Y;
                    Assert.True(apart, $"{a} and {b} are closer than {padding} texels.");
                }
            }
        }
    }

    [Fact]
    public void Packing_DependsOnlyOnTheOrderOfTheCalls()
    {
        static uint[] Pack()
        {
            AtlasBuilder builder = new(48);

            for (uint id = 1; id <= 20; id++)
            {
                int w = (int)((id * 7) % 13) + 1, h = (int)((id * 5) % 9) + 1;
                builder.Add($"i{id}", Image(w, h, id), w, h);
            }

            return builder.ToPixels();
        }

        Assert.Equal(Pack(), Pack());
    }

    [Fact]
    public void AnEmptyBuilder_IsOneTransparentRow()
    {
        AtlasBuilder builder = new(8);
        Assert.Equal(1, builder.Height);
        Assert.Equal(new uint[8], builder.ToPixels());
        Assert.Empty(builder.Regions);
    }

    [Fact]
    public void AnImageAsWideAsTheAtlas_Fits_AndAWiderOneIsRejected()
    {
        AtlasBuilder builder = new(16, padding: 1);
        Assert.Equal(new AtlasRegion(0, 0, 16, 2), builder.Add("full", Image(16, 2, 1), 16, 2));
        Assert.Equal(new AtlasRegion(0, 3, 16, 1), builder.Add("full2", Image(16, 1, 2), 16, 1));

        ArgumentException e = Assert.Throws<ArgumentException>(() => builder.Add("wide", Image(17, 1, 3), 17, 1));
        Assert.Contains("wide", e.Message, StringComparison.Ordinal);
        Assert.Equal(2, builder.Regions.Count);
    }

    [Fact]
    public void Add_ValidatesItsArguments()
    {
        AtlasBuilder builder = new(16);
        builder.Add("a", Image(2, 2, 1), 2, 2);

        Assert.Throws<ArgumentException>(() => builder.Add("a", Image(2, 2, 1), 2, 2));          // duplicate name
        Assert.Throws<ArgumentException>(() => builder.Add("", Image(2, 2, 1), 2, 2));
        Assert.Throws<ArgumentNullException>(() => builder.Add(null!, Image(2, 2, 1), 2, 2));
        Assert.Throws<ArgumentException>(() => builder.Add("b", Image(2, 2, 1), 2, 3));          // 4 pixels for 2 × 3
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Add("b", [], 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Add("b", [], 2, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtlasBuilder(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AtlasBuilder(16, -1));
        Assert.Equal(1, builder.Regions.Count);
    }

    [Fact]
    public void Cell_CutsARegionIntoAGrid_RowByRow()
    {
        AtlasRegion sheet = new(10, 20, 64, 32);

        Assert.Equal(new AtlasRegion(10, 20, 16, 16), sheet.Cell(0, 16, 16));
        Assert.Equal(new AtlasRegion(58, 20, 16, 16), sheet.Cell(3, 16, 16));
        Assert.Equal(new AtlasRegion(10, 36, 16, 16), sheet.Cell(4, 16, 16));
        Assert.Equal(new AtlasRegion(58, 36, 16, 16), sheet.Cell(7, 16, 16));

        // A cell size that does not divide the region leaves the remainder unused.
        Assert.Equal(new AtlasRegion(40, 20, 30, 32), sheet.Cell(1, 30, 32));

        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.Cell(8, 16, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.Cell(-1, 16, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.Cell(0, 0, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.Cell(0, 65, 16));    // no whole cell fits
    }

    [MetalFact]
    public void Build_UploadsThePixels_WithTheNamedRegions()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        AtlasBuilder builder = new(16, padding: 1);
        AtlasRegion a = builder.Add("a", Image(5, 3, 1), 5, 3);
        AtlasRegion b = builder.Add("b", Image(7, 4, 2), 7, 4);

        using SpriteAtlas atlas = builder.Build(device);

        Assert.Equal((16, 4), (atlas.Width, atlas.Height));
        Assert.Equal(builder.ToPixels(), atlas.Pixels.ToArray());
        Assert.Equal(a, atlas["a"]);
        Assert.True(atlas.TryGetRegion("b", out AtlasRegion found));
        Assert.Equal(b, found);
        Assert.False(atlas.TryGetRegion("c", out _));
        Assert.Throws<KeyNotFoundException>(() => atlas["c"]);
        Assert.Equal(new AtlasRegion(0, 0, 16, 4), atlas.Bounds);
        Assert.True(atlas.Contains(b));
        Assert.False(atlas.Contains(new AtlasRegion(10, 0, 7, 4)));
        Assert.False(atlas.Contains(new AtlasRegion(0, 0, 0, 4)));

        // The builder can keep growing and be built again; the first atlas is unaffected.
        builder.Add("c", Image(16, 2, 3), 16, 2);
        using SpriteAtlas larger = builder.Build(device);
        Assert.Equal(7, larger.Height);
        Assert.Equal(4, atlas.Height);
        Assert.False(atlas.TryGetRegion("c", out _));
    }

    [MetalFact]
    public void AnAtlas_CanWrapPixelsThatAreAlreadyLaidOut()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] sheet = Image(32, 16, 9);

        using SpriteAtlas atlas = new(device, sheet, 32, 16, [new("left", new AtlasRegion(0, 0, 16, 16))]);
        Assert.Equal(sheet, atlas.Pixels.ToArray());
        Assert.Equal(new AtlasRegion(16, 0, 16, 16), atlas.Bounds.Cell(1, 16, 16));
        Assert.Equal(new AtlasRegion(0, 0, 16, 16), atlas["left"]);

        Assert.Throws<ArgumentException>(() => new SpriteAtlas(device, sheet, 32, 15));
        Assert.Throws<ArgumentException>(() => new SpriteAtlas(device, sheet, 32, 16, [new("off", new AtlasRegion(20, 0, 16, 16))]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteAtlas(device, [], 0, 16));
        Assert.Throws<ArgumentNullException>(() => new SpriteAtlas(null!, sheet, 32, 16));
    }
}
