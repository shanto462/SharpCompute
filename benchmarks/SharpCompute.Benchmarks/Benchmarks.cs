using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using SharpCompute.Benchmarks.Shaders;
using SharpCompute.Benchmarks.Workloads;

namespace SharpCompute.Benchmarks;

// Every benchmark returns a value read from its output, so no variant's work can be dropped as dead
// code. Inputs and GPU buffers are allocated and the GPU pipelines compiled in GlobalSetup, so a GPU
// measurement is: encode + commit + waitUntilCompleted (+ reading one element back from shared
// memory). Method descriptions start with the variant's kind; Report.cs keys off them.

[BenchmarkCategory("SAXPY")]
public class SaxpyBenchmarks
{
    private Saxpy _saxpy = null!;

    public static IEnumerable<int> Lengths => Sizes.Saxpy;

    [ParamsSource(nameof(Lengths))]
    public int N { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        ShaderInfo.CheckGeneratorRan();
        _saxpy = new Saxpy(N, GraphicsDevice.GetDefault());
        _saxpy.GpuGenerated();
        _saxpy.GpuHandWritten();
    }

    [GlobalCleanup]
    public void Cleanup() => _saxpy.Dispose();

    [Benchmark(Baseline = true, Description = "CPU scalar (1 thread)")]
    public float CpuScalar() => _saxpy.CpuScalar();

    [Benchmark(Description = "CPU SIMD (1 thread, TensorPrimitives)")]
    public float CpuSimd() => _saxpy.CpuSimd();

    [Benchmark(Description = "CPU Parallel.For + SIMD (64K chunks)")]
    public float CpuParallelSimd() => _saxpy.CpuParallelSimd();

    [Benchmark(Description = "GPU generated shader")]
    public float GpuGenerated() => _saxpy.GpuGenerated();

    [Benchmark(Description = "GPU hand-written MSL")]
    public float GpuHandWritten() => _saxpy.GpuHandWritten();
}

[BenchmarkCategory("Mandelbrot")]
public class MandelbrotBenchmarks
{
    private Mandelbrot _mandelbrot = null!;

    public static IEnumerable<string> Resolutions => Sizes.Mandelbrot;

    [ParamsSource(nameof(Resolutions))]
    public string Size { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        (int width, int height) = Sizes.Parse(Size);
        _mandelbrot = new Mandelbrot(width, height, GraphicsDevice.GetDefault());
        _mandelbrot.Gpu();
    }

    [GlobalCleanup]
    public void Cleanup() => _mandelbrot.Dispose();

    [Benchmark(Baseline = true, Description = "CPU scalar (1 thread)")]
    public uint CpuScalar() => _mandelbrot.CpuScalar();

    [Benchmark(Description = "CPU Parallel.For (rows, scalar)")]
    public uint CpuParallel() => _mandelbrot.CpuParallel();

    [Benchmark(Description = "CPU Parallel.For + SIMD (rows, Vector128, 4 pixels at a time)")]
    public uint CpuParallelSimd() => _mandelbrot.CpuParallelSimd();

    [Benchmark(Description = "GPU generated shader")]
    public uint Gpu() => _mandelbrot.Gpu();
}

[BenchmarkCategory("Gaussian blur")]
public class BlurBenchmarks
{
    private GaussianBlur _blur = null!;

    [GlobalSetup]
    public void Setup()
    {
        _blur = new GaussianBlur(Sizes.BlurWidth, Sizes.BlurHeight, Sizes.BlurRadius, GraphicsDevice.GetDefault());
        _blur.Gpu();
    }

    [GlobalCleanup]
    public void Cleanup() => _blur.Dispose();

    [Benchmark(Baseline = true, Description = "CPU scalar (1 thread)")]
    public float CpuScalar() => _blur.CpuScalar();

    [Benchmark(Description = "CPU Parallel.For (rows, scalar)")]
    public float CpuParallel() => _blur.CpuParallel();

    [Benchmark(Description = "CPU Parallel.For + SIMD (rows, TensorPrimitives)")]
    public float CpuParallelSimd() => _blur.CpuParallelSimd();

    [Benchmark(Description = "GPU generated shader (2 dispatches)")]
    public float Gpu() => _blur.Gpu();
}

[BenchmarkCategory("Matrix multiply")]
public class MatMulBenchmarks
{
    private MatMul _matmul = null!;

    [GlobalSetup]
    public void Setup()
    {
        _matmul = new MatMul(Sizes.MatMul, GraphicsDevice.GetDefault());
        _matmul.Gpu();
    }

    [GlobalCleanup]
    public void Cleanup() => _matmul.Dispose();

    [Benchmark(Baseline = true, Description = "CPU Parallel.For (rows, scalar dot, Bᵀ)")]
    public float CpuParallel() => _matmul.CpuParallel();

    [Benchmark(Description = "CPU Parallel.For + SIMD (rows, TensorPrimitives.Dot, Bᵀ)")]
    public float CpuParallelSimd() => _matmul.CpuParallelSimd();

    [Benchmark(Description = "GPU generated shader")]
    public float Gpu() => _matmul.Gpu();
}

[BenchmarkCategory("N-body")]
public class NBodyBenchmarks
{
    private NBody _nbody = null!;

    public static IEnumerable<int> Counts => Sizes.NBody;

    [ParamsSource(nameof(Counts))]
    public int Bodies { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _nbody = new NBody(Bodies, GraphicsDevice.GetDefault());
        _nbody.Gpu();
    }

    [GlobalCleanup]
    public void Cleanup() => _nbody.Dispose();

    [Benchmark(Baseline = true, Description = "CPU Parallel.For (bodies, scalar)")]
    public float CpuParallel() => _nbody.CpuParallel();

    [Benchmark(Description = "CPU Parallel.For + SIMD (bodies, Vector<float> SoA)")]
    public float CpuParallelSimd() => _nbody.CpuParallelSimd();

    [Benchmark(Description = "GPU generated shader")]
    public float Gpu() => _nbody.Gpu();
}
