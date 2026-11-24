using BenchmarkDotNet.Attributes;
using SharpCompute.Benchmarks.Shaders;
using SharpCompute.Benchmarks.Workloads;

namespace SharpCompute.Benchmarks;

// CPU execution of generated shaders (ForCpu) and CPU+GPU split dispatches (ForSplit), against the
// existing GPU-only path (For) on the same shader, buffers and sizes. Every variant writes the same
// shared-memory output buffer and returns one element of it.
//
// What each timing includes:
//   GPU (For)            encode + commit + waitUntilCompleted, as in the other benchmarks.
//   CPU shader (ForCpu)  the shader's Execute run as C# for every thread with Parallel.For: argument
//                        validation/leases, the Parallel.For itself, bounds-checked buffer access.
//   Split (ForSplit)     both parts and their synchronization: validation/leases, choosing the share,
//                        encode + commit of the GPU part, the CPU part on the calling thread and the
//                        pool while the GPU runs, waitUntilCompleted, updating the running estimate.
//                        "adaptive" uses the device's per-shader estimate, already converged during
//                        BenchmarkDotNet's warmup; "fixed" passes an explicit cpuFraction.
//   Hand-written CPU     the main benchmarks' CPU twins: Parallel.For with the same scalar arithmetic
//                        (what ForCpu's overhead is measured against), and the fastest variant
//                        (Parallel.For + SIMD): what the CPU could contribute if its part were
//                        hand-tuned rather than the shader's scalar C#.
//
// Report.cs renders these in their own section (workloads prefixed "CPU+GPU split:"), not in the
// GPU-vs-CPU headline.

[BenchmarkCategory(SplitCategory.Prefix + "Mandelbrot")]
public class SplitMandelbrotBenchmarks
{
    private Mandelbrot _mandelbrot = null!;

    [GlobalSetup]
    public void Setup()
    {
        _mandelbrot = new Mandelbrot(1920, 1080, GraphicsDevice.GetDefault());
        _mandelbrot.Gpu();
        GpuCpuSplitAdaptive();
        GpuCpuSplitFixed();
    }

    [GlobalCleanup]
    public void Cleanup() => _mandelbrot.Dispose();

    [Benchmark(Baseline = true, Description = "GPU generated shader (For)")]
    public uint Gpu() => _mandelbrot.Gpu();

    [Benchmark(Description = "CPU generated shader (ForCpu)")]
    public uint CpuShader()
    {
        _mandelbrot.Device.ForCpu(_mandelbrot.Width, _mandelbrot.Height, _mandelbrot.Shader());
        return Center();
    }

    [Benchmark(Description = "Split CPU+GPU (ForSplit, adaptive)")]
    public uint GpuCpuSplitAdaptive()
    {
        _mandelbrot.Device.ForSplit(_mandelbrot.Width, _mandelbrot.Height, _mandelbrot.Shader());
        return Center();
    }

    [Benchmark(Description = "Split CPU+GPU (ForSplit, cpuFraction 0.05)")]
    public uint GpuCpuSplitFixed()
    {
        _mandelbrot.Device.ForSplit(_mandelbrot.Width, _mandelbrot.Height, _mandelbrot.Shader(), 0.05);
        return Center();
    }

    [Benchmark(Description = "Hand-written CPU (Parallel.For, scalar)")]
    public uint CpuParallel() => _mandelbrot.CpuParallel();

    [Benchmark(Description = "Best hand-written CPU (Parallel.For + SIMD)")]
    public uint CpuParallelSimd() => _mandelbrot.CpuParallelSimd();

    private uint Center() => _mandelbrot.GpuPixels[(_mandelbrot.Height / 2 * _mandelbrot.Width) + (_mandelbrot.Width / 2)];
}

[BenchmarkCategory(SplitCategory.Prefix + "Gaussian blur")]
public class SplitBlurBenchmarks
{
    private GaussianBlur _blur = null!;

    [GlobalSetup]
    public void Setup()
    {
        _blur = new GaussianBlur(Sizes.BlurWidth, Sizes.BlurHeight, Sizes.BlurRadius, GraphicsDevice.GetDefault());
        _blur.Gpu();
        GpuCpuSplitAdaptive();
        GpuCpuSplitFixed();
    }

    [GlobalCleanup]
    public void Cleanup() => _blur.Dispose();

    [Benchmark(Baseline = true, Description = "GPU generated shader (For, 2 dispatches)")]
    public float Gpu() => _blur.Gpu();

    [Benchmark(Description = "CPU generated shader (ForCpu, 2 dispatches)")]
    public float CpuShader()
    {
        _blur.Device.ForCpu(_blur.Width, _blur.Height, Horizontal());
        _blur.Device.ForCpu(_blur.Width, _blur.Height, Vertical());
        return Center();
    }

    [Benchmark(Description = "Split CPU+GPU (ForSplit, adaptive, 2 dispatches)")]
    public float GpuCpuSplitAdaptive()
    {
        _blur.Device.ForSplit(_blur.Width, _blur.Height, Horizontal());
        _blur.Device.ForSplit(_blur.Width, _blur.Height, Vertical());
        return Center();
    }

    [Benchmark(Description = "Split CPU+GPU (ForSplit, cpuFraction 0.05, 2 dispatches)")]
    public float GpuCpuSplitFixed()
    {
        _blur.Device.ForSplit(_blur.Width, _blur.Height, Horizontal(), 0.05);
        _blur.Device.ForSplit(_blur.Width, _blur.Height, Vertical(), 0.05);
        return Center();
    }

    [Benchmark(Description = "Hand-written CPU (Parallel.For, scalar)")]
    public float CpuParallel() => _blur.CpuParallel();

    [Benchmark(Description = "Best hand-written CPU (Parallel.For + SIMD)")]
    public float CpuParallelSimd() => _blur.CpuParallelSimd();

    private BlurHorizontal Horizontal() => new(_blur.GpuSource, _blur.GpuTemp, _blur.GpuWeights, _blur.Width, _blur.Radius);

    private BlurVertical Vertical() => new(_blur.GpuTemp, _blur.GpuOutput, _blur.GpuWeights, _blur.Width, _blur.Height, _blur.Radius);

    private float Center() => _blur.GpuOutput[_blur.GpuOutput.Length / 2];
}

[BenchmarkCategory(SplitCategory.Prefix + "N-body")]
public class SplitNBodyBenchmarks
{
    private NBody _nbody = null!;

    [GlobalSetup]
    public void Setup()
    {
        _nbody = new NBody(16384, GraphicsDevice.GetDefault());
        _nbody.Gpu();
        GpuCpuSplitAdaptive();
        GpuCpuSplitFixed();
    }

    [GlobalCleanup]
    public void Cleanup() => _nbody.Dispose();

    [Benchmark(Baseline = true, Description = "GPU generated shader (For)")]
    public float Gpu() => _nbody.Gpu();

    [Benchmark(Description = "CPU generated shader (ForCpu)")]
    public float CpuShader()
    {
        _nbody.Device.ForCpu(_nbody.Count, Step());
        return Center();
    }

    [Benchmark(Description = "Split CPU+GPU (ForSplit, adaptive)")]
    public float GpuCpuSplitAdaptive()
    {
        _nbody.Device.ForSplit(_nbody.Count, Step());
        return Center();
    }

    [Benchmark(Description = "Split CPU+GPU (ForSplit, cpuFraction 0.05)")]
    public float GpuCpuSplitFixed()
    {
        _nbody.Device.ForSplit(_nbody.Count, Step(), 0.05);
        return Center();
    }

    [Benchmark(Description = "Hand-written CPU (Parallel.For, scalar)")]
    public float CpuParallel() => _nbody.CpuParallel();

    [Benchmark(Description = "Best hand-written CPU (Parallel.For + SIMD)")]
    public float CpuParallelSimd() => _nbody.CpuParallelSimd();

    private NBodyStep Step() => new(_nbody.GpuPositions, _nbody.GpuVelocities, _nbody.GpuNextPositions, _nbody.GpuNextVelocities, _nbody.Count, NBody.Dt, NBody.SofteningSquared);

    private float Center() => _nbody.GpuNextPositions[_nbody.Count / 2].X;
}

/// <summary>The category prefix that keeps the split benchmarks out of the GPU-vs-CPU tables of the report.</summary>
public static class SplitCategory
{
    public const string Prefix = "CPU+GPU split: ";
}
