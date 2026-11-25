// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SharpCompute.Presentation;
using SharpCompute.Samples.ComputeLab;
using SharpCompute.Samples.ComputeLab.Pages;
using SharpCompute.Samples.SimdGroups.Pages;
using SharpCompute.Samples.Shaders;
using static System.FormattableString;

namespace SharpCompute.Samples.SimdGroups;

/// <summary>
/// SIMD-group ("wave") operations, one page per algorithm they make possible. Each page runs
/// it on the GPU, shows the result, checks it against the CPU and times both. In a window by default;
/// <c>--headless</c> writes one PNG per page and a text summary instead.
/// </summary>
public static class Program
{
    private const string Usage = """
        SimdGroups: SIMD-group (wave) operations, one page per algorithm

          SharpCompute.Samples.SimdGroups [--page <name>] [--frames <n>]
          SharpCompute.Samples.SimdGroups --headless [--out <dir>]
          SharpCompute.Samples.SimdGroups --list

        Keys: <-/-> switch page, Space pause, S save PNG, Esc quit; each page lists its own keys.
        --page      start on this page (see --list)
        --frames    close the window after this many frames (for scripted runs)
        --headless  no window: writes <dir>/<n>-<page>.png and <dir>/summary.txt (default dir: simdgroups),
                    and exits with 2 if a GPU result does not match the CPU
        """;

    // Must stay synchronous: MetalWindow needs the process main thread, which only a synchronous Main keeps.
    public static int Main(string[] args)
    {
        bool headless = false, list = false;
        string output = "simdgroups";
        string? start = null;
        int frames = 0;

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

        GraphicsDevice device = GraphicsDevice.GetDefault();
        List<ILabPage> pages = SimdGroupsPages.Create(device);

        try
        {
            if (list)
            {
                foreach (ILabPage page in pages)
                {
                    Console.WriteLine($"{page.Name,-12} {page.Feature}");
                }

                return 0;
            }

            int index = start is null ? 0 : pages.FindIndex(p => string.Equals(p.Name, start, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                Console.Error.WriteLine($"Unknown page '{start}'. Use --list to see the names.");
                return 1;
            }

            if (headless)
            {
                return SimdGroupsHeadless.Run(device, pages, output, Console.Out) ? 0 : 2;
            }

            if (!MetalWindow.IsSupported)
            {
                Console.Error.WriteLine("No window server: run the demo from a terminal in a desktop session, or use --headless.");
                return 1;
            }

            using MetalWindow window = new(device, "SharpCompute SimdGroups", LabCanvas.Width, LabCanvas.Height);
            using SimdGroupsApp app = new(window, device, pages, index, frames);

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
public static class SimdGroupsHeadless
{
    /// <summary>The animation time of the headless pictures.</summary>
    public const float Time = 2.5f;

    // GPU timings are the median of this many dispatches, after one that is not counted (the first
    // dispatch of a shader also compiles it).
    private const int TimedDispatches = 7;

    /// <summary>The PNG a page is written to, e.g. <c>1-reduction.png</c>.</summary>
    public static string FileName(IReadOnlyList<ILabPage> pages, int index) => Invariant($"{index + 1}-{pages[index].Name.ToLowerInvariant()}.png");

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

        summary.AppendLine(Invariant($"SimdGroups on {device.Name}, {Environment.ProcessorCount} CPU cores"));
        summary.AppendLine("GPU: the page's dispatches, commit to completion (median of 7). CPU: plain C#, as each check line says.");

        for (int index = 0; index < pages.Count; index++)
        {
            ILabPage page = pages[index];
            summary.AppendLine();
            summary.AppendLine(Invariant($"[{index + 1}/{pages.Count}] {page.Name}: {page.Feature}"));

            // Every variant is checked and timed; the picture is of the first one.
            for (int variant = page.Variants.Count - 1; variant >= 0; variant--)
            {
                page.Variant = variant;
                LabCheck check = Measure(page, frame, out LabTiming timing);
                allPassed &= check.Passed;

                summary.AppendLine(Invariant($"  {page.Variants[variant]}"));
                summary.AppendLine(Invariant($"    {(check.Passed ? "PASS" : "FAIL")}  {check.Summary}"));
                summary.AppendLine(Invariant($"    {SimdGroupsView.Describe(timing)}"));

                if (variant == 0)
                {
                    SimdGroupsView.Compose(canvas, pages, index, frame, check, timing: null);
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

    /// <summary>Computes the page a few times for a stable GPU time, then checks it against the CPU.</summary>
    public static LabCheck Measure(ILabPage page, LabFrame frame, out LabTiming timing)
    {
        page.Compute(frame);

        double[] gpu = new double[TimedDispatches];

        for (int i = 0; i < gpu.Length; i++)
        {
            page.Compute(frame);
            gpu[i] = page.GpuMilliseconds;
        }

        Array.Sort(gpu);

        // The first check also JIT-compiles the page's C# reference; the second one is the CPU's time.
        page.Check();
        LabCheck check = page.Check();
        timing = new LabTiming(gpu[gpu.Length / 2], check.CpuMilliseconds);
        return check;
    }
}

/// <summary>Per-frame logic and input handling for the lab's window.</summary>
internal sealed class SimdGroupsApp : IDisposable
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

    public SimdGroupsApp(MetalWindow window, GraphicsDevice device, IReadOnlyList<ILabPage> pages, int index, int frameLimit = 0)
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
        SimdGroupsView.Compose(_canvas, _pages, _index, labFrame, _check, timing, _paused);
        _window.Present(_canvas.ToTexture());

        _statsFrames++;

        if (frame.Time - _statsStart >= TitleInterval)
        {
            double fps = _statsFrames / (frame.Time - _statsStart);
            (_statsStart, _statsFrames) = (frame.Time, 0);
            _window.Title = Invariant($"SimdGroups · {page.Name} ({page.Feature}) · {fps:F0} fps{(_paused ? " · paused" : string.Empty)}");
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
                string path = Path.GetFullPath(Invariant($"simdgroups-{Current.Name.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}.png"));
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
