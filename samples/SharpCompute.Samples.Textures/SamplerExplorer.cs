// Original SharpCompute sample (MIT).
using System;
using System.IO;
using System.Linq;
using SharpCompute.Imaging;

namespace SharpCompute.Samples.Textures;

/// <summary>Where the explorer puts things in a frame: the bars of text and the grid of six panels.</summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
public readonly record struct ExplorerLayout(int Width, int Height)
{
    /// <summary>Number of panels: two filters by three address modes.</summary>
    public const int PanelCount = 6;

    /// <summary>The names of the samplers, in panel order (left to right, Point row then Linear row).</summary>
    public static string[] SamplerNames { get; } = ["PointClamp", "PointRepeat", "PointMirror", "LinearClamp", "LinearRepeat", "LinearMirror"];

    /// <summary>Pixels per font pixel for panel labels: 3 at 720p, more on a larger frame.</summary>
    public int LabelScale => Math.Max(1, Height / 240);

    /// <summary>Pixels per font pixel for the bars of small text.</summary>
    public int TextScale => Math.Max(1, LabelScale - 1);

    /// <summary>Height of the bar at the top: two lines of small text.</summary>
    public int TopBar => 19 * TextScale;

    /// <summary>Height of the help bar at the bottom: one line of small text.</summary>
    public int BottomBar => 11 * TextScale;

    /// <summary>Top-left corner of the grid.</summary>
    public Float2 Origin => new(0f, TopBar);

    /// <summary>Size of one panel.</summary>
    public Float2 Cell => new(Width / 3f, (Height - TopBar - BottomBar) / 2f);

    /// <summary>Half the gap between two panels.</summary>
    public float Gap => 1.5f * TextScale;

    /// <summary>The top-left corner of panel <paramref name="panel"/> (0..5), in pixels.</summary>
    public Float2 PanelOrigin(int panel) => new(Origin.X + ((panel % 3) * Cell.X), Origin.Y + ((panel / 3) * Cell.Y));

    /// <summary>
    /// The panel under frame position (<paramref name="x"/>, <paramref name="y"/>), 0..5. A position
    /// off the grid (in a bar of text, or outside the frame) counts for the nearest panel, whose
    /// mapping simply continues there, so the answer never jumps.
    /// </summary>
    public int PanelAt(double x, double y)
    {
        int column = Math.Clamp((int)Math.Floor((x - Origin.X) / Cell.X), 0, 2);
        int row = Math.Clamp((int)Math.Floor((y - Origin.Y) / Cell.Y), 0, 1);

        return (row * 3) + column;
    }

    /// <summary>
    /// The texture coordinate the shader samples at the center of frame pixel (<paramref name="x"/>,
    /// <paramref name="y"/>), which must lie in panel <paramref name="panel"/>: the same arithmetic
    /// as <see cref="SamplerGrid"/>, on the CPU.
    /// </summary>
    public Float2 Uv(int panel, int x, int y, float zoom, Float2 pan, float angle, float aspect = 1f)
    {
        Float2 local = (new Float2(x + 0.5f, y + 0.5f) - PanelOrigin(panel)) / Cell;
        return SamplerGrid.ImageUv(local, Cell, zoom, pan, angle, aspect);
    }
}

/// <summary>
/// The sampler explorer: one small picture in each of the eight texture formats, and a frame that
/// magnifies the selected one through all six samplers at once. The picture is the generated
/// <see cref="SourceImage"/> tile, or an image file loaded with SharpCompute.Imaging.
/// </summary>
/// <remarks>
/// Two passes a frame. The first copies <c>source[x, y]</c>, which is what a shader reads from the
/// selected format, into a <see cref="ReadWriteTexture2D{T}"/> of <see cref="Float4"/>. The second
/// samples that texture's <see cref="ReadWriteTexture2D{T}.AsReadOnly"/> view. Not thread-safe.
/// </remarks>
public sealed class SamplerExplorer : IDisposable
{
    /// <summary>The view the explorer starts with: three texture heights per panel, so a whole neighbour is visible on every side.</summary>
    public const float DefaultZoom = 2.6f;

    private readonly ReadOnlyTexture2D<Rgba32> _rgba32;
    private readonly ReadOnlyTexture2D<Rgba32Srgb> _rgba32Srgb;
    private readonly ReadOnlyTexture2D<R8> _r8;
    private readonly ReadOnlyTexture2D<Rg16> _rg16;
    private readonly ReadOnlyTexture2D<Half> _half;
    private readonly ReadOnlyTexture2D<Rgba16Float> _rgba16Float;
    private readonly ReadOnlyTexture2D<float> _single;
    private readonly ReadOnlyTexture2D<Float4> _float4;
    private readonly ReadWriteTexture2D<Float4> _decoded;
    private readonly ReadOnlyTexture2D<Float4> _decodedView;
    private readonly int _width;
    private readonly int _height;
    private int _format;

    /// <summary>Loads the formats with the generated tile, or with the image at <paramref name="imagePath"/>.</summary>
    /// <param name="device">The device.</param>
    /// <param name="imagePath">An image file (PNG, JPEG, HEIC, ...) to explore instead of the tile; null for the tile.</param>
    public SamplerExplorer(GraphicsDevice device, string? imagePath = null)
    {
        if (imagePath is not null)
        {
            // The image formats load straight from the file, each converted by the loader: sRGB bytes,
            // gray, and linear floats. The others are made from those, as for the tile.
            _rgba32 = ImageLoader.LoadTexture2D<Rgba32>(device, imagePath);
            _rgba32Srgb = ImageLoader.LoadTexture2D<Rgba32Srgb>(device, imagePath);
            _r8 = ImageLoader.LoadTexture2D<R8>(device, imagePath);
            _rgba16Float = ImageLoader.LoadTexture2D<Rgba16Float>(device, imagePath);
            _float4 = ImageLoader.LoadTexture2D<Float4>(device, imagePath);
            (_width, _height) = (_rgba32.Width, _rgba32.Height);

            byte[] gray = [.. _r8.ToArray().Select(static p => p.R)];
            _rg16 = device.AllocateReadOnlyTexture2D<Rg16>(_rgba32.ToArray().Select(static p => new Rg16(p.R, p.G)).ToArray(), _width, _height);
            _half = device.AllocateReadOnlyTexture2D<Half>(gray.Select(static g => (Half)(g / 255f)).ToArray(), _width, _height);
            _single = device.AllocateReadOnlyTexture2D<float>(gray.Select(static g => g / 255f).ToArray(), _width, _height);
            ImageName = Path.GetFileName(imagePath);
        }
        else
        {
            (_width, _height) = (SourceImage.Size, SourceImage.Size);
            (_rgba32, _rgba32Srgb, _r8, _rg16, _half, _rgba16Float, _single, _float4) = CreateTile(device);
        }

        _decoded = device.AllocateReadWriteTexture2D<Float4>(_width, _height);
        _decodedView = _decoded.AsReadOnly();
    }

    private static (ReadOnlyTexture2D<Rgba32>, ReadOnlyTexture2D<Rgba32Srgb>, ReadOnlyTexture2D<R8>, ReadOnlyTexture2D<Rg16>, ReadOnlyTexture2D<Half>,
        ReadOnlyTexture2D<Rgba16Float>, ReadOnlyTexture2D<float>, ReadOnlyTexture2D<Float4>) CreateTile(GraphicsDevice device)
    {
        const int size = SourceImage.Size;
        Rgba32[] tile = SourceImage.Create();

        // The same picture in every format. The 8-bit formats keep the bytes (the sRGB one too: it
        // is the decode that differs), the float formats store value / 255, and the one- and
        // two-channel formats store what fits: brightness, or red and green.
        return (
            device.AllocateReadOnlyTexture2D<Rgba32>(tile, size, size),
            device.AllocateReadOnlyTexture2D<Rgba32Srgb>(tile.Select(static p => new Rgba32Srgb(p.R, p.G, p.B, p.A)).ToArray(), size, size),
            device.AllocateReadOnlyTexture2D<R8>(tile.Select(static p => new R8(SourceImage.Luminance(p))).ToArray(), size, size),
            device.AllocateReadOnlyTexture2D<Rg16>(tile.Select(static p => new Rg16(p.R, p.G)).ToArray(), size, size),
            device.AllocateReadOnlyTexture2D<Half>(tile.Select(static p => (Half)(SourceImage.Luminance(p) / 255f)).ToArray(), size, size),
            device.AllocateReadOnlyTexture2D<Rgba16Float>(
                tile.Select(static p => new Rgba16Float((Half)(p.R / 255f), (Half)(p.G / 255f), (Half)(p.B / 255f), (Half)(p.A / 255f))).ToArray(), size, size),
            device.AllocateReadOnlyTexture2D<float>(tile.Select(static p => SourceImage.Luminance(p) / 255f).ToArray(), size, size),
            device.AllocateReadOnlyTexture2D<Float4>(tile.Select(static p => new Float4(p.R / 255f, p.G / 255f, p.B / 255f, p.A / 255f)).ToArray(), size, size));
    }

    /// <summary>The file name of the loaded image, or null for the generated tile.</summary>
    public string? ImageName { get; }

    /// <summary>Width of the picture in pixels.</summary>
    public int Width => _width;

    /// <summary>Height of the picture in pixels.</summary>
    public int Height => _height;

    /// <summary>The picture's width over its height: 1 for the tile.</summary>
    public float Aspect => _width / (float)_height;

    /// <summary>The selected format, an index into <see cref="SourceImage.Formats"/>.</summary>
    public int Format
    {
        get => _format;
        set => _format = ((value % SourceImage.Formats.Length) + SourceImage.Formats.Length) % SourceImage.Formats.Length;
    }

    /// <summary>Texture heights per panel height; smaller is closer.</summary>
    public float Zoom { get; set; } = DefaultZoom;

    /// <summary>The texture coordinate offset of the view.</summary>
    public Float2 Pan { get; set; }

    /// <summary>The rotation of the view, in radians.</summary>
    public float Angle { get; set; }

    /// <summary>What the last frame's first pass read from the selected format (valid once that frame has executed).</summary>
    public ReadWriteTexture2D<Float4> Decoded => _decoded;

    public void ResetView()
    {
        Zoom = DefaultZoom;
        Pan = default;
        Angle = 0f;
    }

    /// <summary>Multiplies <see cref="Zoom"/> by <paramref name="factor"/> (below 1 moves closer), within sensible limits.</summary>
    public void ZoomBy(float factor) => Zoom = Math.Clamp(Zoom * factor, 0.05f, 16f);

    /// <summary>
    /// <see cref="ZoomBy"/>, around a point of the frame instead of the middle of the panels: the
    /// texture coordinate under frame position (<paramref name="x"/>, <paramref name="y"/>) is still
    /// under it afterwards, at any rotation. Every panel maps its pixels the same way, so the matching
    /// point of the other five panels stays put too.
    /// </summary>
    public void ZoomAt(float factor, double x, double y, ExplorerLayout layout)
    {
        Float2 local = (new Float2((float)x, (float)y) - layout.PanelOrigin(layout.PanelAt(x, y))) / layout.Cell;
        float zoom = Zoom;

        ZoomBy(factor);

        // PanelUv is 0.5 + rotate(p) * zoom + pan: the pan takes up what the zoom's change would have
        // moved under the pointer. Both terms come from the shader's own mapping, without the pan, so
        // a large pan costs no precision; and the zoom is the one actually reached, so nothing slides
        // once it is at a limit.
        Pan += SamplerGrid.ImageUv(local, layout.Cell, zoom, default, Angle, Aspect) - SamplerGrid.ImageUv(local, layout.Cell, Zoom, default, Angle, Aspect);
    }

    /// <summary>
    /// Pans so that the picture follows the pointer: what was under a pixel is, afterwards, under
    /// the pixel (<paramref name="dx"/>, <paramref name="dy"/>) further on, in every panel and at
    /// any rotation.
    /// </summary>
    public void DragBy(double dx, double dy, ExplorerLayout layout)
    {
        // A pixel is Zoom / panel height texture heights, along the view's (possibly rotated) axes, and
        // a texture width is Aspect texture heights; see SamplerGrid.ImageUv.
        float scale = Zoom / layout.Cell.Y;
        float sine = MathF.Sin(Angle), cosine = MathF.Cos(Angle);
        float px = (float)dx * scale, py = (float)dy * scale;

        Pan -= new Float2(((px * cosine) - (py * sine)) / Aspect, (px * sine) + (py * cosine));
    }

    /// <summary>The shader that draws the grid for a frame of <paramref name="layout"/>'s size with the current view.</summary>
    public SamplerGrid Shader(ExplorerLayout layout, TextLayer labels) =>
        new(_decodedView, labels.Texture, layout.Origin, layout.Cell, layout.Gap, Zoom, Pan, Angle, Aspect);

    /// <summary>Records the frame's two passes: read the selected format, then sample the result six ways into <paramref name="target"/>.</summary>
    public void Record(ComputeContext context, ReadWriteTexture2D<Rgba32> target, TextLayer labels)
    {
        int width = _width, height = _height;

        switch (_format)
        {
            case 0:
                context.For(width, height, new DecodeRgba32(_rgba32, _decoded));
                break;
            case 1:
                context.For(width, height, new DecodeRgba32Srgb(_rgba32Srgb, _decoded));
                break;
            case 2:
                context.For(width, height, new DecodeR8(_r8, _decoded));
                break;
            case 3:
                context.For(width, height, new DecodeRg16(_rg16, _decoded));
                break;
            case 4:
                context.For(width, height, new DecodeHalf(_half, _decoded));
                break;
            case 5:
                context.For(width, height, new DecodeRgba16Float(_rgba16Float, _decoded));
                break;
            case 6:
                context.For(width, height, new DecodeSingle(_single, _decoded));
                break;
            default:
                context.For(width, height, new DecodeFloat4(_float4, _decoded));
                break;
        }

        context.ForEach(target, Shader(new ExplorerLayout(target.Width, target.Height), labels));
    }

    /// <summary>Draws the explorer's text: what the format is and reads as, a name on every panel, and the keys.</summary>
    public void DrawLabels(TextLayer labels)
    {
        ExplorerLayout layout = new(labels.Width, labels.Height);
        SourceFormat format = SourceImage.Formats[_format];
        int small = layout.TextScale;
        int line = PixelFont.GlyphHeight * small;

        labels.Fill(0, 0, labels.Width, layout.TopBar, TextLayer.Plate);
        labels.Text(
            $"SAMPLER EXPLORER   FORMAT {_format + 1}/{SourceImage.Formats.Length}: READONLYTEXTURE2D<{format.Type}> = {format.MetalFormat}",
            3 * small, 3 * small, small);
        string source = ImageName is null ? "" : $"   IMAGE {_width}X{_height} LOADED WITH SHARPCOMPUTE.IMAGING";
        labels.Text($"SOURCE[X, Y] READS {format.Reads}   OUTLINE = UV 0..1, THE REST IS THE ADDRESS MODE{source}", 3 * small, (6 * small) + line, small);

        for (int panel = 0; panel < ExplorerLayout.PanelCount; panel++)
        {
            Float2 corner = layout.PanelOrigin(panel);
            int inset = (int)MathF.Ceiling(layout.Gap) + (3 * small);

            labels.Label("SAMPLER." + ExplorerLayout.SamplerNames[panel], (int)corner.X + inset, (int)corner.Y + inset, layout.LabelScale, 2 * small);
        }

        labels.Fill(0, labels.Height - layout.BottomBar, labels.Width, layout.BottomBar, TextLayer.Plate);
        labels.Text(
            "TAB PING-PONG   F FORMAT   WHEEL OR +/- ZOOM   DRAG PAN   RIGHT-DRAG ZOOM   R ROTATE   0 RESET   S SAVE PNG   ESC QUIT",
            3 * small, labels.Height - layout.BottomBar + (3 * small), small);
    }

    public void Dispose()
    {
        _decodedView.Dispose();
        _decoded.Dispose();
        _float4.Dispose();
        _single.Dispose();
        _rgba16Float.Dispose();
        _half.Dispose();
        _rg16.Dispose();
        _r8.Dispose();
        _rgba32Srgb.Dispose();
        _rgba32.Dispose();
    }
}
