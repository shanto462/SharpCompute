// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SharpCompute.Presentation;
using SharpCompute.Samples.ComputeLab.Pages;
using SharpCompute.Samples.Shaders;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab;

/// <summary>
/// ComputeLab: one page per core compute feature. Each page runs the feature on the GPU, shows the
/// result, checks it against the CPU and times both. In a window by default; <c>--headless</c>
/// writes one PNG per page and a text summary instead.
/// </summary>
public static class Program
{
    private const string Usage = """
        ComputeLab: SharpCompute's compute features, one page each

          SharpCompute.Samples.ComputeLab [--page <name>] [--frames <n>]
          SharpCompute.Samples.ComputeLab --headless [--out <dir>]
          SharpCompute.Samples.ComputeLab --headless --page <name> --record <dir> [--seconds <s>] [--fps <f>]
          SharpCompute.Samples.ComputeLab --list

        Keys: <-/-> switch page, Space pause, S save PNG, Esc quit; each page lists its own keys.
        --page      start on this page: a name from --list, e.g. histogram-privatised or simd-reduction
        --frames    close the window after this many frames (for scripted runs)
        --headless  no window: writes <dir>/<n>-<page>.png and <dir>/summary.txt (default dir: computelab),
                    and exits with 2 if a GPU result does not match the CPU
        --record    with --headless and --page: writes that page's frames over --seconds (default 6) at
                    --fps (default 12) as <dir>/<page>-0001.png, ... (the same pixels every run)
        --list      the pages, by topic: atomics (Histogram, Histogram: privatised, Compaction), shared
                    memory (Reduction, PrefixSum, MatMul, Blur), local arrays (LocalArrays), SIMD groups
                    (SIMD reduction, SIMD compaction, SIMD shuffle), dispatch (Indirect) and math (Bits,
                    FastMath)
        """;

    // Must stay synchronous: MetalWindow needs the process main thread, which only a synchronous Main keeps.
    public static int Main(string[] args)
    {
        bool headless = false, list = false;
        string output = "computelab";
        string? start = null, record = null;
        int frames = 0;
        double seconds = 6, fps = 12;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--headless":
                    headless = true;
                    break;
                case "--list":
                    list = true;
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--page" when i + 1 < args.Length:
                    start = args[++i];
                    break;
                case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out frames) && frames > 0:
                    i++;
                    break;
                case "--record" when i + 1 < args.Length:
                    record = args[++i];
                    break;
                case "--seconds" when i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds is > 0 and <= 60:
                    i++;
                    break;
                case "--fps" when i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out fps) && fps is >= 1 and <= 60:
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

        if (record is not null && (!headless || start is null))
        {
            Console.Error.WriteLine("--record needs --headless and --page <name>.");
            return 1;
        }

        if (!GraphicsDevice.IsSupported)
        {
            Console.Error.WriteLine("No Metal device: SharpCompute needs macOS 15+ on Apple Silicon.");
            return 1;
        }

        GraphicsDevice device = GraphicsDevice.GetDefault();
        List<ILabPage> pages = LabPages.Create(device);

        try
        {
            if (list)
            {
                foreach (ILabPage page in pages)
                {
                    Console.WriteLine($"{LabPages.Slug(page.Name),-22} {page.Topic,-14} {page.Name}: {page.Feature}");
                }

                return 0;
            }

            int index = start is null ? 0 : LabPages.Find(pages, start);

            if (index < 0)
            {
                Console.Error.WriteLine($"Unknown page '{start}'. Use --list to see the names.");
                return 1;
            }

            if (record is not null)
            {
                int written = Headless.Record(device, pages, index, record, seconds, fps);
                Console.WriteLine(Invariant($"{pages[index].Name}: {written} frames at {fps:G} fps in {Path.GetFullPath(record)}"));
                return 0;
            }

            if (headless)
            {
                return Headless.Run(device, pages, output, Console.Out) ? 0 : 2;
            }

            if (!MetalWindow.IsSupported)
            {
                Console.Error.WriteLine("No window server: run ComputeLab from a terminal in a desktop session, or use --headless.");
                return 1;
            }

            using MetalWindow window = new(device, "SharpCompute ComputeLab", LabCanvas.Width, LabCanvas.Height);
            using LabApp app = new(window, device, pages, index, frames);

            Console.WriteLine("<-/-> switch page · Space pause · S save PNG · Esc quit");
            window.Run(app.OnFrame);

            // A scripted run reports what it saw: the last check of the page it ended on.
            if (frames > 0)
            {
                Console.WriteLine(app.LastCheck is { } check ? $"{(check.Passed ? "PASS" : "FAIL")}  {check.Summary}" : "No check ran.");
                return app.LastCheck is { Passed: true } ? 0 : 2;
            }

            return 0;
        }
        finally
        {
            foreach (ILabPage page in pages)
            {
                page.Dispose();
            }
        }
    }
}

/// <summary>The headless mode: every page once, at a fixed time, into PNGs and a summary.</summary>
public static class Headless
{
    /// <summary>The animation time of the headless pictures.</summary>
    public const float Time = 2.5f;

    // GPU timings are the median of this many dispatches, after one that is not counted (the first
    // dispatch of a shader also compiles it).
    private const int TimedDispatches = 7;

    /// <summary>The PNG a page is written to, e.g. <c>1-histogram.png</c>.</summary>
    public static string FileName(IReadOnlyList<ILabPage> pages, int index) => Invariant($"{index + 1}-{LabPages.Slug(pages[index].Name)}.png");

    /// <summary>
    /// Renders every page into <paramref name="directory"/> and writes <c>summary.txt</c> there (also
    /// echoed to <paramref name="log"/>). The pictures are the same on every run; the timings in the
    /// summary are not. Returns false if any page's GPU result differs from the CPU's.
    /// </summary>
    public static bool Run(GraphicsDevice device, IReadOnlyList<ILabPage> pages, string directory, TextWriter log)
    {
        Directory.CreateDirectory(directory);

        using LabCanvas canvas = new(device);
        LabFrame frame = new(Time, Deterministic: true);
        StringBuilder summary = new();
        bool allPassed = true;

        summary.AppendLine(Invariant($"ComputeLab on {device.Name}, {Environment.ProcessorCount} CPU cores"));
        summary.AppendLine("GPU: the page's dispatches, commit to completion (median of 7). CPU: the same work, on the cores the CPU note says.");

        for (int index = 0; index < pages.Count; index++)
        {
            ILabPage page = pages[index];
            summary.AppendLine();
            summary.AppendLine(Invariant($"[{index + 1}/{pages.Count}] {page.Topic} / {page.Name}: {page.Feature}"));

            // Every variant is checked and timed; the picture is of the first one.
            for (int variant = page.Variants.Count - 1; variant >= 0; variant--)
            {
                page.Variant = variant;
                LabCheck check = Measure(page, frame, out LabTiming timing);
                allPassed &= check.Passed;

                summary.AppendLine(Invariant($"  {page.Variants[variant]}"));
                summary.AppendLine(Invariant($"    {(check.Passed ? "PASS" : "FAIL")}  {check.Summary}"));
                summary.AppendLine(Invariant($"    {LabView.TimingLine(page, timing)}"));

                if (timing.BaselineText is { } baseline)
                {
                    summary.AppendLine(Invariant($"    {baseline}"));
                }

                if (variant == 0)
                {
                    LabView.Compose(canvas, pages, index, frame, check, timing: null);
                    string path = Path.Combine(directory, FileName(pages, index));
                    PngEncoder.Write(path, canvas.ToArray(), LabCanvas.Width, LabCanvas.Height);
                    summary.AppendLine(Invariant($"    {Path.GetFileName(path)}"));
                }
            }
        }

        summary.AppendLine();
        summary.AppendLine(allPassed ? "All GPU results match the CPU." : "SOME GPU RESULTS DO NOT MATCH THE CPU.");

        File.WriteAllText(Path.Combine(directory, "summary.txt"), summary.ToString());
        log.Write(summary.ToString());
        return allPassed;
    }

    /// <summary>
    /// Writes <paramref name="pages"/>[<paramref name="index"/>] as <paramref name="seconds"/> of
    /// animation at <paramref name="fps"/> into <paramref name="directory"/>, one PNG per frame
    /// (<c>&lt;page&gt;-0001.png</c>, ...), for a GIF of a page whose point is its motion. The frames
    /// start at <see cref="Time"/> and are reproducible like the headless pictures: the page's first
    /// variant, no timings, and the check of each frame's own inputs. Returns the number of frames.
    /// </summary>
    public static int Record(GraphicsDevice device, IReadOnlyList<ILabPage> pages, int index, string directory, double seconds, double fps)
    {
        Directory.CreateDirectory(directory);

        ILabPage page = pages[index];
        string stem = LabPages.Slug(page.Name);
        int count = Math.Max(1, (int)Math.Round(seconds * fps));
        page.Variant = 0;

        using LabCanvas canvas = new(device);

        for (int i = 0; i < count; i++)
        {
            LabFrame frame = new(Time + (float)(i / fps), Deterministic: true);
            page.Compute(frame);
            LabView.Compose(canvas, pages, index, frame, page.Check(), timing: null);
            PngEncoder.Write(Path.Combine(directory, Invariant($"{stem}-{i + 1:D4}.png")), canvas.ToArray(), LabCanvas.Width, LabCanvas.Height);
        }

        return count;
    }

    /// <summary>Computes the page a few times for a stable GPU time, then checks it against the CPU.</summary>
    public static LabCheck Measure(ILabPage page, LabFrame frame, out LabTiming timing)
    {
        page.Compute(frame);

        double[] gpu = new double[TimedDispatches];
        double[] baseline = new double[TimedDispatches];
        string? baselineName = null;

        for (int i = 0; i < gpu.Length; i++)
        {
            page.Compute(frame);
            gpu[i] = page.GpuMilliseconds;

            if (page.Baseline is { } other)
            {
                (baselineName, baseline[i]) = (other.Name, other.GpuMilliseconds);
            }
        }

        Array.Sort(gpu);
        Array.Sort(baseline);

        // The first check also JIT-compiles the page's C# reference; the second one is the CPU's time.
        page.Check();
        LabCheck check = page.Check();
        timing = new LabTiming(gpu[gpu.Length / 2], check.CpuMilliseconds)
        {
            Baseline = baselineName is null ? null : new LabBaseline(baselineName, baseline[baseline.Length / 2]),
        };
        return check;
    }
}

/// <summary>Per-frame logic and input handling for the lab's window.</summary>
internal sealed class LabApp : IDisposable
{
    // The CPU side of a check keeps every core busy for a few milliseconds, so it runs on a timer
    // rather than every frame; the GPU side runs every frame.
    private const double CheckInterval = 1.0;
    private const double TitleInterval = 0.5;

    private readonly MetalWindow _window;
    private readonly IReadOnlyList<ILabPage> _pages;
    private readonly LabCanvas _canvas;
    private readonly int _frameLimit;
    private int _frames;
    private int _index;
    private bool _paused;
    private double _time;
    private LabCheck? _check;
    private double _lastCheck = double.NegativeInfinity;

    // Smoothed times for the timing line: single dispatches jitter by tens of microseconds.
    private double _gpuMs;
    private double _cpuMs;

    private double _statsStart;
    private int _statsFrames;

    public LabApp(MetalWindow window, GraphicsDevice device, IReadOnlyList<ILabPage> pages, int index, int frameLimit = 0)
    {
        _window = window;
        _pages = pages;
        _index = index;
        _frameLimit = frameLimit;
        _canvas = new LabCanvas(device);

        window.KeyDown += OnKeyDown;
        Announce();
    }

    /// <summary>The latest comparison of the current page with the CPU, or null before the first one.</summary>
    public LabCheck? LastCheck => _check;

    private ILabPage Current => _pages[_index];

    public void OnFrame(FrameInfo frame)
    {
        // Clamp the step so a stall (a window drag, a breakpoint) doesn't make the animation jump.
        _time += _paused ? 0 : Math.Min(frame.DeltaTime, 0.1);

        LabFrame labFrame = new((float)_time, Deterministic: false);
        ILabPage page = Current;

        page.Compute(labFrame);
        _gpuMs = _gpuMs == 0 ? page.GpuMilliseconds : (_gpuMs * 0.9) + (page.GpuMilliseconds * 0.1);

        if (frame.Time - _lastCheck >= CheckInterval)
        {
            LabCheck check = page.Check();
            _cpuMs = _cpuMs == 0 ? check.CpuMilliseconds : (_cpuMs * 0.7) + (check.CpuMilliseconds * 0.3);
            _check = check;
            _lastCheck = frame.Time;
        }

        LabTiming? timing = _check is null ? null : new LabTiming(_gpuMs, _cpuMs);
        LabView.Compose(_canvas, _pages, _index, labFrame, _check, timing, _paused);
        _window.Present(_canvas.ToTexture());

        _statsFrames++;

        if (frame.Time - _statsStart >= TitleInterval)
        {
            double fps = _statsFrames / (frame.Time - _statsStart);
            (_statsStart, _statsFrames) = (frame.Time, 0);
            _window.Title = Invariant($"ComputeLab · {page.Name} ({page.Feature}) · {fps:F0} fps{(_paused ? " · paused" : string.Empty)}");
        }

        if (_frameLimit > 0 && ++_frames >= _frameLimit)
        {
            _window.Close();
        }
    }

    public void Dispose() => _canvas.Dispose();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _window.Close();
                break;

            case Key.Left or Key.Right:
                _index = (_index + (e.Key == Key.Right ? 1 : _pages.Count - 1)) % _pages.Count;
                Invalidate();
                Announce();
                break;

            case Key.Space when !e.IsRepeat:
                _paused = !_paused;
                break;

            case Key.S when !e.IsRepeat:
                string path = Path.GetFullPath(Invariant($"computelab-{LabPages.Slug(Current.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.png"));
                PngEncoder.Write(path, _canvas.ToArray(), LabCanvas.Width, LabCanvas.Height);
                Console.WriteLine($"Saved {path}");
                break;

            default:
                if (Current.OnKeyDown(e.Key))
                {
                    Invalidate();
                }

                break;
        }
    }

    /// <summary>What is computed changed: the last check and the smoothed times no longer describe it.</summary>
    private void Invalidate()
    {
        _check = null;
        _lastCheck = double.NegativeInfinity;
        _gpuMs = 0;
        _cpuMs = 0;
    }

    private void Announce()
    {
        ILabPage page = Current;
        Console.WriteLine(page.Help.Length == 0 ? $"{page.Name}: {page.Feature}" : $"{page.Name}: {page.Feature} — {page.Help}");
    }
}
