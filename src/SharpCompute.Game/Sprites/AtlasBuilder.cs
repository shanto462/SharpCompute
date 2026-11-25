using System;
using System.Collections.Generic;

namespace SharpCompute.Game;

/// <summary>
/// Packs RGBA images into one atlas. Each <see cref="Add"/> places its image at once and returns
/// where it went, so fonts and sprite tables can be set up before the atlas is built; then
/// <see cref="Build"/> uploads the result as a <see cref="SpriteAtlas"/>.
/// </summary>
/// <remarks>
/// <para>
/// Packing is by shelves: images fill a row from left to right, and a new row starts below the
/// tallest image of the previous one when the next image doesn't fit. The width is fixed and the
/// height grows as needed. Adding images of similar height together (or tallest first) wastes the
/// least space. The result depends only on the order of the calls, so it is reproducible.
/// </para>
/// <para>
/// Images are in-memory pixels (packed RGBA8, R in the low byte, straight alpha, rows from the
/// top); decoding files is up to the caller. Not thread-safe.
/// </para>
/// </remarks>
public sealed class AtlasBuilder
{
    private readonly Dictionary<string, AtlasRegion> _regions = new(StringComparer.Ordinal);
    private uint[] _pixels = [];
    private int _shelfY;
    private int _shelfHeight;
    private int _cursorX;

    /// <param name="width">The atlas width in texels; no image may be wider.</param>
    /// <param name="padding">
    /// Transparent texels left between images. Sprites drawn at whole-number scales never read a
    /// neighbour, so 0 is safe today; 1 (the default) also keeps a filtered source from bleeding.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not positive or <paramref name="padding"/> is negative.</exception>
    public AtlasBuilder(int width = 256, int padding = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegative(padding);
        Width = width;
        Padding = padding;
    }

    /// <summary>The atlas width in texels.</summary>
    public int Width { get; }

    /// <summary>Transparent texels between images.</summary>
    public int Padding { get; }

    /// <summary>The height the atlas has reached so far (at least 1, so an empty atlas can still be built).</summary>
    public int Height => Math.Max(1, _shelfY + _shelfHeight);

    /// <summary>The regions added so far, by name.</summary>
    public IReadOnlyDictionary<string, AtlasRegion> Regions => _regions;

    /// <summary>Copies an image into the atlas and returns the region it occupies.</summary>
    /// <param name="name">A unique name to find the region by later.</param>
    /// <param name="pixels">Packed RGBA8, <paramref name="width"/> × <paramref name="height"/>.</param>
    /// <param name="width">Image width in texels.</param>
    /// <param name="height">Image height in texels.</param>
    /// <exception cref="ArgumentException">
    /// The name is empty or already used, the pixel count is not <paramref name="width"/> ×
    /// <paramref name="height"/>, or the image is wider than the atlas.
    /// </exception>
    public AtlasRegion Add(string name, ReadOnlySpan<uint> pixels, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Length != (long)width * height)
        {
            throw new ArgumentException($"A {width} × {height} image needs {(long)width * height} pixels, not {pixels.Length}.", nameof(pixels));
        }

        if (width > Width)
        {
            throw new ArgumentException($"Image '{name}' is {width} texels wide; the atlas is only {Width}.", nameof(width));
        }

        if (_regions.ContainsKey(name))
        {
            throw new ArgumentException($"The atlas already has a region named '{name}'.", nameof(name));
        }

        // A new shelf when this one is full. (An empty shelf always fits: width <= Width.)
        if (_cursorX + width > Width)
        {
            _shelfY += _shelfHeight + Padding;
            _shelfHeight = 0;
            _cursorX = 0;
        }

        AtlasRegion region = new(_cursorX, _shelfY, width, height);
        _cursorX += width + Padding;
        _shelfHeight = Math.Max(_shelfHeight, height);
        EnsureRows(checked(_shelfY + _shelfHeight));

        for (int row = 0; row < height; row++)
        {
            pixels.Slice(row * width, width).CopyTo(_pixels.AsSpan(((region.Y + row) * Width) + region.X, width));
        }

        _regions.Add(name, region);
        return region;
    }

    /// <summary>The packed atlas so far: <see cref="Width"/> × <see cref="Height"/> texels, transparent where nothing was placed.</summary>
    public uint[] ToPixels()
    {
        uint[] result = new uint[checked(Width * Height)];
        _pixels.AsSpan(0, Math.Min(_pixels.Length, result.Length)).CopyTo(result);
        return result;
    }

    /// <summary>Uploads the atlas to <paramref name="device"/>. The builder can keep growing and be built again.</summary>
    public SpriteAtlas Build(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return new SpriteAtlas(device, ToPixels(), Width, Height, _regions);
    }

    private void EnsureRows(int rows)
    {
        int needed = checked(rows * Width);

        if (needed > _pixels.Length)
        {
            Array.Resize(ref _pixels, Math.Max(needed, _pixels.Length * 2));
        }
    }
}
