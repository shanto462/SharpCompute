using System;
using System.Collections.Generic;

namespace SharpCompute.Game;

/// <summary>A rectangle of an atlas, in texels: one sprite, one animation frame, one glyph.</summary>
public readonly record struct AtlasRegion(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Cell <paramref name="index"/> of this region cut into a grid of <paramref name="cellWidth"/> ×
    /// <paramref name="cellHeight"/> cells, counted row by row from the top-left: frame n of a sprite sheet.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The cell size is not positive, or the grid has no such cell.</exception>
    public AtlasRegion Cell(int index, int cellWidth, int cellHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellHeight);
        int columns = Width / cellWidth;
        int rows = Height / cellHeight;

        if (index < 0 || index >= columns * rows)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"A {Width} × {Height} region has {columns * rows} cells of {cellWidth} × {cellHeight}.");
        }

        return new AtlasRegion(X + (index % columns * cellWidth), Y + (index / columns * cellHeight), cellWidth, cellHeight);
    }
}

/// <summary>
/// Where a <see cref="SpriteBatch"/> gets its texels. Today that is a buffer of packed RGBA8 the
/// shader indexes directly; the interface is the seam for a sampled-texture source once the core
/// has texture reads, without changing code that draws sprites.
/// </summary>
public interface ISpriteAtlasSource
{
    /// <summary>Width in texels.</summary>
    int Width { get; }

    /// <summary>Height in texels.</summary>
    int Height { get; }

    /// <summary>
    /// The texels, packed RGBA8 with R in the low byte and straight (not premultiplied) alpha, row by
    /// row from the top: texel (x, y) is element <c>y * Width + x</c>.
    /// </summary>
    ReadOnlyBuffer<uint> Pixels { get; }
}

/// <summary>
/// Sprite images packed into one GPU buffer, with a named <see cref="AtlasRegion"/> for each. Build
/// one with <see cref="AtlasBuilder"/>, or wrap pixels that are already laid out (a sprite sheet).
/// </summary>
/// <remarks>
/// Immutable after construction, so safe to read from any thread. Disposing releases the GPU
/// buffer; a batch that still draws from it then fails with <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class SpriteAtlas : ISpriteAtlasSource, IDisposable
{
    private readonly Dictionary<string, AtlasRegion> _regions;

    /// <summary>An atlas over <paramref name="pixels"/> (copied to the GPU) with the given named regions.</summary>
    /// <param name="device">The device to allocate on.</param>
    /// <param name="pixels">Packed RGBA8, R in the low byte, <paramref name="width"/> × <paramref name="height"/>.</param>
    /// <param name="width">Width in texels.</param>
    /// <param name="height">Height in texels.</param>
    /// <param name="regions">Named regions, or null for none (use <see cref="Bounds"/> and <see cref="AtlasRegion.Cell"/>).</param>
    /// <exception cref="ArgumentException">The pixel count is not <paramref name="width"/> × <paramref name="height"/>, or a region lies outside the atlas.</exception>
    public SpriteAtlas(GraphicsDevice device, ReadOnlySpan<uint> pixels, int width, int height, IEnumerable<KeyValuePair<string, AtlasRegion>>? regions = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Length != (long)width * height)
        {
            throw new ArgumentException($"A {width} × {height} atlas needs {(long)width * height} pixels, not {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        _regions = new Dictionary<string, AtlasRegion>(StringComparer.Ordinal);

        foreach ((string name, AtlasRegion region) in regions ?? [])
        {
            if (!Contains(region))
            {
                throw new ArgumentException($"Region '{name}' ({region}) lies outside the {width} × {height} atlas.", nameof(regions));
            }

            _regions[name] = region;
        }

        Pixels = device.AllocateReadOnlyBuffer(pixels);
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyBuffer<uint> Pixels { get; }

    /// <summary>The whole atlas as one region.</summary>
    public AtlasRegion Bounds => new(0, 0, Width, Height);

    /// <summary>The named regions.</summary>
    public IReadOnlyDictionary<string, AtlasRegion> Regions => _regions;

    /// <summary>The region called <paramref name="name"/>.</summary>
    /// <exception cref="KeyNotFoundException">The atlas has no such region.</exception>
    public AtlasRegion this[string name] =>
        _regions.TryGetValue(name, out AtlasRegion region) ? region : throw new KeyNotFoundException($"The atlas has no region named '{name}'.");

    /// <summary>Looks a region up by name.</summary>
    public bool TryGetRegion(string name, out AtlasRegion region) => _regions.TryGetValue(name, out region);

    /// <summary>True when <paramref name="region"/> is non-empty and lies entirely inside the atlas.</summary>
    public bool Contains(AtlasRegion region) => Contains(region, Width, Height);

    public void Dispose() => Pixels.Dispose();

    internal static bool Contains(AtlasRegion region, int width, int height) =>
        region.X >= 0 && region.Y >= 0 && region.Width > 0 && region.Height > 0 &&
        (long)region.X + region.Width <= width && (long)region.Y + region.Height <= height;
}
