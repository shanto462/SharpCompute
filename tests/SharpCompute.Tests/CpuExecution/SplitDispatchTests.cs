using System;
using System.Linq;
using System.Threading.Tasks;
using SharpCompute.Samples.Shaders;
using static SharpCompute.Tests.CpuExecution.CpuComparison;

namespace SharpCompute.Tests.CpuExecution;

/// <summary>
/// <c>ForSplit</c> / <c>ForEachSplit</c>: one grid, the leading part on the CPU and the rest on the GPU
/// at the same time. Every part must see the whole grid, so the results equal a single GPU dispatch.
/// </summary>
public class SplitDispatchTests
{
    public static TheoryData<double> Fractions => new(0.0, 0.01, 0.2, 0.5, 0.8, 0.99, 1.0);

    [MetalTheory]
    [MemberData(nameof(Fractions))]
    public void Builtins3D_EqualOneGpuDispatch(double fraction)
    {
        const int x = 9, y = 5, z = 7;
        const int count = x * y * z;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpuIds = device.AllocateReadWriteBuffer<int>(count * 12);
        using ReadWriteBuffer<Float3> gpuNormalized = device.AllocateReadWriteBuffer<Float3>(count);
        using ReadWriteBuffer<int> splitIds = device.AllocateReadWriteBuffer<int>(count * 12);
        using ReadWriteBuffer<Float3> splitNormalized = device.AllocateReadWriteBuffer<Float3>(count);

        device.For(x, y, z, new RecordBuiltins3D(gpuIds, gpuNormalized));
        SplitDispatchInfo info = device.ForSplit(x, y, z, new RecordBuiltins3D(splitIds, splitNormalized), fraction);

        Assert.Equal(gpuIds.ToArray(), splitIds.ToArray());
        Assert.Equal(Flatten(gpuNormalized.ToArray()), Flatten(splitNormalized.ToArray()));

        // Cut along z at a multiple of the threadgroup depth (ThreadGroupSize(4, 2, 2)).
        Assert.Equal(2, info.Axis);
        Assert.Equal(z, info.CpuExtent + info.GpuExtent);
        Assert.True(info.CpuExtent % 2 == 0 || info.CpuExtent == z, $"CPU extent {info.CpuExtent}");
        Assert.Equal(fraction, info.CpuFraction);
        Assert.Equal(info.CpuExtent > 0, info.CpuTime > TimeSpan.Zero);
        Assert.Equal(info.GpuExtent > 0, info.GpuTime > TimeSpan.Zero);
    }

    [MetalTheory]
    [MemberData(nameof(Fractions))]
    public void Builtins2D_EqualOneGpuDispatch(double fraction)
    {
        const int x = 37, y = 45;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpuIds = device.AllocateReadWriteBuffer<int>(x * y * 8);
        using ReadWriteBuffer<Float2> gpuNormalized = device.AllocateReadWriteBuffer<Float2>(x * y);
        using ReadWriteBuffer<int> splitIds = device.AllocateReadWriteBuffer<int>(x * y * 8);
        using ReadWriteBuffer<Float2> splitNormalized = device.AllocateReadWriteBuffer<Float2>(x * y);

        device.For(x, y, new RecordBuiltins2D(gpuIds, gpuNormalized));
        SplitDispatchInfo info = device.ForSplit(x, y, new RecordBuiltins2D(splitIds, splitNormalized), fraction);

        Assert.Equal(gpuIds.ToArray(), splitIds.ToArray());
        Assert.Equal(gpuNormalized.ToArray(), splitNormalized.ToArray());

        // Rows, in whole 16-row threadgroups.
        Assert.Equal(1, info.Axis);
        Assert.Equal(y, info.CpuExtent + info.GpuExtent);
        Assert.True(info.CpuExtent % 16 == 0 || info.CpuExtent == y, $"CPU extent {info.CpuExtent}");
    }

    [MetalTheory]
    [MemberData(nameof(Fractions))]
    public void Builtins1D_EqualOneGpuDispatch(double fraction)
    {
        const int x = 3000;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpuIds = device.AllocateReadWriteBuffer<int>(x * 4);
        using ReadWriteBuffer<float> gpuNormalized = device.AllocateReadWriteBuffer<float>(x);
        using ReadWriteBuffer<int> splitIds = device.AllocateReadWriteBuffer<int>(x * 4);
        using ReadWriteBuffer<float> splitNormalized = device.AllocateReadWriteBuffer<float>(x);

        device.For(x, new RecordBuiltins1D(gpuIds, gpuNormalized));
        SplitDispatchInfo info = device.ForSplit(x, new RecordBuiltins1D(splitIds, splitNormalized), fraction);

        Assert.Equal(gpuIds.ToArray(), splitIds.ToArray());
        Assert.Equal(gpuNormalized.ToArray(), splitNormalized.ToArray());
        Assert.Equal(0, info.Axis);
        Assert.Equal(x, info.CpuExtent + info.GpuExtent);
        Assert.True(info.CpuExtent % 256 == 0 || info.CpuExtent == x, $"CPU extent {info.CpuExtent}");
    }

    [MetalTheory]
    [MemberData(nameof(Fractions))]
    public void PixelShader_EqualsOneGpuDispatch(double fraction)
    {
        const int width = 70, height = 50;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpuIds = device.AllocateReadWriteBuffer<int>(width * height * 4);
        using ReadWriteBuffer<int> splitIds = device.AllocateReadWriteBuffer<int>(width * height * 4);
        using ReadWriteTexture2D<Rgba32> gpu = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteTexture2D<Rgba32> split = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        device.ForEach(gpu, new PixelWithIds(gpuIds));
        SplitDispatchInfo info = device.ForEachSplit(split, new PixelWithIds(splitIds), fraction);

        Assert.Equal(gpuIds.ToArray(), splitIds.ToArray());
        Assert.Equal(gpu.ToArray(), split.ToArray());
        Assert.Equal(1, info.Axis);
        Assert.Equal(height, info.CpuExtent + info.GpuExtent);

        // Pixel shaders are rank 2 with 16×16 groups; 50 rows is not a whole number of them.
        Assert.True(info.CpuExtent % 16 == 0 || info.CpuExtent == height, $"CPU extent {info.CpuExtent}");
    }

    [MetalTheory]
    [InlineData(100)]   // smaller than one 256-thread group
    [InlineData(1100)]  // 4.3 groups: rounding alone would leave 76 threads on the GPU
    [InlineData(4096)]
    public void FractionsZeroAndOne_AreExactlyGpuOnlyAndCpuOnly(int x)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpuIds = device.AllocateReadWriteBuffer<int>(x * 4);
        using ReadWriteBuffer<float> gpuNormalized = device.AllocateReadWriteBuffer<float>(x);
        using ReadWriteBuffer<int> ids = device.AllocateReadWriteBuffer<int>(x * 4);
        using ReadWriteBuffer<float> normalized = device.AllocateReadWriteBuffer<float>(x);

        device.For(x, new RecordBuiltins1D(gpuIds, gpuNormalized));

        SplitDispatchInfo cpuOnly = device.ForSplit(x, new RecordBuiltins1D(ids, normalized), 1.0);
        Assert.Equal((x, 0), (cpuOnly.CpuExtent, cpuOnly.GpuExtent));
        Assert.Equal(gpuIds.ToArray(), ids.ToArray());

        ids.AsSpan().Clear();
        SplitDispatchInfo gpuOnly = device.ForSplit(x, new RecordBuiltins1D(ids, normalized), 0.0);
        Assert.Equal((0, x), (gpuOnly.CpuExtent, gpuOnly.GpuExtent));
        Assert.Equal(gpuIds.ToArray(), ids.ToArray());
    }

    [MetalTheory]
    [MemberData(nameof(Fractions))]
    public void ExplicitFraction_CutsAtTheSamePlaceEveryTime(double fraction)
    {
        const int x = 37, y = 45;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ids = device.AllocateReadWriteBuffer<int>(x * y * 8);
        using ReadWriteBuffer<Float2> normalized = device.AllocateReadWriteBuffer<Float2>(x * y);

        SplitDispatchInfo first = device.ForSplit(x, y, new RecordBuiltins2D(ids, normalized), fraction);

        for (int i = 0; i < 5; i++)
        {
            SplitDispatchInfo again = device.ForSplit(x, y, new RecordBuiltins2D(ids, normalized), fraction);
            Assert.Equal((first.Axis, first.CpuExtent, first.GpuExtent), (again.Axis, again.CpuExtent, again.GpuExtent));
        }
    }

    [MetalFact]
    public void HelloWorld_Split_MatchesTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> gpu = device.AllocateReadWriteTexture2D<Rgba32>(320, 180);
        using ReadWriteTexture2D<Rgba32> split = device.AllocateReadWriteTexture2D<Rgba32>(320, 180);

        device.ForEach(gpu, new HelloWorld(2f));
        SplitDispatchInfo info = device.ForEachSplit(split, new HelloWorld(2f), 0.5);

        Assert.Equal(96, info.CpuExtent);
        AssertPixelsClose(gpu.ToArray(), split.ToArray(), maxStep: 1, outlierFraction: 0, "HelloWorld");
    }

    [MetalFact]
    public void AdaptiveShare_StartsAtTenPercent_ThenFollowsTheMeasuredSpeeds()
    {
        // A fresh device, so no other test has trained the estimate for this shader.
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        const int width = 512, height = 256, radius = 4;
        Random random = new(3);
        float[] source = Enumerable.Range(0, width * height).Select(_ => random.NextSingle()).ToArray();
        float[] weights = Enumerable.Repeat(1f / ((2 * radius) + 1), (2 * radius) + 1).ToArray();

        using ReadOnlyBuffer<float> sourceBuffer = device.AllocateReadOnlyBuffer<float>(source);
        using ReadOnlyBuffer<float> weightBuffer = device.AllocateReadOnlyBuffer<float>(weights);
        using ReadWriteBuffer<float> expected = device.AllocateReadWriteBuffer<float>(width * height);
        using ReadWriteBuffer<float> actual = device.AllocateReadWriteBuffer<float>(width * height);

        device.For(width, height, new BlurRows(sourceBuffer, expected, weightBuffer, width, radius));

        // Two split dispatches with the initial 10% (the first is warm-up), then two GPU-only ones to
        // measure the alternative; after that either mode may win on this machine.
        for (int i = 0; i < 8; i++)
        {
            actual.AsSpan().Clear();
            SplitDispatchInfo info = device.ForSplit(width, height, new BlurRows(sourceBuffer, actual, weightBuffer, width, radius));

            AssertClose(expected.ToArray(), actual.ToArray());
            Assert.Equal(height, info.CpuExtent + info.GpuExtent);
            Assert.Equal(0, info.CpuExtent % 16);

            if (i < 2)
            {
                Assert.Equal(0.1, info.CpuFraction);
                Assert.Equal(32, info.CpuExtent);
            }
            else if (i < 4)
            {
                Assert.Equal(0.0, info.CpuFraction);
                Assert.Equal(0, info.CpuExtent);
            }
            else
            {
                Assert.InRange(info.CpuFraction, 0.0, 1.0);
                Assert.NotEqual(0.1, info.CpuFraction);
            }
        }
    }

    [Fact]
    public void SplitBalance_DiscardsWarmUp_AndBalancesTheMeasuredTimePerThread()
    {
        SplitBalance balance = new();
        Assert.Equal(SplitBalance.InitialCpuFraction, balance.CpuFraction);

        // Warm-up: ignored however slow it was.
        balance.Record(cpuThreads: 1000, cpuSeconds: 10, gpuThreads: 9000, gpuSeconds: 0.001, wallSeconds: 10);
        Assert.Equal(SplitBalance.InitialCpuFraction, balance.CpuFraction);

        // CPU 4 µs/thread, GPU 1 µs/thread: the CPU should take 1/5 so both take as long.
        balance.Record(cpuThreads: 1000, cpuSeconds: 0.004, gpuThreads: 9000, gpuSeconds: 0.009, wallSeconds: 0.009);
        Assert.Equal(0.2, balance.CpuFraction, 12);

        // Then a moving average with weight ½: CPU (4 + 2) / 2 = 3 µs, GPU 1 µs -> 1/4.
        balance.Record(cpuThreads: 1000, cpuSeconds: 0.002, gpuThreads: 9000, gpuSeconds: 0.009, wallSeconds: 0.009);
        Assert.Equal(0.25, balance.CpuFraction, 12);

        // GPU-only dispatches do not move the share (the GPU is slower while the CPU works, so only
        // split dispatches measure the rates that matter for it).
        balance.Record(cpuThreads: 0, cpuSeconds: 0, gpuThreads: 10000, gpuSeconds: 0.001, wallSeconds: 0.001);
        balance.Record(cpuThreads: 0, cpuSeconds: 0, gpuThreads: 10000, gpuSeconds: 0.001, wallSeconds: 0.001);
        Assert.Equal(0.25, balance.CpuFraction, 12);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SplitBalance_UsesTheFasterMode_AndProbesTheOtherWithBackoff(bool splitIsFaster)
    {
        SplitBalance balance = new();
        double splitWall = splitIsFaster ? 0.001 : 0.002;
        const double gpuOnlyWall = 0.0015;

        // Explore: two split dispatches (the first is warm-up), then two GPU-only ones.
        Assert.Equal(SplitBalance.InitialCpuFraction, balance.NextCpuFraction());
        balance.Record(1000, 0.001, 9000, 0.001, splitWall);
        Assert.Equal(SplitBalance.InitialCpuFraction, balance.NextCpuFraction());
        balance.Record(1000, 0.001, 9000, 0.001, splitWall);
        Assert.Equal(0, balance.NextCpuFraction());
        balance.Record(0, 0, 10000, gpuOnlyWall, gpuOnlyWall);
        Assert.Equal(0, balance.NextCpuFraction());
        balance.Record(0, 0, 10000, gpuOnlyWall, gpuOnlyWall);

        // Then the winner, with the loser probed on the 4th, 8th, ... 64th dispatch, then every 64th.
        int[] probes = Enumerable.Range(1, 256)
            .Where(_ => (balance.NextCpuFraction() > 0) != splitIsFaster)
            .ToArray();

        Assert.Equal([4, 8, 16, 32, 64, 128, 192, 256], probes);
        // CPU 1 µs/thread, GPU 1/9 µs/thread -> 1/10.
        Assert.Equal(0.1, balance.CpuFraction, 12);
    }

    [MetalFact]
    public void AdaptiveDispatches_StayCorrect_WhateverSideTheEstimateFavours()
    {
        // The estimate depends on this machine's load, so the split itself is not asserted: only that
        // every adaptive dispatch (balanced, one-sided or probing) cuts at a threadgroup boundary and
        // produces exactly what one GPU dispatch does.
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<int> ids = device.AllocateReadWriteBuffer<int>(4096 * 4);
        using ReadWriteBuffer<float> normalized = device.AllocateReadWriteBuffer<float>(4096);
        using ReadWriteBuffer<int> expected = device.AllocateReadWriteBuffer<int>(4096 * 4);
        device.For(4096, new RecordBuiltins1D(expected, normalized));

        for (int i = 0; i < 12; i++)
        {
            ids.AsSpan().Clear();
            SplitDispatchInfo info = device.ForSplit(4096, new RecordBuiltins1D(ids, normalized));

            Assert.Equal(expected.ToArray(), ids.ToArray());
            Assert.Equal(0, info.CpuExtent % 256);
            Assert.Equal(4096, info.CpuExtent + info.GpuExtent);
        }
    }

    [MetalFact]
    public void GpuTime_IsMeasuredFromTheCommit_OnTheHostClock()
    {
        // GPUStartTime/GPUEndTime are compared with Stopwatch timestamps; if the clocks were unrelated
        // the split would fall back to the GPU's execution time, which this would not notice, so pin
        // the relation: a GPU part that runs concurrently with a CPU part can't take longer than the
        // whole dispatch.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> data = device.AllocateReadWriteBuffer<float>(1 << 16);

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        SplitDispatchInfo info = device.ForSplit(data.Length, new SlowWrite(data, 2000), 0.25);
        TimeSpan total = System.Diagnostics.Stopwatch.GetElapsedTime(start);

        Assert.True(info.GpuTime > TimeSpan.Zero && info.GpuTime <= total, $"GPU {info.GpuTime}, whole dispatch {total}.");
        Assert.True(info.CpuTime > TimeSpan.Zero && info.CpuTime <= total, $"CPU {info.CpuTime}, whole dispatch {total}.");
    }

    [MetalFact]
    public void FloatWorkloads_EqualOneGpuDispatch_UpToRounding()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int bodies = 1024;
        Random random = new(9);
        Float4[] positions = Enumerable.Range(0, bodies).Select(_ => new Float4(random.NextSingle(), random.NextSingle(), random.NextSingle(), 0.5f + random.NextSingle())).ToArray();

        using ReadOnlyBuffer<Float4> positionBuffer = device.AllocateReadOnlyBuffer<Float4>(positions);
        using ReadWriteBuffer<Float4> expected = device.AllocateReadWriteBuffer<Float4>(bodies);
        using ReadWriteBuffer<Float4> actual = device.AllocateReadWriteBuffer<Float4>(bodies);

        device.For(bodies, new Gravity(positionBuffer, expected, bodies, 0.01f));
        SplitDispatchInfo info = device.ForSplit(bodies, new Gravity(positionBuffer, actual, bodies, 0.01f), 0.4);

        // ThreadGroupSize(64): 409.6 threads round to 384.
        Assert.Equal(384, info.CpuExtent);
        AssertClose(Flatten(expected.ToArray()), Flatten(actual.ToArray()), relative: 1e-4f, absolute: 1e-4f);
    }

    [MetalFact]
    public void ShaderWithoutGridBuiltins_CanBeSplit()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> data = device.AllocateReadWriteBuffer<float>(4);

        SplitDispatchInfo info = device.ForSplit(1024, new NoBuiltins(data, 7f), 0.5);

        Assert.Equal(512, info.CpuExtent);
        Assert.Equal([7f, 0f, 0f, 0f], data.ToArray());
    }

    [MetalFact]
    public void HandWrittenDescriptors_CannotBeSplit()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1f, 2f]);
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(2, 2);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForSplit(2, new Shaders.MultiplyByTwo(buffer, 2f)));
        Assert.Contains("cannot run a partial grid", exception.Message);
        Assert.Throws<NotSupportedException>(() => device.ForEachSplit(texture, new Shaders.PixelCoordinates(stats, 0.5f)));
        Assert.Equal([1f, 2f], buffer.ToArray());
    }

    [MetalFact]
    public void InvalidArguments_FailBeforeAnythingRuns()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ids = device.AllocateReadWriteBuffer<int>(64 * 4);
        using ReadWriteBuffer<float> normalized = device.AllocateReadWriteBuffer<float>(64);
        RecordBuiltins1D shader = new(ids, normalized);

        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForSplit(64, shader, -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForSplit(64, shader, 1.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForSplit(64, shader, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForSplit(-1, shader));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForSplit(1, -1, shader));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForSplit(1, 1, -1, shader));
        Assert.Equal(default(SplitDispatchInfo), device.ForSplit(0, shader));
        Assert.Equal(default(SplitDispatchInfo), device.ForSplit(64, 0, 1, shader, 0.5));

        Assert.Throws<ArgumentNullException>(() => device.ForSplit(64, default(RecordBuiltins1D)));

        using GraphicsDevice other = GraphicsDevice.CreateStandInForAnotherDevice();
        using ReadWriteBuffer<int> foreign = other.AllocateReadWriteBuffer<int>(64 * 4);
        Assert.Throws<ArgumentException>(() => device.ForSplit(64, new RecordBuiltins1D(foreign, normalized), 0.5));

        ReadWriteBuffer<float> disposed = device.AllocateReadWriteBuffer<float>(64);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => device.ForSplit(64, new RecordBuiltins1D(ids, disposed), 0.5));

        using ReadWriteTexture2D<Rgba32> foreignTexture = other.AllocateReadWriteTexture2D<Rgba32>(4, 4);
        Assert.Throws<ArgumentException>(() => device.ForEachSplit(foreignTexture, new HelloWorld(0f)));
        Assert.Throws<ArgumentNullException>(() => device.ForEachSplit(null!, new HelloWorld(0f)));

        // Nothing ran: no part of any of those dispatches wrote a value.
        Assert.All(ids.ToArray(), static v => Assert.Equal(0, v));
        Assert.All(foreign.ToArray(), static v => Assert.Equal(0, v));
    }

    [MetalFact]
    public void DisposedDevice_Throws_AndDisposingAfterASplitReleasesBothPipelines()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<int> ids = device.AllocateReadWriteBuffer<int>(256 * 4);
        using ReadWriteBuffer<float> normalized = device.AllocateReadWriteBuffer<float>(256);
        device.For(256, new RecordBuiltins1D(ids, normalized));
        device.ForSplit(256, new RecordBuiltins1D(ids, normalized), 0.5);

        device.Dispose();

        Assert.Throws<ObjectDisposedException>(() => device.ForSplit(256, new RecordBuiltins1D(ids, normalized)));
        Assert.Throws<ObjectDisposedException>(() => device.ForSplit(0, new RecordBuiltins1D(ids, normalized)));
        Assert.Equal(255, ids[(255 * 4) + 0]);
    }

    [MetalFact]
    public void AnExceptionInTheCpuPart_SurfacesAfterTheGpuPartCompletes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int count = 4096;
        using ReadWriteBuffer<int> data = device.AllocateReadWriteBuffer<int>(count);

        // Thread 0 (always in the CPU's leading part) indexes -1.
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForSplit(count, new FirstThreadOutOfRange(data), 0.5));

        // The GPU's part was waited for and wrote its elements; the device is still usable.
        int[] values = data.ToArray();
        Assert.Equal(Enumerable.Range(2048, 2048), values[2048..]);
        Assert.Throws<InvalidOperationException>(() => ThreadIds.X);

        using ReadWriteBuffer<float> after = device.AllocateReadWriteBuffer<float>(count);
        device.For(count, new SlowWrite(after, 1));
        Assert.Equal(count - 1, after[count - 1]);
    }

    [MetalFact]
    public void ConcurrentSplits_AreIndependent()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        Parallel.For(0, 8, i =>
        {
            int x = 37 + (i * 13), y = 23 + (i * 7);
            using ReadWriteBuffer<int> ids = device.AllocateReadWriteBuffer<int>(x * y * 8);
            using ReadWriteBuffer<Float2> normalized = device.AllocateReadWriteBuffer<Float2>(x * y);
            using ReadWriteBuffer<int> expectedIds = device.AllocateReadWriteBuffer<int>(x * y * 8);
            using ReadWriteBuffer<Float2> expectedNormalized = device.AllocateReadWriteBuffer<Float2>(x * y);

            device.For(x, y, new RecordBuiltins2D(expectedIds, expectedNormalized));

            if (i % 2 == 0)
            {
                device.ForSplit(x, y, new RecordBuiltins2D(ids, normalized), 0.5);
            }
            else
            {
                device.ForSplit(x, y, new RecordBuiltins2D(ids, normalized));
            }

            Assert.Equal(expectedIds.ToArray(), ids.ToArray());
            Assert.Equal(expectedNormalized.ToArray(), normalized.ToArray());
        });
    }
}
