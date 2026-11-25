// Original SharpCompute sample (MIT).
using System;
using System.Linq;

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
    /// The texture coordinate the shader samples at the center of frame pixel (<paramref name="x"/>,
    /// <paramref name="y"/>), which must lie in panel <paramref name="panel"/>: the same arithmetic
    /// as <see cref="SamplerGrid"/>, on the CPU.
    /// </summary>
    public Float2 Uv(int panel, int x, int y, float zoom, Float2 pan, float angle)
    {
        Float2 local = (new Float2(x + 0.5f, y + 0.5f) - PanelOrigin(panel)) / Cell;
        return SamplerGrid.PanelUv(local, Cell, zoom, pan, angle);
    }
}

/// <summary>
/// The sampler explorer: one small picture in each of the eight texture formats, and a frame that
/// magnifies the selected one through all six samplers at once.
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
    private int _format;

    public SamplerExplorer(GraphicsDevice device)
    {
        const int size = SourceImage.Size;
        Rgba32[] tile = SourceImage.Create();

        // The same picture in every format. The 8-bit formats keep the bytes (the sRGB one too: it
        // is the decode that differs), the float formats store value / 255, and the one- and
        // two-channel formats store what fits: brightness, or red and green.
        _rgba32 = device.AllocateReadOnlyTexture2D<Rgba32>(tile, size, size);
        _rgba32Srgb = device.AllocateReadOnlyTexture2D<Rgba32Srgb>(tile.Select(static p => new Rgba32Srgb(p.R, p.G, p.B, p.A)).ToArray(), size, size);
        _r8 = device.AllocateReadOnlyTexture2D<R8>(tile.Select(static p => new R8(SourceImage.Luminance(p))).ToArray(), size, size);
        _rg16 = device.AllocateReadOnlyTexture2D<Rg16>(tile.Select(static p => new Rg16(p.R, p.G)).ToArray(), size, size);
        _half = device.AllocateReadOnlyTexture2D<Half>(tile.Select(static p => (Half)(SourceImage.Luminance(p) / 255f)).ToArray(), size, size);
        _rgba16Float = device.AllocateReadOnlyTexture2D<Rgba16Float>(
            tile.Select(static p => new Rgba16Float((Half)(p.R / 255f), (Half)(p.G / 255f), (Half)(p.B / 255f), (Half)(p.A / 255f))).ToArray(), size, size);
        _single = device.AllocateReadOnlyTexture2D<float>(tile.Select(static p => SourceImage.Luminance(p) / 255f).ToArray(), size, size);
        _float4 = device.AllocateReadOnlyTexture2D<Float4>(tile.Select(static p => new Float4(p.R / 255f, p.G / 255f, p.B / 255f, p.A / 255f)).ToArray(), size, size);

        _decoded = device.AllocateReadWriteTexture2D<Float4>(size, size);
        _decodedView = _decoded.AsReadOnly();
    }

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
    /// Pans so that the picture follows the pointer: what was under a pixel is, afterwards, under
    /// the pixel (<paramref name="dx"/>, <paramref name="dy"/>) further on, in every panel and at
    /// any rotation.
    /// </summary>
    public void DragBy(double dx, double dy, ExplorerLayout layout)
    {
        // A pixel is Zoom / panel height texture heights, along the view's (possibly rotated) axes; see SamplerGrid.PanelUv.
        float scale = Zoom / layout.Cell.Y;
        float sine = MathF.Sin(Angle), cosine = MathF.Cos(Angle);
        float px = (float)dx * scale, py = (float)dy * scale;

        Pan -= new Float2((px * cosine) - (py * sine), (px * sine) + (py * cosine));
    }

    /// <summary>The shader that draws the grid for a frame of <paramref name="layout"/>'s size with the current view.</summary>
    public SamplerGrid Shader(ExplorerLayout layout, TextLayer labels) =>
        new(_decodedView, labels.Texture, layout.Origin, layout.Cell, layout.Gap, Zoom, Pan, Angle);

    /// <summary>Records the frame's two passes: read the selected format, then sample the result six ways into <paramref name="target"/>.</summary>
    public void Record(ComputeContext context, ReadWriteTexture2D<Rgba32> target, TextLayer labels)
    {
        const int size = SourceImage.Size;

        switch (_format)
        {
            case 0:
                context.For(size, size, new DecodeRgba32(_rgba32, _decoded));
                break;
            case 1:
                context.For(size, size, new DecodeRgba32Srgb(_rgba32Srgb, _decoded));
                break;
            case 2:
                context.For(size, size, new DecodeR8(_r8, _decoded));
                break;
            case 3:
                context.For(size, size, new DecodeRg16(_rg16, _decoded));
                break;
            case 4:
                context.For(size, size, new DecodeHalf(_half, _decoded));
                break;
            case 5:
                context.For(size, size, new DecodeRgba16Float(_rgba16Float, _decoded));
                break;
            case 6:
                context.For(size, size, new DecodeSingle(_single, _decoded));
                break;
            default:
                context.For(size, size, new DecodeFloat4(_float4, _decoded));
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
        labels.Text($"SOURCE[X, Y] READS {format.Reads}   OUTLINE = UV 0..1, THE REST IS THE ADDRESS MODE", 3 * small, (6 * small) + line, small);

        for (int panel = 0; panel < ExplorerLayout.PanelCount; panel++)
        {
            Float2 corner = layout.PanelOrigin(panel);
            int inset = (int)MathF.Ceiling(layout.Gap) + (3 * small);

            labels.Label("SAMPLER." + ExplorerLayout.SamplerNames[panel], (int)corner.X + inset, (int)corner.Y + inset, layout.LabelScale, 2 * small);
        }

        labels.Fill(0, labels.Height - layout.BottomBar, labels.Width, layout.BottomBar, TextLayer.Plate);
        labels.Text(
            "TAB PING-PONG   F FORMAT   +/- ZOOM   DRAG PAN   RIGHT-DRAG ZOOM   R ROTATE   0 RESET   S SAVE PNG   ESC QUIT",
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
