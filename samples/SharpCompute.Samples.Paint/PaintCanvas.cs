// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using SharpCompute.Samples.Imaging;

namespace SharpCompute.Samples.Paint;

/// <summary>
/// The GPU side of Paint: a premultiplied <see cref="Float4"/> canvas, brush strokes, whole-canvas
/// filters through ping-pong buffers, and a ring of undo snapshots. Every call is synchronous.
/// </summary>
/// <remarks>
/// Undo snapshots are packed to premultiplied RGBA8 (4 bytes per pixel instead of 16), so the 21
/// slots of a 1920×1080 canvas take 174 MB rather than 700 MB. Restoring one rounds the canvas to
/// 8 bits per channel, which is the precision it's displayed and saved at anyway.
/// </remarks>
public sealed class PaintCanvas : IDisposable
{
    public const int DefaultUndoSteps = 20;

    /// <summary>The most stamps one dispatch handles; longer runs are split, each with its own bounding box.</summary>
    public const int StampsPerDispatch = 64;

    public static readonly Float4 Paper = new(1f, 1f, 1f, 1f);

    private readonly GraphicsDevice _device;
    private readonly GpuFilters _filters;
    private readonly ReadWriteBuffer<float> _mask;
    private readonly ReadWriteBuffer<Float2> _stamps;
    private readonly ReadWriteBuffer<uint>?[] _snapshots;
    private ReadWriteBuffer<Float4> _canvas;

    // The back buffer is the filters' ping-pong target, and during a stroke it holds the stroke base.
    private ReadWriteBuffer<Float4> _back;
    private bool _stroking;
    private Float2? _lastStamp;

    public PaintCanvas(GraphicsDevice device, int width, int height, int undoSteps = DefaultUndoSteps)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _device = device;
        Width = width;
        Height = height;
        History = new UndoRing(undoSteps);
        _snapshots = new ReadWriteBuffer<uint>?[History.SlotCount];
        _filters = new GpuFilters(device, width, height);
        _canvas = device.AllocateReadWriteBuffer<Float4>(PixelCount);
        _back = device.AllocateReadWriteBuffer<Float4>(PixelCount);
        _mask = device.AllocateReadWriteBuffer<float>(PixelCount);
        _stamps = device.AllocateReadWriteBuffer<Float2>(StampsPerDispatch);

        Fill(Paper);
        Commit();
    }

    public int Width { get; }

    public int Height { get; }

    public UndoRing History { get; }

    /// <summary>The current canvas: premultiplied RGBA, row-major. Replaced (not modified) by filters and undo.</summary>
    public ReadWriteBuffer<Float4> Pixels => _canvas;

    public bool IsStroking => _stroking;

    private int PixelCount => Width * Height;

    /// <summary>Starts a stroke: remembers the canvas as the stroke base and clears the coverage mask.</summary>
    public void BeginStroke()
    {
        EndStroke();
        _device.For(PixelCount, new CopyPixels(_canvas, _back));
        _device.For(PixelCount, new FillMask(_mask, 0f));
        _stroking = true;
        _lastStamp = null;
    }

    /// <summary>
    /// Paints <paramref name="stamps"/> (canvas pixel positions) with <paramref name="brush"/> as
    /// the continuation of the current stroke: the brush is swept from the previous call's last
    /// stamp through these. Use one brush for the whole stroke.
    /// </summary>
    public void Stamp(ReadOnlySpan<Float2> stamps, BrushSettings brush)
    {
        ArgumentNullException.ThrowIfNull(brush);

        if (!_stroking)
        {
            throw new InvalidOperationException("Call BeginStroke before Stamp.");
        }

        if (stamps.IsEmpty)
        {
            return;
        }

        // Continue from the previous call's last stamp so the swept brush has no gaps between calls.
        Float2[] path = _lastStamp is Float2 last ? [last, .. stamps] : stamps.ToArray();
        _lastStamp = path[^1];

        float radius = brush.Radius;
        Float4 color = new(brush.Color, 1f);

        // Consecutive runs share their boundary stamp, so the segment between runs is painted too.
        for (int start = 0; start == 0 || start < path.Length - 1; start += StampsPerDispatch - 1)
        {
            ReadOnlySpan<Float2> run = path.AsSpan(start, Math.Min(StampsPerDispatch, path.Length - start));
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            foreach (Float2 s in run)
            {
                minX = MathF.Min(minX, s.X);
                minY = MathF.Min(minY, s.Y);
                maxX = MathF.Max(maxX, s.X);
                maxY = MathF.Max(maxY, s.Y);
            }

            int x0 = Math.Max(0, (int)MathF.Floor(minX - radius - 1f));
            int y0 = Math.Max(0, (int)MathF.Floor(minY - radius - 1f));
            int x1 = Math.Min(Width, (int)MathF.Ceiling(maxX + radius + 1f));
            int y1 = Math.Min(Height, (int)MathF.Ceiling(maxY + radius + 1f));

            if (x1 <= x0 || y1 <= y0)
            {
                continue;
            }

            run.CopyTo(_stamps.AsSpan());
            _device.For(x1 - x0, y1 - y0, new BrushStamp(
                _canvas, _back, _mask, _stamps, run.Length, Width, x0, y0, radius, brush.Hardness, brush.Opacity, color, brush.Erase ? 1 : 0));
        }
    }

    /// <summary>Finishes the current stroke (if any) and records it for undo.</summary>
    public void EndStroke()
    {
        if (_stroking)
        {
            _stroking = false;
            Commit();
        }
    }

    /// <summary>Runs a filter over the whole canvas (ping-pong into the back buffer, then swap) and records it for undo.</summary>
    public void Apply(FilterSpec filter)
    {
        EndStroke();
        _filters.Apply(filter, _canvas, _back);
        (_canvas, _back) = (_back, _canvas);
        Commit();
    }

    /// <summary>Fills the canvas with <paramref name="color"/> (premultiplied) and records it for undo.</summary>
    public void Clear(Float4 color)
    {
        EndStroke();
        Fill(color);
        Commit();
    }

    public bool Undo()
    {
        EndStroke();

        if (!History.TryUndo(out int slot))
        {
            return false;
        }

        _device.For(PixelCount, new UnpackSnapshot(_snapshots[slot]!, _canvas));
        return true;
    }

    public bool Redo()
    {
        EndStroke();

        if (!History.TryRedo(out int slot))
        {
            return false;
        }

        _device.For(PixelCount, new UnpackSnapshot(_snapshots[slot]!, _canvas));
        return true;
    }

    /// <summary>Copies the canvas out (premultiplied).</summary>
    public Float4[] ToArray() => _canvas.ToArray();

    /// <summary>Draws the window view (canvas, HUD and optional color wheel) into <paramref name="pixels"/> as packed RGBA8.</summary>
    public void Render(ReadWriteBuffer<uint> pixels, BrushSettings brush, bool picker, Float2 wheelCenter, float wheelRadius)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(brush);

        _device.For(Width, Height, new RenderView(
            _canvas, pixels, Width, new Float4(brush.Color, 1f), brush.Radius, brush.Hardness, brush.Opacity,
            brush.Erase ? 1 : 0, picker ? 1 : 0, wheelCenter, wheelRadius));
    }

    public void Dispose()
    {
        _filters.Dispose();
        _canvas.Dispose();
        _back.Dispose();
        _mask.Dispose();
        _stamps.Dispose();

        foreach (ReadWriteBuffer<uint>? snapshot in _snapshots)
        {
            snapshot?.Dispose();
        }
    }

    private void Fill(Float4 color) => _device.For(PixelCount, new FillPixels(_canvas, color));

    private void Commit()
    {
        int slot = History.Push();
        ReadWriteBuffer<uint> snapshot = _snapshots[slot] ??= _device.AllocateReadWriteBuffer<uint>(PixelCount);
        _device.For(PixelCount, new PackSnapshot(_canvas, snapshot));
    }
}

/// <summary>Helpers for stamping without a GPU stroke in progress (tests and scripted replays).</summary>
public static class StrokeExtensions
{
    /// <summary>Paints a polyline as one stroke, interpolating stamps between the points.</summary>
    public static void StrokePolyline(this PaintCanvas canvas, IReadOnlyList<Float2> points, BrushSettings brush)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(brush);

        if (points.Count == 0)
        {
            return;
        }

        StrokePath path = new();
        List<Float2> stamps = [];
        canvas.BeginStroke();
        path.Begin(points[0], stamps);

        for (int i = 1; i < points.Count; i++)
        {
            path.MoveTo(points[i], brush.Spacing, stamps);
        }

        canvas.Stamp(stamps.ToArray(), brush);
        canvas.EndStroke();
    }
}
