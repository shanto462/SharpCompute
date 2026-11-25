// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;

namespace SharpCompute.Samples.Textures;

/// <summary>
/// The ping-pong mode: a Gray-Scott reaction-diffusion that lives in two
/// <see cref="ReadWriteTexture2D{T}"/> of <see cref="Float4"/>. Every step reads one and writes the
/// other, then they swap; a frame records several steps and the display pass in one
/// <see cref="ComputeContext"/>.
/// </summary>
/// <remarks>
/// A read-write texture cannot be sampled, so each texture has an
/// <see cref="ReadWriteTexture2D{T}.AsReadOnly"/> view made once up front: a step samples the view of
/// the texture it reads (PointRepeat, so the field wraps around), and the display samples the view of
/// the texture the last step wrote (LinearRepeat). Not thread-safe.
/// </remarks>
public sealed class ReactionDiffusion : IDisposable
{
    public const int Width = 320;
    public const int Height = 180;

    /// <summary>Steps recorded per displayed frame.</summary>
    public const int StepsPerFrame = 16;

    /// <summary>Radius of the mouse brush, in field pixels.</summary>
    public const float BrushRadius = 4f;

    /// <summary>How fast A is fed in. With <see cref="Kill"/>, the "coral growth" corner of the Gray-Scott parameter space: spots grow into branching walls.</summary>
    public const float Feed = 0.0545f;

    /// <summary>How fast B is removed.</summary>
    public const float Kill = 0.062f;

    private readonly GraphicsDevice _device;
    private readonly ReadWriteTexture2D<Float4>[] _textures;
    private readonly ReadOnlyTexture2D<Float4>[] _views;
    private readonly List<(Float2 Center, float Radius, bool Erase)> _strokes = [];
    private int _current;

    public ReactionDiffusion(GraphicsDevice device)
    {
        _device = device;
        _textures = [device.AllocateReadWriteTexture2D<Float4>(Width, Height), device.AllocateReadWriteTexture2D<Float4>(Width, Height)];
        _views = [_textures[0].AsReadOnly(), _textures[1].AsReadOnly()];
        Reset();
    }

    /// <summary>Frame heights of field per frame height in the display: above 1 shows the field tiled.</summary>
    public float Zoom { get; set; } = 1f;

    /// <summary>The texture that holds the latest state.</summary>
    public ReadWriteTexture2D<Float4> Current => _textures[_current];

    /// <summary>Steps taken since the last <see cref="Reset"/>.</summary>
    public long Steps { get; private set; }

    /// <summary>Starts over: A everywhere, and a fixed scatter of small spots of B to grow from.</summary>
    public void Reset()
    {
        Float4[] state = new Float4[Width * Height];
        state.AsSpan().Fill(new Float4(1f, 0f, 0f, 1f));

        // A small deterministic generator: the same spots every time.
        uint seed = 0x9E3779B9;

        for (int spot = 0; spot < 14; spot++)
        {
            int centerX = (int)(Next(ref seed) % Width), centerY = (int)(Next(ref seed) % Height);
            int radius = 2 + (int)(Next(ref seed) % 4);

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if ((dx * dx) + (dy * dy) <= radius * radius)
                    {
                        int x = ((centerX + dx) % Width + Width) % Width, y = ((centerY + dy) % Height + Height) % Height;
                        state[(y * Width) + x] = new Float4(0.5f, 1f, 0f, 1f);
                    }
                }
            }
        }

        _strokes.Clear();
        _current = 0;
        _textures[0].CopyFrom(state);
        Steps = 0;

        static uint Next(ref uint seed)
        {
            seed = (seed * 1664525u) + 1013904223u;
            return seed >> 8;
        }
    }

    /// <summary>
    /// Queues a dab of the brush at a field coordinate (0..1; outside wraps around). It is painted by
    /// the next <see cref="RecordSteps"/>, before the steps.
    /// </summary>
    public void Paint(Float2 uv, bool erase = false, float radius = BrushRadius) =>
        _strokes.Add((new Float2(uv.X * Width, uv.Y * Height), radius, erase));

    /// <summary>The field coordinate under frame pixel (<paramref name="x"/>, <paramref name="y"/>) of a <paramref name="width"/> × <paramref name="height"/> frame.</summary>
    public Float2 FrameToField(double x, double y, int width, int height) =>
        ShowField.FieldUv(new Float2((float)(x / width), (float)(y / height)), Zoom);

    /// <summary>Records the queued brush dabs, then <paramref name="steps"/> steps, each reading the texture the one before wrote.</summary>
    public void RecordSteps(ComputeContext context, int steps = StepsPerFrame)
    {
        foreach ((Float2 center, float radius, bool erase) in _strokes)
        {
            // Only the brush's bounding box is dispatched; the shader offsets its thread ids by `origin`.
            int extent = (int)MathF.Ceiling(radius);
            Int2 origin = new((int)MathF.Floor(center.X) - extent, (int)MathF.Floor(center.Y) - extent);
            int size = (2 * extent) + 1;

            context.For(size, size, new PaintBrush(_textures[_current], origin, center, radius, erase ? 1 : 0));
        }

        _strokes.Clear();

        for (int i = 0; i < steps; i++)
        {
            int next = 1 - _current;
            context.For(Width, Height, new ReactionStep(_views[_current], _textures[next], Feed, Kill));
            _current = next;
        }

        Steps += steps;
    }

    /// <summary>Records the display pass: the latest state, sampled through its read-only view into <paramref name="target"/>.</summary>
    public void RecordDisplay(ComputeContext context, ReadWriteTexture2D<Rgba32> target, TextLayer labels) =>
        context.ForEach(target, new ShowField(_views[_current], labels.Texture, Zoom));

    /// <summary>Runs <paramref name="steps"/> steps now, in one submission.</summary>
    public void Run(int steps)
    {
        using ComputeContext context = _device.CreateComputeContext();
        RecordSteps(context, steps);
    }

    /// <summary>Draws the mode's text.</summary>
    public void DrawLabels(TextLayer labels, bool paused)
    {
        ExplorerLayout layout = new(labels.Width, labels.Height);
        int small = layout.TextScale;
        int line = PixelFont.GlyphHeight * small;

        labels.Fill(0, 0, labels.Width, layout.TopBar, TextLayer.Plate);
        labels.Text(
            $"PING-PONG   TWO READWRITETEXTURE2D<FLOAT4>, {StepsPerFrame} REACTION-DIFFUSION STEPS A FRAME IN ONE COMPUTECONTEXT{(paused ? "   PAUSED" : "")}",
            3 * small, 3 * small, small);
        labels.Text(
            "EACH STEP SAMPLES THE OTHER TEXTURE THROUGH ASREADONLY() WITH POINTREPEAT, THE DISPLAY WITH LINEARREPEAT",
            3 * small, (6 * small) + line, small);

        labels.Fill(0, labels.Height - layout.BottomBar, labels.Width, layout.BottomBar, TextLayer.Plate);
        labels.Text(
            "TAB SAMPLERS   DRAG PAINT   RIGHT-DRAG ERASE   +/- ZOOM (TILES)   SPACE PAUSE   C RESTART   0 RESET ZOOM   S SAVE PNG   ESC QUIT",
            3 * small, labels.Height - layout.BottomBar + (3 * small), small);
    }

    public void Dispose()
    {
        foreach (ReadOnlyTexture2D<Float4> view in _views)
        {
            view.Dispose();
        }

        foreach (ReadWriteTexture2D<Float4> texture in _textures)
        {
            texture.Dispose();
        }
    }
}
