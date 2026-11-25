// Original SharpCompute sample (MIT).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SharpCompute.Presentation;
using SharpCompute.Samples.Shaders;
using static System.FormattableString;

namespace SharpCompute.Samples.Textures;

/// <summary>
/// A tour of textures in shaders. One screen magnifies a small picture through all six samplers and
/// cycles it through the eight texture formats; the other runs a reaction-diffusion back and forth
/// between two read-write textures and lets the mouse paint into it.
/// </summary>
public static class Program
{
    private const string Usage = """
        SharpCompute textures demo

          SharpCompute.Samples.Textures [--mode samplers|pingpong] [--format <1-8>] [--image <path>] [--frames <n>]
          SharpCompute.Samples.Textures --headless --out <dir> [--size <W>x<H>] [--image <path>] [--record]

        Keys: Tab switches between the sampler explorer and the ping-pong simulation · S saves a PNG · Esc quits
          Both:      the scroll wheel (or a two-finger scroll) zooms around the pointer · + / - zoom
                     around the middle · 0 resets the view
          Samplers:  F next format (Shift+F previous) · drag pans · right-drag zooms ·
                     R toggles a slow rotation
          Ping-pong: drag paints · right-drag erases · zoomed out, the field tiles ·
                     Space pauses · C restarts
        --headless  no window: writes one PNG per format of the sampler explorer and one of the
                    ping-pong simulation to --out (the same pixels every run)
        --record    with --headless: also writes every frame of the ping-pong run, pingpong-0001.png ...
        --image     explore your own picture (PNG, JPEG, HEIC, ...) instead of the generated tile,
                    loaded into each texture format with SharpCompute.Imaging
        --frames    exit after that many frames (for scripted runs)
        """;

    // Must stay synchronous: MetalWindow needs the process main thread, which only a synchronous Main keeps.
    public static int Main(string[] args)
    {
        bool headless = false, record = false;
        string? output = null;
        (int Width, int Height) size = (1280, 720);
        DemoMode mode = DemoMode.Samplers;
        int format = 0;
        int frames = 0;
        string? image = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless":
                    headless = true;
                    break;
                case "--record":
                    record = true;
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--image" when i + 1 < args.Length:
                    image = args[++i];
                    break;
                case "--size" when i + 1 < args.Length && TryParseSize(args[i + 1], out size):
                    i++;
                    break;
                case "--mode" when i + 1 < args.Length && args[i + 1] is "samplers" or "pingpong":
                    mode = args[++i] == "samplers" ? DemoMode.Samplers : DemoMode.PingPong;
                    break;
                case "--format" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f >= 1 && f <= SourceImage.Formats.Length:
                    format = f - 1;
                    i++;
                    break;
                case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0:
                    frames = n;
                    i++;
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

        if (headless)
        {
            if (output is null)
            {
                Console.Error.WriteLine("--headless needs --out <dir>.");
                return 1;
            }

            return WithImageErrors(() => RunHeadless(output, size.Width, size.Height, record, image));
        }

        if (!MetalWindow.IsSupported)
        {
            Console.Error.WriteLine("No window server: run the demo from a terminal in a desktop session (or use --headless --out <dir>).");
            return 1;
        }

        return WithImageErrors(() =>
        {
            GraphicsDevice device = GraphicsDevice.GetDefault();
            using TextureDemo demo = new(device, image) { Mode = mode };
            demo.Explorer.Format = format;

            using MetalWindow window = new(device, "SharpCompute Textures", size.Width, size.Height);
            using TexturesApp app = new(window, device, demo, frames);

            Console.WriteLine(Usage);
            window.Run(app.OnFrame);
            return 0;
        });
    }

    /// <summary>Runs <paramref name="run"/>, reporting an <c>--image</c> that can't be loaded as an error message and exit code 1.</summary>
    private static int WithImageErrors(Func<int> run)
    {
        try
        {
            return run();
        }
        catch (Exception e) when (e is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            Console.Error.WriteLine($"--image: {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Writes <c>samplers-&lt;format&gt;.png</c> for each of the eight formats and <c>pingpong.png</c>
    /// to <paramref name="directory"/>, and with <paramref name="record"/> also every frame of the
    /// ping-pong run as <c>pingpong-0001.png</c> and on (for a GIF of the simulation). Nothing depends
    /// on time or input, so every run writes the same pixels. With <paramref name="imagePath"/>, the
    /// sampler explorer shows that image instead of its generated tile.
    /// </summary>
    public static int RunHeadless(string directory, int width, int height, bool record = false, string? imagePath = null)
    {
        Directory.CreateDirectory(directory);

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using TextureDemo demo = new(device, imagePath);
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        for (int format = 0; format < SourceImage.Formats.Length; format++)
        {
            demo.Explorer.Format = format;
            demo.Render(target);
            Save(target, Path.Combine(directory, HeadlessFileName(format)));
        }

        // The simulation, with a scripted brush: a diagonal stroke over the first frames.
        demo.Mode = DemoMode.PingPong;

        for (int frame = 0; frame < HeadlessPingPongFrames; frame++)
        {
            if (frame < 48 && frame % 2 == 0)
            {
                float t = frame / 48f;
                demo.Reaction.Paint(new Float2(0.2f + (0.6f * t), 0.75f - (0.5f * t)));
            }

            demo.Render(target);

            if (record)
            {
                Save(target, Path.Combine(directory, Invariant($"pingpong-{frame + 1:D4}.png")));
            }
        }

        Save(target, Path.Combine(directory, "pingpong.png"));

        Console.WriteLine(Invariant(
            $"Textures: {SourceImage.Formats.Length} sampler frames and {(record ? Invariant($"{HeadlessPingPongFrames + 1} ping-pong frames") : "1 ping-pong frame")} ({demo.Reaction.Steps} steps) at {width}×{height} in {Path.GetFullPath(directory)}"));
        return 0;
    }

    /// <summary>Frames the headless ping-pong run simulates before its picture is taken.</summary>
    public const int HeadlessPingPongFrames = 150;

    /// <summary>The file <see cref="RunHeadless"/> writes for a format of the sampler explorer.</summary>
    public static string HeadlessFileName(int format) =>
        "samplers-" + SourceImage.Formats[format].Type.ToLowerInvariant() + ".png";

    internal static void Save(ReadWriteTexture2D<Rgba32> frame, string path)
    {
        PngEncoder.Write(path, frame.ToArray(), frame.Width, frame.Height);
    }

    private static bool TryParseSize(string text, out (int Width, int Height) size)
    {
        string[] parts = text.Split('x', 'X');
        size = default;

        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height) &&
            width is >= 320 and <= 8192 && height is >= 200 and <= 8192)
        {
            size = (width, height);
            return true;
        }

        return false;
    }
}

/// <summary>The window: turns keys and the mouse into changes of a <see cref="TextureDemo"/>, renders it at the drawable's size and presents it.</summary>
internal sealed class TexturesApp : IDisposable
{
    private const double TitleInterval = 0.5;

    private readonly MetalWindow _window;
    private readonly GraphicsDevice _device;
    private readonly TextureDemo _demo;
    private readonly int _frameLimit;
    private ReadWriteTexture2D<Rgba32>? _target;

    // The last mouse position of a drag, in frame pixels.
    private double _mouseX;
    private double _mouseY;

    // Title statistics, accumulated over TitleInterval.
    private double _statsStart;
    private int _statsFrames;
    private double _statsRenderMs;
    private double _fps;
    private double _renderMs;
    private double _totalRenderMs;
    private long _frames;

    public TexturesApp(MetalWindow window, GraphicsDevice device, TextureDemo demo, int frameLimit)
    {
        _window = window;
        _device = device;
        _demo = demo;
        _frameLimit = frameLimit;

        window.KeyDown += OnKeyDown;
        window.MouseDown += (_, e) => OnMouse(e, pressed: true);
        window.MouseDragged += (_, e) => OnMouse(e, pressed: false);
        window.MouseWheel += OnWheel;
        UpdateTitle();
    }

    public void OnFrame(FrameInfo frame)
    {
        // The frame is rendered at the drawable's own size, so it is only reallocated on a resize.
        if (_target is null || _target.Width != _window.Width || _target.Height != _window.Height)
        {
            _target?.Dispose();
            _target = _device.AllocateReadWriteTexture2D<Rgba32>(_window.Width, _window.Height);
        }

        _demo.Advance(Math.Min(frame.DeltaTime, 0.1));

        long start = Stopwatch.GetTimestamp();
        _demo.Render(_target);
        double renderMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        _window.Present(_target);

        _frames++;
        _statsFrames++;
        _statsRenderMs += renderMs;
        _totalRenderMs += renderMs;

        if (frame.Time - _statsStart >= TitleInterval)
        {
            _fps = _statsFrames / (frame.Time - _statsStart);
            _renderMs = _statsRenderMs / _statsFrames;
            (_statsStart, _statsFrames, _statsRenderMs) = (frame.Time, 0, 0);
            UpdateTitle();
        }

        if (_frameLimit > 0 && _frames >= _frameLimit)
        {
            Console.WriteLine(Invariant(
                $"Textures: {_frames} frames at {_target.Width}×{_target.Height}, {(frame.Time > 0 ? (_frames - 1) / frame.Time : 0):F1} fps, {_totalRenderMs / _frames:F2} ms/frame on the GPU"));
            _window.Close();
        }
    }

    public void Dispose() => _target?.Dispose();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool samplers = _demo.Mode == DemoMode.Samplers;

        // Zoom repeats while the key is held; everything else acts once per press.
        if (e.Character is '+' or '=')
        {
            _demo.ZoomBy(1f / WheelZoom.Step);
            return;
        }

        if (e.Character is '-' or '_')
        {
            _demo.ZoomBy(WheelZoom.Step);
            return;
        }

        if (e.IsRepeat)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                _window.Close();
                break;

            case Key.Tab:
                _demo.Mode = samplers ? DemoMode.PingPong : DemoMode.Samplers;
                break;

            case Key.F when samplers:
                _demo.Explorer.Format += (e.Modifiers & KeyModifiers.Shift) != 0 ? -1 : 1;
                break;

            case Key.R when samplers:
                _demo.Rotating = !_demo.Rotating;
                break;

            case Key.D0 when samplers:
                _demo.Explorer.ResetView();
                _demo.Rotating = false;
                break;

            case Key.D0:
                _demo.Reaction.ResetView();
                break;

            case Key.Space when !samplers:
                _demo.Paused = !_demo.Paused;
                break;

            case Key.C when !samplers:
                _demo.Reaction.Reset();
                break;

            case Key.S when _target is not null:
                string path = Path.GetFullPath(Invariant($"textures-{(samplers ? "samplers" : "pingpong")}-{DateTime.Now:yyyyMMdd-HHmmss}.png"));
                Program.Save(_target, path);
                Console.WriteLine($"Saved {path}");
                break;
        }

        UpdateTitle();
    }

    /// <summary>
    /// The wheel zooms the current mode around the pointer. The frame is rendered at the drawable's
    /// own size, so the event's position is already a frame position, as in <see cref="OnMouse"/>.
    /// </summary>
    private void OnWheel(object? sender, MouseWheelEventArgs e)
    {
        if (_target is null)
        {
            return;
        }

        float factor = WheelZoom.Factor(e, _window.ScaleFactor);

        if (factor != 1f)
        {
            _demo.ZoomAt(factor, e.X, e.Y, _target.Width, _target.Height);
        }
    }

    private void OnMouse(MouseEventArgs e, bool pressed)
    {
        if (_target is null || e.Button is not (MouseButton.Left or MouseButton.Right))
        {
            return;
        }

        double dx = pressed ? 0 : e.X - _mouseX, dy = pressed ? 0 : e.Y - _mouseY;
        (double fromX, double fromY) = pressed ? (e.X, e.Y) : (_mouseX, _mouseY);
        (_mouseX, _mouseY) = (e.X, e.Y);

        if (_demo.Mode == DemoMode.PingPong)
        {
            // Dabs along the segment since the last event, so a fast drag still leaves a line.
            int dabs = Math.Max(1, (int)(Math.Sqrt((dx * dx) + (dy * dy)) / 8));

            for (int i = 1; i <= dabs; i++)
            {
                double t = i / (double)dabs;
                Float2 uv = _demo.Reaction.FrameToField(fromX + (dx * t), fromY + (dy * t), _target.Width, _target.Height);
                _demo.Reaction.Paint(uv, erase: e.Button == MouseButton.Right);
            }

            return;
        }

        SamplerExplorer explorer = _demo.Explorer;

        if (e.Button == MouseButton.Right)
        {
            // Dragging down moves away, dragging up moves closer.
            explorer.ZoomBy((float)Math.Exp(dy * 0.005));
            return;
        }

        explorer.DragBy(dx, dy, new ExplorerLayout(_target.Width, _target.Height));
    }

    private void UpdateTitle()
    {
        string mode = _demo.Mode == DemoMode.Samplers
            ? $"Sampler explorer · {SourceImage.Formats[_demo.Explorer.Format].Type}"
            : $"Ping-pong{(_demo.Paused ? " · paused" : "")}";
        string stats = _fps > 0 ? Invariant($" · {_fps:F0} fps · {_renderMs:F2} ms/frame on the GPU") : "";
        string resolution = _target is null ? "" : Invariant($" · {_target.Width}×{_target.Height}");

        _window.Title = $"SharpCompute Textures · {mode}{stats}{resolution}";
    }
}
