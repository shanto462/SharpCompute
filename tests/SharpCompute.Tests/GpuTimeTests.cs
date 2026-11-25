using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Interop;
using SharpCompute.Tests.NativeInterop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// <see cref="GraphicsDevice.LastGpuTime"/> and <see cref="ComputeContext.LastGpuTime"/>: the GPU
/// execution time of a command buffer, from Metal's <c>GPUStartTime</c> and <c>GPUEndTime</c>.
/// </summary>
public class GpuTimeTests
{
    private const int Length = 4096;

    // A dispatch the GPU needs milliseconds for, against one it needs microseconds for.
    private const int HeavyLength = 1 << 16;
    private const int HeavyRounds = 4096;
    private const int LightRounds = 1;

    [Theory]
    [InlineData(0.0, 0.0, 0L)]
    [InlineData(100.0, 100.0, 0L)]
    [InlineData(100.0, 100.5, 5_000_000L)]
    [InlineData(100.0, 100.0000001, 1L)]
    // Never negative: a command buffer that never reached the GPU has an end of 0.
    [InlineData(100.0, 0.0, 0L)]
    [InlineData(100.5, 100.0, 0L)]
    [InlineData(double.NaN, 100.0, 0L)]
    [InlineData(100.0, double.NaN, 0L)]
    [InlineData(0.0, double.PositiveInfinity, 0L)]
    [InlineData(double.NegativeInfinity, 0.0, 0L)]
    public void GpuTimeTicks_IsTheForwardInterval_OrZero(double start, double end, long expected)
    {
        long ticks = GraphicsDevice.GpuTimeTicks(start, end);

        // 100.0000001 − 100.0 is not exactly 1e-7 in binary, so the last tick is allowed to truncate.
        Assert.InRange(ticks, Math.Max(0, expected - 1), expected);
    }

    [MetalFact]
    public void Context_ReportsZero_UntilABatchHasRun()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ComputeContext context = device.CreateComputeContext();

        Assert.Equal(TimeSpan.Zero, context.LastGpuTime);

        // Recording runs nothing, and neither does executing nothing.
        context.Execute();
        context.For(Length, new IncrementAll(buffer));
        Assert.Equal(TimeSpan.Zero, context.LastGpuTime);
    }

    [MetalFact]
    public void Execute_SetsTheBatchsGpuTime_WhichIsInsideTheWallTime()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(HeavyLength);
        using ComputeContext context = device.CreateComputeContext();
        device.Prepare(ShaderReference.Of<MixRounds>());

        context.For(HeavyLength, new MixRounds(buffer, HeavyRounds));
        long start = Stopwatch.GetTimestamp();
        context.Execute();
        TimeSpan wall = Stopwatch.GetElapsedTime(start);

        // The GPU ran between the commit and the end of the wait, both inside the stopwatch.
        Assert.True(context.LastGpuTime > TimeSpan.Zero, "No GPU time was recorded.");
        Assert.True(context.LastGpuTime <= wall, $"GPU {context.LastGpuTime}, wall {wall}.");
        Assert.Equal(MixRounds.Expected(0, 123, HeavyRounds), buffer[123]);
    }

    [MetalFact]
    public void GpuTime_FollowsTheWorkTheKernelDoes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(HeavyLength);
        using ComputeContext context = device.CreateComputeContext();

        TimeSpan Median(int rounds)
        {
            TimeSpan[] samples = new TimeSpan[7];

            for (int i = 0; i < samples.Length; i++)
            {
                context.For(HeavyLength, new MixRounds(buffer, rounds));
                context.Execute();
                samples[i] = context.LastGpuTime;
            }

            Array.Sort(samples);
            return samples[samples.Length / 2];
        }

        Median(LightRounds);
        TimeSpan light = Median(LightRounds);
        TimeSpan heavy = Median(HeavyRounds);

        // 4096 times the rounds; a time that was the submission's rather than the kernel's would not move.
        Assert.True(heavy > light * 4, $"{HeavyRounds} rounds took {heavy}, {LightRounds} took {light}.");
    }

    [MetalFact]
    public void Execute_WithNothingPending_KeepsThePreviousTime()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ComputeContext context = device.CreateComputeContext();

        context.For(Length, new IncrementAll(buffer));
        context.Execute();
        TimeSpan first = context.LastGpuTime;
        Assert.True(first > TimeSpan.Zero);

        context.Execute();
        context.For(0, new IncrementAll(buffer));
        context.Copy(buffer, 0, buffer, 0, 0);
        context.Execute();

        Assert.Equal(first, context.LastGpuTime);
    }

    [MetalFact]
    public async Task ExecuteAsync_SetsTheTime_BeforeItsTaskCompletes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(HeavyLength);
        await using ComputeContext context = device.CreateComputeContext();

        for (int i = 0; i < 20; i++)
        {
            context.For(HeavyLength, new MixRounds(buffer, i % 2 == 0 ? HeavyRounds : LightRounds));
            Task done = context.ExecuteAsync();

            // A continuation that runs the moment the task completes must already see this batch's time.
            TimeSpan seen = await done.ContinueWith(_ => context.LastGpuTime, TaskScheduler.Default).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(seen > TimeSpan.Zero, $"Execution {i} completed without a GPU time.");
            Assert.Equal(seen, context.LastGpuTime);
        }
    }

    [MetalFact]
    public void Dispose_RunsWhatIsPending_AndTheTimeCanBeReadAfterwards()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        ComputeContext context = device.CreateComputeContext();

        using (context)
        {
            context.For(Length, new IncrementAll(buffer));
        }

        Assert.True(context.LastGpuTime > TimeSpan.Zero);
        Assert.Equal(1, buffer[0]);
    }

    [MetalFact]
    public async Task DisposeAsync_SetsTheTime_BeforeItCompletes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        ComputeContext context = device.CreateComputeContext();

        context.For(Length, new IncrementAll(buffer));
        await context.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(context.LastGpuTime > TimeSpan.Zero);
    }

    [MetalFact]
    public void BatchTheGpuFailed_StillReportsItsTime()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ComputeContext context = device.CreateComputeContext();

        context.For(Length, new IncrementAll(buffer));
        context.SimulateGpuError("Simulated fault");

        Assert.Throws<MetalException>(context.Execute);
        Assert.True(context.LastGpuTime > TimeSpan.Zero);
        Assert.Equal(context.LastGpuTime, device.LastGpuTime);
    }

    [MetalFact]
    public async Task EncodeInto_ReportsTheTimeOfTheCallersCommandBuffer()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);

        try
        {
            using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(HeavyLength);
            using ComputeContext context = device.CreateComputeContext();

            context.For(HeavyLength, new MixRounds(buffer, HeavyRounds));
            Task done = context.EncodeInto(commandBuffer);

            // Encoded, not run: the caller has not committed yet.
            Assert.Equal(TimeSpan.Zero, context.LastGpuTime);

            NativeMetal.CommitAndWait(commandBuffer);
            await done.WaitAsync(TimeSpan.FromSeconds(10));

            double seconds = ObjC.SendDouble(commandBuffer, Selectors.GPUEndTime) - ObjC.SendDouble(commandBuffer, Selectors.GPUStartTime);
            Assert.True(context.LastGpuTime > TimeSpan.Zero);
            Assert.Equal(seconds, context.LastGpuTime.TotalSeconds, 1e-6);
        }
        finally
        {
            ObjC.Release(commandBuffer);
        }
    }

    [MetalFact]
    public async Task CommandBufferDroppedUncommitted_KeepsThePreviousTime()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ComputeContext context = device.CreateComputeContext();

        context.For(Length, new IncrementAll(buffer));
        context.Execute();
        TimeSpan ran = context.LastGpuTime;

        nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);
        context.For(Length, new IncrementAll(buffer));
        Task dropped = context.EncodeInto(commandBuffer);
        ObjC.Release(commandBuffer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => dropped.WaitAsync(TimeSpan.FromSeconds(10)));

        // The work never ran, so there is no time to report for it.
        Assert.Equal(ran, context.LastGpuTime);
        Assert.Equal(1, buffer[0]);
    }

    [MetalFact]
    public void Device_ReportsTheTimeOfEachSynchronousCall()
    {
        // Its own thread, so that what other tests ran on a pool thread cannot be what is read.
        RunOnNewThread(() =>
        {
            GraphicsDevice device = GraphicsDevice.GetDefault();
            using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(HeavyLength);
            using ReadWriteBuffer<uint> copy = device.AllocateReadWriteBuffer<uint>(HeavyLength);
            device.Prepare(ShaderReference.Of<MixRounds>());

            Assert.Equal(TimeSpan.Zero, device.LastGpuTime);

            long start = Stopwatch.GetTimestamp();
            device.For(HeavyLength, new MixRounds(buffer, HeavyRounds));
            TimeSpan wall = Stopwatch.GetElapsedTime(start);
            TimeSpan dispatch = device.LastGpuTime;

            Assert.True(dispatch > TimeSpan.Zero, "No GPU time was recorded for For.");
            Assert.True(dispatch <= wall, $"GPU {dispatch}, wall {wall}.");

            // Nothing to do: no command buffer, so the time stays the dispatch's.
            device.For(0, new MixRounds(buffer, HeavyRounds));
            device.Copy(buffer, 0, copy, 0, 0);
            Assert.Equal(dispatch, device.LastGpuTime);

            // A blit is a command buffer of its own; its time replaces the dispatch's.
            SetLastGpuTimeToSentinel(device);
            device.Copy(buffer, copy);
            Assert.NotEqual(Sentinel, device.LastGpuTime);
            Assert.Equal(buffer[77], copy[77]);

            SetLastGpuTimeToSentinel(device);
            device.Clear(copy);
            Assert.NotEqual(Sentinel, device.LastGpuTime);
            Assert.Equal(0u, copy[77]);
        });
    }

    [MetalFact]
    public void Device_ReportsTheTimeOfAContextThisThreadExecuted()
    {
        RunOnNewThread(() =>
        {
            GraphicsDevice device = GraphicsDevice.GetDefault();
            using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
            using ComputeContext context = device.CreateComputeContext();

            context.For(Length, new IncrementAll(buffer));
            context.Execute();

            Assert.True(context.LastGpuTime > TimeSpan.Zero);
            Assert.Equal(context.LastGpuTime, device.LastGpuTime);
        });
    }

    [MetalFact]
    public void Device_KeepsOneTimePerThread()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> mine = device.AllocateReadWriteBuffer<uint>(HeavyLength);
        using ReadWriteBuffer<uint> theirs = device.AllocateReadWriteBuffer<uint>(HeavyLength);

        RunOnNewThread(() =>
        {
            device.For(HeavyLength, new MixRounds(mine, HeavyRounds));
            TimeSpan before = device.LastGpuTime;
            Assert.True(before > TimeSpan.Zero);

            TimeSpan seenByOther = TimeSpan.MaxValue, ranByOther = TimeSpan.Zero;

            RunOnNewThread(() =>
            {
                // A thread that has dispatched nothing has no time, whatever other threads ran.
                seenByOther = device.LastGpuTime;
                device.For(HeavyLength, new MixRounds(theirs, LightRounds));
                ranByOther = device.LastGpuTime;
            });

            Assert.Equal(TimeSpan.Zero, seenByOther);
            Assert.True(ranByOther > TimeSpan.Zero);

            // The other thread's dispatch did not replace this thread's time.
            Assert.Equal(before, device.LastGpuTime);
        });
    }

    [MetalFact]
    public void Device_KeepsTimesApart_UnderConcurrentDispatches()
    {
        // Four threads, each alternating a heavy and a light kernel on one device. A time shared by
        // the device would hand a thread another thread's (much shorter or longer) reading.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        device.Prepare(ShaderReference.Of<MixRounds>());
        using Barrier barrier = new(4);
        string?[] failures = new string?[4];

        Thread[] threads = new Thread[4];

        for (int t = 0; t < threads.Length; t++)
        {
            int index = t;
            threads[t] = new Thread(() =>
            {
                using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(HeavyLength);
                barrier.SignalAndWait();

                for (int i = 0; i < 12 && failures[index] is null; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    device.For(HeavyLength, new MixRounds(buffer, i % 2 == 0 ? HeavyRounds * 4 : LightRounds));
                    TimeSpan wall = Stopwatch.GetElapsedTime(start);
                    TimeSpan gpu = device.LastGpuTime;

                    // Always true of a thread's own dispatch; a heavy dispatch's time read after a
                    // light one's wall time is not.
                    if (gpu <= TimeSpan.Zero || gpu > wall)
                    {
                        failures[index] = $"thread {index}, dispatch {i}: GPU {gpu}, wall {wall}";
                    }
                }
            });
            threads[t].Start();
        }

        foreach (Thread thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)));
        }

        Assert.All(failures, Assert.Null);
    }

    [MetalFact]
    public void Device_ReportsZero_WhenTheThreadsLastCallWasOnAnotherDevice()
    {
        RunOnNewThread(() =>
        {
            GraphicsDevice device = GraphicsDevice.GetDefault();
            using GraphicsDevice other = GraphicsDevice.CreateSystemDefault();
            using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
            using ReadWriteBuffer<int> otherBuffer = other.AllocateReadWriteBuffer<int>(Length);

            device.For(Length, new IncrementAll(buffer));
            Assert.True(device.LastGpuTime > TimeSpan.Zero);
            Assert.Equal(TimeSpan.Zero, other.LastGpuTime);

            other.For(Length, new IncrementAll(otherBuffer));
            Assert.True(other.LastGpuTime > TimeSpan.Zero);
            Assert.Equal(TimeSpan.Zero, device.LastGpuTime);
        });
    }

    [MetalFact]
    public void ForSplit_SetsTheTimeOfItsGpuPart()
    {
        RunOnNewThread(() =>
        {
            GraphicsDevice device = GraphicsDevice.GetDefault();
            using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(1 << 16);

            SplitDispatchInfo info = device.ForSplit(buffer.Length, new EndToEnd.MultiplyByTwo(buffer, 2f), 0.25);

            // The split's own GpuTime runs from the commit, so it is at least the execution time.
            Assert.True(info.GpuExtent > 0);
            Assert.True(device.LastGpuTime > TimeSpan.Zero);
            Assert.True(device.LastGpuTime <= info.GpuTime + TimeSpan.FromTicks(1), $"Executed {device.LastGpuTime}, split reported {info.GpuTime}.");

            // A split that leaves the GPU nothing creates no command buffer.
            TimeSpan before = device.LastGpuTime;
            device.ForSplit(buffer.Length, new EndToEnd.MultiplyByTwo(buffer, 2f), 1.0);
            Assert.Equal(before, device.LastGpuTime);
        });
    }

    [MetalFact]
    public void ReadingTheTime_AndTimingEveryExecution_DoesNotAllocate()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ComputeContext context = device.CreateComputeContext();
        long ticks = 0;

        void Frame()
        {
            context.For(Length, new IncrementAll(buffer));
            context.Execute();
            ticks += context.LastGpuTime.Ticks + device.LastGpuTime.Ticks;
        }

        for (int frame = 0; frame < 5; frame++)
        {
            Frame();
        }

        const int frames = 50;
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int frame = 0; frame < frames; frame++)
        {
            Frame();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated == 0, $"Timing {frames} executions allocated {allocated} bytes.");
        Assert.True(ticks > 0);
    }

    private static readonly TimeSpan Sentinel = TimeSpan.FromDays(1);

    /// <summary>Makes the calling thread's time one no command buffer could have, to see the next call replace it.</summary>
    private static void SetLastGpuTimeToSentinel(GraphicsDevice device) => device.SetLastGpuTime(Sentinel.Ticks);

    /// <summary>Runs <paramref name="body"/> on a thread that has never dispatched, and rethrows what it threw.</summary>
    private static void RunOnNewThread(Action body)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "The test thread did not finish.");

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
