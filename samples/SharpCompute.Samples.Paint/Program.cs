// Original SharpCompute sample (MIT).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.Paint;

/// <summary>
/// A GPU paint app on a 1920×1080 buffer canvas: soft round brushes, an eraser, whole-canvas
/// filters, 20 steps of undo and PNG export.
/// </summary>
/// <remarks>
/// Screenshot hook: with <c>SHARPCOMPUTE_PAINT_SCRIPT=script.json</c> (or <c>--script</c>) Paint
/// replays the script (see <see cref="PaintScript"/>), one action per frame in the window, saves
/// the canvas PNG to the script's <c>output</c> (or <c>SHARPCOMPUTE_PAINT_OUTPUT</c>) and exits.
/// Without a window server, or with <c>--headless</c>, it replays without a window.
/// </remarks>
public static class Program
{
    internal const string ScriptVariable = "SHARPCOMPUTE_PAINT_SCRIPT";
    internal const string OutputVariable = "SHARPCOMPUTE_PAINT_OUTPUT";

    private const string Usage = """
        SharpCompute Paint

          SharpCompute.Samples.Paint [--script <script.json>] [--headless]

        Environment: SHARPCOMPUTE_PAINT_SCRIPT=<script.json> replays a script, saves the PNG and exits;
                     SHARPCOMPUTE_PAINT_OUTPUT=<file.png> overrides the script's output path.
        """;

    // Must stay synchronous: MetalWindow needs the process main thread, which only a synchronous Main keeps.
    public static int Main(string[] args)
    {
        string? scriptPath = Environment.GetEnvironmentVariable(ScriptVariable);
        bool headless = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--script" when i + 1 < args.Length:
                    scriptPath = args[++i];
                    break;
                case "--headless":
                    headless = true;
                    break;
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete argument '{args[i]}'.");
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        if (!GraphicsDevice.IsSupported)
        {
            Console.Error.WriteLine("No Metal device: SharpCompute needs macOS 15+ on Apple Silicon.");
            return 1;
        }

        PaintScript? script = null;

        if (!string.IsNullOrWhiteSpace(scriptPath))
        {
            try
            {
                script = PaintScript.Load(scriptPath, Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } o ? Path.GetFullPath(o) : null);
            }
            catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
            {
                Console.Error.WriteLine($"Invalid paint script '{scriptPath}': {e.Message}");
                return 1;
            }
        }

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using PaintController controller = new(device);

        if (script is not null && (headless || !MetalWindow.IsSupported))
        {
            long start = Stopwatch.GetTimestamp();
            string saved = script.Run(controller);
            Console.WriteLine(Invariant($"Replayed {script.Actions.Count} actions headless in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms → {saved}"));
            return 0;
        }

        if (!MetalWindow.IsSupported)
        {
            Console.Error.WriteLine("No window server: run Paint from a terminal in a desktop session (or replay a script with --headless).");
            return 1;
        }

        using MetalWindow window = new(device, controller.Status, 1280, 720);
        using PaintApp app = new(window, device, controller, script);

        if (script is null)
        {
            Console.WriteLine(PaintController.Keys);
        }

        window.Run(app.OnFrame);
        return app.ExitCode;
    }
}

/// <summary>Connects a <see cref="PaintController"/> to a window: input, the per-frame view and the title.</summary>
internal sealed class PaintApp : IDisposable
{
    private readonly MetalWindow _window;
    private readonly PaintController _controller;
    private readonly PaintScript? _script;
    private readonly ReadWriteTexture2D<Rgba32> _frame;
    private readonly ReadWriteBuffer<uint> _view;
    private int _renderedVersion = -1;
    private bool _captured;
    private int _nextAction;
    private int _presented;

    public PaintApp(MetalWindow window, GraphicsDevice device, PaintController controller, PaintScript? script)
    {
        _window = window;
        _controller = controller;
        _script = script;
        _frame = device.AllocateReadWriteTexture2D<Rgba32>(controller.Canvas.Width, controller.Canvas.Height);
        _view = device.AllocateReadWriteBuffer<uint>(controller.Canvas.Width * controller.Canvas.Height);

        window.KeyDown += OnKeyDown;
        window.MouseDown += (_, e) => OnMouse(e, MouseState.Down);
        window.MouseDragged += (_, e) => OnMouse(e, MouseState.Drag);
        window.MouseUp += (_, e) => OnMouse(e, MouseState.Up);
    }

    private enum MouseState
    {
        Down,
        Drag,
        Up,
    }

    public int ExitCode { get; private set; }

    public void OnFrame(FrameInfo frame)
    {
        if (_script is not null && _nextAction < _script.Actions.Count)
        {
            _script.Actions[_nextAction++](_controller);
        }

        if (_controller.Version != _renderedVersion)
        {
            _controller.Render(_view);
            _frame.CopyFrom(MemoryMarshal.Cast<uint, Rgba32>(_view.AsSpan()));
            _renderedVersion = _controller.Version;
        }

        _presented += _window.Present(_frame) ? 1 : 0;
        _window.Title = _controller.Status;

        if (_script is not null && _nextAction >= _script.Actions.Count)
        {
            string saved = _script.Finish(_controller);
            Console.WriteLine(Invariant($"Replayed {_script.Actions.Count} actions over {frame.FrameIndex + 1} frames ({_presented} presented) → {saved}"));

            // Nothing presented means the window was hidden the whole time: the PNG is still valid,
            // but the presentation path wasn't exercised, which the caller should know.
            ExitCode = _presented > 0 ? 0 : 3;
            _window.Close();
        }
    }

    public void Dispose()
    {
        _frame.Dispose();
        _view.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _window.Close();
            return;
        }

        // Filters, clear and undo shouldn't auto-repeat; brush adjustments may.
        if (e.IsRepeat && e.Key is not Key.Unknown)
        {
            return;
        }

        string? message = _controller.HandleKey(e.Key, e.Character, e.Modifiers);

        if (message is not null)
        {
            Console.WriteLine(message);
        }
    }

    private void OnMouse(MouseEventArgs e, MouseState state)
    {
        bool inside = _window.TryMapToFrame(e.X, e.Y, _frame.Width, _frame.Height, out double x, out double y);

        // A press in a letterbox bar is ignored; once captured, drags and the release are forwarded even outside the canvas.
        if (state == MouseState.Down)
        {
            _captured = inside;

            if (inside)
            {
                _controller.PointerDown(x, y, e.Button == MouseButton.Right);
            }

            return;
        }

        if (!_captured)
        {
            return;
        }

        if (state == MouseState.Drag)
        {
            _controller.PointerDrag(x, y);
        }
        else
        {
            _controller.PointerUp();
            _captured = false;
        }
    }
}
