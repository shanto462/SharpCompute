using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SharpCompute.Benchmarks.Shaders;
using static System.FormattableString;

namespace SharpCompute.Benchmarks;

/// <summary>
/// Quick measurements of the runtime features that are a choice between ways of doing one thing:
/// a GPU copy against a CPU copy (and, inside a context, a blit against a copy kernel), a kernel with
/// FP contraction off against the default, and the first frame of a shader with and without
/// <see cref="GraphicsDevice.Prepare{T}()"/>. A plain <see cref="Stopwatch"/> and medians, a few
/// seconds in all: indicative numbers for a pull request, not BenchmarkDotNet statistics.
/// </summary>
public static class RuntimeFeatures
{
    private const string FirstFramePrefix = "first-frame:";
    private const int FirstFrameSamples = 7;

    // Small, so that a frame takes a few milliseconds and the compile is not lost in it.
    private const int FirstFrameWidth = 320;
    private const int FirstFrameHeight = 180;

    public static int Run()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Console.WriteLine(Invariant($"{device.Name}, {Environment.ProcessorCount} CPU cores. Medians; GPU times are commit to completion."));

        Copies(device);
        FpContract(device);
        FirstFrames();
        return 0;
    }

    /// <summary>
    /// The child process of <see cref="FirstFrames"/>: one cold start, timed step by step. With
    /// <c>uncached</c> the shader's MSL gets a comment that is unique to this process, so that macOS's
    /// shader cache cannot have seen it: the compile a shader gets the first time it ever runs on a machine.
    /// </summary>
    public static int FirstFrame(string[] args)
    {
        bool prepare = args.Contains("prepared");

        return args.Contains("uncached")
            ? FirstFrame(prepare, static time => new NeverSeen<RaymarchedGarden>(new RaymarchedGarden(time)))
            : FirstFrame(prepare, static time => new RaymarchedGarden(time));
    }

    private static int FirstFrame<T>(bool prepare, Func<float, T> create)
        where T : struct, IComputeShader<Float4>, IComputeShaderDescriptor<T>
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        GraphicsDevice device = GraphicsDevice.GetDefault();
        double deviceMs = stopwatch.Elapsed.TotalMilliseconds;

        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(FirstFrameWidth, FirstFrameHeight);
        double prepareMs = 0;

        if (prepare)
        {
            stopwatch.Restart();
            device.Prepare<T>();
            prepareMs = stopwatch.Elapsed.TotalMilliseconds;
        }

        stopwatch.Restart();
        device.ForEach(texture, create(1.0f));
        double firstMs = stopwatch.Elapsed.TotalMilliseconds;

        // The steady frame time: the fastest of a few, since one frame is only a few milliseconds.
        double steadyMs = double.MaxValue;

        for (int i = 0; i < 5; i++)
        {
            stopwatch.Restart();
            device.ForEach(texture, create(1.1f + (i * 0.1f)));
            steadyMs = Math.Min(steadyMs, stopwatch.Elapsed.TotalMilliseconds);
        }

        Console.WriteLine(Invariant($"{FirstFramePrefix} {deviceMs:F3} {prepareMs:F3} {firstMs:F3} {steadyMs:F3}"));
        return 0;
    }

    private static void Copies(GraphicsDevice device)
    {
        Console.WriteLine();
        Console.WriteLine("Copy of a uint buffer. 'in a context' is dispatch -> copy -> dispatch in one execution,");
        Console.WriteLine("minus the same context with the two (1,024-thread) dispatches alone.");
        Console.WriteLine("| Size | CPU span copy | device.Copy (blit, own commit) | in a context: blit encoder | in a context: copy kernel |");
        Console.WriteLine("|---|---|---|---|---|");

        using ReadWriteBuffer<int> small = device.AllocateReadWriteBuffer<int>(1024);

        foreach (int length in new[] { 1024, 256 * 1024, 16 * 1024 * 1024 })
        {
            using ReadWriteBuffer<uint> source = device.AllocateReadWriteBuffer<uint>(length);
            using ReadWriteBuffer<uint> destination = device.AllocateReadWriteBuffer<uint>(length);
            using ComputeContext context = device.CreateComputeContext();
            source.AsSpan().Fill(0x01020304u);
            destination.AsSpan().Clear();

            ReadOnlyBuffer<uint> readOnlySource = device.AllocateReadOnlyBuffer<uint>(length);
            readOnlySource.AsSpan().Fill(0x01020304u);

            int runs = length > 1024 * 1024 ? 15 : 41;

            double cpu = Median(runs, () => source.AsReadOnlySpan().CopyTo(destination.AsSpan()));
            double blit = Median(runs, () => device.Copy(source, destination));

            double baseline = Median(runs, () =>
            {
                context.For(1024, new TouchInts(small));
                context.For(1024, new TouchInts(small));
                context.Execute();
            });

            double contextBlit = Median(runs, () =>
            {
                context.For(1024, new TouchInts(small));
                context.Copy(source, destination);
                context.For(1024, new TouchInts(small));
                context.Execute();
            });

            double contextKernel = Median(runs, () =>
            {
                context.For(1024, new TouchInts(small));
                context.For(length, new CopyKernel(readOnlySource, destination));
                context.For(1024, new TouchInts(small));
                context.Execute();
            });

            readOnlySource.Dispose();

            Console.WriteLine(Invariant(
                $"| {Bytes((long)length * sizeof(uint))} | {Micro(cpu)} | {Micro(blit)} | {Micro(contextBlit - baseline)} | {Micro(contextKernel - baseline)} |"));
        }
    }

    private static void FpContract(GraphicsDevice device)
    {
        Console.WriteLine();
        Console.WriteLine("FP contraction: the same kernel with Metal's default and with [ShaderOptions(FpContract = false)].");
        Console.WriteLine("Each is the median of 31 dispatches, the two kernels taking turns; (fastest) is the best of them.");
        Console.WriteLine("| Kernel | Default (fused) | FpContract = false | Cost |");
        Console.WriteLine("|---|---|---|---|");

        // Large enough that the kernel, not the submission, is what is timed.
        const int width = 3840, height = 2160, iterations = 1024;
        using ReadWriteBuffer<uint> pixels = device.AllocateReadWriteBuffer<uint>(width * height);
        Float2 center = new(-0.5f, 0f);
        float step = 3f / height;

        device.Prepare(ShaderReference.Of<MandelbrotIterations>(), ShaderReference.Of<MandelbrotIterationsNoContract>(), ShaderReference.Of<SaxpyGenerated>(), ShaderReference.Of<SaxpyNoContract>());

        // Interleaved, so that drift (thermal, other load) hits both the same way.
        double[] fused = new double[31], exact = new double[31];

        for (int i = 0; i < fused.Length; i++)
        {
            fused[i] = Time(() => device.For(width, height, new MandelbrotIterations(pixels, width, height, iterations, center, step)));
            exact[i] = Time(() => device.For(width, height, new MandelbrotIterationsNoContract(pixels, width, height, iterations, center, step)));
        }

        Report(Invariant($"Mandelbrot {width}×{height}, {iterations} iterations"), fused, exact);

        const int length = 16 * 1024 * 1024;
        using ReadOnlyBuffer<float> x = device.AllocateReadOnlyBuffer<float>(length);
        using ReadWriteBuffer<float> y = device.AllocateReadWriteBuffer<float>(length);
        x.AsSpan().Fill(1.5f);

        for (int i = 0; i < fused.Length; i++)
        {
            fused[i] = Time(() => device.For(length, new SaxpyGenerated(x, y, 0.5f)));
            exact[i] = Time(() => device.For(length, new SaxpyNoContract(x, y, 0.5f)));
        }

        Report("Saxpy, 16M floats", fused, exact);

        static void Report(string name, double[] fused, double[] exact)
        {
            double a = MedianOf(fused), b = MedianOf(exact), fastestA = fused.Min(), fastestB = exact.Min();
            Console.WriteLine(Invariant(
                $"| {name} | {a:F2} ms ({fastestA:F2}) | {b:F2} ms ({fastestB:F2}) | {(b / a - 1) * 100:+0.0;-0.0}% ({(fastestB / fastestA - 1) * 100:+0.0;-0.0}%) |"));
        }
    }

    private static void FirstFrames()
    {
        Console.WriteLine();
        Console.WriteLine(Invariant($"First frame of RaymarchedGarden (a raymarched SDF scene) at {FirstFrameWidth}×{FirstFrameHeight}, each sample a fresh process (median of {FirstFrameSamples})."));
        Console.WriteLine("'Seen before' is what a second launch of an app gets: macOS keeps compiled shaders in a cache across");
        Console.WriteLine("processes. 'Never seen' defeats that cache with a unique comment in the MSL: the first launch ever.");
        Console.WriteLine("| Shader cache | Warm-up | Prepare | First frame | Steady frame | Stall in the first frame |");
        Console.WriteLine("|---|---|---|---|---|---|");

        foreach (string cache in new[] { "cached", "uncached" })
        {
            double[][] cold = new double[FirstFrameSamples][], prepared = new double[FirstFrameSamples][];

            for (int i = 0; i < FirstFrameSamples; i++)
            {
                cold[i] = SpawnFirstFrame("cold", cache);
                prepared[i] = SpawnFirstFrame("prepared", cache);
            }

            string name = cache == "cached" ? "Seen before" : "Never seen";
            Console.WriteLine(Invariant($"| {name} | none | — | {Column(cold, 2):F1} ms | {Column(cold, 3):F1} ms | {Column(cold, 2) - Column(cold, 3):F1} ms |"));
            Console.WriteLine(Invariant(
                $"| {name} | device.Prepare<T>() | {Column(prepared, 1):F1} ms | {Column(prepared, 2):F1} ms | {Column(prepared, 3):F1} ms | {Column(prepared, 2) - Column(prepared, 3):F1} ms |"));
        }

        static double Column(double[][] samples, int index) => MedianOf(samples.Select(s => s[index]).ToArray());
    }

    private static double[] SpawnFirstFrame(string mode, string cache)
    {
        string self = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        ProcessStartInfo info = new(self) { RedirectStandardOutput = true, UseShellExecute = false };

        // When launched as `dotnet SharpCompute.Benchmarks.dll`, the host needs the dll again.
        if (System.IO.Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            info.ArgumentList.Add(typeof(RuntimeFeatures).Assembly.Location);
        }

        info.ArgumentList.Add("--first-frame");
        info.ArgumentList.Add(mode);
        info.ArgumentList.Add(cache);

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the first-frame process.");
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        string line = stdout.Split('\n').FirstOrDefault(l => l.StartsWith(FirstFramePrefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The first-frame process printed no result:\n{stdout}");

        return Array.ConvertAll(line[FirstFramePrefix.Length..].Trim().Split(' '), s => double.Parse(s, CultureInfo.InvariantCulture));
    }

    /// <summary>The median time of <paramref name="runs"/> calls, in milliseconds, after two calls that are not counted.</summary>
    private static double Median(int runs, Action action)
    {
        action();
        action();

        double[] samples = new double[runs];

        for (int i = 0; i < runs; i++)
        {
            samples[i] = Time(action);
        }

        return MedianOf(samples);
    }

    private static double Time(Action action)
    {
        long start = Stopwatch.GetTimestamp();
        action();
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static double MedianOf(double[] samples)
    {
        double[] sorted = [.. samples];
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static string Micro(double milliseconds) => Invariant($"{milliseconds * 1000:F0} µs");

    private static string Bytes(long bytes) => bytes >= 1 << 20 ? Invariant($"{bytes >> 20} MB") : Invariant($"{bytes >> 10} KB");
}

/// <summary>
/// <typeparamref name="T"/> with a comment appended to its MSL that no other process has used, so the
/// system shader cache cannot hold its compiled form. Everything else is forwarded.
/// </summary>
internal readonly struct NeverSeen<T>(T shader) : IComputeShader<Float4>, IComputeShaderDescriptor<NeverSeen<T>>
    where T : struct, IComputeShader<Float4>, IComputeShaderDescriptor<T>
{
    private static readonly string s_source = T.MslSource + "\n// " + Guid.NewGuid().ToString("N") + "\n";

    private readonly T _shader = shader;

    public static string EntryPoint => T.EntryPoint;

    public static string MslSource => s_source;

    public static int ThreadGroupSizeX => T.ThreadGroupSizeX;

    public static int ThreadGroupSizeY => T.ThreadGroupSizeY;

    public static int ThreadGroupSizeZ => T.ThreadGroupSizeZ;

    public static void SetArguments(in NeverSeen<T> shader, ref ShaderArgumentWriter writer) => T.SetArguments(in shader._shader, ref writer);

    public Float4 Execute() => _shader.Execute();
}
