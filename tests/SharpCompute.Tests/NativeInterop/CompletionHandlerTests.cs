using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests.NativeInterop;

/// <summary>
/// The tests that count live completion handlers process-wide (<see cref="CompletionBlock.LiveAttachments"/>)
/// run on their own, so that no other test's handlers are in flight meanwhile.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CompletionHandlerCollection
{
    public const string Name = "Completion handlers";
}

/// <summary>
/// The lifetime of the completed handler SharpCompute attaches to a command buffer: it runs once when
/// the command buffer completes, or, when a caller's command buffer is deallocated without having been
/// committed, its dispose helper releases everything the batch pinned and faults the task.
/// </summary>
[Collection(CompletionHandlerCollection.Name)]
public sealed class CompletionHandlerTests
{
    private const int Length = 4096;
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(30);

    [MetalFact]
    public async Task CommandBufferDroppedUncommitted_ReleasesEverything_AndFaultsTheTask()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        nint mtlBuffer = ObjC.Retain(buffer.Handle.DangerousGetHandle());

        try
        {
            nuint baseline = NativeMetal.RetainCount(mtlBuffer);
            ComputeContext context = device.CreateComputeContext();
            context.For(Length, new WriteIndexAndLength(buffer));

            nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);
            Assert.Equal(1u, NativeMetal.RetainCount(commandBuffer));

            Task done = context.EncodeInto(commandBuffer);

            // SharpCompute pins what the work uses, but not the caller's command buffer.
            Assert.Equal(1u, NativeMetal.RetainCount(commandBuffer));
            Assert.True(NativeMetal.RetainCount(mtlBuffer) > baseline);

            // The host drops its frame without committing it.
            ObjC.Release(commandBuffer);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => done.WaitAsync(Watchdog));
            Assert.StartsWith("The command buffer was released without being committed", error.Message);
            Assert.Contains(typeof(WriteIndexAndLength).FullName!, error.Message);

            // Released before anything else is disposed: the work never ran, and its lease is gone.
            ComputeContextTests.WaitForRetainCount(mtlBuffer, baseline);
            Assert.All(buffer.ToArray(), value => Assert.Equal(0, value));

            // The context goes on, with the dropped batch recycled.
            Assert.True(context.HasSpareBatch);
            context.Dispose();
            buffer.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            ComputeContextTests.WaitForRetainCount(mtlBuffer, 1);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public async Task DroppedCommandBuffers_LeakNothing_FrameAfterFrame()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        nint mtlBuffer = ObjC.Retain(buffer.Handle.DangerousGetHandle());

        try
        {
            nuint baseline = NativeMetal.RetainCount(mtlBuffer);
            int liveBefore = CompletionBlock.LiveAttachments;
            using ComputeContext context = device.CreateComputeContext();
            List<Task> frames = [];

            for (int frame = 0; frame < 100; frame++)
            {
                context.For(Length, new WriteIndexAndLength(buffer));
                nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);
                frames.Add(context.EncodeInto(commandBuffer));
                ObjC.Release(commandBuffer);
            }

            await Task.WhenAll(frames.Select(static t => t.ContinueWith(static _ => { }, TaskScheduler.Default))).WaitAsync(Watchdog);

            Assert.All(frames, static t => Assert.IsType<InvalidOperationException>(t.Exception!.InnerException));
            Assert.Equal(liveBefore, CompletionBlock.LiveAttachments);
            Assert.Equal(baseline, NativeMetal.RetainCount(mtlBuffer));
            Assert.All(buffer.ToArray(), value => Assert.Equal(0, value));
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public async Task CommittedCommandBuffer_StillCompletes_AndReleases()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        nint mtlBuffer = ObjC.Retain(buffer.Handle.DangerousGetHandle());

        try
        {
            nuint baseline = NativeMetal.RetainCount(mtlBuffer);
            int liveBefore = CompletionBlock.LiveAttachments;
            using ComputeContext context = device.CreateComputeContext();
            context.For(Length, new WriteIndexAndLength(buffer));

            nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);
            Task done = context.EncodeInto(commandBuffer);
            Assert.Equal(1u, NativeMetal.RetainCount(commandBuffer));

            NativeMetal.CommitAndWait(commandBuffer);
            await done.WaitAsync(Watchdog);

            Assert.Equal(Length * 1000 + 17, buffer[17]);
            ComputeContextTests.WaitForRetainCount(mtlBuffer, baseline);

            // The handler ran; dropping the completed command buffer disposes it, which only frees the handle.
            ObjC.Release(commandBuffer);
            Assert.Equal(liveBefore, CompletionBlock.LiveAttachments);
            Assert.True(done.IsCompletedSuccessfully);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public async Task HostThatCommitsLater_OnAnotherThread_Works()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ComputeContext context = device.CreateComputeContext();
        context.For(Length, new WriteIndexAndLength(buffer));

        nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);
        Task done = context.EncodeInto(commandBuffer);

        await Task.Delay(200);
        Assert.False(done.IsCompleted);

        // Committed and released by another thread, as a render thread would.
        await Task.Run(() =>
        {
            ObjC.SendVoid(commandBuffer, Selectors.Commit);
            ObjC.Release(commandBuffer);
        });

        await done.WaitAsync(Watchdog);
        Assert.Equal(Length * 1000 + 4095, buffer[4095]);
    }

    [MetalFact]
    public async Task GpuError_StillFaultsTheTask()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(16);
        using ComputeContext context = device.CreateComputeContext();

        context.For(16, new WriteIndexAndLength(buffer));
        context.SimulateGpuError("Simulated fault");
        nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);
        Task encoded = context.EncodeInto(commandBuffer);
        NativeMetal.CommitAndWait(commandBuffer);
        ObjC.Release(commandBuffer);

        context.For(16, new WriteIndexAndLength(buffer));
        context.SimulateGpuError("Simulated fault");
        Task executed = context.ExecuteAsync();

        Assert.Contains("Simulated fault", (await Assert.ThrowsAsync<MetalException>(() => encoded.WaitAsync(Watchdog))).Message);
        Assert.Contains("Simulated fault", (await Assert.ThrowsAsync<MetalException>(() => executed.WaitAsync(Watchdog))).Message);
    }

    [MetalFact]
    public async Task HandlerAndDispose_NeverBothFinishABatch()
    {
        // Mixed frames, with ForAsync on another thread: committed and released at once (the handler
        // runs, then Metal's last release disposes it), and dropped uncommitted (only the dispose runs).
        // Every task finishes exactly once and every handle is freed exactly once: a double free would
        // leave the live count below where it started, a missed one above.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteBuffer<int> other = device.AllocateReadWriteBuffer<int>(Length);
        nint mtlBuffer = ObjC.Retain(buffer.Handle.DangerousGetHandle());

        try
        {
            nuint baseline = NativeMetal.RetainCount(mtlBuffer);
            int liveBefore = CompletionBlock.LiveAttachments;
            using ComputeContext context = device.CreateComputeContext();
            List<Task> committed = [];
            List<Task> dropped = [];

            Task asyncDispatches = Task.Run(async () =>
            {
                for (int i = 0; i < 200; i++)
                {
                    await device.ForAsync(Length, new WriteIndexAndLength(other));
                }
            });

            for (int frame = 0; frame < 400; frame++)
            {
                context.For(Length, new WriteIndexAndLength(buffer));
                nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);
                Task done = context.EncodeInto(commandBuffer);

                if (frame % 3 == 2)
                {
                    dropped.Add(done);
                }
                else
                {
                    ObjC.SendVoid(commandBuffer, Selectors.Commit);
                    committed.Add(done);
                }

                ObjC.Release(commandBuffer);
            }

            await Task.WhenAll(committed).WaitAsync(Watchdog);
            await Task.WhenAll(dropped.Select(static t => t.ContinueWith(static _ => { }, TaskScheduler.Default))).WaitAsync(Watchdog);
            await asyncDispatches.WaitAsync(Watchdog);

            Assert.All(dropped, static t => Assert.IsType<InvalidOperationException>(t.Exception!.InnerException));

            // Metal releases a completed command buffer, and with it the handler, shortly after it ran.
            DateTime deadline = DateTime.UtcNow + Watchdog;

            while (CompletionBlock.LiveAttachments != liveBefore && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            Assert.Equal(liveBefore, CompletionBlock.LiveAttachments);
            ComputeContextTests.WaitForRetainCount(mtlBuffer, baseline);
            Assert.Equal(Length * 1000 + 9, buffer[9]);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public async Task HandlerReleasedWithoutBeingCalled_DropsTheBatch()
    {
        // The dispose helper's own path, through libclosure's real Block_copy and Block_release.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        nint mtlBuffer = ObjC.Retain(buffer.Handle.DangerousGetHandle());

        try
        {
            int liveBefore = CompletionBlock.LiveAttachments;

            CommandBatch batch = new(device, commandCapacity: 4);
            GraphicsDevice.RecordFill(batch, buffer, 1);
            Task done = batch.TrackCompletionForTests();

            // Disposed while the batch is pending: the batch's lease defers the release.
            buffer.Dispose();
            Assert.Equal(2u, NativeMetal.RetainCount(mtlBuffer));

            CompletionBlock.CopyAndReleaseWithoutInvokingForTests(batch);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => done.WaitAsync(Watchdog));
            Assert.StartsWith("The command buffer was released without being committed", error.Message);
            Assert.Equal(1u, NativeMetal.RetainCount(mtlBuffer));
            Assert.Equal(liveBefore, CompletionBlock.LiveAttachments);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public async Task CommandBufferDroppedWhileAttaching_IsFreedAndFaultedExactlyOnce()
    {
        // The block is copied and its copy already disposed (the batch dropped, the handle freed) before
        // Attach checks whether Metal kept it. Attach must neither free the handle again nor throw.
        GraphicsDevice device = GraphicsDevice.GetDefault();

        for (int i = 0; i < 50; i++)
        {
            ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
            nint mtlBuffer = ObjC.Retain(buffer.Handle.DangerousGetHandle());

            try
            {
                int liveBefore = CompletionBlock.LiveAttachments;
                CommandBatch batch = new(device, commandCapacity: 4);
                GraphicsDevice.RecordFill(batch, buffer, 1);
                Task done = batch.TrackCompletionForTests();
                int faults = 0;
                Task counted = done.ContinueWith(_ => Interlocked.Increment(ref faults), TaskContinuationOptions.OnlyOnFaulted);

                buffer.Dispose();
                CompletionBlock.AttachAndDropForTests(batch);

                InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => done.WaitAsync(Watchdog));
                Assert.StartsWith("The command buffer was released without being committed", error.Message);
                await counted.WaitAsync(Watchdog);
                Assert.Equal(1, faults);
                Assert.Equal(liveBefore, CompletionBlock.LiveAttachments);
                Assert.Equal(1u, NativeMetal.RetainCount(mtlBuffer));
            }
            finally
            {
                ObjC.Release(mtlBuffer);
            }
        }

        // The GC handle table is intact: allocating and freeing handles still works.
        GCHandle probe = GCHandle.Alloc(new object());
        probe.Free();
    }

    [MetalFact]
    public async Task ForAsyncHandlers_AreFreedOnceTheirCommandBuffersGo()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        int liveBefore = CompletionBlock.LiveAttachments;

        for (int i = 0; i < 50; i++)
        {
            await device.ForAsync(Length, new WriteIndexAndLength(buffer)).WaitAsync(Watchdog);
        }

        DateTime deadline = DateTime.UtcNow + Watchdog;

        while (CompletionBlock.LiveAttachments != liveBefore && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        Assert.Equal(liveBefore, CompletionBlock.LiveAttachments);
        Assert.Equal(Length * 1000 + 1, buffer[1]);
    }
}
