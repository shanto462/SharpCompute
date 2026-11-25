using System;
using System.Diagnostics;
using System.Linq;
using SharpCompute.Benchmarks.Shaders;
using static System.FormattableString;

namespace SharpCompute.Benchmarks;

/// <summary>
/// Quick measurements of the runtime features that are a choice between ways of doing one thing:
/// a GPU copy against a CPU copy (and, inside a context, a blit against a copy kernel), a kernel with
/// FP contraction off against the default, and (<see cref="FirstFrames"/>) the first frame of a shader
/// compiled at run time, prepared, and precompiled. A plain <see cref="Stopwatch"/> and medians, a
/// few minutes in all: indicative numbers for a pull request, not BenchmarkDotNet statistics.
/// </summary>
public static class RuntimeFeatures
{
    public static int Run()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Console.WriteLine(Invariant($"{device.Name}, {Environment.ProcessorCount} CPU cores. Medians; GPU times are commit to completion."));

        Copies(device);
        FpContract(device);
        FirstFrames.Run();
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

    internal static double MedianOf(double[] samples)
    {
        double[] sorted = [.. samples];
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static string Micro(double milliseconds) => Invariant($"{milliseconds * 1000:F0} µs");

    private static string Bytes(long bytes) => bytes >= 1 << 20 ? Invariant($"{bytes >> 20} MB") : Invariant($"{bytes >> 10} KB");
}
