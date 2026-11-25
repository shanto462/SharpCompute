// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SharpCompute.Presentation;
using SharpCompute.Samples.Imaging;
using SharpCompute.Samples.Shaders;
using static System.FormattableString;

namespace SharpCompute.Samples.Paint;

/// <summary>
/// Paint's input handling and tool state, independent of the window: keys and pointer positions
/// (in canvas pixels) in, canvas edits out. The window app and the scripted replay both drive it.
/// </summary>
public sealed class PaintController : IDisposable
{
    public const int CanvasWidth = 1920;
    public const int CanvasHeight = 1080;

    public const string Keys =
        "drag: paint (right button erases) · [ ] or wheel: size · ; ' or Shift+wheel: hardness · - = opacity · 1–9 color · H color wheel · E eraser · " +
        "B blur · K sharpen · D edges · I invert · P posterize · G grayscale · C clear · ⌘Z undo · ⇧⌘Z redo · ⌘S save PNG · Esc quit";

    private readonly StrokePath _path = new();
    private readonly List<Float2> _stamps = [];
    private bool _rightButtonErase;

    public PaintController(GraphicsDevice device, int width = CanvasWidth, int height = CanvasHeight)
    {
        Canvas = new PaintCanvas(device, width, height);
        WheelCenter = new Float2(width / 2f, height / 2f);
        WheelRadius = height * 0.3f;
    }

    public PaintCanvas Canvas { get; }

    public BrushSettings Brush { get; } = new();

    /// <summary>True while the color wheel is shown; the next press picks from it.</summary>
    public bool Picking { get; private set; }

    public Float2 WheelCenter { get; }

    public float WheelRadius { get; }

    /// <summary>Incremented on every change that affects the view.</summary>
    public int Version { get; private set; }

    /// <summary>Where Cmd-S saves; defaults to ~/Pictures.</summary>
    public string SaveDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");

    /// <summary>One line for the window title: tool, size, hardness, opacity, color and undo depth.</summary>
    public string Status
    {
        get
        {
            string tool = Picking ? "Pick color" : Brush.Erase ? "Eraser" : "Brush";
            return Invariant(
                $"SharpCompute Paint · {tool} {Brush.Size:F0} px · hardness {Brush.Hardness * 100:F0}% · opacity {Brush.Opacity * 100:F0}% · {Brush.ColorHex} · undo {Canvas.History.UndoCount}/{Canvas.History.Steps}");
        }
    }

    /// <summary>Handles a key press. Returns a message for the console, or null.</summary>
    public string? HandleKey(Key key, char character, KeyModifiers modifiers)
    {
        bool command = (modifiers & KeyModifiers.Command) != 0;
        string? message = null;

        if (command)
        {
            switch (key)
            {
                case Key.Z when (modifiers & KeyModifiers.Shift) != 0:
                    message = Canvas.Redo() ? "Redo" : "Nothing to redo";
                    break;
                case Key.Z:
                    message = Canvas.Undo() ? "Undo" : "Nothing to undo";
                    break;
                case Key.S:
                    message = $"Saved {Save(Path.Combine(SaveDirectory, Invariant($"SharpCompute-Paint-{DateTime.Now:yyyyMMdd-HHmmss}.png")))}";
                    break;
                default:
                    return null;
            }

            Version++;
            return message;
        }

        switch (character)
        {
            case '[':
                Brush.Size /= BrushSettings.SizeStep;
                break;
            case ']':
                Brush.Size *= BrushSettings.SizeStep;
                break;
            case ';':
                Brush.Hardness -= 0.1f;
                break;
            case '\'':
                Brush.Hardness += 0.1f;
                break;
            case '-':
                Brush.Opacity -= 0.1f;
                break;
            case '=':
                Brush.Opacity += 0.1f;
                break;
            case >= '1' and <= '9':
                Brush.Color = BrushSettings.Palette[character - '1'];
                Brush.Erase = false;
                break;
            default:
                if (!HandleLetter(key, ref message))
                {
                    return null;
                }

                break;
        }

        Version++;
        return message;
    }

    /// <summary>
    /// The scroll wheel or a two-finger scroll: brush size, or hardness with Shift held. The deltas
    /// are <see cref="MouseWheelEventArgs"/>'s, with precise ones converted to points; a positive one
    /// makes the brush bigger or harder. Returns whether a setting changed; the new value shows in
    /// <see cref="Status"/>.
    /// </summary>
    public bool HandleWheel(float deltaX, float deltaY, bool precise, KeyModifiers modifiers)
    {
        bool shift = (modifiers & KeyModifiers.Shift) != 0;

        // macOS turns Shift + a notched wheel into horizontal scrolling, so with Shift held the
        // movement can arrive on either axis: take whichever moved more.
        float delta = shift && MathF.Abs(deltaX) > MathF.Abs(deltaY) ? deltaX : deltaY;
        float steps = BrushSettings.WheelSteps(delta, precise);

        if (steps == 0f)
        {
            return false;
        }

        (float size, float hardness) = (Brush.Size, Brush.Hardness);

        if (shift)
        {
            Brush.ScrollHardness(steps);
        }
        else
        {
            Brush.ScrollSize(steps);
        }

        if (Brush.Size == size && Brush.Hardness == hardness)
        {
            return false;
        }

        Version++;
        return true;
    }

    /// <summary>A press at canvas position (<paramref name="x"/>, <paramref name="y"/>): starts a stroke, or picks a color while the wheel is shown.</summary>
    public void PointerDown(double x, double y, bool rightButton = false)
    {
        Float2 point = new((float)x, (float)y);

        if (Picking)
        {
            if (!TryPick(point))
            {
                Picking = false;
            }

            Version++;
            return;
        }

        _rightButtonErase = rightButton;
        _stamps.Clear();
        Canvas.BeginStroke();
        _path.Begin(point, _stamps);
        Flush();
    }

    /// <summary>The pointer moved with the button held: extends the stroke (or keeps picking).</summary>
    public void PointerDrag(double x, double y)
    {
        Float2 point = new((float)x, (float)y);

        if (Picking)
        {
            TryPick(point);
            Version++;
            return;
        }

        if (!Canvas.IsStroking)
        {
            return;
        }

        _path.MoveTo(point, Brush.Spacing, _stamps);
        Flush();
    }

    public void PointerUp()
    {
        if (Picking)
        {
            Picking = false;
        }
        else if (Canvas.IsStroking)
        {
            _path.End();
            Canvas.EndStroke();
        }

        Version++;
    }

    /// <summary>Draws the window view into <paramref name="pixels"/> (packed RGBA8, canvas size).</summary>
    public void Render(ReadWriteBuffer<uint> pixels) => Canvas.Render(pixels, Brush, Picking, WheelCenter, WheelRadius);

    /// <summary>Saves the canvas (not the HUD) as a PNG with straight alpha and returns the full path.</summary>
    public string Save(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        PngEncoder.Write(fullPath, PixelConversion.ToStraightRgba32(Canvas.ToArray()), Canvas.Width, Canvas.Height);
        return fullPath;
    }

    /// <summary>Saves what the window shows (canvas over the checkerboard, the HUD, the wheel while picking) as a PNG.</summary>
    public string SaveView(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using ReadWriteBuffer<uint> pixels = Canvas.Pixels.Device.AllocateReadWriteBuffer<uint>(Canvas.Width * Canvas.Height);
        Render(pixels);
        PngEncoder.Write(fullPath, MemoryMarshal.Cast<uint, Rgba32>(pixels.AsSpan()), Canvas.Width, Canvas.Height);
        return fullPath;
    }

    public void Dispose() => Canvas.Dispose();

    /// <summary>The color the wheel shows at <paramref name="point"/>, or false outside it.</summary>
    public bool TryPickColor(Float2 point, out Float3 color)
    {
        float dx = point.X - WheelCenter.X;
        float dy = point.Y - WheelCenter.Y;
        float r = MathF.Sqrt((dx * dx) + (dy * dy));
        color = default;

        if (r > WheelRadius)
        {
            return false;
        }

        float hue = (MathF.Atan2(dy, dx) / 6.2831855f) + 0.5f;
        color = RenderView.HueSaturation(hue, Math.Clamp(r / WheelRadius, 0f, 1f));
        return true;
    }

    private bool TryPick(Float2 point)
    {
        if (!TryPickColor(point, out Float3 color))
        {
            return false;
        }

        Brush.Color = color;
        Brush.Erase = false;
        return true;
    }

    private bool HandleLetter(Key key, ref string? message)
    {
        switch (key)
        {
            case Key.E:
                Brush.Erase = !Brush.Erase;
                break;
            case Key.H:
                Canvas.EndStroke();
                Picking = !Picking;
                break;
            case Key.C:
                Canvas.Clear(PaintCanvas.Paper);
                message = "Cleared";
                break;
            case Key.B:
                message = Filter(FilterSpec.Create(FilterKind.Blur, 6));
                break;
            case Key.K:
                message = Filter(FilterSpec.Create(FilterKind.Sharpen, 1.5f));
                break;
            case Key.D:
                message = Filter(FilterSpec.Create(FilterKind.Edges));
                break;
            case Key.I:
                message = Filter(FilterSpec.Create(FilterKind.Invert));
                break;
            case Key.P:
                message = Filter(FilterSpec.Create(FilterKind.Posterize, 5));
                break;
            case Key.G:
                message = Filter(FilterSpec.Create(FilterKind.Grayscale));
                break;
            default:
                return false;
        }

        return true;
    }

    private string Filter(FilterSpec filter)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Canvas.Apply(filter);
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return Invariant($"{filter} on {Canvas.Width}×{Canvas.Height}: {ms:F1} ms");
    }

    private void Flush()
    {
        if (_stamps.Count == 0)
        {
            return;
        }

        bool erase = Brush.Erase;
        Brush.Erase = erase || _rightButtonErase;
        Canvas.Stamp(_stamps.ToArray(), Brush);
        Brush.Erase = erase;
        _stamps.Clear();
        Version++;
    }
}
