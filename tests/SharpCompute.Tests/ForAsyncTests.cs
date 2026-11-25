using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

public class ForAsyncTests
{
    private const int Length = 4096;

    [MetalFact]
    public async Task ForAsync_ProducesTheSameResultAsFor()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] input = Enumerable.Range(0, Length).ToArray();
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(Length);

        await device.ForAsync(Length, new AddAmount(source, output, 42));

        Assert.Equal(input.Select(i => i + 42), output.ToArray());
    }

    [MetalFact]
    public async Task ForAsync2DAnd3D_UseTheWholeGrid()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> grid2D = device.AllocateReadWriteBuffer<uint>(37 * 19);
        using ReadWriteBuffer<uint> grid3D = device.AllocateReadWriteBuffer<uint>(5 * 6 * 7);
        using ReadWriteBuffer<uint> sync3D = device.AllocateReadWriteBuffer<uint>(5 * 6 * 7);

        await device.ForAsync(37, 19, new WriteIndex2D(grid2D, 37));
        await device.ForAsync(5, 6, 7, new EncodePosition3D(grid3D));
        device.For(5, 6, 7, new EncodePosition3D(sync3D));

        Assert.Equal(Enumerable.Range(0, 37 * 19).Select(i => (uint)i), grid2D.ToArray());
        Assert.Equal(sync3D.ToArray(), grid3D.ToArray());
    }

    [MetalFact]
    public async Task ForEachAsync_MatchesForEach()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> asyncTexture = device.AllocateReadWriteTexture2D<Rgba32>(321, 123);
        using ReadWriteTexture2D<Rgba32> syncTexture = device.AllocateReadWriteTexture2D<Rgba32>(321, 123);
        using ReadWriteBuffer<uint> asyncStats = device.AllocateReadWriteBuffer<uint>(4);
        using ReadWriteBuffer<uint> syncStats = device.AllocateReadWriteBuffer<uint>(4);

        device.ForEach(syncTexture, new PixelCoordinates(syncStats, 0.6f));
        await device.ForEachAsync(asyncTexture, new PixelCoordinates(asyncStats, 0.6f));

        Assert.Equal(syncTexture.ToArray(), asyncTexture.ToArray());
        Assert.Equal(syncStats.ToArray(), asyncStats.ToArray());
    }

    [MetalFact]
    public async Task BackToBackDependentDispatches_RunInSubmissionOrder()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] initial = Enumerable.Range(0, Length).Select(i => i * 3).ToArray();
        using ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(initial);
        using ReadWriteBuffer<int> b = device.AllocateReadWriteBuffer<int>(Length);

        // None of these is awaited before the next is committed.
        List<Task> tasks = [];

        for (int pass = 0; pass < 200; pass++)
        {
            tasks.Add(device.ForAsync(Length, pass % 2 == 0 ? new ReverseAddOne(a, b) : new ReverseAddOne(b, a)));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(initial.Select(v => v + 200), a.ToArray());
    }

    [MetalFact]
    public async Task ThousandsOfConcurrentDispatches_UnderGcPressure_AllComplete()
    {
        const int dispatches = 4000;
        const int elements = 256;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>(Enumerable.Range(0, elements).ToArray());
        using CancellationTokenSource stop = new();

        Thread collector = new(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }) { IsBackground = true };
        collector.Start();

        try
        {
            // Issued from several threads at once; each dispatch owns its output buffer, which is
            // dropped (not disposed) right away so only the pending dispatch keeps it alive.
            Task<(int Amount, WeakReference<ReadWriteBuffer<int>> Output, Task Done)>[] issued = Enumerable.Range(0, dispatches)
                .Select(i => Task.Run(() =>
                {
                    ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(elements);
                    Task done = device.ForAsync(elements, new AddAmount(source, output, i));
                    return (i, new WeakReference<ReadWriteBuffer<int>>(output), done);
                }))
                .ToArray();

            var results = await Task.WhenAll(issued);
            await Task.WhenAll(results.Select(r => r.Done));

            Assert.All(results, r => Assert.True(r.Done.IsCompletedSuccessfully));
        }
        finally
        {
            stop.Cancel();
            collector.Join();
        }

        // Results are checked on buffers that are kept alive explicitly (the run above checks that
        // dropped ones do not crash), mixing awaited and fire-and-forget dispatches.
        ReadWriteBuffer<int>[] outputs = Enumerable.Range(0, 500).Select(_ => device.AllocateReadWriteBuffer<int>(elements)).ToArray();

        try
        {
            Task[] tasks = outputs.Select((o, i) => device.ForAsync(elements, new AddAmount(source, o, i))).ToArray();
            await Task.WhenAll(tasks);

            for (int i = 0; i < outputs.Length; i++)
            {
                Assert.Equal(i, outputs[i][0]);
                Assert.Equal(elements - 1 + i, outputs[i][elements - 1]);
            }
        }
        finally
        {
            Array.ForEach(outputs, o => o.Dispose());
        }
    }

    [MetalFact]
    public void Continuations_DoNotRunOnMetalsCompletionThread()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        int testThread = Environment.CurrentManagedThreadId;
        int pending = 0;

        for (int i = 0; i < 100; i++)
        {
            // Enough work that the task is usually still pending when the continuation is attached.
            ComputeContext context = device.CreateComputeContext();

            for (int k = 0; k < 20; k++)
            {
                context.For(Length, new IncrementAll(buffer));
            }

            Task done = context.ExecuteAsync();

            // ExecuteSynchronously runs a continuation inline on whichever thread completes the task,
            // unless the source was created with RunContinuationsAsynchronously. Inline on this thread
            // means the task had already completed; any other non-pool thread would be Metal's.
            (int Thread, bool Pool) where = done
                .ContinueWith(_ => (Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread), TaskContinuationOptions.ExecuteSynchronously)
                .GetAwaiter().GetResult();

            if (where.Thread != testThread)
            {
                pending++;
                Assert.True(where.Pool, "A continuation ran inline on Metal's completion thread.");
            }
        }

        Assert.True(pending > 0, "Every task had completed before its continuation was attached; the test proved nothing.");
        Assert.Equal(2000, buffer[0]);
    }

    [MetalFact]
    public async Task ResourcesDisposedRightAfterSubmitting_StayAliveUntilCompletion()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> result = device.AllocateReadWriteBuffer<int>(Length);
        ReadWriteBuffer<int> source = device.AllocateReadWriteBuffer<int>(Enumerable.Range(0, Length).ToArray());
        nint mtlBuffer = ObjC.Retain(source.Handle.DangerousGetHandle());

        try
        {
            Task first = device.ForAsync(Length, new IncrementAll(source));
            Task second = device.ForAsync(Length, new ReverseAddOne(source, result));
            source.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            await Task.WhenAll(first, second);

            int[] values = result.ToArray();

            for (int i = 0; i < Length; i++)
            {
                Assert.Equal(Length - i + 1, values[i]);
            }

            ComputeContextTests.WaitForRetainCount(mtlBuffer, 1);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public async Task TextureDisposedRightAfterSubmitting_StaysAliveUntilCompletion()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);
        ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(512, 512);
        nint mtlTexture = ObjC.Retain(texture.Handle.DangerousGetHandle());

        try
        {
            Task done = device.ForEachAsync(texture, new PixelCoordinates(stats, 0f));
            texture.Dispose();
            GC.Collect();

            await done;

            Assert.Equal(512u * 512u, stats[0]);
            ComputeContextTests.WaitForRetainCount(mtlTexture, 1);
        }
        finally
        {
            ObjC.Release(mtlTexture);
        }
    }

    [MetalFact]
    public async Task DeviceDisposedWhileInFlight_StillCompletes()
    {
        for (int round = 0; round < 5; round++)
        {
            GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
            using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);

            Task[] tasks = Enumerable.Range(0, 20).Select(_ => device.ForAsync(Length, new IncrementAll(buffer))).ToArray();
            device.Dispose();

            await Task.WhenAll(tasks);
            Assert.All(buffer.ToArray(), value => Assert.Equal(20, value));
            ThrowsSynchronously<ObjectDisposedException>(() => device.ForAsync(Length, new IncrementAll(buffer)));
        }
    }

    [MetalFact]
    public void InvalidArguments_ThrowSynchronously()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using GraphicsDevice other = GraphicsDevice.CreateStandInForAnotherDevice();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteBuffer<int> foreign = other.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteTexture2D<Rgba32> foreignTexture = other.AllocateReadWriteTexture2D<Rgba32>(4, 4);
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);
        ReadWriteBuffer<int> disposed = device.AllocateReadWriteBuffer<int>(Length);
        ReadWriteTexture2D<Rgba32> disposedTexture = device.AllocateReadWriteTexture2D<Rgba32>(4, 4);
        disposed.Dispose();
        disposedTexture.Dispose();

        ThrowsSynchronously<ArgumentOutOfRangeException>(() => device.ForAsync(-1, new IncrementAll(buffer)));
        ThrowsSynchronously<ArgumentOutOfRangeException>(() => device.ForAsync(1, -1, new IncrementAll(buffer)));
        ThrowsSynchronously<ArgumentOutOfRangeException>(() => device.ForAsync(1, 1, -1, new IncrementAll(buffer)));
        ThrowsSynchronously<ArgumentException>(() => device.ForAsync(Length, new IncrementAll(foreign)));
        ThrowsSynchronously<ObjectDisposedException>(() => device.ForAsync(Length, new IncrementAll(disposed)));
        ThrowsSynchronously<ShaderCompilationException>(() => device.ForAsync(1, new BrokenSyntax()));
        ThrowsSynchronously<ArgumentNullException>(() => device.ForEachAsync(null!, new PixelCoordinates(stats, 0f)));
        ThrowsSynchronously<ArgumentException>(() => device.ForEachAsync(foreignTexture, new PixelCoordinates(stats, 0f)));
        ThrowsSynchronously<ObjectDisposedException>(() => device.ForEachAsync(disposedTexture, new PixelCoordinates(stats, 0f)));

        // Nothing leaked a command buffer: the device still dispatches normally.
        device.ForAsync(Length, new IncrementAll(buffer)).Wait();
        Assert.Equal(1, buffer[0]);
    }

    [MetalFact]
    public void EmptyGrid_ReturnsACompletedTask()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);

        Assert.True(device.ForAsync(0, new IncrementAll(buffer)).IsCompletedSuccessfully);
        Assert.True(device.ForAsync(4, 0, new IncrementAll(buffer)).IsCompletedSuccessfully);
        Assert.Equal(0, buffer[0]);
    }

    [MetalFact]
    public async Task AsyncAndSyncDispatches_FromManyThreads_Interleave()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(async () =>
        {
            using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);

            for (int i = 0; i < 50; i++)
            {
                Task pending = device.ForAsync(Length, new IncrementAll(buffer));
                device.For(Length, new IncrementAll(buffer));
                await pending;
            }

            Assert.All(buffer.ToArray(), value => Assert.Equal(100, value));
        })));
    }

    /// <summary>Asserts that starting the operation throws before a task is even returned.</summary>
    internal static T ThrowsSynchronously<T>(Func<Task> start)
        where T : Exception => Assert.Throws<T>(() => { _ = start(); });
}
