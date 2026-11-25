using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SharpCompute.Benchmarks.Shaders;
using SharpCompute.Samples.Shaders;
using static System.FormattableString;

namespace SharpCompute.Benchmarks;

/// <summary>
/// What the first use of a shader costs, three ways: its MSL compiled at run time by the first
/// dispatch, compiled ahead of the frame by <see cref="GraphicsDevice.Prepare{T}()"/>, and taken
/// from the library the build precompiled. Each for a first launch on a machine and for a later one.
/// </summary>
/// <remarks>
/// <para>
/// A shader becomes GPU code in two steps, and macOS caches both on disk, per application: the MSL is
/// compiled to Metal's intermediate representation (AIR), and the GPU driver compiles the AIR to
/// machine code when the pipeline is created. Precompiling does the first step at build time. The
/// second is always done on the machine that runs the shader.
/// </para>
/// <para>
/// So "first launch" has to mean that both caches are empty, and neither can be emptied by changing
/// the shader (a comment added to the MSL leaves the AIR as it was, and the driver's cache still
/// has it; a precompiled kernel cannot be changed at all). macOS keys those caches by the
/// application's bundle identifier, so every sample runs from a throwaway <c>.app</c> bundle (links
/// to this build) whose identifier has never been used: its first run starts with empty caches, the
/// second run of the same bundle is a later launch. The bundle and its cache folder are removed
/// afterwards; nothing that another application uses is touched.
/// </para>
/// </remarks>
public static partial class FirstFrames
{
    private const string Prefix = "first-frame:";
    private const int Samples = 7;

    // Small, so that a frame takes a few milliseconds and the compile is not lost in it.
    private const int Width = 320;
    private const int Height = 180;

    /// <summary>The same switch the runtime reads; false makes a device compile every shader from its MSL.</summary>
    private const string PrecompiledSwitch = "SharpCompute.UsePrecompiledShaders";

    /// <summary>
    /// One first frame in this process: <c>garden</c> or <c>gallery</c>, <c>none</c> or <c>prepare</c>,
    /// <c>source</c> or <c>precompiled</c>. Prints the device, prepare, first-frame and steady-frame
    /// times and where the pipeline came from.
    /// </summary>
    public static int Child(string[] args)
    {
        if (args.Contains("source"))
        {
            AppContext.SetSwitch(PrecompiledSwitch, false);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        GraphicsDevice device = GraphicsDevice.GetDefault();
        double deviceMs = stopwatch.Elapsed.TotalMilliseconds;

        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(Width, Height);
        double prepareMs = 0, firstMs, steadyMs = double.MaxValue;
        PipelineOrigin origin;

        if (args.Contains("gallery"))
        {
            // The first frame of every shader of the gallery, one after the other: what a viewer that
            // steps through them pays in all. (There is nothing to prepare them with from outside:
            // the gallery hands out render functions, not shader types.)
            stopwatch.Restart();
            Render(device, texture, 1.0f);
            firstMs = stopwatch.Elapsed.TotalMilliseconds;

            for (int i = 0; i < 3; i++)
            {
                stopwatch.Restart();
                Render(device, texture, 1.1f + (i * 0.1f));
                steadyMs = Math.Min(steadyMs, stopwatch.Elapsed.TotalMilliseconds);
            }

            origin = device.GetPipelineOrigin<HelloWorld>();
        }
        else
        {
            if (args.Contains("prepare"))
            {
                stopwatch.Restart();
                device.Prepare<RaymarchedGarden>();
                prepareMs = stopwatch.Elapsed.TotalMilliseconds;
            }

            stopwatch.Restart();
            device.ForEach(texture, new RaymarchedGarden(1.0f));
            firstMs = stopwatch.Elapsed.TotalMilliseconds;

            // The steady frame time: the fastest of a few, since one frame is only a few milliseconds.
            for (int i = 0; i < 5; i++)
            {
                stopwatch.Restart();
                device.ForEach(texture, new RaymarchedGarden(1.1f + (i * 0.1f)));
                steadyMs = Math.Min(steadyMs, stopwatch.Elapsed.TotalMilliseconds);
            }

            origin = device.GetPipelineOrigin<RaymarchedGarden>();
        }

        Console.WriteLine(Invariant($"{Prefix} {deviceMs:F3} {prepareMs:F3} {firstMs:F3} {steadyMs:F3} {origin}"));
        return 0;

        static void Render(GraphicsDevice device, ReadWriteTexture2D<Rgba32> texture, float time)
        {
            foreach (GalleryShader shader in Gallery.All)
            {
                shader.Render(device, texture, time);
            }
        }
    }

    public static int Run()
    {
        string self = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");

        Console.WriteLine();

        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            // The bundle needs a launcher of its own: the shared `dotnet` has a bundle identifier already.
            Console.WriteLine("First frames: not measured. Run the built SharpCompute.Benchmarks executable, not `dotnet SharpCompute.Benchmarks.dll`.");
            return 1;
        }

        Console.WriteLine(Invariant($"First frame at {Width}×{Height}. Each sample is a new process (median of {Samples}). 'First launch' is a run from"));
        Console.WriteLine("an application bundle macOS has not seen, so with an empty system shader cache; 'later launch' is that bundle run again.");
        Console.WriteLine("'Stall' is the first frame minus a steady frame: what the frame waited for the shader.");
        Console.WriteLine();
        Console.WriteLine("RaymarchedGarden (a raymarched SDF scene of about 300 lines):");
        Console.WriteLine("| Shader | Warm-up | First launch: Prepare | First launch: first frame | First launch: stall | Later launch: Prepare | Later launch: first frame | Later launch: stall | Steady frame |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");

        bool precompiled = true;

        foreach (string origin in new[] { "source", "precompiled" })
        {
            foreach (string warmup in new[] { "none", "prepare" })
            {
                if (Measure("garden", warmup, origin) is not { } result)
                {
                    precompiled = false;
                    continue;
                }

                string prepareFirst = warmup == "none" ? "—" : Invariant($"{result.First.Prepare:F1} ms");
                string prepareLater = warmup == "none" ? "—" : Invariant($"{result.Later.Prepare:F1} ms");

                Console.WriteLine(Invariant(
                    $"| {Name(origin)} | {(warmup == "none" ? "none" : "device.Prepare<T>()")} | {prepareFirst} | {result.First.Frame:F1} ms | {result.First.Stall:F1} ms | {prepareLater} | {result.Later.Frame:F1} ms | {result.Later.Stall:F1} ms | {result.Later.Steady:F1} ms |"));
            }
        }

        Console.WriteLine();
        Console.WriteLine(Invariant($"The {Gallery.All.Count} shaders of the gallery, the first frame of each, in all:"));
        Console.WriteLine("| Shaders | First launch: first frames | First launch: stall | Later launch: first frames | Later launch: stall | Steady frames |");
        Console.WriteLine("|---|---|---|---|---|---|");

        foreach (string origin in new[] { "source", "precompiled" })
        {
            if (Measure("gallery", "none", origin) is not { } result)
            {
                precompiled = false;
                continue;
            }

            Console.WriteLine(Invariant(
                $"| {Name(origin)} | {result.First.Frame:F0} ms | {result.First.Stall:F0} ms | {result.Later.Frame:F0} ms | {result.Later.Stall:F0} ms | {result.Later.Steady:F0} ms |"));
        }

        if (!precompiled)
        {
            Console.WriteLine();
            Console.WriteLine("The precompiled rows are missing: this build has no precompiled library (the Metal toolchain is not installed).");
        }

        return 0;

        static string Name(string origin) => origin == "source" ? "compiled at run time" : "precompiled";
    }

    /// <summary>
    /// The medians of <see cref="Samples"/> first launches and of the launch after each. Null when
    /// <paramref name="origin"/> is <c>precompiled</c> and the child did not find its shader in a library.
    /// </summary>
    private static (Timing First, Timing Later)? Measure(string set, string warmup, string origin)
    {
        List<Timing> first = [], later = [];

        for (int i = 0; i < Samples; i++)
        {
            using Bundle bundle = Bundle.Create();

            foreach (List<Timing> timings in new[] { first, later })
            {
                (Timing timing, string actual) = Spawn(bundle.Executable, set, warmup, origin);

                if (!actual.Equals(origin == "source" ? "CompiledAtRunTime" : "Precompiled", StringComparison.Ordinal))
                {
                    return null;
                }

                timings.Add(timing);
            }
        }

        return (Median(first), Median(later));

        static Timing Median(List<Timing> timings) => new(
            RuntimeFeatures.MedianOf([.. timings.Select(static t => t.Prepare)]),
            RuntimeFeatures.MedianOf([.. timings.Select(static t => t.Frame)]),
            RuntimeFeatures.MedianOf([.. timings.Select(static t => t.Steady)]));
    }

    private static (Timing Timing, string Origin) Spawn(string executable, string set, string warmup, string origin)
    {
        ProcessStartInfo info = new(executable) { RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add("--first-frame");
        info.ArgumentList.Add(set);
        info.ArgumentList.Add(warmup);
        info.ArgumentList.Add(origin);

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the first-frame process.");
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        string line = stdout.Split('\n').FirstOrDefault(static l => l.StartsWith(Prefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The first-frame process printed no result:\n{stdout}");

        string[] parts = line[Prefix.Length..].Trim().Split(' ');
        double[] values = Array.ConvertAll(parts[..4], static s => double.Parse(s, CultureInfo.InvariantCulture));

        return (new Timing(values[1], values[2], values[3]), parts[4]);
    }

    /// <summary>One first frame, in milliseconds: the Prepare before it (0 without one), the frame, and a steady frame.</summary>
    private readonly record struct Timing(double Prepare, double Frame, double Steady)
    {
        public double Stall => Frame - Steady;
    }

    /// <summary>
    /// A copy of this build as an application bundle with an identifier of its own: the launcher is
    /// copied (the bundle is found from the executable's path), everything else is linked to.
    /// </summary>
    private sealed partial class Bundle : IDisposable
    {
        private const int DarwinUserCacheDirectory = 65538; // _CS_DARWIN_USER_CACHE_DIR

        private readonly string _root;
        private readonly string _identifier;

        private Bundle(string root, string identifier, string executable)
        {
            _root = root;
            _identifier = identifier;
            Executable = executable;
        }

        public string Executable { get; }

        public static Bundle Create()
        {
            string self = Environment.ProcessPath!;
            string identifier = "sharpcompute.benchmarks.firstframe." + Guid.NewGuid().ToString("N");
            string root = Path.Combine(Path.GetTempPath(), identifier);
            string contents = Path.Combine(root, "FirstFrame.app", "Contents");
            string binaries = Path.Combine(contents, "MacOS");
            string executable = Path.Combine(binaries, Path.GetFileName(self));

            Directory.CreateDirectory(binaries);
            File.Copy(self, executable);

            foreach (string entry in Directory.EnumerateFileSystemEntries(AppContext.BaseDirectory))
            {
                string link = Path.Combine(binaries, Path.GetFileName(entry));

                if (Directory.Exists(entry))
                {
                    Directory.CreateSymbolicLink(link, entry);
                }
                else if (!File.Exists(link))
                {
                    File.CreateSymbolicLink(link, entry);
                }
            }

            File.WriteAllText(
                Path.Combine(contents, "Info.plist"),
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0"><dict>
                <key>CFBundleIdentifier</key><string>{identifier}</string>
                <key>CFBundleExecutable</key><string>{Path.GetFileName(self)}</string>
                <key>CFBundleName</key><string>FirstFrame</string>
                <key>CFBundlePackageType</key><string>APPL</string>
                </dict></plist>
                """);

            return new Bundle(root, identifier, executable);
        }

        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);

            // The caches macOS made for the identifier: <user cache directory>/<identifier>.
            if (UserCacheDirectory() is { } caches && Path.Combine(caches, _identifier) is { } cache && Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }
        }

        private static unsafe string? UserCacheDirectory()
        {
            byte* buffer = stackalloc byte[1024];
            nuint length = confstr(DarwinUserCacheDirectory, buffer, 1024);

            return length is > 1 and <= 1024 ? Marshal.PtrToStringUTF8((nint)buffer) : null;
        }

        [LibraryImport("libc")]
        private static unsafe partial nuint confstr(int name, byte* buffer, nuint length);
    }
}
