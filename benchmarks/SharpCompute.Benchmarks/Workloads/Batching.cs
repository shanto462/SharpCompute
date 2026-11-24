using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary>
/// <c>K</c> tiny SAXPY dispatches (4,096 floats each, in place on one buffer, so each depends on the
/// previous one) issued three ways: K synchronous <c>For</c> calls, one <see cref="ComputeContext"/>,
/// or K <c>ForAsync</c> calls committed back to back and awaited together.
/// </summary>
public sealed class SmallDispatches : IDisposable
{
    public const int Length = 4096;
    public const float A = 0.5f;

    private readonly float[] _y0;

    public SmallDispatches(int dispatches, GraphicsDevice device)
    {
        Dispatches = dispatches;
        Device = device;

        Random random = new(11);
        float[] x = new float[Length];
        _y0 = new float[Length];

        for (int i = 0; i < Length; i++)
        {
            x[i] = random.NextSingle();
            _y0[i] = random.NextSingle();
        }

        X = device.AllocateReadOnlyBuffer<float>(x);
        Y = device.AllocateReadWriteBuffer<float>(_y0);
    }

    public int Dispatches { get; }

    public GraphicsDevice Device { get; }

    public ReadOnlyBuffer<float> X { get; }

    public ReadWriteBuffer<float> Y { get; }

    public void Reset() => Y.CopyFrom(_y0);

    /// <summary>Before: one command buffer, commit and wait per dispatch.</summary>
    public float Sync()
    {
        SaxpyGenerated shader = new(X, Y, A);

        for (int i = 0; i < Dispatches; i++)
        {
            Device.For(Length, shader);
        }

        return Y[0];
    }

    /// <summary>After: every dispatch in one command buffer and encoder; one commit, one wait.</summary>
    public float Batched()
    {
        SaxpyGenerated shader = new(X, Y, A);

        using (ComputeContext context = Device.CreateComputeContext())
        {
            for (int i = 0; i < Dispatches; i++)
            {
                context.For(Length, shader);
            }
        }

        return Y[0];
    }

    /// <summary>K command buffers committed without waiting in between, then one wait for all of them.</summary>
    public float Async()
    {
        SaxpyGenerated shader = new(X, Y, A);
        Task[] tasks = new Task[Dispatches];

        for (int i = 0; i < Dispatches; i++)
        {
            tasks[i] = Device.ForAsync(Length, shader);
        }

        Task.WaitAll(tasks);
        return Y[0];
    }

    public void Dispose()
    {
        X.Dispose();
        Y.Dispose();
    }
}

/// <summary>
/// The CPU + GPU overlap workload: <see cref="Frames"/> frames, each a 1920×1080 Mandelbrot render on
/// the GPU (the benchmark's <see cref="MandelbrotIterations"/>) followed by CPU analysis of the frame
/// (an iteration-count histogram on one thread). Synchronous: render, wait, analyze. Asynchronous,
/// double-buffered: the GPU renders frame N+1 into the other buffer while the CPU analyzes frame N.
/// </summary>
public sealed class FrameOverlap : IDisposable
{
    public const int Frames = 8;
    public const int Width = 1920;
    public const int Height = 1080;
    public const int Bins = 1024;

    private readonly ReadWriteBuffer<uint>[] _frames;
    private readonly int[] _histogram = new int[Bins];
    private ReadWriteBuffer<uint>[]? _rendered;

    public FrameOverlap(GraphicsDevice device)
    {
        Device = device;
        _frames = [device.AllocateReadWriteBuffer<uint>(Width * Height), device.AllocateReadWriteBuffer<uint>(Width * Height)];
    }

    public GraphicsDevice Device { get; }

    /// <summary>GPU only: the frames rendered back to back with <c>For</c>.</summary>
    public long GpuOnly()
    {
        for (int k = 0; k < Frames; k++)
        {
            Device.For(Width, Height, Shader(_frames[0], k));
        }

        return _frames[0][0];
    }

    /// <summary>
    /// CPU only: the analysis of frames 0 … <see cref="Frames"/> − 1, rendered once beforehand, so it
    /// is exactly the CPU work of <see cref="Sync"/> and <see cref="Async"/> (the histogram's cost
    /// depends on the data).
    /// </summary>
    public long CpuOnly()
    {
        ReadWriteBuffer<uint>[] rendered = _rendered ??= RenderAll();
        long checksum = 0;

        for (int k = 0; k < Frames; k++)
        {
            checksum = Mix(checksum, Analyze(rendered[k]));
        }

        return checksum;
    }

    private ReadWriteBuffer<uint>[] RenderAll()
    {
        ReadWriteBuffer<uint>[] frames = new ReadWriteBuffer<uint>[Frames];

        for (int k = 0; k < Frames; k++)
        {
            frames[k] = Device.AllocateReadWriteBuffer<uint>(Width * Height);
            Device.For(Width, Height, Shader(frames[k], k));
        }

        return frames;
    }

    /// <summary>Before: render frame k (<c>For</c>, waits), then analyze it. The CPU and GPU take turns.</summary>
    public long Sync()
    {
        long checksum = 0;

        for (int k = 0; k < Frames; k++)
        {
            Device.For(Width, Height, Shader(_frames[0], k));
            checksum = Mix(checksum, Analyze(_frames[0]));
        }

        return checksum;
    }

    /// <summary>After: <c>ForAsync</c> frame k + 1 into the other buffer, then analyze frame k while it renders.</summary>
    public long Async()
    {
        long checksum = 0;
        Task rendering = Device.ForAsync(Width, Height, Shader(_frames[0], 0));

        for (int k = 0; k < Frames; k++)
        {
            rendering.Wait();

            rendering = k + 1 < Frames
                ? Device.ForAsync(Width, Height, Shader(_frames[(k + 1) % 2], k + 1))
                : Task.CompletedTask;

            checksum = Mix(checksum, Analyze(_frames[k % 2]));
        }

        rendering.Wait();
        return checksum;
    }

    public void Dispose()
    {
        _frames[0].Dispose();
        _frames[1].Dispose();

        foreach (ReadWriteBuffer<uint> frame in _rendered ?? [])
        {
            frame.Dispose();
        }
    }

    /// <summary>Frame k: the full set, zooming in by 10 % per frame so each frame is different work.</summary>
    private static MandelbrotIterations Shader(ReadWriteBuffer<uint> target, int k) =>
        new(target, Width, Height, Mandelbrot.MaxIterations, Mandelbrot.Center, 3f / Height * MathF.Pow(0.9f, k));

    /// <summary>Histogram of the iteration counts, then a weighted sum of it (so the work cannot be skipped).</summary>
    private long Analyze(ReadWriteBuffer<uint> frame)
    {
        Array.Clear(_histogram);

        foreach (uint pixel in frame.AsSpan())
        {
            _histogram[Math.Min(Mandelbrot.Iterations(pixel), Bins - 1)]++;
        }

        GC.KeepAlive(frame);
        long sum = 0;

        for (int i = 0; i < Bins; i++)
        {
            sum += (long)_histogram[i] * (i + 1);
        }

        return sum;
    }

    private static long Mix(long hash, long value) => (hash ^ value) * 0x100000001B3L;
}

/// <summary>The --verify checks for the batching and async variants: each must match its synchronous twin exactly.</summary>
public static class BatchingVerify
{
    public const string Workload = "Batching";

    public static IEnumerable<Check> Run(GraphicsDevice device)
    {
        // K = 10 chained dispatches: the batch and the back-to-back async dispatches must run in order.
        using (SmallDispatches small = new(10, device))
        {
            small.Sync();
            float[] sync = small.Y.ToArray();

            small.Reset();
            small.Batched();
            yield return Identical("10 × SAXPY 4,096", "GPU one ComputeContext", "GPU 10× device.For", small.Y.ToArray(), sync);

            small.Reset();
            small.Async();
            yield return Identical("10 × SAXPY 4,096", "GPU 10× ForAsync", "GPU 10× device.For", small.Y.ToArray(), sync);
        }

        using (GaussianBlur blur = new(Sizes.BlurWidth, Sizes.BlurHeight, Sizes.BlurRadius, device))
        {
            blur.Gpu();
            float[] sync = blur.GpuOutput.ToArray();

            blur.GpuOutput.AsSpan().Clear();
            blur.GpuTemp.AsSpan().Clear();
            BlurBatched(blur);
            yield return Identical($"{Sizes.BlurWidth}x{Sizes.BlurHeight}", "GPU blur, one ComputeContext", "GPU blur, 2× device.For", blur.GpuOutput.ToArray(), sync);
        }

        using (FrameOverlap overlap = new(device))
        {
            long sync = overlap.Sync();
            long async = overlap.Async();
            long cpuOnly = overlap.CpuOnly();
            yield return new Check(Workload, $"{FrameOverlap.Frames} frames", "GPU+CPU async (double-buffered)", "GPU+CPU sync", "checksum differs",
                sync == async ? 0 : 1, 0, string.Create(CultureInfo.InvariantCulture, $"frame checksums {sync:X16} vs {async:X16}"));
            yield return new Check(Workload, $"{FrameOverlap.Frames} frames", "CPU analysis only (pre-rendered)", "GPU+CPU sync", "checksum differs",
                sync == cpuOnly ? 0 : 1, 0, "the CPU-only row analyzes the same 8 frames");
        }
    }

    /// <summary>The two blur passes recorded into one context: one commit, one wait.</summary>
    public static float BlurBatched(GaussianBlur blur)
    {
        using (ComputeContext context = blur.Device.CreateComputeContext())
        {
            context.For(blur.Width, blur.Height, new BlurHorizontal(blur.GpuSource, blur.GpuTemp, blur.GpuWeights, blur.Width, blur.Radius));
            context.For(blur.Width, blur.Height, new BlurVertical(blur.GpuTemp, blur.GpuOutput, blur.GpuWeights, blur.Width, blur.Height, blur.Radius));
        }

        return blur.GpuOutput[blur.GpuOutput.Length / 2];
    }

    private static Check Identical(string size, string variant, string reference, float[] actual, float[] expected)
    {
        int differing = 0;

        for (int i = 0; i < expected.Length; i++)
        {
            differing += BitConverter.SingleToInt32Bits(actual[i]) != BitConverter.SingleToInt32Bits(expected[i]) ? 1 : 0;
        }

        return new Check(Workload, size, variant, reference, "elements not bit-identical", differing, 0,
            string.Create(CultureInfo.InvariantCulture, $"{differing} of {expected.Length} differ (same kernels, same order: exact match required)"));
    }
}
