namespace SharpCompute.Game;

/// <summary>One sprite to draw, as the shaders read it. Written by <see cref="SpriteBatch"/> straight into shared memory.</summary>
internal struct SpriteInstance
{
    /// <summary>Top-left corner in the frame, pixels (may be off-frame).</summary>
    public int X;
    public int Y;

    /// <summary>Top-left texel of the source region in the atlas.</summary>
    public int SourceX;
    public int SourceY;

    /// <summary>Size of the source region, texels.</summary>
    public int Width;
    public int Height;

    /// <summary>Packed RGBA multiplied into every texel.</summary>
    public uint Tint;

    /// <summary>Bit 0 mirror horizontally, bit 1 mirror vertically, bits 2–3 the blend mode, bits 8 and up the whole-number scale.</summary>
    public int Flags;
}

/// <summary>
/// Bins sprites into 16 × 16 pixel tiles: one thread per tile writes the list of sprites that
/// overlap it, in draw order. Every pixel then looks only at its tile's short list instead of at
/// every sprite, and because each tile is written by exactly one thread, no atomics are needed and
/// the result is deterministic.
/// </summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct BinSprites(
    ReadOnlyBuffer<SpriteInstance> sprites,
    int spriteCount,
    ReadWriteBuffer<int> tileSprites,
    ReadWriteBuffer<int> tileCounts,
    int tilesX,
    int maxPerTile) : IComputeShader
{
    private const int TileSize = 16;

    public void Execute()
    {
        int tile = (ThreadIds.Y * tilesX) + ThreadIds.X;
        int x0 = ThreadIds.X * TileSize;
        int y0 = ThreadIds.Y * TileSize;
        int x1 = x0 + TileSize;
        int y1 = y0 + TileSize;
        int n = 0;

        for (int k = 0; k < spriteCount && n < maxPerTile; k++)
        {
            SpriteInstance s = sprites[k];
            int scale = s.Flags >> 8;

            if (s.X < x1 && s.X + (s.Width * scale) > x0 && s.Y < y1 && s.Y + (s.Height * scale) > y0)
            {
                tileSprites[(tile * maxPerTile) + n] = k;
                n++;
            }
        }

        tileCounts[tile] = n;
    }
}

/// <summary>
/// Draws the binned sprites, one thread per frame pixel: start from the pixel already there (or the
/// clear color), then blend each sprite of the tile that covers the pixel, in draw order. All the
/// arithmetic is in integers, so the result is exact and the same on every run: an opaque texel
/// with a white tint lands in the frame unchanged.
/// </summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct DrawSprites(
    ReadWriteBuffer<uint> frame,
    ReadOnlyBuffer<uint> atlas,
    ReadOnlyBuffer<SpriteInstance> sprites,
    ReadWriteBuffer<int> tileSprites,
    ReadWriteBuffer<int> tileCounts,
    int width,
    int atlasWidth,
    int tilesX,
    int maxPerTile,
    int clear,
    uint clearColor) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int tile = ((y / 16) * tilesX) + (x / 16);
        int count = tileCounts[tile];

        // Nothing here and nothing to clear: leave the pixel untouched.
        if (count == 0 && clear == 0)
        {
            return;
        }

        int index = (y * width) + x;
        uint packed = frame[index];

        if (clear != 0)
        {
            packed = clearColor;
        }

        UInt4 color = Unpack(packed);

        for (int k = 0; k < count; k++)
        {
            SpriteInstance s = sprites[tileSprites[(tile * maxPerTile) + k]];
            int scale = s.Flags >> 8;
            int lx = x - s.X;
            int ly = y - s.Y;

            if (lx < 0 || ly < 0 || lx >= s.Width * scale || ly >= s.Height * scale)
            {
                continue;
            }

            int u = lx / scale;
            int v = ly / scale;

            if ((s.Flags & 1) != 0)
            {
                u = s.Width - 1 - u;
            }

            if ((s.Flags & 2) != 0)
            {
                v = s.Height - 1 - v;
            }

            // Tint: a per-channel multiply, rounded; white leaves the texel exactly as it is.
            UInt4 source = ((Unpack(atlas[((s.SourceY + v) * atlasWidth) + s.SourceX + u]) * Unpack(s.Tint)) + 127u) / 255u;
            uint alpha = source.W;

            if (alpha == 0u)
            {
                continue;
            }

            int blend = (s.Flags >> 2) & 3;

            if (blend == 2)
            {
                // Cutout: the texel replaces the pixel, alpha and all.
                color = source;
            }
            else if (blend == 1)
            {
                // Additive: light on top of what is there; the pixel keeps its own alpha.
                UInt4 sum = Gpu.Min(color + (((source * alpha) + 127u) / 255u), 255u);
                color = new UInt4(sum.X, sum.Y, sum.Z, color.W);
            }
            else
            {
                // Straight alpha, source over destination.
                UInt4 mixed = ((source * alpha) + (color * (255u - alpha)) + 127u) / 255u;
                color = new UInt4(mixed.X, mixed.Y, mixed.Z, alpha + (((color.W * (255u - alpha)) + 127u) / 255u));
            }
        }

        frame[index] = color.X | (color.Y << 8) | (color.Z << 16) | (color.W << 24);
    }

    private static UInt4 Unpack(uint c) => new(c & 0xFFu, (c >> 8) & 0xFFu, (c >> 16) & 0xFFu, c >> 24);
}
