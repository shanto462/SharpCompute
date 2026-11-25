// Original SharpCompute sample (MIT).
using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Samples.Textures;

/// <summary>
/// The labels of a frame, as a texture: the CPU draws text with <see cref="PixelFont"/> into a
/// byte per pixel and uploads it into a <see cref="ReadOnlyTexture2D{T}"/> of <see cref="R8"/> the
/// size of the frame, which the display shaders read with <c>labels[ThreadIds.XY]</c>.
/// A pixel is <see cref="Ink"/> (text), <see cref="Plate"/> (the dark box behind text) or 0.
/// </summary>
/// <remarks>Not thread-safe; draw and upload between frames, never while a dispatch that reads it is in flight.</remarks>
public sealed class TextLayer : IDisposable
{
    /// <summary>A text pixel. A shader reads it as 1.</summary>
    public const byte Ink = 255;

    /// <summary>A pixel of the box behind text. A shader reads it as about 0.5.</summary>
    public const byte Plate = 128;

    private readonly byte[] _pixels;

    public TextLayer(GraphicsDevice device, int width, int height)
    {
        Width = width;
        Height = height;
        _pixels = new byte[width * height];
        Texture = device.AllocateReadOnlyTexture2D<R8>(width, height);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The labels as the shaders see them. Current after <see cref="Upload"/>.</summary>
    public ReadOnlyTexture2D<R8> Texture { get; }

    /// <summary>The value drawn at a pixel (before or after upload).</summary>
    public byte this[int x, int y] => _pixels[(y * Width) + x];

    public void Clear() => Array.Clear(_pixels);

    /// <summary>Fills a rectangle (clipped to the layer) with <paramref name="value"/>.</summary>
    public void Fill(int x, int y, int width, int height, byte value)
    {
        int x0 = Math.Max(x, 0), y0 = Math.Max(y, 0);
        int x1 = Math.Min(x + width, Width), y1 = Math.Min(y + height, Height);

        if (x1 <= x0)
        {
            return;
        }

        for (int row = y0; row < y1; row++)
        {
            _pixels.AsSpan((row * Width) + x0, x1 - x0).Fill(value);
        }
    }

    /// <summary>Draws <paramref name="text"/> with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>), each font pixel <paramref name="scale"/> pixels wide.</summary>
    public void Text(string text, int x, int y, int scale)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int left = x + (i * (PixelFont.GlyphWidth + 1) * scale);

            for (int row = 0; row < PixelFont.GlyphHeight; row++)
            {
                for (int column = 0; column < PixelFont.GlyphWidth; column++)
                {
                    if (PixelFont.IsSet(text[i], column, row))
                    {
                        Fill(left + (column * scale), y + (row * scale), scale, scale, Ink);
                    }
                }
            }
        }
    }

    /// <summary>Text on a plate that extends <paramref name="padding"/> pixels around it.</summary>
    public void Label(string text, int x, int y, int scale, int padding)
    {
        Fill(x - padding, y - padding, PixelFont.Measure(text, scale) + (2 * padding), (PixelFont.GlyphHeight * scale) + (2 * padding), Plate);
        Text(text, x, y, scale);
    }

    /// <summary>Copies what was drawn into <see cref="Texture"/>.</summary>
    public void Upload() => Texture.CopyFrom(MemoryMarshal.Cast<byte, R8>(_pixels));

    public void Dispose() => Texture.Dispose();
}
