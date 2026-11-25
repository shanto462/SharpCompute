// Original SharpCompute sample (MIT).
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using SharpCompute.Presentation;
using SharpCompute.Samples.Shaders;
using static System.FormattableString;

namespace SharpCompute.Samples.NativeInterop;

/// <summary>
/// SharpCompute inside an app that already renders with Metal. The "app" is a small renderer written
/// straight against Metal (see <c>Host/</c>); SharpCompute wraps its device, queue, particle buffer and
/// textures, and adds a particle simulation and a post-process pass to the app's own command buffer.
/// </summary>
public static class Program
{
    private const string Usage = """
        SharpCompute native interop demo

          SharpCompute.Samples.NativeInterop [--frames <n>]
          SharpCompute.Samples.NativeInterop --headless --out <dir> [--size <W>x<H>] [--frames <n>]
          SharpCompute.Samples.NativeInterop --screenshot <file.png> [--size <W>x<H>]

        host: a raw Metal render pipeline (a textured quad and an instanced particle field, from its own
        MTLBuffer and MTLTexture) · SharpCompute: a particle simulation and a post-process pass, encoded
        into the host's command buffer, on the host's own resources

        Keys: 1 toggles the particle simulation · 2 toggles the post-process · click or drag moves the
              ripples · S saves a PNG · Esc quits
        --headless  no window: renders the same frames every run and writes them to --out as PNGs:
                    nativeinterop.png (both passes), no-sim.png, no-post.png and host-only.png
        --frames    window: exit after that many frames; headless: frames simulated (default 240)
        --screenshot  the README picture: the headless nativeinterop.png frame, stored with 6 bits per
                      channel so that the file stays small
        """;

    // Must stay synchronous: MetalWindow needs the process main thread, which only a synchronous Main keeps.
    public static int Main(string[] args)
    {
        bool headless = false;
        string? output = null;
        string? screenshot = null;
        (int Width, int Height) size = (1280, 720);
        int frames = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless":
                    headless = true;
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--screenshot" when i + 1 < args.Length:
                    screenshot = args[++i];
                    break;
                case "--size" when i + 1 < args.Length && TryParseSize(args[i + 1], out size):
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

        if (screenshot is not null)
        {
            return WriteScreenshot(screenshot, size.Width, size.Height);
        }

        if (headless)
        {
            if (output is null)
            {
                Console.Error.WriteLine("--headless needs --out <dir>.");
                return 1;
            }

            return RunHeadless(output, size.Width, size.Height, frames > 0 ? frames : HeadlessFrames);
        }

        if (!MetalWindow.IsSupported)
        {
            Console.Error.WriteLine("No window server: run the demo from a terminal in a desktop session (or use --headless --out <dir>).");
            return 1;
        }

        using InteropDemo demo = new(size.Width, size.Height);
        using MetalWindow window = new(demo.Device, "SharpCompute Native Interop", size.Width, size.Height);
        InteropApp app = new(window, demo, frames);

        Console.WriteLine(Usage);
        window.Run(app.OnFrame);
        return 0;
    }

    /// <summary>The frames the headless mode simulates before it takes its pictures.</summary>
    public const int HeadlessFrames = 240;

    /// <summary>The files <see cref="RunHeadless"/> writes, with the passes each one has on.</summary>
    public static readonly (string File, bool Simulate, bool PostProcess)[] HeadlessFrameSet =
    [
        ("nativeinterop.png", true, true),
        ("no-sim.png", false, true),
        ("no-post.png", true, false),
        ("host-only.png", false, false),
    ];

    /// <summary>
    /// Renders <paramref name="frames"/> frames of <see cref="InteropDemo.FixedStep"/> for each entry of
    /// <see cref="HeadlessFrameSet"/>, from the same start, and writes the last one. Nothing depends on
    /// time or input, so every run writes the same pixels.
    /// </summary>
    public static int RunHeadless(string directory, int width, int height, int frames)
    {
        Directory.CreateDirectory(directory);

        double[] gpuMilliseconds = new double[frames];

        foreach ((string file, bool simulate, bool post) in HeadlessFrameSet)
        {
            using InteropDemo demo = new(width, height) { Simulate = simulate, PostProcess = post };
            Rgba32[] pixels = demo.RenderFixed(frames, gpuMilliseconds);
            PngEncoder.Write(Path.Combine(directory, file), pixels, width, height, CompressionLevel.SmallestSize);

            // The median of the second half: the first frames include pipeline compilation and warm-up.
            double[] settled = gpuMilliseconds[(frames / 2)..];
            Array.Sort(settled);
            Console.WriteLine(Invariant(
                $"  {file,-18} sim {(simulate ? "on " : "off")} · post {(post ? "on " : "off")} · GPU {settled[settled.Length / 2]:F3} ms per frame (median of the last {settled.Length})"));
        }

        Console.WriteLine(Invariant(
            $"Native interop: {HeadlessFrameSet.Length} frames of {InteropDemo.ParticleCount} particles at {width}×{height} after {frames} steps in {Path.GetFullPath(directory)}"));
        return 0;
    }

    /// <summary>
    /// Writes the frame <see cref="RunHeadless"/> writes as <c>nativeinterop.png</c>, rounded to 6 bits
    /// per channel. A frame full of
    /// soft gradients and particles compresses poorly; at 1200×675 this keeps the README picture
    /// under 400 KB with no visible change.
    /// </summary>
    public static int WriteScreenshot(string path, int width, int height)
    {
        using InteropDemo demo = new(width, height);
        Rgba32[] pixels = demo.RenderFixed(HeadlessFrames);

        foreach (ref Rgba32 pixel in pixels.AsSpan())
        {
            pixel = new Rgba32(SixBits(pixel.R), SixBits(pixel.G), SixBits(pixel.B), pixel.A);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        PngEncoder.Write(path, pixels, width, height, CompressionLevel.SmallestSize);
        Console.WriteLine(Invariant($"Native interop: screenshot at {width}×{height} in {Path.GetFullPath(path)} ({new FileInfo(path).Length / 1024} KB)"));
        return 0;

        // The middle of the four values that share the top six bits: no low-bit noise left for the compressor.
        static byte SixBits(byte value) => (byte)((value & 0xFC) | 2);
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

/// <summary>The window: keys and the mouse change the <see cref="InteropDemo"/>; each frame renders at the drawable's size and is presented.</summary>
internal sealed class InteropApp
{
    private const double TitleInterval = 0.5;

    private readonly MetalWindow _window;
    private readonly InteropDemo _demo;
    private readonly int _frameLimit;
    private bool _save;

    // Title statistics, accumulated over TitleInterval.
    private double _statsStart;
    private int _statsFrames;
    private double _statsGpuMs;
    private double _fps;
    private double _gpuMs;
    private double _totalGpuMs;
    private long _frames;

    public InteropApp(MetalWindow window, InteropDemo demo, int frameLimit)
    {
        _window = window;
        _demo = demo;
        _frameLimit = frameLimit;

        window.KeyDown += OnKeyDown;
        window.MouseDown += (_, e) => MoveRipples(e);
        window.MouseDragged += (_, e) => MoveRipples(e);
        UpdateTitle();
    }

    public void OnFrame(FrameInfo frame)
    {
        // The host renders at the drawable's own size, so its textures only change on a resize.
        _demo.Resize(_window.Width, _window.Height);

        InteropDemo.Frame rendered = _demo.Render(Math.Min(frame.DeltaTime, 0.1));
        _window.Present(rendered.Final);
        _demo.Finish(rendered);

        if (_save)
        {
            _save = false;
            string path = Path.GetFullPath(Invariant($"nativeinterop-{DateTime.Now:yyyyMMdd-HHmmss}.png"));
            PngEncoder.Write(path, _demo.Capture(rendered.Final), _demo.Width, _demo.Height);
            Console.WriteLine($"Saved {path}");
        }

        _frames++;
        _statsFrames++;
        _statsGpuMs += _demo.LastGpuMilliseconds;
        _totalGpuMs += _demo.LastGpuMilliseconds;

        if (frame.Time - _statsStart >= TitleInterval)
        {
            _fps = _statsFrames / (frame.Time - _statsStart);
            _gpuMs = _statsGpuMs / _statsFrames;
            (_statsStart, _statsFrames, _statsGpuMs) = (frame.Time, 0, 0);
            UpdateTitle();
        }

        if (_frameLimit > 0 && _frames >= _frameLimit)
        {
            Console.WriteLine(Invariant(
                $"Native interop: {_frames} frames at {_demo.Width}×{_demo.Height}, {(frame.Time > 0 ? (_frames - 1) / frame.Time : 0):F1} fps, {_totalGpuMs / _frames:F2} ms per frame on the GPU"));
            _window.Close();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.IsRepeat)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                _window.Close();
                break;
            case Key.D1:
                _demo.Simulate = !_demo.Simulate;
                break;
            case Key.D2:
                _demo.PostProcess = !_demo.PostProcess;
                break;
            case Key.S:
                _save = true;
                break;
        }

        UpdateTitle();
    }

    /// <summary>The frame is the drawable's size, so a mouse position is a frame position.</summary>
    private void MoveRipples(MouseEventArgs e)
    {
        if (e.Button == MouseButton.Left && _demo.Width > 0)
        {
            _demo.RippleCenter = new Float2((float)(e.X / _demo.Width), (float)(e.Y / _demo.Height));
        }
    }

    private void UpdateTitle()
    {
        string passes = (_demo.Simulate, _demo.PostProcess) switch
        {
            (true, true) => "sim + post",
            (true, false) => "sim only",
            (false, true) => "post only",
            _ => "host only",
        };
        string stats = _fps > 0 ? Invariant($" · {_fps:F0} fps · GPU {_gpuMs:F2} ms") : "";

        _window.Title = Invariant($"SharpCompute Native Interop · {passes}{stats} · {_demo.Width}×{_demo.Height}");
    }
}
