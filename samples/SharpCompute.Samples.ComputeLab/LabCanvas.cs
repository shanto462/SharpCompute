// Original SharpCompute sample (MIT).
using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Samples.ComputeLab;

/// <summary>The colors of the lab's text; the index goes into a text cell and <see cref="DrawText"/> looks it up.</summary>
public enum TextColor
{
    White = 0,
    Dim = 1,
    Good = 2,
    Bad = 3,
    Accent = 4,
    Cool = 5,
}

/// <summary>
/// What every page draws into: a fixed 1280 × 720 frame of packed RGBA pixels in a
/// <c>ReadWriteBuffer&lt;uint&gt;</c>, plus a grid of text cells drawn over it.
/// </summary>
/// <remarks>
/// The frame is a buffer rather than a texture because compute shaders can read and write buffers
/// freely: a page draws with one kernel, the text is composited by a second one that reads what is
/// under it, and <see cref="ToTexture"/> copies the result out for the window. One pixel is one
/// <c>uint</c> made by <see cref="Gpu.PackUnorm4x8"/>, which is byte for byte an <see cref="Rgba32"/>,
/// so the same memory is the PNG. Not thread-safe; use it from the thread that owns the pages.
/// </remarks>
public sealed class LabCanvas : IDisposable
{
    public const int Width = 1280;
    public const int Height = 720;

    /// <summary>Screen pixels per font pixel.</summary>
    public const int TextScale = 2;

    /// <summary>A text cell: a 5 × 7 glyph plus one font pixel of spacing each way, times <see cref="TextScale"/>.</summary>
    public const int CellWidth = (PixelFont.GlyphWidth + 1) * TextScale;
    public const int CellHeight = (PixelFont.GlyphHeight + 1) * TextScale;

    public const int Columns = Width / CellWidth;
    public const int Rows = Height / CellHeight;

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<uint> _cells;
    private readonly ReadOnlyBuffer<uint> _glyphs;
    private ReadWriteTexture2D<Rgba32>? _texture;

    public LabCanvas(GraphicsDevice device)
    {
        _device = device;
        Pixels = device.AllocateReadWriteBuffer<uint>(Width * Height);
        _cells = device.AllocateReadWriteBuffer<uint>(Columns * Rows);
        _glyphs = device.AllocateReadOnlyBuffer<uint>(PixelFont.Masks);
    }

    /// <summary>The frame, row by row from the top: <c>Pixels[y * Width + x]</c>, packed RGBA.</summary>
    public ReadWriteBuffer<uint> Pixels { get; }

    /// <summary>Removes all text. The pixels are left alone: every page redraws all of them.</summary>
    public void ClearText() => _cells.AsSpan().Clear();

    /// <summary>
    /// Puts <paramref name="text"/> into the cell grid at (<paramref name="column"/>, <paramref name="row"/>);
    /// what does not fit is cut off. Each cell holds a glyph, a color and a "used" bit, so that a
    /// space inside a text still gets the dark backing that keeps text readable over any page.
    /// </summary>
    public void Text(int column, int row, string text, TextColor color = TextColor.White)
    {
        if ((uint)row >= Rows)
        {
            return;
        }

        Span<uint> cells = _cells.AsSpan();

        for (int i = 0; i < text.Length; i++)
        {
            int c = column + i;

            if ((uint)c < Columns)
            {
                cells[(row * Columns) + c] = (uint)PixelFont.Index(text[i]) | ((uint)color << 8) | (1u << 16);
            }
        }
    }

    /// <summary>Text at a pixel position, snapped to the cell grid (for labels next to what a page drew).</summary>
    public void Label(int x, int y, string text, TextColor color = TextColor.Dim) => Text(x / CellWidth, y / CellHeight, text, color);

    /// <summary>Draws the text cells over the pixels, on the GPU.</summary>
    public void ComposeText() => _device.For(Width, Height, new DrawText(Pixels, _cells, _glyphs, Width, Columns));

    /// <summary>The frame as a texture a <c>MetalWindow</c> can present. The texture is owned by the canvas and reused.</summary>
    public ReadWriteTexture2D<Rgba32> ToTexture()
    {
        _texture ??= _device.AllocateReadWriteTexture2D<Rgba32>(Width, Height);
        _device.ForEach(_texture, new Blit(Pixels, Width));
        return _texture;
    }

    /// <summary>The frame as pixels, read straight out of the buffer (no GPU work, no conversion).</summary>
    public Rgba32[] ToArray() => MemoryMarshal.Cast<uint, Rgba32>(Pixels.AsReadOnlySpan()).ToArray();

    public void Dispose()
    {
        Pixels.Dispose();
        _cells.Dispose();
        _glyphs.Dispose();
        _texture?.Dispose();
    }
}

/// <summary>
/// Composites the text cells over the frame: a used cell darkens what is under it (unpack, scale,
/// pack) and the set bits of its glyph get the cell's color.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawText(ReadWriteBuffer<uint> pixels, ReadWriteBuffer<uint> cells, ReadOnlyBuffer<uint> glyphs, int width, int columns) : IComputeShader
{
    private static Float3 Color(uint index)
    {
        if (index == 1u)
        {
            return new Float3(0.62f, 0.66f, 0.72f);
        }

        if (index == 2u)
        {
            return new Float3(0.45f, 0.95f, 0.55f);
        }

        if (index == 3u)
        {
            return new Float3(1f, 0.4f, 0.35f);
        }

        if (index == 4u)
        {
            return new Float3(1f, 0.8f, 0.35f);
        }

        if (index == 5u)
        {
            return new Float3(0.45f, 0.85f, 1f);
        }

        return new Float3(1f, 1f, 1f);
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        uint cell = cells[((y / LabCanvas.CellHeight) * columns) + (x / LabCanvas.CellWidth)];

        if (cell != 0u)
        {
            int i = (y * width) + x;
            int gx = (x % LabCanvas.CellWidth) / LabCanvas.TextScale;
            int gy = (y % LabCanvas.CellHeight) / LabCanvas.TextScale;
            bool lit = false;

            if (gx < PixelFont.GlyphWidth && gy < PixelFont.GlyphHeight)
            {
                int bit = (gy * PixelFont.GlyphWidth) + gx;
                uint word = glyphs[((int)(cell & 255u) * 2) + (bit >> 5)];
                lit = ((word >> (bit & 31)) & 1u) != 0u;
            }

            if (lit)
            {
                pixels[i] = Gpu.PackUnorm4x8(new Float4(Color((cell >> 8) & 15u), 1f));
            }
            else
            {
                Float4 under = Gpu.UnpackUnorm4x8(pixels[i]);
                pixels[i] = Gpu.PackUnorm4x8(new Float4(under.XYZ * 0.22f, 1f));
            }
        }
    }
}

/// <summary>Copies the packed frame into the texture the window presents.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Blit(ReadWriteBuffer<uint> pixels, int width) : IComputeShader<Float4>
{
    public Float4 Execute() => Gpu.UnpackUnorm4x8(pixels[(ThreadIds.Y * width) + ThreadIds.X]);
}
