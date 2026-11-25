using System;

namespace SharpCompute.Game;

/// <summary>How a sprite's texels combine with the pixels already in the frame.</summary>
public enum SpriteBlend
{
    /// <summary>Straight alpha: the sprite covers what is behind it by its alpha.</summary>
    Alpha = 0,

    /// <summary>The sprite's color (times its alpha) is added to the frame and clamped: glows, sparks, light.</summary>
    Additive = 1,

    /// <summary>
    /// Every texel with any alpha replaces the pixel outright, alpha included; nothing is blended.
    /// For hard-edged pixel art, and for layers a later pass interprets (a game may use the alpha
    /// channel as a material id, say, and light the layer in its own shader).
    /// </summary>
    Cutout = 2,
}

/// <summary>Mirroring of a sprite.</summary>
[Flags]
public enum SpriteFlip
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
}

/// <summary>
/// Draws textured sprites into a <see cref="FrameBuffer"/> with compute shaders. Queue sprites with
/// <see cref="Draw(AtlasRegion, int, int, Rgba32?, SpriteFlip, SpriteBlend, int)"/> (they are
/// written straight into a shared GPU buffer: no upload, no per-frame allocation), then
/// <see cref="Record(ComputeContext, FrameBuffer)"/> adds two dispatches to your
/// <see cref="ComputeContext"/>: one bins the sprites into 16 × 16 pixel tiles, one shades every
/// pixel from its tile's list. Sprites are drawn in the order they were queued, later over earlier.
/// </summary>
/// <example>
/// <code>
/// batch.Begin();
/// batch.Draw(atlas["hero"], heroX, heroY, flip: facingLeft ? SpriteFlip.Horizontal : SpriteFlip.None);
/// batch.DrawText(font, "SCORE 100", 4, 4, new Rgba32(255, 255, 255, 255));
/// batch.Record(context, frame, clearColor: new Rgba32(10, 10, 30, 255));
/// context.Execute();
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>Limits.</b> A batch holds up to <see cref="Capacity"/> sprites; further draws are dropped
/// (<see cref="Draw(AtlasRegion, int, int, Rgba32?, SpriteFlip, SpriteBlend, int)"/> returns false).
/// A single tile lists at most <see cref="MaxSpritesPerTile"/> sprites; where more overlap one
/// tile, the last-queued ones are left out there. Sprites are placed on whole pixels and scaled by
/// whole numbers, with nearest-texel sampling; there is no rotation.
/// </para>
/// <para>
/// <b>Recording.</b> <see cref="Record(ComputeContext, FrameBuffer)"/> captures the sprite count,
/// but the sprites themselves live in shared memory the GPU reads when the context executes. So
/// execute the context before the next <see cref="Begin"/>: one batch is one draw per frame. To
/// draw two layers in a frame (a world layer and a HUD, say), use two batches.
/// </para>
/// <para>Not thread-safe: queue and record from one thread.</para>
/// </remarks>
public sealed class SpriteBatch : IDisposable
{
    /// <summary>The side of a binning tile, in pixels.</summary>
    public const int TileSize = 16;

    private const int MaxScale = 1024;

    private readonly ReadOnlyBuffer<SpriteInstance> _sprites;
    private readonly ReadWriteBuffer<int> _tileSprites;
    private readonly ReadWriteBuffer<int> _tileCounts;
    private readonly int _tilesX;
    private readonly int _tilesY;
    private int _count;

    /// <summary>A batch for frames of <paramref name="width"/> × <paramref name="height"/> pixels, drawing from <paramref name="atlas"/>.</summary>
    /// <param name="device">The device to allocate on; the atlas and the frames must belong to it.</param>
    /// <param name="atlas">The texels to draw from.</param>
    /// <param name="width">Width of the frames this batch draws into.</param>
    /// <param name="height">Height of the frames this batch draws into.</param>
    /// <param name="capacity">The most sprites one batch holds.</param>
    /// <param name="maxSpritesPerTile">The most sprites one 16 × 16 tile lists; clamped to <paramref name="capacity"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size, <paramref name="capacity"/> or <paramref name="maxSpritesPerTile"/> is not positive.</exception>
    public SpriteBatch(GraphicsDevice device, ISpriteAtlasSource atlas, int width, int height, int capacity = 1024, int maxSpritesPerTile = 64)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(atlas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpritesPerTile);

        Atlas = atlas;
        Width = width;
        Height = height;
        Capacity = capacity;
        MaxSpritesPerTile = Math.Min(maxSpritesPerTile, capacity);
        _tilesX = (width + TileSize - 1) / TileSize;
        _tilesY = (height + TileSize - 1) / TileSize;
        _sprites = device.AllocateReadOnlyBuffer<SpriteInstance>(capacity);
        _tileSprites = device.AllocateReadWriteBuffer<int>(checked(_tilesX * _tilesY * MaxSpritesPerTile));
        _tileCounts = device.AllocateReadWriteBuffer<int>(_tilesX * _tilesY);
    }

    /// <summary>The atlas sprites are drawn from.</summary>
    public ISpriteAtlasSource Atlas { get; }

    /// <summary>Width of the frames this batch draws into.</summary>
    public int Width { get; }

    /// <summary>Height of the frames this batch draws into.</summary>
    public int Height { get; }

    /// <summary>The most sprites one batch holds.</summary>
    public int Capacity { get; }

    /// <summary>The most sprites one tile lists.</summary>
    public int MaxSpritesPerTile { get; }

    /// <summary>Sprites queued since <see cref="Begin"/>.</summary>
    public int Count => _count;

    /// <summary>
    /// Subtracted from the position of every sprite queued from now on: set it to the camera's
    /// <see cref="Camera2D.Snapped"/> to draw in world coordinates, and back to (0, 0) for the HUD.
    /// </summary>
    public (int X, int Y) Origin { get; set; }

    /// <summary>Starts a new frame: forgets the queued sprites and resets <see cref="Origin"/>.</summary>
    public void Begin()
    {
        _count = 0;
        Origin = default;
    }

    /// <summary>Queues a sprite with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="source">The atlas region to draw.</param>
    /// <param name="x">Left edge, in frame pixels (world pixels when <see cref="Origin"/> is set).</param>
    /// <param name="y">Top edge.</param>
    /// <param name="tint">Multiplied into every texel, alpha included; null is white (no change).</param>
    /// <param name="flip">Mirroring.</param>
    /// <param name="blend">How the sprite combines with the frame.</param>
    /// <param name="scale">Whole-number magnification: each texel becomes a <c>scale × scale</c> block.</param>
    /// <returns>
    /// True when the sprite was queued; false when it lies entirely outside the frame (culled) or the
    /// batch is full.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is empty or not inside the atlas.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not in 1..1024.</exception>
    public bool Draw(AtlasRegion source, int x, int y, Rgba32? tint = null, SpriteFlip flip = SpriteFlip.None, SpriteBlend blend = SpriteBlend.Alpha, int scale = 1)
    {
        if (!SpriteAtlas.Contains(source, Atlas.Width, Atlas.Height))
        {
            throw new ArgumentException($"The source {source} is empty or not inside the {Atlas.Width} × {Atlas.Height} atlas.", nameof(source));
        }

        if (scale is < 1 or > MaxScale)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, $"The scale must be between 1 and {MaxScale}.");
        }

        if ((uint)blend > (uint)SpriteBlend.Cutout)
        {
            throw new ArgumentOutOfRangeException(nameof(blend), blend, "Unknown blend mode.");
        }

        // In long arithmetic, so a far-off sprite can't wrap around into view.
        long left = (long)x - Origin.X;
        long top = (long)y - Origin.Y;

        if (_count == Capacity ||
            left >= Width || top >= Height ||
            left + ((long)source.Width * scale) <= 0 || top + ((long)source.Height * scale) <= 0 ||
            left < int.MinValue / 2 || top < int.MinValue / 2)
        {
            return false;
        }

        _sprites.AsSpan()[_count++] = new SpriteInstance
        {
            X = (int)left,
            Y = (int)top,
            SourceX = source.X,
            SourceY = source.Y,
            Width = source.Width,
            Height = source.Height,
            Tint = tint is { } t ? FrameBuffer.Pack(t) : 0xFFFFFFFFu,
            Flags = ((int)flip & 3) | ((int)blend << 2) | (scale << 8),
        };

        return true;
    }

    /// <summary>
    /// Queues a line of text (or several, split at <c>'\n'</c>) with its top-left corner at
    /// (<paramref name="x"/>, <paramref name="y"/>). The font's glyphs must be regions of this
    /// batch's atlas.
    /// </summary>
    /// <returns>The number of glyphs queued (blank characters, culled glyphs and glyphs beyond <see cref="Capacity"/> are not counted).</returns>
    public int DrawText(BitmapFont font, ReadOnlySpan<char> text, int x, int y, Rgba32 color, int scale = 1, SpriteBlend blend = SpriteBlend.Alpha)
    {
        ArgumentNullException.ThrowIfNull(font);
        int queued = 0;

        foreach (PlacedGlyph glyph in font.Layout(text, x, y, scale))
        {
            if (Draw(glyph.Source, glyph.X, glyph.Y, color, SpriteFlip.None, blend, scale))
            {
                queued++;
            }
        }

        return queued;
    }

    /// <summary>
    /// Records the batch over what <paramref name="target"/> already holds. With nothing queued,
    /// nothing is recorded.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="target"/> is not <see cref="Width"/> × <see cref="Height"/>.</exception>
    public void Record(ComputeContext context, FrameBuffer target) => Record(context, target, clear: false, default);

    /// <summary>
    /// Records the batch onto <paramref name="target"/> cleared to <paramref name="clearColor"/>
    /// first, in the same pass (no separate clear, and the old frame is never read).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="target"/> is not <see cref="Width"/> × <see cref="Height"/>.</exception>
    public void Record(ComputeContext context, FrameBuffer target, Rgba32 clearColor) => Record(context, target, clear: true, clearColor);

    public void Dispose()
    {
        _sprites.Dispose();
        _tileSprites.Dispose();
        _tileCounts.Dispose();
    }

    private void Record(ComputeContext context, FrameBuffer target, bool clear, Rgba32 clearColor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);

        if (target.Width != Width || target.Height != Height)
        {
            throw new ArgumentException($"This batch draws {Width} × {Height} frames; the target is {target.Width} × {target.Height}.", nameof(target));
        }

        if (_count == 0 && !clear)
        {
            return;
        }

        context.For(_tilesX, _tilesY, new BinSprites(_sprites, _count, _tileSprites, _tileCounts, _tilesX, MaxSpritesPerTile));
        context.For(Width, Height, new DrawSprites(
            target.Buffer, Atlas.Pixels, _sprites, _tileSprites, _tileCounts,
            Width, Atlas.Width, _tilesX, MaxSpritesPerTile, clear ? 1 : 0, FrameBuffer.Pack(clearColor)));
    }
}
