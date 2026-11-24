using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

public class ComputeContextTests
{
    private const int Length = 4096;

    [MetalFact]
    public void ManyDispatchesOfOneShader_AllRunWithOneExecute()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);

        using (ComputeContext context = device.CreateComputeContext())
        {
            for (int i = 0; i < 1000; i++)
            {
                context.For(Length, new IncrementAll(buffer));
            }

            Assert.Equal(1000, context.PendingCount);
        }

        Assert.All(buffer.ToArray(), value => Assert.Equal(1000, value));
    }

    [MetalFact]
    public void RecordedWork_DoesNotRunBeforeExecute()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ComputeContext context = device.CreateComputeContext();

        context.For(Length, new IncrementAll(buffer));

        // The command buffer is not committed yet, so the GPU cannot have touched the buffer.
        Thread.Sleep(20);
        Assert.All(buffer.ToArray(), value => Assert.Equal(0, value));

        context.Execute();

        Assert.Equal(0, context.PendingCount);
        Assert.All(buffer.ToArray(), value => Assert.Equal(1, value));
    }

    [MetalFact]
    public void DependentDispatches_SeeEveryWriteOfThePreviousOne()
    {
        // Each ReverseAddOne thread reads an element written by a different threadgroup of the
        // previous dispatch. 101 ping-pong passes give source[length - 1 - i] + 101 only if every
        // dispatch observes all writes of the one before it (serial dispatch, no explicit barrier).
        const int passes = 101;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] initial = Enumerable.Range(0, Length).Select(i => i * 7).ToArray();
        using ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(initial);
        using ReadWriteBuffer<int> b = device.AllocateReadWriteBuffer<int>(Length);

        using (ComputeContext context = device.CreateComputeContext())
        {
            for (int pass = 0; pass < passes; pass++)
            {
                context.For(Length, pass % 2 == 0 ? new ReverseAddOne(a, b) : new ReverseAddOne(b, a));
            }
        }

        int[] result = b.ToArray();

        for (int i = 0; i < Length; i++)
        {
            Assert.Equal(initial[Length - 1 - i] + passes, result[i]);
        }
    }

    [MetalFact]
    public void Constants_AreCapturedWhenEachDispatchIsRecorded()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] input = Enumerable.Range(0, Length).ToArray();
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>(input);
        ReadWriteBuffer<int>[] outputs = Enumerable.Range(0, 32).Select(_ => device.AllocateReadWriteBuffer<int>(Length)).ToArray();

        try
        {
            using (ComputeContext context = device.CreateComputeContext())
            {
                AddAmount shader = default;

                for (int k = 0; k < outputs.Length; k++)
                {
                    // The same local is overwritten after each call: only the recorded copy may matter.
                    shader = new AddAmount(source, outputs[k], k * 1000);
                    context.For(Length, in shader);
                }
            }

            for (int k = 0; k < outputs.Length; k++)
            {
                int[] result = outputs[k].ToArray();
                Assert.Equal(k * 1000, result[0]);
                Assert.Equal(Length - 1 + (k * 1000), result[^1]);
            }
        }
        finally
        {
            Array.ForEach(outputs, o => o.Dispose());
        }
    }

    [MetalFact]
    public void LargeConstants_EachDispatchKeepsItsOwnTemporaryBuffer()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<float>[] outputs = Enumerable.Range(0, 16).Select(_ => device.AllocateReadWriteBuffer<float>(LargeConstants.ValueCount)).ToArray();

        try
        {
            using (ComputeContext context = device.CreateComputeContext())
            {
                for (int k = 0; k < outputs.Length; k++)
                {
                    context.For(LargeConstants.ValueCount, new ReadLargeConstants(outputs[k], CreateLargeConstants((uint)(k * 10_000))));
                }

                // The temporaries are only referenced by the pending batch now.
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            for (int k = 0; k < outputs.Length; k++)
            {
                Assert.Equal(k * 10_000f, outputs[k][0]);
                Assert.Equal(LargeConstants.ValueCount - 1 + (k * 10_000f), outputs[k][LargeConstants.ValueCount - 1]);
            }
        }
        finally
        {
            Array.ForEach(outputs, o => o.Dispose());
        }
    }

    [MetalFact]
    public void ForEachAndForInOneContext_MatchSeparateDispatches()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> batched = device.AllocateReadWriteTexture2D<Rgba32>(300, 97);
        using ReadWriteTexture2D<Rgba32> separate = device.AllocateReadWriteTexture2D<Rgba32>(300, 97);
        using ReadWriteBuffer<uint> batchedStats = device.AllocateReadWriteBuffer<uint>(4);
        using ReadWriteBuffer<uint> separateStats = device.AllocateReadWriteBuffer<uint>(4);
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>(64);

        device.ForEach(separate, new PixelCoordinates(separateStats, 0.2f));

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(counter.Length, new IncrementAll(counter));
            context.ForEach(batched, new PixelCoordinates(batchedStats, 0.2f));
            context.For(counter.Length, new IncrementAll(counter));
        }

        Assert.Equal(separate.ToArray(), batched.ToArray());
        Assert.Equal(separateStats.ToArray(), batchedStats.ToArray());
        Assert.All(counter.ToArray(), value => Assert.Equal(2, value));
    }

    [MetalFact]
    public void Context_CanExecuteRepeatedly()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ComputeContext context = device.CreateComputeContext();

        for (int round = 1; round <= 5; round++)
        {
            context.For(Length, new IncrementAll(buffer));
            context.For(Length, new IncrementAll(buffer));
            context.Execute();

            Assert.Equal(round * 2, buffer[0]);
            Assert.Equal(round * 2, buffer[Length - 1]);
        }

        // Nothing pending: a no-op.
        context.Execute();
        Assert.Equal(10, buffer[0]);
    }

    [MetalFact]
    public void EmptyGrids_RecordNothing()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ComputeContext context = device.CreateComputeContext();

        context.For(0, new IncrementAll(buffer));
        context.For(Length, 0, new IncrementAll(buffer));
        context.For(Length, 1, 0, new IncrementAll(buffer));

        Assert.Equal(0, context.PendingCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => context.For(-1, new IncrementAll(buffer)));
    }

    [MetalFact]
    public void Dispose_IsIdempotent_AndLaterUseThrows()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(8, 8);
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);
        ComputeContext context = device.CreateComputeContext();

        context.For(Length, new IncrementAll(buffer));
        context.Dispose();
        context.Dispose();

        Assert.Equal(1, buffer[0]);
        Assert.Throws<ObjectDisposedException>(() => context.For(Length, new IncrementAll(buffer)));
        Assert.Throws<ObjectDisposedException>(() => context.ForEach(texture, new PixelCoordinates(stats, 0f)));
        Assert.Throws<ObjectDisposedException>(context.Execute);
        ForAsyncTests.ThrowsSynchronously<ObjectDisposedException>(() => context.ExecuteAsync());
        Assert.True(context.DisposeAsync().IsCompletedSuccessfully);
    }

    [MetalFact]
    public void ExceptionInsideUsing_StillRunsWhatWasRecordedBefore()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        ReadWriteBuffer<int> disposed = device.AllocateReadWriteBuffer<int>(Length);
        disposed.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            using ComputeContext context = device.CreateComputeContext();
            context.For(Length, new IncrementAll(buffer));
            context.For(Length, new IncrementAll(disposed));
        });

        Assert.All(buffer.ToArray(), value => Assert.Equal(1, value));
    }

    [MetalFact]
    public void FailedRecording_LeavesTheContextUsable()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using GraphicsDevice other = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteBuffer<int> foreign = other.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteTexture2D<Rgba32> foreignTexture = other.AllocateReadWriteTexture2D<Rgba32>(4, 4);
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);
        ReadWriteBuffer<int> disposed = device.AllocateReadWriteBuffer<int>(Length);
        disposed.Dispose();

        using (ComputeContext context = device.CreateComputeContext())
        {
            // Failing first: the empty batch is dropped rather than kept pending.
            Assert.Throws<ArgumentException>(() => context.For(Length, new IncrementAll(foreign)));
            Assert.Equal(0, context.PendingCount);

            context.For(Length, new IncrementAll(buffer));
            Assert.Throws<ArgumentException>(() => context.For(Length, new IncrementAll(foreign)));
            Assert.Throws<ObjectDisposedException>(() => context.For(Length, new IncrementAll(disposed)));
            Assert.Throws<ArgumentException>(() => context.ForEach(foreignTexture, new PixelCoordinates(stats, 0f)));
            Assert.Throws<ArgumentNullException>(() => context.ForEach(null!, new PixelCoordinates(stats, 0f)));
            Assert.Throws<ShaderCompilationException>(() => context.For(1, new BrokenSyntax()));
            context.For(Length, new IncrementAll(buffer));

            Assert.Equal(2, context.PendingCount);
        }

        Assert.All(buffer.ToArray(), value => Assert.Equal(2, value));
        Assert.All(foreign.ToArray(), value => Assert.Equal(0, value));
    }

    [MetalFact]
    public void BufferDisposedWhilePending_StaysAliveUntilTheGpuIsDone()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> result = device.AllocateReadWriteBuffer<int>(Length);
        ReadWriteBuffer<int> source = device.AllocateReadWriteBuffer<int>(Enumerable.Range(0, Length).ToArray());
        nint mtlBuffer = ObjC.Retain(source.Handle.DangerousGetHandle());

        try
        {
            using (ComputeContext context = device.CreateComputeContext())
            {
                context.For(Length, new IncrementAll(source));
                context.For(Length, new ReverseAddOne(source, result));

                // The command buffer retains what is bound, but the handle's own reference must not be
                // dropped either while the batch is pending: Dispose leaves the retain count unchanged.
                nuint before = RetainCount(mtlBuffer);
                source.Dispose();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Assert.Equal(before, RetainCount(mtlBuffer));
            }

            // source[i] = i + 1, then result[i] = source[length - 1 - i] + 1.
            int[] values = result.ToArray();

            for (int i = 0; i < Length; i++)
            {
                Assert.Equal(Length - i + 1, values[i]);
            }

            // Once the batch completed its leases were dropped, so the deferred release ran.
            WaitForRetainCount(mtlBuffer, 1);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void DeviceDisposedWhilePending_StillExecutes()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        ComputeContext context = device.CreateComputeContext();

        context.For(Length, new IncrementAll(buffer));
        device.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.For(Length, new IncrementAll(buffer)));
        Assert.Throws<ObjectDisposedException>(device.CreateComputeContext);

        context.Execute();
        context.Dispose();

        Assert.All(buffer.ToArray(), value => Assert.Equal(1, value));
        buffer.Dispose();
    }

    [MetalFact]
    public void ConcurrentUseOfOneContext_IsDetected()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();
        using ComputeContext context = device.CreateComputeContext();

        Task recording = Task.Run(() => context.For(Length, new BlockingIncrement(buffer, entered, release)));

        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

            Assert.Throws<InvalidOperationException>(() => context.For(Length, new IncrementAll(buffer)));
            Assert.Throws<InvalidOperationException>(context.Execute);
            ForAsyncTests.ThrowsSynchronously<InvalidOperationException>(() => context.ExecuteAsync());
            Assert.Throws<InvalidOperationException>(context.Dispose);
        }
        finally
        {
            release.Set();
            recording.Wait();
        }

        context.For(Length, new IncrementAll(buffer));
        context.Execute();

        Assert.All(buffer.ToArray(), value => Assert.Equal(2, value));
    }

    [MetalFact]
    public void ContextsOnManyThreads_RunIndependently()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, thread =>
        {
            using ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(Enumerable.Repeat(thread, Length).ToArray());
            using ReadWriteBuffer<int> b = device.AllocateReadWriteBuffer<int>(Length);

            for (int iteration = 0; iteration < 40; iteration++)
            {
                using ComputeContext context = device.CreateComputeContext();

                for (int pass = 0; pass < 10; pass++)
                {
                    context.For(Length, pass % 2 == 0 ? new ReverseAddOne(a, b) : new ReverseAddOne(b, a));
                }
            }

            // 40 × 10 passes, an even number per context, so every context leaves its result in a.
            Assert.All(a.ToArray(), value => Assert.Equal(thread + 400, value));
        });
    }

    [MetalFact]
    public async Task ExecuteAsync_CompletesAndTheContextKeepsRecording()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        ComputeContext context = device.CreateComputeContext();

        context.For(Length, new IncrementAll(buffer));
        Task first = context.ExecuteAsync();

        // Recorded into a fresh command buffer; queue order puts it after the first one.
        context.For(Length, new IncrementAll(buffer));
        context.For(Length, new IncrementAll(buffer));
        Task second = context.ExecuteAsync();

        Assert.Same(Task.CompletedTask, context.ExecuteAsync());

        await Task.WhenAll(first, second);
        Assert.All(buffer.ToArray(), value => Assert.Equal(3, value));

        context.For(Length, new IncrementAll(buffer));
        await context.DisposeAsync();
        Assert.All(buffer.ToArray(), value => Assert.Equal(4, value));
    }

    [MetalFact]
    public void GpuError_FromExecute_IsAMetalExceptionNamingEveryShader()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>(new int[Length]);
        nint mtlBuffer = ObjC.Retain(buffer.Handle.DangerousGetHandle());

        try
        {
            using ComputeContext context = device.CreateComputeContext();
            context.For(Length, new IncrementAll(buffer));
            context.For(Length, new AddAmount(source, buffer, 5));
            context.For(Length, new IncrementAll(buffer));
            context.SimulateGpuError("Simulated fault");

            MetalException error = Assert.Throws<MetalException>(context.Execute);

            Assert.Contains("a batch of 3 dispatches", error.Message);
            Assert.Contains(typeof(IncrementAll).FullName!, error.Message);
            Assert.Contains(typeof(AddAmount).FullName!, error.Message);
            Assert.Contains("Simulated fault", error.Message);

            // The failed batch released everything; the context records the next one normally.
            context.For(Length, new IncrementAll(buffer));
            context.Execute();
        }
        finally
        {
            buffer.Dispose();
            WaitForRetainCount(mtlBuffer, 1);
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public async Task GpuError_FromExecuteAsyncAndDispose_IsReported()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);

        ComputeContext context = device.CreateComputeContext();
        context.For(Length, new IncrementAll(buffer));
        context.SimulateGpuError("Simulated fault");

        MetalException asyncError = await Assert.ThrowsAsync<MetalException>(context.ExecuteAsync);
        Assert.Contains($"shader '{typeof(IncrementAll).FullName}'", asyncError.Message);

        context.For(Length, new IncrementAll(buffer));
        context.SimulateGpuError("Simulated fault");
        await Assert.ThrowsAsync<MetalException>(async () => await context.DisposeAsync());

        ComputeContext disposing = device.CreateComputeContext();
        disposing.For(Length, new IncrementAll(buffer));
        disposing.SimulateGpuError("Simulated fault");
        Assert.Throws<MetalException>(disposing.Dispose);

        // Disposed regardless of the error.
        Assert.Throws<ObjectDisposedException>(() => disposing.For(Length, new IncrementAll(buffer)));
    }

    [MetalFact]
    public void AbandonedContext_IsFinalizedWithoutRunningOrLeaking()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        nint mtlBuffer = ObjC.Retain(buffer.Handle.DangerousGetHandle());

        try
        {
            for (int i = 0; i < 8; i++)
            {
                RecordAndAbandon(device, buffer);
            }

            // The finalizer ends each open encoder (Metal aborts on a dealloc without endEncoding),
            // drops the uncommitted command buffer and returns the leases.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.All(buffer.ToArray(), value => Assert.Equal(0, value));

            buffer.Dispose();
            WaitForRetainCount(mtlBuffer, 1);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordAndAbandon(GraphicsDevice device, ReadWriteBuffer<int> buffer)
    {
        ComputeContext context = device.CreateComputeContext();
        context.For(Length, new IncrementAll(buffer));
    }

    internal static unsafe LargeConstants CreateLargeConstants(uint bias)
    {
        LargeConstants constants = default;

        for (int i = 0; i < LargeConstants.ValueCount; i++)
        {
            constants.Values[i] = i;
        }

        constants.Bias = bias;
        return constants;
    }

    internal static nuint RetainCount(nint obj) => ObjC.SendNUInt(obj, ObjC.GetSelector("retainCount"));

    /// <summary>Metal drops its own references to a completed command buffer's resources shortly after the handler runs.</summary>
    internal static void WaitForRetainCount(nint obj, nuint expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);

        while (RetainCount(obj) != expected && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        Assert.Equal(expected, RetainCount(obj));
    }
}
