using System;
using System.Collections.Generic;

namespace SharpCompute.Game.Tests;

/// <summary>The sprite batch on the GPU: where sprites land, flips, tints, blending, clipping and limits.</summary>
public sealed class SpriteBatchTests : IDisposable
{
    private const int Width = 70;      // deliberately not multiples of the 16-pixel tile
    private const int Height = 45;

    private static readonly Rgba32 Background = new(20, 40, 60, 255);
    private static readonly Rgba32 White = new(255, 255, 255, 255);

    private readonly GraphicsDevice? _device;
    private readonly SpriteAtlas? _atlas;
    private readonly FrameBuffer? _frame;
    private readonly uint[] _atlasPixels = [];

    // Regions of the test atlas.
    private readonly AtlasRegion _ramp;     // 4 × 3, every texel a different opaque color
    private readonly AtlasRegion _glass;    // 2 × 2, alpha 128
    private readonly AtlasRegion _ring;     // 3 × 3, opaque with a transparent middle
    private readonly AtlasRegion _dot;      // 1 × 1 opaque white

    public SpriteBatchTests()
    {
        if (!GraphicsDevice.IsSupported)
        {
            return;
        }

        _device = GraphicsDevice.GetDefault();
        AtlasBuilder builder = new(16);
        uint[] ramp = new uint[12];

        for (int i = 0; i < ramp.Length; i++)
        {
            ramp[i] = Rgba((byte)(40 + (i * 17)), (byte)(250 - (i * 19)), (byte)(i * 9), 255);
        }

        uint solid = Rgba(200, 100, 50, 255);
        _ramp = builder.Add("ramp", ramp, 4, 3);
        _glass = builder.Add("glass", [Rgba(255, 0, 0, 128), Rgba(0, 255, 0, 128), Rgba(0, 0, 255, 128), Rgba(90, 90, 90, 128)], 2, 2);
        _ring = builder.Add("ring", [solid, solid, solid, solid, Rgba(255, 255, 255, 0), solid, solid, solid, solid], 3, 3);
        _dot = builder.Add("dot", [0xFFFFFFFFu], 1, 1);
        _atlasPixels = builder.ToPixels();
        _atlas = builder.Build(_device);
        _frame = new FrameBuffer(_device, Width, Height);
    }

    public void Dispose()
    {
        _frame?.Dispose();
        _atlas?.Dispose();
    }

    [MetalFact]
    public void ASprite_LandsAtItsPixels_TexelForTexel_AndNowhereElse()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        Assert.True(batch.Draw(_ramp, 30, 20));
        Render(batch, Background);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool inside = x is >= 30 and < 34 && y is >= 20 and < 23;
                uint expected = inside ? Texel(_ramp, x - 30, y - 20) : FrameBuffer.Pack(Background);
                Assert.Equal(expected, _frame!.Packed[(y * Width) + x]);
            }
        }
    }

    [MetalTheory]
    [InlineData(SpriteFlip.None)]
    [InlineData(SpriteFlip.Horizontal)]
    [InlineData(SpriteFlip.Vertical)]
    [InlineData(SpriteFlip.Horizontal | SpriteFlip.Vertical)]
    public void Flip_MirrorsTheSprite_InPlace(SpriteFlip flip)
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_ramp, 17, 9, flip: flip);
        Render(batch, Background);

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                int u = flip.HasFlag(SpriteFlip.Horizontal) ? 3 - x : x;
                int v = flip.HasFlag(SpriteFlip.Vertical) ? 2 - y : y;
                Assert.Equal(Texel(_ramp, u, v), _frame!.Packed[((9 + y) * Width) + 17 + x]);
            }
        }

        // The footprint does not move.
        Assert.Equal(Background, _frame![16, 9]);
        Assert.Equal(Background, _frame[21, 11]);
        Assert.Equal(Background, _frame[17, 12]);
    }

    [MetalFact]
    public void Tint_MultipliesEveryChannel_Rounded()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_ring, 5, 5, tint: new Rgba32(128, 255, 0, 255));
        batch.Draw(_dot, 40, 5, tint: new Rgba32(13, 77, 201, 255));
        batch.Draw(_ring, 50, 5, tint: null);
        Render(batch, Background);

        // (200, 100, 50) × (128, 255, 0) / 255, rounded to nearest.
        Assert.Equal(new Rgba32(100, 100, 0, 255), _frame![5, 5]);
        Assert.Equal(Background, _frame[6, 6]);                       // the ring's hole
        Assert.Equal(new Rgba32(13, 77, 201, 255), _frame[40, 5]);    // white × tint is the tint, exactly
        Assert.Equal(new Rgba32(200, 100, 50, 255), _frame[50, 5]);   // no tint leaves the texel alone
    }

    [MetalFact]
    public void AlphaBlend_MixesWithWhatIsBehind_ByTheSourceAlpha()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_glass, 10, 10);
        batch.Draw(_dot, 20, 10, tint: new Rgba32(255, 255, 255, 0));     // fully transparent tint: invisible
        batch.Draw(_dot, 22, 10, tint: new Rgba32(100, 200, 50, 64));
        Render(batch, Background);

        // Red at alpha 128 over (20, 40, 60): (255·128 + 20·127 + 127) / 255, and so on.
        Assert.Equal(new Rgba32(138, 20, 30, 255), _frame![10, 10]);
        Assert.Equal(new Rgba32(10, 148, 30, 255), _frame[11, 10]);
        Assert.Equal(new Rgba32(10, 20, 158, 255), _frame[10, 11]);
        Assert.Equal(Background, _frame[20, 10]);
        Assert.Equal(new Rgba32(40, 80, 57, 255), _frame[22, 10]);
    }

    [MetalFact]
    public void AlphaBlend_OntoATransparentFrame_KeepsTheSourceAlpha()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_glass, 10, 10);
        batch.Draw(_ramp, 20, 10);
        Render(batch, clear: default(Rgba32));

        Assert.Equal(new Rgba32(128, 0, 0, 128), _frame![10, 10]);     // straight alpha: color weighted once
        Assert.Equal(Texel(_ramp, 0, 0), _frame.Packed[(10 * Width) + 20]);
        Assert.Equal(default, _frame[0, 0]);
    }

    [MetalFact]
    public void Additive_AddsLight_AndClampsAtWhite()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_dot, 10, 10, tint: new Rgba32(100, 10, 0, 255), blend: SpriteBlend.Additive);
        batch.Draw(_dot, 12, 10, tint: new Rgba32(250, 250, 250, 255), blend: SpriteBlend.Additive);
        batch.Draw(_glass, 14, 10, blend: SpriteBlend.Additive);

        // Twice on the same pixel adds twice.
        batch.Draw(_dot, 30, 10, tint: new Rgba32(60, 60, 60, 255), blend: SpriteBlend.Additive);
        batch.Draw(_dot, 30, 10, tint: new Rgba32(60, 60, 60, 255), blend: SpriteBlend.Additive);
        Render(batch, Background);

        Assert.Equal(new Rgba32(120, 50, 60, 255), _frame![10, 10]);
        Assert.Equal(new Rgba32(255, 255, 255, 255), _frame[12, 10]);
        Assert.Equal(new Rgba32(148, 40, 60, 255), _frame[14, 10]);      // red × 128/255 = 128, added
        Assert.Equal(new Rgba32(140, 160, 180, 255), _frame[30, 10]);
        Assert.Equal(Background, _frame[11, 10]);
    }

    [MetalFact]
    public void Additive_IsBrighterThanAlpha_ForTheSameSprite()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_glass, 10, 10, blend: SpriteBlend.Alpha);
        batch.Draw(_glass, 20, 10, blend: SpriteBlend.Additive);
        Render(batch, new Rgba32(100, 100, 100, 255));

        for (int i = 0; i < 4; i++)
        {
            Rgba32 alpha = _frame![10 + (i % 2), 10 + (i / 2)], additive = _frame[20 + (i % 2), 10 + (i / 2)];
            Assert.True(additive.R >= alpha.R && additive.G >= alpha.G && additive.B >= alpha.B);
            Assert.True(additive.R + additive.G + additive.B > alpha.R + alpha.G + alpha.B);
            Assert.True(additive.R >= 100 && additive.G >= 100 && additive.B >= 100, "Additive never darkens.");
        }
    }

    [MetalFact]
    public void Cutout_ReplacesThePixel_AlphaIncluded_AndSkipsTransparentTexels()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_glass, 10, 10, blend: SpriteBlend.Cutout);
        batch.Draw(_ring, 20, 10, blend: SpriteBlend.Cutout);
        Render(batch, Background);

        Assert.Equal(new Rgba32(255, 0, 0, 128), _frame![10, 10]);
        Assert.Equal(new Rgba32(90, 90, 90, 128), _frame[11, 11]);
        Assert.Equal(new Rgba32(200, 100, 50, 255), _frame[20, 10]);
        Assert.Equal(Background, _frame[21, 11]);
    }

    [MetalFact]
    public void Sprites_AreDrawnInOrder_LaterOverEarlier()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_ramp, 10, 10);
        batch.Draw(_ring, 11, 10);       // covers part of the ramp; its hole shows the ramp through
        Render(batch, Background);

        Assert.Equal(Texel(_ramp, 0, 0), _frame!.Packed[(10 * Width) + 10]);
        Assert.Equal(new Rgba32(200, 100, 50, 255), _frame[11, 10]);
        Assert.Equal(Texel(_ramp, 2, 1), _frame.Packed[(11 * Width) + 12]);
        Assert.Equal(new Rgba32(200, 100, 50, 255), _frame[13, 12]);
    }

    [MetalFact]
    public void Scale_TurnsEveryTexelIntoABlock()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_ramp, 8, 6, scale: 5, flip: SpriteFlip.Horizontal);
        Render(batch, Background);

        for (int y = 0; y < 15; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                Assert.Equal(Texel(_ramp, 3 - (x / 5), y / 5), _frame!.Packed[((6 + y) * Width) + 8 + x]);
            }
        }

        Assert.Equal(Background, _frame![7, 6]);
        Assert.Equal(Background, _frame[28, 20]);
        Assert.Equal(Background, _frame[8, 21]);
    }

    [MetalFact]
    public void Sprites_AreClippedAtEveryEdge()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        Assert.True(batch.Draw(_ramp, -2, -1));                          // top-left corner
        Assert.True(batch.Draw(_ramp, Width - 1, 10));                   // right edge: one column visible
        Assert.True(batch.Draw(_ramp, 20, Height - 2));                  // bottom edge: two rows visible
        Assert.True(batch.Draw(_ramp, Width - 3, Height - 1, scale: 2)); // bottom-right corner, scaled
        Assert.True(batch.Draw(_ramp, -3, 30));                          // left edge: one column visible
        Render(batch, Background);

        Assert.Equal(Texel(_ramp, 2, 1), _frame!.Packed[0]);
        Assert.Equal(Texel(_ramp, 3, 2), _frame.Packed[Width + 1]);
        Assert.Equal(Background, _frame[2, 0]);
        Assert.Equal(Texel(_ramp, 0, 1), _frame.Packed[(11 * Width) + Width - 1]);
        Assert.Equal(Texel(_ramp, 3, 1), _frame.Packed[((Height - 1) * Width) + 23]);
        Assert.Equal(Texel(_ramp, 1, 0), _frame.Packed[((Height - 1) * Width) + Width - 1]);
        Assert.Equal(Texel(_ramp, 3, 0), _frame.Packed[30 * Width]);
        Assert.Equal(Background, _frame[1, 30]);

        // Nothing wrapped around to the opposite edge.
        Assert.Equal(Background, _frame[0, 10]);
        Assert.Equal(Background, _frame[Width - 1, 0]);
        Assert.Equal(Background, _frame[20, 0]);
    }

    [MetalFact]
    public void SpritesEntirelyOffTheFrame_AreCulled_AndNotCounted()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();

        Assert.False(batch.Draw(_ramp, -4, 10));          // touches the left edge from outside
        Assert.False(batch.Draw(_ramp, 10, -3));
        Assert.False(batch.Draw(_ramp, Width, 10));
        Assert.False(batch.Draw(_ramp, 10, Height));
        Assert.False(batch.Draw(_ramp, -8, 10, scale: 2));
        Assert.False(batch.Draw(_ramp, int.MinValue, int.MinValue));
        Assert.False(batch.Draw(_ramp, int.MaxValue, int.MaxValue, scale: 1024));
        Assert.Equal(0, batch.Count);

        Assert.True(batch.Draw(_ramp, -7, 10, scale: 2)); // one column of the last texel shows
        Assert.Equal(1, batch.Count);
    }

    [MetalFact]
    public void Origin_ShiftsWorldCoordinates_IntoTheFrame()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Origin = (1000, 500);
        Assert.True(batch.Draw(_dot, 1010, 520));
        Assert.False(batch.Draw(_dot, 10, 20));           // far outside the camera
        batch.Origin = (0, 0);
        batch.Draw(_dot, 3, 4);
        Render(batch, Background);

        Assert.Equal(White, _frame![10, 20]);
        Assert.Equal(White, _frame[3, 4]);

        batch.Origin = (5, 5);
        batch.Begin();
        Assert.Equal((0, 0), batch.Origin);               // Begin resets it
    }

    [MetalFact]
    public void WithoutAClear_TheBatchDrawsOverTheFrame_AndLeavesTheRestUntouched()
    {
        using SpriteBatch batch = NewBatch();
        Span<uint> pixels = _frame!.AsSpan();

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (uint)i | 0xFF000000u;
        }

        batch.Begin();
        batch.Draw(_glass, 40, 20);
        batch.Draw(_dot, 1, 1, blend: SpriteBlend.Additive);
        Render(batch, clear: null);

        for (int i = 0; i < Width * Height; i++)
        {
            int x = i % Width, y = i / Width;

            if ((x is 40 or 41 && y is 20 or 21) || (x == 1 && y == 1))
            {
                Assert.NotEqual((uint)i | 0xFF000000u, _frame.Packed[i]);
            }
            else
            {
                Assert.Equal((uint)i | 0xFF000000u, _frame.Packed[i]);
            }
        }

        // Red at alpha 128 over the pixel that was there (index 20 · 70 + 40 = 1440 = 0x5A0: R 160, G 5).
        Assert.Equal(new Rgba32(208, 2, 0, 255), _frame[40, 20]);
    }

    [MetalFact]
    public void AnEmptyBatch_RecordsNothing_UnlessItClears()
    {
        using SpriteBatch batch = NewBatch();
        using ComputeContext context = _device!.CreateComputeContext();
        _frame!.Clear(new Rgba32(1, 2, 3, 4));
        batch.Begin();

        batch.Record(context, _frame);
        Assert.Equal(0, context.PendingCount);

        batch.Record(context, _frame, Background);
        Assert.Equal(2, context.PendingCount);
        context.Execute();
        Assert.All(_frame.Pixels.ToArray(), static p => Assert.Equal(Background, p));
    }

    [MetalFact]
    public void ABatchBeyondItsCapacity_DropsTheExtraSprites()
    {
        using SpriteBatch batch = new(_device!, _atlas!, Width, Height, capacity: 3);
        batch.Begin();

        Assert.True(batch.Draw(_dot, 1, 1));
        Assert.True(batch.Draw(_dot, 3, 1));
        Assert.True(batch.Draw(_dot, 5, 1));
        Assert.False(batch.Draw(_dot, 7, 1));
        Assert.Equal(3, batch.Count);
        Assert.Equal(3, batch.MaxSpritesPerTile);       // clamped to the capacity
        Render(batch, Background);

        Assert.Equal(White, _frame![5, 1]);
        Assert.Equal(Background, _frame[7, 1]);

        batch.Begin();
        Assert.Equal(0, batch.Count);
        Assert.True(batch.Draw(_dot, 7, 1));
    }

    [MetalFact]
    public void ATileBeyondItsLimit_KeepsTheFirstSprites_AndOtherTilesAreUnaffected()
    {
        using SpriteBatch batch = new(_device!, _atlas!, Width, Height, capacity: 64, maxSpritesPerTile: 4);
        batch.Begin();

        // Six sprites in the tile at (0, 0); the last two don't fit its list.
        for (int i = 0; i < 6; i++)
        {
            batch.Draw(_dot, i * 2, 3);
        }

        // The neighbouring tile has its own list.
        for (int i = 0; i < 4; i++)
        {
            batch.Draw(_dot, 16 + (i * 2), 3);
        }

        Render(batch, Background);

        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(i < 4 ? White : Background, _frame![i * 2, 3]);
        }

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(White, _frame![16 + (i * 2), 3]);
        }
    }

    [MetalFact]
    public void ASpriteAcrossManyTiles_IsWhole()
    {
        using SpriteBatch batch = NewBatch();
        batch.Begin();
        batch.Draw(_ramp, 3, 2, scale: 14);      // 56 × 42: spans a 4 × 3 block of tiles
        Render(batch, Background);

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool inside = x is >= 3 and < 59 && y is >= 2 and < 44;
                uint expected = inside ? Texel(_ramp, (x - 3) / 14, (y - 2) / 14) : FrameBuffer.Pack(Background);
                Assert.Equal(expected, _frame!.Packed[(y * Width) + x]);
            }
        }
    }

    [MetalTheory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void ARandomBatch_MatchesTheCpuReference_PixelForPixel(int seed, bool clear)
    {
        using SpriteBatch batch = new(_device!, _atlas!, Width, Height, capacity: 300, maxSpritesPerTile: 300);
        Random random = new(seed);
        AtlasRegion[] regions = [_ramp, _glass, _ring, _dot];
        uint[] expected = new uint[Width * Height];
        Span<uint> pixels = _frame!.AsSpan();

        for (int i = 0; i < expected.Length; i++)
        {
            expected[i] = clear ? FrameBuffer.Pack(Background) : (uint)random.Next() | ((uint)random.Next(2) << 31);
        }

        expected.CopyTo(pixels);
        batch.Begin();

        for (int i = 0; i < 300; i++)
        {
            AtlasRegion region = regions[random.Next(regions.Length)];
            int x = random.Next(-12, Width + 4), y = random.Next(-12, Height + 4), scale = random.Next(1, 5);
            Rgba32 tint = random.Next(3) == 0 ? White : new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            SpriteFlip flip = (SpriteFlip)random.Next(4);
            SpriteBlend blend = (SpriteBlend)random.Next(3);

            if (batch.Draw(region, x, y, tint, flip, blend, scale))
            {
                Reference(expected, region, x, y, FrameBuffer.Pack(tint), flip, blend, scale);
            }
        }

        Assert.True(batch.Count > 200);
        Render(batch, clear ? Background : null);

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(expected[i] == _frame.Packed[i], $"Pixel ({i % Width}, {i / Width}): expected {expected[i]:X8}, got {_frame.Packed[i]:X8}.");
        }
    }

    [MetalFact]
    public void Rendering_IsDeterministic()
    {
        using SpriteBatch batch = NewBatch();
        uint[]? first = null;

        for (int run = 0; run < 3; run++)
        {
            batch.Begin();

            for (int i = 0; i < 100; i++)
            {
                batch.Draw(_glass, (i * 7) % Width, (i * 5) % Height, new Rgba32((byte)(i * 2), 200, (byte)(255 - i), 180), blend: (SpriteBlend)(i % 2), scale: 1 + (i % 3));
            }

            Render(batch, Background);
            first ??= _frame!.Packed.ToArray();
            Assert.Equal(first, _frame!.Packed.ToArray());
        }
    }

    [MetalFact]
    public void Draw_AndRecord_ValidateTheirArguments()
    {
        using SpriteBatch batch = NewBatch();
        using FrameBuffer other = new(_device!, Width, Height + 1);
        using ComputeContext context = _device!.CreateComputeContext();
        batch.Begin();

        Assert.Throws<ArgumentException>(() => batch.Draw(new AtlasRegion(0, 0, 17, 1), 0, 0));       // wider than the atlas
        Assert.Throws<ArgumentException>(() => batch.Draw(new AtlasRegion(-1, 0, 2, 2), 0, 0));
        Assert.Throws<ArgumentException>(() => batch.Draw(default, 0, 0));                           // empty
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.Draw(_dot, 0, 0, scale: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.Draw(_dot, 0, 0, scale: 1025));
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.Draw(_dot, 0, 0, blend: (SpriteBlend)3));
        Assert.Equal(0, batch.Count);

        batch.Draw(_dot, 0, 0);
        Assert.Throws<ArgumentException>(() => batch.Record(context, other));
        Assert.Throws<ArgumentNullException>(() => batch.Record(null!, _frame!));
        Assert.Throws<ArgumentNullException>(() => batch.Record(context, null!));
        Assert.Throws<ArgumentNullException>(() => batch.DrawText(null!, "A", 0, 0, White));
        Assert.Equal(0, context.PendingCount);

        Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteBatch(_device, _atlas!, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteBatch(_device, _atlas!, 10, 10, capacity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteBatch(_device, _atlas!, 10, 10, maxSpritesPerTile: 0));
        Assert.Throws<ArgumentNullException>(() => new SpriteBatch(_device, null!, 10, 10));
        Assert.Throws<ArgumentNullException>(() => new SpriteBatch(null!, _atlas!, 10, 10));
    }

    [MetalFact]
    public void FrameBuffer_ExposesTheSameMemory_AsPackedAndAsPixels()
    {
        using FrameBuffer frame = new(_device!, 5, 3);
        Assert.Equal((5, 3), (frame.Width, frame.Height));
        Assert.All(frame.Packed.ToArray(), static p => Assert.Equal(0u, p));

        frame.Clear(new Rgba32(1, 2, 3, 4));
        Assert.Equal(0x04030201u, frame.Packed[7]);
        frame.AsSpan()[(2 * 5) + 4] = 0x80706050u;
        Assert.Equal(new Rgba32(0x50, 0x60, 0x70, 0x80), frame[4, 2]);
        Assert.Equal(new Rgba32(1, 2, 3, 4), frame.Pixels[0]);
        Assert.Equal(15, frame.Buffer.Length);
        Assert.Same(_device, frame.Device);

        Assert.Equal(0x80706050u, FrameBuffer.Pack(new Rgba32(0x50, 0x60, 0x70, 0x80)));
        Assert.Equal(new Rgba32(0x50, 0x60, 0x70, 0x80), FrameBuffer.Unpack(0x80706050u));

        Assert.Throws<ArgumentOutOfRangeException>(() => frame[5, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => frame[0, -1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameBuffer(_device!, 0, 3));
        Assert.Throws<ArgumentNullException>(() => new FrameBuffer(null!, 5, 3));
    }

    private SpriteBatch NewBatch() => new(_device!, _atlas!, Width, Height);

    private void Render(SpriteBatch batch, Rgba32? clear)
    {
        using ComputeContext context = _device!.CreateComputeContext();

        if (clear is { } color)
        {
            batch.Record(context, _frame!, color);
        }
        else
        {
            batch.Record(context, _frame!);
        }
    }

    private uint Texel(AtlasRegion region, int u, int v) => _atlasPixels[((region.Y + v) * _atlas!.Width) + region.X + u];

    private static uint Rgba(byte r, byte g, byte b, byte a) => FrameBuffer.Pack(new Rgba32(r, g, b, a));

    /// <summary>The batch's blending, written out on the CPU.</summary>
    private void Reference(uint[] frame, AtlasRegion region, int x, int y, uint tint, SpriteFlip flip, SpriteBlend blend, int scale)
    {
        for (int ly = 0; ly < region.Height * scale; ly++)
        {
            for (int lx = 0; lx < region.Width * scale; lx++)
            {
                int px = x + lx, py = y + ly;

                if (px < 0 || py < 0 || px >= Width || py >= Height)
                {
                    continue;
                }

                int u = flip.HasFlag(SpriteFlip.Horizontal) ? region.Width - 1 - (lx / scale) : lx / scale;
                int v = flip.HasFlag(SpriteFlip.Vertical) ? region.Height - 1 - (ly / scale) : ly / scale;
                uint texel = Texel(region, u, v);
                Span<uint> s = stackalloc uint[4], d = stackalloc uint[4];

                for (int c = 0; c < 4; c++)
                {
                    s[c] = ((((texel >> (8 * c)) & 0xFF) * ((tint >> (8 * c)) & 0xFF)) + 127) / 255;
                    d[c] = (frame[(py * Width) + px] >> (8 * c)) & 0xFF;
                }

                uint a = s[3];

                if (a == 0)
                {
                    continue;
                }

                for (int c = 0; c < 3; c++)
                {
                    d[c] = blend switch
                    {
                        SpriteBlend.Cutout => s[c],
                        SpriteBlend.Additive => Math.Min(255, d[c] + (((s[c] * a) + 127) / 255)),
                        _ => ((s[c] * a) + (d[c] * (255 - a)) + 127) / 255,
                    };
                }

                d[3] = blend switch
                {
                    SpriteBlend.Cutout => a,
                    SpriteBlend.Additive => d[3],
                    _ => a + (((d[3] * (255 - a)) + 127) / 255),
                };

                frame[(py * Width) + px] = d[0] | (d[1] << 8) | (d[2] << 16) | (d[3] << 24);
            }
        }
    }
}
