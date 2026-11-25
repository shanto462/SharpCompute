using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static SharpCompute.Tests.CpuExecution.CpuComparison;
using static SharpCompute.Tests.Threadgroups.GroupSharedTests;

namespace SharpCompute.Tests.Threadgroups;

/// <summary>
/// The CPU's group executor (<see cref="CpuGroupDispatcher"/>): shaders with group-shared memory or
/// barriers run through <c>ForCpu</c> and <c>ForSplit</c> and must compute what the GPU computes,
/// finish whatever their threads do at barriers, and report a thread's exception.
/// </summary>
public class CpuGroupExecutionTests
{
    /// <summary>Fails instead of hanging the test run if the executor deadlocks.</summary>
    private static void WithinTimeout(Action action, int seconds = 60)
    {
        Task task = Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        if (!task.Wait(TimeSpan.FromSeconds(seconds)))
        {
            Assert.Fail($"The CPU dispatch did not finish within {seconds} s: the group executor is deadlocked.");
        }

        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void GeneratedDescriptors_ReportThreadgroupMemoryAndBarriers()
    {
        static (bool Uses, int Bytes) Read<T>()
            where T : struct, IComputeShaderDescriptor<T> => (T.UsesThreadgroupMemory, T.ThreadgroupMemoryLength);

        Assert.Equal((true, 1024), Read<BlockSum>());
        Assert.Equal((true, 2048), Read<TiledMatMul>());
        Assert.Equal((true, (32 * 12) + (32 * 16)), Read<NeighbourExchange>());

        // Barriers alone make the threads cooperate; built-ins alone do not.
        Assert.Equal((true, 0), Read<DeviceBarrier>());
        Assert.Equal((false, 0), Read<RecordGroupBuiltins3D>());

        // The interface defaults, for hand-written descriptors.
        Assert.Equal((false, 0), Read<UnreportedBarrier>());
    }

    [MetalTheory]
    [InlineData(256)]
    [InlineData(1)]
    [InlineData(257)]
    [InlineData(1000)]
    public void TreeReduction_MatchesTheGpu(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] input = Floats(count, seed: count);

        float[] gpu = RunBlockSum(device, input, (threads, shader) => device.For(threads, shader));
        float[] cpu = null!;
        WithinTimeout(() => cpu = RunBlockSum(device, input, (threads, shader) => device.ForCpu(threads, shader)));

        Assert.Equal(gpu, cpu);
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(300)]
    public void PartialLastGroup_MatchesTheGpu(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] input = Ints(count, seed: count);
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>((count + 63) / 64);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>((count + 63) / 64);

        device.For(count, new PartialGroupSum(source, gpu));
        WithinTimeout(() => device.ForCpu(count, new PartialGroupSum(source, cpu)));

        Assert.Equal(input.Chunk(64).Select(static block => block.Sum()).ToArray(), cpu.ToArray());
        Assert.Equal(gpu.ToArray(), cpu.ToArray());
    }

    [MetalTheory]
    [InlineData(256)]
    [InlineData(255)]
    [InlineData(700)]
    public void BlellochScan_MatchesTheGpu(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] input = Ints(count, seed: count);

        (int[] gpuScan, int[] gpuTotals) = RunBlockScan(device, input, (threads, shader) => device.For(threads, shader));
        (int[] Scan, int[] Totals) cpu = default;
        WithinTimeout(() => cpu = RunBlockScan(device, input, (threads, shader) => device.ForCpu(threads, shader)));

        Assert.Equal(gpuScan, cpu.Scan);
        Assert.Equal(gpuTotals, cpu.Totals);
        Assert.Equal(BlockScans(input).Scan, cpu.Scan);
    }

    [MetalTheory]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(40)]
    public void TiledMatrixProduct_MatchesTheGpu(int n)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] a = Floats(n * n, seed: n);
        float[] b = Floats(n * n, seed: n + 1);

        float[] gpu = RunMatMul(device, a, b, n, (size, shader) => device.For(size, size, shader));
        float[] cpu = null!;
        WithinTimeout(() => cpu = RunMatMul(device, a, b, n, (size, shader) => device.ForCpu(size, size, shader)));

        // The GPU contracts the multiply-adds; the CPU does not.
        AssertClose(gpu, cpu, relative: 1e-4f, absolute: 1e-4f);
        AssertClose(Multiply(a, b, n), cpu, relative: 1e-4f, absolute: 1e-4f);
    }

    [MetalTheory]
    [InlineData(8, 4)]
    [InlineData(24, 12)]
    public void VectorsAndUserStructs_MatchTheGpu(int x, int y)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float3> gpuPositions = device.AllocateReadWriteBuffer<Float3>(x * y);
        using ReadWriteBuffer<int> gpuWeights = device.AllocateReadWriteBuffer<int>(x * y);
        using ReadWriteBuffer<Float3> cpuPositions = device.AllocateReadWriteBuffer<Float3>(x * y);
        using ReadWriteBuffer<int> cpuWeights = device.AllocateReadWriteBuffer<int>(x * y);

        device.For(x, y, new NeighbourExchange(gpuPositions, gpuWeights));
        WithinTimeout(() => device.ForCpu(x, y, new NeighbourExchange(cpuPositions, cpuWeights)));

        Assert.Equal(Flatten(gpuPositions.ToArray()), Flatten(cpuPositions.ToArray()));
        Assert.Equal(gpuWeights.ToArray(), cpuWeights.ToArray());
    }

    [MetalTheory]
    [InlineData(16)]
    [InlineData(100)]
    public void DeviceMemoryBarrier_MatchesTheGpu(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> scratch = device.AllocateReadWriteBuffer<int>(count);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(count);

        WithinTimeout(() => device.ForCpu(count, new DeviceBarrier(scratch, output)));

        Assert.Equal(ExpectedDeviceBarrier(count), output.ToArray());
    }

    [MetalFact]
    public void PixelShader_MatchesTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(20, 12);

        WithinTimeout(() => device.ForEachCpu(texture, default(TileMean)));

        AssertPixelsClose(ExpectedTileMean(20, 12), texture.ToArray(), maxStep: 1, outlierFraction: 0, "TileMean on the CPU");
    }

    [MetalTheory]
    [InlineData(64)]
    [InlineData(1000)]
    public void AtomicsOnABuffer_WorkNextToSharedMemory_OnBothDevices(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] input = Ints(count, seed: count, max: 1000);
        int threads = RoundUp(count, 64);
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(1);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(1);
        using ReadWriteBuffer<int> split = device.AllocateReadWriteBuffer<int>(1);

        device.For(threads, new CountOddPerGroup(source, gpu, count));
        WithinTimeout(() => device.ForCpu(threads, new CountOddPerGroup(source, cpu, count)));

        int expected = input.Count(static v => (v & 1) != 0);
        Assert.Equal(expected, gpu[0]);
        Assert.Equal(expected, cpu[0]);

        // A shader with atomics is never divided between the devices; whichever runs it, the count is right.
        SplitDispatchInfo info = default;
        WithinTimeout(() => info = device.ForSplit(threads, new CountOddPerGroup(source, split, count)));
        Assert.Equal(expected, split[0]);
        Assert.True(info.CpuExtent == 0 || info.GpuExtent == 0, $"divided: {info.CpuExtent} + {info.GpuExtent}");
    }

    [MetalFact]
    public void BarrierHeavyShader_DoesNotDeadlock_AndStaysInStep()
    {
        // 4 groups × 32 threads × 401 barriers: any lost or doubled wake-up either hangs or shifts a round.
        const int count = 128, rounds = 200;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] start = Ints(count, seed: 7);
        using ReadWriteBuffer<int> values = device.AllocateReadWriteBuffer(start);

        WithinTimeout(() => device.ForCpu(count, new Rotate(values, rounds)));

        Assert.Equal(ExpectedRotation(start, rounds), values.ToArray());
    }

    [MetalFact]
    public void RepeatedDispatches_ReuseNothingFromEachOther()
    {
        // Threads are created per dispatch; many short dispatches must each start and stop cleanly.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] start = Ints(64, seed: 11);
        int[] expected = ExpectedRotation(start, 3);

        WithinTimeout(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                using ReadWriteBuffer<int> values = device.AllocateReadWriteBuffer(start);
                device.ForCpu(64, new Rotate(values, 3));
                Assert.Equal(expected, values.ToArray());
            }
        });
    }

    [MetalFact]
    public void ConcurrentDispatchesOfOneShaderType_AreSerialized()
    {
        // The [GroupShared] arrays are static fields of the shader type: two dispatches at once would
        // share them. Each must still see only its own group's data.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[][] starts = Enumerable.Range(0, 4).Select(i => Ints(96, seed: 20 + i)).ToArray();
        int[][] results = new int[starts.Length][];

        WithinTimeout(() => Parallel.For(0, starts.Length, i =>
        {
            using ReadWriteBuffer<int> values = device.AllocateReadWriteBuffer(starts[i]);
            device.ForCpu(96, new Rotate(values, 40));
            results[i] = values.ToArray();
        }));

        for (int i = 0; i < starts.Length; i++)
        {
            Assert.Equal(ExpectedRotation(starts[i], 40), results[i]);
        }
    }

    [MetalFact]
    public void LargestThreadgroup_RunsWithAThreadPerShaderThread()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(64 * 32);

        WithinTimeout(() => device.ForCpu(64, 32, new LargestGroup(output)));

        Assert.Equal(ExpectedLargestGroup(64, 32), output.ToArray());
    }

    [MetalFact]
    public void ThreadsThatLeaveBeforeABarrier_DoNotHangTheRest()
    {
        // Undefined on the GPU; on the CPU a thread that returned no longer takes part in barriers.
        const int count = 40;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(count);

        WithinTimeout(() => device.ForCpu(count, new UnevenBarriers(output)));

        int[] values = output.ToArray();

        for (int i = 0; i < count; i++)
        {
            int local = i % 16;

            if ((local & 1) == 1)
            {
                Assert.Equal(-1, values[i]);
            }
            else if (i < 32)
            {
                // Full groups: the even threads synchronized among themselves.
                Assert.Equal(local + ((local + 2) % 16), values[i]);
            }
        }
    }

    [MetalTheory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(37)]
    [InlineData(47)]
    public void AThreadThatThrows_StopsItsGroupAndTheDispatch_AndIsRethrown(int failingThread)
    {
        const int count = 48;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(count);
        output.CopyFrom(Enumerable.Repeat(-7, count).ToArray());

        ArgumentOutOfRangeException? exception = null;
        WithinTimeout(() => exception = Assert.Throws<ArgumentOutOfRangeException>(() => device.ForCpu(count, new ThrowsBeforeBarrier(output, failingThread))));

        Assert.NotNull(exception);

        // Groups run in order and the dispatch stops at the failing one: earlier groups completed,
        // the failing group never got past its first barrier, later groups never started.
        int[] values = output.ToArray();
        int failingGroup = failingThread / 16;

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i / 16 < failingGroup ? i : -7, values[i]);
        }

        // And the executor is usable afterwards.
        WithinTimeout(() => device.ForCpu(count, new ThrowsBeforeBarrier(output, failingThread: -1)));
        Assert.Equal(Enumerable.Range(0, count).ToArray(), output.ToArray());
    }

    [Fact]
    public void GroupsLargerThanTheCap_AreRejectedWithAClearMessage()
    {
        CountingKernel kernel = new(new int[1]);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => CpuGroupDispatcher.Run(
            kernel, 64, 64, 1, new Extent3(32, 32, 1), default, new Extent3(64, 64, 1), new object(), typeof(LargestGroup), maxGroupThreads: 1023));

        Assert.Contains("32 x 32 x 1", exception.Message);
        Assert.Contains("1024 threads", exception.Message);
        Assert.Contains("limit of 1023", exception.Message);
        Assert.Contains(nameof(LargestGroup), exception.Message);
        Assert.Contains("[ThreadGroupSize]", exception.Message);
    }

    [Fact]
    public void TheCap_CountsOnlyTheThreadsAGroupReallyHas()
    {
        // Ten threads dispatched in groups of up to 2048 need ten CPU threads, not 2048.
        int[] ran = new int[1];

        CpuGroupDispatcher.Run(new CountingKernel(ran), 10, 1, 1, new Extent3(2048, 1, 1), default, new Extent3(10, 1, 1), new object(), typeof(LargestGroup));

        Assert.Equal(10, ran[0]);
        Assert.Equal(1024, CpuGroupDispatcher.MaxGroupThreads);
    }

    [Fact]
    public void EveryThreadOfABox_RunsOnce_WithItsOwnPositionAndAThreadOfItsOwn()
    {
        // 7 × 5 × 3 in groups of 4 × 2 × 2: partial groups along every axis.
        const int x = 7, y = 5, z = 3;
        int[] visits = new int[x * y * z];
        int[] managedThreads = new int[x * y * z];
        int dispatcher = Environment.CurrentManagedThreadId;

        CpuGroupDispatcher.Run(
            new RecordingKernel(visits, managedThreads, x, y), x, y, z, new Extent3(4, 2, 2), default, new Extent3(x, y, z), new object(), typeof(LargestGroup));

        Assert.All(visits, static count => Assert.Equal(1, count));
        Assert.DoesNotContain(dispatcher, managedThreads);

        // One thread per position of a group: at most 16 distinct ones, each always at the same position.
        Assert.Equal(16, managedThreads.Distinct().Count());
    }

    [Fact]
    public void AnEmptyBox_RunsNothing()
    {
        int[] ran = new int[1];

        CpuGroupDispatcher.Run(new CountingKernel(ran), 8, 8, 1, new Extent3(4, 4, 1), default, new Extent3(8, 0, 1), new object(), typeof(LargestGroup));

        Assert.Equal(0, ran[0]);
    }

    [MetalFact]
    public void ABarrierWithoutAThreadgroup_ExplainsTheMissingDescriptorMember()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => GraphicsDevice.GetDefault().ForCpu(4, default(UnreportedBarrier)));

        Assert.Contains("UsesThreadgroupMemory", exception.Message);
    }

    [Fact]
    public void Barriers_OutsideADispatch_Throw()
    {
        Assert.Throws<InvalidOperationException>(Gpu.GroupMemoryBarrierWithGroupSync);
        Assert.Throws<InvalidOperationException>(Gpu.DeviceMemoryBarrierWithGroupSync);
        Assert.Throws<InvalidOperationException>(Gpu.AllMemoryBarrierWithGroupSync);
    }

    [MetalTheory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.7)]
    [InlineData(1.0)]
    public void Split_RunsTheCpuPartOnTheGroupExecutor(double fraction)
    {
        // Every group writes only its own outputs, so the reduction is safe to split.
        const int count = 2000;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] input = Floats(count, seed: 3);
        SplitDispatchInfo info = default;
        float[] sums = null!;

        WithinTimeout(() => sums = RunBlockSum(device, input, (threads, shader) => info = device.ForSplit(threads, shader, fraction)));

        Assert.Equal(BlockSums(input), sums);
        Assert.Equal(2048, info.CpuExtent + info.GpuExtent);
        Assert.Equal(0, info.CpuExtent % 256);
        Assert.Equal(fraction > 0, info.CpuExtent > 0);
        Assert.Equal(fraction < 1, info.GpuExtent > 0);
    }

    [MetalTheory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    public void Split2D_RunsTheCpuPartOnTheGroupExecutor(double fraction)
    {
        const int n = 40;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] a = Floats(n * n, seed: 5);
        float[] b = Floats(n * n, seed: 6);
        float[] c = null!;

        WithinTimeout(() => c = RunMatMul(device, a, b, n, (size, shader) => device.ForSplit(size, size, shader, fraction)));

        AssertClose(Multiply(a, b, n), c, relative: 1e-4f, absolute: 1e-4f);
    }

    private readonly struct CountingKernel(int[] counter) : ICpuKernel
    {
        public void Invoke(CpuThread thread) => Interlocked.Increment(ref counter[0]);
    }

    private readonly struct RecordingKernel(int[] visits, int[] managedThreads, int gridX, int gridY) : ICpuKernel
    {
        public void Invoke(CpuThread thread)
        {
            int i = (((thread.Z * gridY) + thread.Y) * gridX) + thread.X;

            Interlocked.Increment(ref visits[i]);
            managedThreads[i] = Environment.CurrentManagedThreadId;
        }
    }
}
