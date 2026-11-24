using BenchmarkDotNet.Attributes;
using SharpCompute.Benchmarks.Workloads;

namespace SharpCompute.Benchmarks;

// Batching (ComputeContext) and async (ForAsync) against the synchronous v1 path, same machine and
// session. As elsewhere, buffers are allocated and pipelines compiled in GlobalSetup; each GPU number
// is the whole submission: encode, commit and wait (once per call, or once per batch).

/// <summary>Per-dispatch overhead: K dependent tiny dispatches, synchronous vs batched vs async.</summary>
[BenchmarkCategory("Batching and async")]
public class SmallDispatchBenchmarks
{
    private SmallDispatches _work = null!;

    [Params(1, 10, 100)]
    public int K { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _work = new SmallDispatches(K, GraphicsDevice.GetDefault());
        _work.Sync();
        _work.Batched();
        _work.Async();
    }

    [GlobalCleanup]
    public void Cleanup() => _work.Dispose();

    [Benchmark(Baseline = true, Description = "GPU K× device.For (before)")]
    public float Sync() => _work.Sync();

    [Benchmark(Description = "GPU one ComputeContext, K dispatches (after)")]
    public float Batched() => _work.Batched();

    [Benchmark(Description = "GPU K× ForAsync, one Task.WaitAll (after)")]
    public float Async() => _work.Async();
}

/// <summary>The two-pass 3840×2160 blur as two synchronous dispatches vs one context.</summary>
[BenchmarkCategory("Batching and async")]
public class BatchedBlurBenchmarks
{
    private GaussianBlur _blur = null!;

    [GlobalSetup]
    public void Setup()
    {
        _blur = new GaussianBlur(Sizes.BlurWidth, Sizes.BlurHeight, Sizes.BlurRadius, GraphicsDevice.GetDefault());
        _blur.Gpu();
        BatchingVerify.BlurBatched(_blur);
    }

    [GlobalCleanup]
    public void Cleanup() => _blur.Dispose();

    [Benchmark(Baseline = true, Description = "GPU blur, 2× device.For (before)")]
    public float Sync() => _blur.Gpu();

    [Benchmark(Description = "GPU blur, one ComputeContext (after)")]
    public float Batched() => BatchingVerify.BlurBatched(_blur);
}

/// <summary>8 frames of GPU render + CPU analysis: taking turns vs double-buffered with ForAsync.</summary>
[BenchmarkCategory("Batching and async")]
public class FrameOverlapBenchmarks
{
    private FrameOverlap _work = null!;

    [GlobalSetup]
    public void Setup()
    {
        _work = new FrameOverlap(GraphicsDevice.GetDefault());
        _work.Sync();
        _work.Async();
    }

    [GlobalCleanup]
    public void Cleanup() => _work.Dispose();

    [Benchmark(Baseline = true, Description = "GPU+CPU sync: For, then analyze (before)")]
    public long Sync() => _work.Sync();

    [Benchmark(Description = "GPU+CPU async: ForAsync N+1 while analyzing N (after)")]
    public long Async() => _work.Async();

    [Benchmark(Description = "GPU render only (8× For)")]
    public long GpuOnly() => _work.GpuOnly();

    [Benchmark(Description = "CPU analysis only (8 frames, 1 thread)")]
    public long CpuOnly() => _work.CpuOnly();
}
