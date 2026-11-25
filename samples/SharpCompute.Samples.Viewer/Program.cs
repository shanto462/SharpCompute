using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SharpCompute.Presentation;
using SharpCompute.Samples.Shaders;
using SharpCompute.Samples.Viewer.Demos;
using static System.FormattableString;

namespace SharpCompute.Samples.Viewer;

/// <summary>
/// The live gallery: every <see cref="Gallery.All"/> shader plus the live-only demos, in a window.
/// </summary>
/// <remarks>
/// Hidden test hook: with <c>SHARPCOMPUTE_VIEWER_SCREENSHOT=path.png</c> the viewer renders
/// <c>SHARPCOMPUTE_VIEWER_FRAMES</c> frames (default 120), saves the last frame it presented to the
/// PNG, prints timing to stdout and exits. Pick the demo with <c>--shader</c>.
/// </remarks>
public static class Program
{
    internal const string ScreenshotVariable = "SHARPCOMPUTE_VIEWER_SCREENSHOT";
    internal const string FramesVariable = "SHARPCOMPUTE_VIEWER_FRAMES";

    private const string Usage = """
        SharpCompute live viewer

          SharpCompute.Samples.Viewer [--shader <name>] [--size <W>x<H>]
          SharpCompute.Samples.Viewer --list

        Keys: ←/→ switch shader, Space pause, S save PNG, F toggle 1280×720 / window resolution, Esc quit.
        """;

    // Must stay synchronous: MetalWindow needs the process main thread, which only a synchronous Main keeps.
    public static int Main(string[] args)
    {
        string? start = null;
        (int Width, int Height) size = (1280, 720);

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--list":
                    foreach (IDemo demo in CreateDemos())
                    {
                        Console.WriteLine($"{demo.Name,-24} {demo.Author}");
                        demo.Dispose();
                    }

                    return 0;

                case "--shader" when i + 1 < args.Length:
                    start = args[++i];
                    break;

                case "--size" when i + 1 < args.Length && TryParseSize(args[++i], out size):
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

        if (!MetalWindow.IsSupported)
        {
            Console.Error.WriteLine("No window server: run the viewer from a terminal in a desktop session (not over SSH or in CI).");
            return 1;
        }

        List<IDemo> demos = CreateDemos();

        try
        {
            int index = start is null ? 0 : demos.FindIndex(d => string.Equals(d.Name, start, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                Console.Error.WriteLine($"Unknown shader '{start}'. Use --list to see the names.");
                return 1;
            }

            Screenshot? screenshot = Screenshot.FromEnvironment();
            GraphicsDevice device = GraphicsDevice.GetDefault();

            using MetalWindow window = new(device, "SharpCompute Viewer", size.Width, size.Height);
            using ViewerApp app = new(window, device, demos, index, screenshot);

            if (screenshot is null)
            {
                Console.WriteLine("←/→ switch shader · Space pause · S save PNG · F toggle 1280×720 / window resolution · Esc quit");
            }

            window.Run(app.OnFrame);
            return app.ExitCode;
        }
        finally
        {
            foreach (IDemo demo in demos)
            {
                demo.Dispose();
            }
        }
    }

    internal static List<IDemo> CreateDemos() =>
    [
        .. Gallery.All.Select(static shader => new GalleryDemo(shader)),
        new GameOfLifeDemo(),
        new NBodyDemo(),
    ];

    private static bool TryParseSize(string text, out (int Width, int Height) size)
    {
        string[] parts = text.Split('x', 'X');
        size = default;

        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height) &&
            width is > 0 and <= 8192 && height is > 0 and <= 8192)
        {
            size = (width, height);
            return true;
        }

        return false;
    }
}

/// <summary>The screenshot test hook's settings.</summary>
internal sealed record Screenshot(string Path, int Frames)
{
    public static Screenshot? FromEnvironment()
    {
        string? path = Environment.GetEnvironmentVariable(Program.ScreenshotVariable);

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? frames = Environment.GetEnvironmentVariable(Program.FramesVariable);
        int count = int.TryParse(frames, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 120;
        return new Screenshot(System.IO.Path.GetFullPath(path), count);
    }
}

/// <summary>Per-frame logic and input handling for the viewer window.</summary>
internal sealed class ViewerApp : IDisposable
{
    private const int FixedWidth = 1280;
    private const int FixedHeight = 720;
    private const double TitleInterval = 0.5;

    private readonly MetalWindow _window;
    private readonly GraphicsDevice _device;
    private readonly IReadOnlyList<IDemo> _demos;
    private readonly Screenshot? _screenshot;

    private ReadWriteTexture2D<Rgba32>? _target;
    private int _index;
    private bool _paused;
    private bool _windowResolution;
    private bool _mouseCaptured;
    private double _time;

    // Title statistics, accumulated over TitleInterval.
    private double _statsStart;
    private int _statsFrames;
    private double _statsRenderMs;
    private double _fps;
    private double _renderMs;

    // Screenshot hook bookkeeping.
    private int _frames;
    private int _presented;
    private double _totalRenderMs;

    public ViewerApp(MetalWindow window, GraphicsDevice device, IReadOnlyList<IDemo> demos, int index, Screenshot? screenshot)
    {
        _window = window;
        _device = device;
        _demos = demos;
        _index = index;
        _screenshot = screenshot;

        window.KeyDown += OnKeyDown;
        window.MouseDown += (_, e) => OnMouse(MouseAction.Down, e);
        window.MouseDragged += (_, e) => OnMouse(MouseAction.Drag, e);
        window.MouseUp += (_, e) => OnMouse(MouseAction.Up, e);
        window.MouseWheel += OnWheel;

        Announce();
        UpdateTitle();
    }

    public int ExitCode { get; private set; }

    private IDemo Current => _demos[_index];

    public void OnFrame(FrameInfo frame)
    {
        (int width, int height) = _windowResolution ? (_window.Width, _window.Height) : (FixedWidth, FixedHeight);

        if (_target is null || _target.Width != width || _target.Height != height)
        {
            _target?.Dispose();
            _target = _device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        }

        // Clamp the step so a stall (a window drag, a breakpoint) doesn't make simulations jump.
        double dt = _paused ? 0 : Math.Min(frame.DeltaTime, 0.1);
        _time += dt;

        long start = Stopwatch.GetTimestamp();
        Current.Render(_device, _target, new DemoFrame((float)_time, (float)dt, _paused));
        double renderMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        bool shown = _window.Present(_target);

        _statsFrames++;
        _statsRenderMs += renderMs;

        if (frame.Time - _statsStart >= TitleInterval)
        {
            _fps = _statsFrames / (frame.Time - _statsStart);
            _renderMs = _statsRenderMs / _statsFrames;
            (_statsStart, _statsFrames, _statsRenderMs) = (frame.Time, 0, 0);
            UpdateTitle();
        }

        if (_screenshot is not null)
        {
            _frames++;
            _presented += shown ? 1 : 0;
            _totalRenderMs += renderMs;

            if (_frames >= _screenshot.Frames)
            {
                SaveScreenshot(frame);
            }
        }
    }

    public void Dispose() => _target?.Dispose();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _window.Close();
                break;

            case Key.Left or Key.Right:
                _index = (_index + (e.Key == Key.Right ? 1 : _demos.Count - 1)) % _demos.Count;
                _time = 0;
                _mouseCaptured = false;
                Announce();
                UpdateTitle();
                break;

            case Key.Space when !e.IsRepeat:
                _paused = !_paused;
                UpdateTitle();
                break;

            case Key.S when !e.IsRepeat && _target is not null:
                string path = Path.GetFullPath(Invariant($"{Current.Name}-{DateTime.Now:yyyyMMdd-HHmmss}.png"));
                PngEncoder.Write(path, _target.ToArray(), _target.Width, _target.Height);
                Console.WriteLine($"Saved {path}");
                break;

            case Key.F when !e.IsRepeat:
                _windowResolution = !_windowResolution;
                UpdateTitle();
                break;

            default:
                Current.OnKeyDown(e.Key);
                break;
        }
    }

    private void OnMouse(MouseAction action, MouseEventArgs e)
    {
        if (_target is null)
        {
            return;
        }

        bool inside = _window.TryMapToFrame(e.X, e.Y, _target.Width, _target.Height, out double x, out double y);

        // A press in a letterbox bar is ignored; once a press is captured, drags and the release
        // are forwarded even outside the frame.
        if (action == MouseAction.Down)
        {
            _mouseCaptured = inside;
        }

        if (_mouseCaptured)
        {
            Current.OnMouse(action, e.Button, x, y, _target.Width, _target.Height);
        }

        if (action == MouseAction.Up)
        {
            _mouseCaptured = false;
        }
    }

    private void OnWheel(object? sender, MouseWheelEventArgs e)
    {
        // Scrolling in a letterbox bar is ignored, like a press there.
        if (_target is null || !_window.TryMapToFrame(e.X, e.Y, _target.Width, _target.Height, out _, out _))
        {
            return;
        }

        float steps = WheelInput.ToSteps(e.DeltaY, e.IsPrecise, _window.ScaleFactor);

        if (steps != 0f)
        {
            Current.OnWheel(steps);
        }
    }

    private void Announce()
    {
        if (_screenshot is null)
        {
            IDemo demo = Current;
            Console.WriteLine(demo.Controls is null ? $"{demo.Name} by {demo.Author}" : $"{demo.Name} by {demo.Author} — {demo.Controls}");
        }
    }

    private void UpdateTitle()
    {
        string resolution = _target is null ? "" : Invariant($" · {_target.Width}×{_target.Height}");
        string stats = _fps > 0 ? Invariant($" · {_fps:F0} fps · {_renderMs:F2} ms/frame") : "";
        string paused = _paused ? " · paused" : "";
        _window.Title = $"{Current.Name} by {Current.Author}{stats}{resolution}{paused}";
    }

    private void SaveScreenshot(FrameInfo frame)
    {
        Screenshot screenshot = _screenshot!;
        string? directory = Path.GetDirectoryName(screenshot.Path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        PngEncoder.Write(screenshot.Path, _target!.ToArray(), _target.Width, _target.Height);

        double fps = frame.Time > 0 ? (_frames - 1) / frame.Time : 0;
        Console.WriteLine(Invariant(
            $"{Current.Name}: {_frames} frames, {_presented} presented, {fps:F1} fps, {_totalRenderMs / _frames:F2} ms/frame render, drawable {_window.Width}×{_window.Height} (scale {_window.ScaleFactor}) → {screenshot.Path}"));

        // Nothing presented means the window was hidden the whole time: the PNG is still valid, but
        // the presentation path wasn't exercised, which the caller should know.
        ExitCode = _presented > 0 ? 0 : 3;
        _window.Close();
    }
}
