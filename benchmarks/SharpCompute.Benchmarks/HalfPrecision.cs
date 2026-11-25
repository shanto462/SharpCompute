using System;
using System.Diagnostics;
using SharpCompute.Benchmarks.Shaders;
using static System.FormattableString;

namespace SharpCompute.Benchmarks;

/// <summary>
/// Quick measurements of what half precision buys on the GPU: the same kernel on <see cref="Float4"/>
/// and on <see cref="Half4"/>, for a kernel bound by memory bandwidth, one that is all arithmetic, and
/// a many-tap blur in between. A plain <see cref="Stopwatch"/> and medians, a few seconds in all:
/// indicative numbers for a pull request, not BenchmarkDotNet statistics.
/// </summary>
public static class HalfPrecision
{
    private const int Runs = 31;

    public static int Run()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Console.WriteLine(Invariant($"{device.Name}, {Environment.ProcessorCount} CPU cores. GPU times are commit to completion:"));
        Console.WriteLine(Invariant($"the median of {Runs} dispatches each, the kernels of a row taking turns."));
        Console.WriteLine();
        Console.WriteLine("| Kernel | Size | Float4 | Half4 | Half4 is |");
        Console.WriteLine("|---|---|---|---|---|");

        Stream(device);
        Blur(device);
        Fold(device);
        return 0;
    }

    private static void Stream(GraphicsDevice device)
    {
        const int length = 16 * 1024 * 1024;
        using ReadOnlyBuffer<Float4> floatX = device.AllocateReadOnlyBuffer<Float4>(length);
        using ReadWriteBuffer<Float4> floatY = device.AllocateReadWriteBuffer<Float4>(length);
        using ReadOnlyBuffer<Half4> halfX = device.AllocateReadOnlyBuffer<Half4>(length);
        using ReadWriteBuffer<Half4> halfY = device.AllocateReadWriteBuffer<Half4>(length);

        floatX.AsSpan().Fill(new Float4(0.5f));
        halfX.AsSpan().Fill((Half4)new Float4(0.5f));

        double[] times = Medians(
            () =>
            {
                // Reset first, so that the values do not grow from run to run (the fill is not timed).
                floatY.AsSpan().Fill(new Float4(0.25f));
                return () => device.For(length, new StreamFloat4(floatX, floatY));
            },
            () =>
            {
                halfY.AsSpan().Fill((Half4)new Float4(0.25f));
                return () => device.For(length, new StreamHalf4(halfX, halfY));
            });

        Row("Streaming: `y = x * 0.5 + y` (bound by memory bandwidth)", "16M elements, 2 buffers: 512 MB / 256 MB", times[0], times[1]);
    }

    private static void Blur(GraphicsDevice device)
    {
        const int size = 1024, radius = 8, stride = size + (2 * radius);
        Float4[] image = new Float4[stride * stride];
        Half4[] halfImage = new Half4[image.Length];
        Random random = new(1);

        for (int i = 0; i < image.Length; i++)
        {
            image[i] = new Float4(random.Next(257) / 256f, random.Next(257) / 256f, random.Next(257) / 256f, 1f);
            halfImage[i] = (Half4)image[i];
        }

        using ReadOnlyBuffer<Float4> floatInput = device.AllocateReadOnlyBuffer<Float4>(image);
        using ReadWriteBuffer<Float4> floatOutput = device.AllocateReadWriteBuffer<Float4>(size * size);
        using ReadOnlyBuffer<Half4> halfInput = device.AllocateReadOnlyBuffer<Half4>(halfImage);
        using ReadWriteBuffer<Half4> halfOutput = device.AllocateReadWriteBuffer<Half4>(size * size);

        double[] times = Medians(
            () => () => device.For(size, size, new TapBlurFloat4(floatInput, floatOutput, size, stride, radius)),
            () => () => device.For(size, size, new TapBlurHalf4(halfInput, halfOutput, size, stride, radius)),
            () => () => device.For(size, size, new TapBlurHalfStorage(halfInput, halfOutput, size, stride, radius)));

        Row("Blur, 289 taps per pixel", "1024 x 1024", times[0], times[1]);
        Row("The same blur: halves in memory, float arithmetic", "1024 x 1024", times[0], times[2]);
    }

    private static void Fold(GraphicsDevice device)
    {
        const int size = 2048, rounds = 64;
        using ReadWriteBuffer<Float4> floatOutput = device.AllocateReadWriteBuffer<Float4>(size * size);
        using ReadWriteBuffer<Half4> halfOutput = device.AllocateReadWriteBuffer<Half4>(size * size);

        double[] times = Medians(
            () => () => device.For(size, size, new FoldFloat4(floatOutput, size, rounds)),
            () => () => device.For(size, size, new FoldHalf4(halfOutput, size, rounds)));

        Row("Arithmetic only: 64 rounds of a folding map (nothing read)", "2048 x 2048", times[0], times[1]);
    }

    private static void Row(string kernel, string size, double floatMs, double halfMs) =>
        Console.WriteLine(Invariant($"| {kernel} | {size} | {floatMs:F2} ms | {halfMs:F2} ms | {floatMs / halfMs:F2}x as fast |"));

    /// <summary>
    /// The median time of each kernel. A kernel is given as a function that prepares one run (not
    /// timed) and returns the dispatch (timed). The kernels take turns, so that drift (thermal, other
    /// load) hits all of them the same way; two rounds before the timed ones compile and warm up.
    /// </summary>
    private static double[] Medians(params Func<Action>[] kernels)
    {
        double[][] samples = new double[kernels.Length][];

        for (int k = 0; k < kernels.Length; k++)
        {
            samples[k] = new double[Runs];
        }

        for (int run = -2; run < Runs; run++)
        {
            for (int k = 0; k < kernels.Length; k++)
            {
                Action dispatch = kernels[k]();
                long start = Stopwatch.GetTimestamp();
                dispatch();
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                if (run >= 0)
                {
                    samples[k][run] = elapsed;
                }
            }
        }

        double[] medians = new double[kernels.Length];

        for (int k = 0; k < kernels.Length; k++)
        {
            Array.Sort(samples[k]);
            medians[k] = samples[k][Runs / 2];
        }

        return medians;
    }
}
