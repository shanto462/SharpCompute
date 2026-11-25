using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// <c>Copy</c>, <c>Fill</c> and <c>Clear</c> on the device (one command buffer each) and in a
/// <see cref="ComputeContext"/> (a blit encoder between compute encoders, in recorded order).
/// </summary>
public class GpuCopyTests
{
    private const int Length = 4096;

    [MetalFact]
    public void Copy_WholeBuffer_CopiesEveryElement()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] data = Enumerable.Range(0, Length).Select(i => i * 3).ToArray();
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>(data);
        using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(Enumerable.Repeat(-1, Length + 10).ToArray());

        device.Copy(source, destination);

        int[] result = destination.ToArray();
        Assert.Equal(data, result[..Length]);

        // A longer destination keeps its tail.
        Assert.All(result[Length..], value => Assert.Equal(-1, value));
    }

    [MetalTheory]
    [InlineData(0, 0, 100)]
    [InlineData(17, 3, 1)]
    [InlineData(1000, 2000, 2096)]
    [InlineData(4095, 0, 1)]
    [InlineData(0, 4095, 1)]
    [InlineData(5, 4096, 0)]
    [InlineData(4096, 0, 0)]
    public void Copy_Range_CopiesExactlyThatRange(int sourceOffset, int destinationOffset, int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] data = Enumerable.Range(1, Length).ToArray();
        using ReadWriteBuffer<int> source = device.AllocateReadWriteBuffer<int>(data);
        using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(Length);

        device.Copy(source, sourceOffset, destination, destinationOffset, count);

        int[] expected = new int[Length];
        data.AsSpan(sourceOffset, count).CopyTo(expected.AsSpan(destinationOffset));
        Assert.Equal(expected, destination.ToArray());
        Assert.Equal(data, source.ToArray());
    }

    [MetalFact]
    public void Copy_OfElementsSmallerThanFourBytes_AtOddOffsets()
    {
        // Byte offsets and sizes that are not multiples of 4 (UnalignedCopyTests covers every alignment).
        GraphicsDevice device = GraphicsDevice.GetDefault();
        byte[] data = Enumerable.Range(0, 64).Select(i => (byte)(i + 1)).ToArray();
        using ReadWriteBuffer<byte> source = device.AllocateReadWriteBuffer<byte>(data);
        using ReadWriteBuffer<byte> destination = device.AllocateReadWriteBuffer<byte>(64);

        device.Copy(source, 3, destination, 5, 7);
        device.Fill(destination.Slice(21, 5), 0xAB);

        byte[] expected = new byte[64];
        data.AsSpan(3, 7).CopyTo(expected.AsSpan(5));
        expected.AsSpan(21, 5).Fill(0xAB);
        Assert.Equal(expected, destination.ToArray());
    }

    [MetalFact]
    public void Copy_BetweenViews_IsRelativeToEachView()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> source = device.AllocateReadWriteBuffer<Float4>(Enumerable.Range(0, 100).Select(i => new Float4(i, -i, i * 2, 1)).ToArray());
        using ReadWriteBuffer<Float4> destination = device.AllocateReadWriteBuffer<Float4>(100);

        // Elements 22..26 of the source go to elements 61..65 of the destination.
        device.Copy(source.Slice(20, 30), 2, destination.Slice(50, 40).Slice(10, 20), 1, 5);

        Float4[] result = destination.ToArray();

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i is >= 61 and < 66 ? new Float4(i - 39, -(i - 39), (i - 39) * 2, 1) : default, result[i]);
        }

        // The whole-buffer form copies the whole view.
        device.Copy(source.Slice(90, 10), destination.Slice(0, 10));
        Assert.Equal(new Float4(90, -90, 180, 1), destination[0]);
        Assert.Equal(new Float4(99, -99, 198, 1), destination[9]);
        Assert.Equal(default, destination[10]);
    }

    [MetalFact]
    public void Copy_WithinOneBuffer_WorksForDisjointRanges_AndThrowsForOverlappingOnes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] data = Enumerable.Range(0, 100).ToArray();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(data);

        // Touching but disjoint: [0, 50) -> [50, 100).
        device.Copy(buffer, 0, buffer, 50, 50);
        Assert.Equal(data[..50].Concat(data[..50]), buffer.ToArray());

        buffer.CopyFrom(data);

        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(buffer, buffer)).ParamName);
        Assert.Throws<ArgumentException>(() => device.Copy(buffer, 0, buffer, 49, 50));
        Assert.Throws<ArgumentException>(() => device.Copy(buffer, 49, buffer, 0, 50));
        Assert.Throws<ArgumentException>(() => device.Copy(buffer, 10, buffer, 10, 1));
        Assert.Throws<ArgumentException>(() => device.Copy(buffer, 0, buffer, 0, 100));

        // Views of one buffer overlap where their elements do, wherever each view starts.
        Assert.Throws<ArgumentException>(() => device.Copy(buffer.Slice(0, 60), buffer.Slice(40, 60)));
        Assert.Throws<ArgumentException>(() => device.Copy(buffer.Slice(10, 20), 5, buffer.Slice(0, 100), 20, 10));
        Assert.Throws<ArgumentException>(() => device.Copy(buffer.Slice(20, 30), buffer));

        // Nothing was copied by the rejected calls.
        Assert.Equal(data, buffer.ToArray());

        // Disjoint views of one buffer are fine, and so is a zero-length copy at the same position.
        device.Copy(buffer.Slice(0, 10), buffer.Slice(90, 10));
        device.Copy(buffer, 10, buffer, 10, 0);
        Assert.Equal(data[..90].Concat(data[..10]), buffer.ToArray());

        using ComputeContext context = device.CreateComputeContext();
        Assert.Throws<ArgumentException>(() => context.Copy(buffer, buffer));
        Assert.Throws<ArgumentException>(() => context.Copy(buffer, 0, buffer, 1, 2));
        Assert.Equal(0, context.PendingCount);
    }

    [MetalFact]
    public void Copy_RejectsRangesOutsideTheBuffers()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> source = device.AllocateReadWriteBuffer<int>(100);
        using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(50);

        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(source, destination)).ParamName);
        Assert.Equal("sourceOffset", Assert.Throws<ArgumentOutOfRangeException>(() => device.Copy(source, -1, destination, 0, 1)).ParamName);
        Assert.Equal("destinationOffset", Assert.Throws<ArgumentOutOfRangeException>(() => device.Copy(source, 0, destination, -1, 1)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => device.Copy(source, 0, destination, 0, -1)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => device.Copy(source, 0, destination, 0, 51)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => device.Copy(source, 90, destination, 0, 11)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => device.Copy(source, 0, destination, 40, 11)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => device.Copy(source, 101, destination, 0, 0)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => device.Copy(source, 0, destination, int.MaxValue, 1)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => device.Copy(source, int.MaxValue, destination, 0, int.MaxValue)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => device.Copy((Buffer<int>)null!, destination)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentNullException>(() => device.Copy(source, (Buffer<int>)null!)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentNullException>(() => device.Copy(null!, 0, destination, 0, 1)).ParamName);
        Assert.Equal("buffer", Assert.Throws<ArgumentNullException>(() => device.Fill<int>(null!, 1)).ParamName);
        Assert.Equal("buffer", Assert.Throws<ArgumentNullException>(() => device.Clear<int>(null!)).ParamName);

        // An empty source copies nothing into anything.
        using ReadWriteBuffer<int> empty = device.AllocateReadWriteBuffer<int>(0);
        device.Copy(empty, destination);
        device.Copy(empty, empty);
        device.Fill(empty, 7);
    }

    [MetalFact]
    public void Fill_SetsEveryByte_AndClearZeroes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(Enumerable.Range(0, Length).ToArray());
        using ReadWriteBuffer<Float4> vectors = device.AllocateReadWriteBuffer<Float4>(Enumerable.Repeat(new Float4(1, 2, 3, 4), 100).ToArray());

        device.Fill(ints, 0xFF);
        Assert.All(ints.ToArray(), value => Assert.Equal(-1, value));

        device.Fill(ints, 0x01);
        Assert.All(ints.ToArray(), value => Assert.Equal(0x01010101, value));

        device.Clear(ints);
        Assert.All(ints.ToArray(), value => Assert.Equal(0, value));

        device.Clear(vectors);
        Assert.All(vectors.ToArray(), value => Assert.Equal(default, value));
    }

    [MetalFact]
    public void Fill_OfAView_TouchesOnlyTheView()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Enumerable.Repeat(5, 100).ToArray());

        device.Fill(buffer.Slice(10, 20), 0xFF);
        device.Clear(buffer.Slice(50, 1));
        device.Clear(buffer.Slice(60, 0));

        int[] result = buffer.ToArray();

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i is >= 10 and < 30 ? -1 : i == 50 ? 0 : 5, result[i]);
        }
    }

    [MetalFact]
    public void Context_DispatchCopyDispatch_RunInRecordedOrder()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteBuffer<int> b = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteBuffer<int> c = device.AllocateReadWriteBuffer<int>(Length);

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(Length, new IncrementAll(a));   // a = 1
            context.Copy(a, b);                         // b = 1: the copy sees the dispatch before it
            context.For(Length, new IncrementAll(a));   // a = 2: same pipeline as before the copy
            context.For(Length, new IncrementAll(b));   // b = 2: the dispatch sees the copy
            context.Copy(b, c);                         // c = 2
            context.Fill(a, 0);                         // a = 0, after both increments
            context.For(Length, new IncrementAll(c));   // c = 3

            Assert.Equal(7, context.PendingCount);
        }

        Assert.All(a.ToArray(), value => Assert.Equal(0, value));
        Assert.All(b.ToArray(), value => Assert.Equal(2, value));
        Assert.All(c.ToArray(), value => Assert.Equal(3, value));
    }

    [MetalFact]
    public void Context_LongChainOfAlternatingDispatchesAndCopies_KeepsExactOrder()
    {
        // Each round increments `a` and copies it to the next slice of the log: slice k must hold k + 1.
        // Any reordering of a copy with its neighbours changes a slice.
        const int rounds = 100;
        const int width = 256;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(width);
        using ReadWriteBuffer<int> log = device.AllocateReadWriteBuffer<int>(rounds * width);

        using (ComputeContext context = device.CreateComputeContext())
        {
            for (int round = 0; round < rounds; round++)
            {
                context.For(width, new IncrementAll(a));
                context.Copy(a, 0, log, round * width, width);
            }
        }

        int[] result = log.ToArray();

        for (int i = 0; i < result.Length; i++)
        {
            Assert.Equal((i / width) + 1, result[i]);
        }
    }

    [MetalFact]
    public void Context_ClearBetweenPasses_ResetsACounterOnTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> values = device.AllocateReadOnlyBuffer<int>(Enumerable.Range(0, Length).ToArray());
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([123]);
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(3);

        using (ComputeContext context = device.CreateComputeContext())
        {
            for (int pass = 0; pass < 3; pass++)
            {
                context.Clear(counter);
                context.For(Length, new CountAbove(values, counter, cutoff: 1000 * (pass + 1)));
                context.Copy(counter, 0, counts, pass, 1);
            }
        }

        Assert.Equal([Length - 1000, Length - 2000, Length - 3000], counts.ToArray());
    }

    [MetalFact]
    public void Context_WithOnlyCopiesAndFills_Executes_AndCanBeReused()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(Enumerable.Range(0, 64).ToArray());
        using ReadWriteBuffer<int> b = device.AllocateReadWriteBuffer<int>(64);
        using ComputeContext context = device.CreateComputeContext();

        context.Copy(a, b);
        context.Fill(a, 0xFF);
        Assert.Equal(2, context.PendingCount);

        // Nothing has run yet.
        Assert.Equal(0, b[63]);
        context.Execute();
        Assert.Equal(0, context.PendingCount);
        Assert.Equal(Enumerable.Range(0, 64), b.ToArray());
        Assert.Equal(-1, a[0]);

        // The recycled batch records dispatches after having recorded only blits.
        context.For(64, new IncrementAll(b));
        context.Clear(a);
        context.Execute();
        Assert.Equal(Enumerable.Range(1, 64), b.ToArray());
        Assert.Equal(0, a[0]);

        // Zero-length operations record nothing.
        context.Copy(a, 0, b, 0, 0);
        context.Fill(a.Slice(0, 0), 1);
        Assert.Equal(0, context.PendingCount);
    }

    [MetalFact]
    public async Task Context_ExecuteAsync_RunsCopies()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteBuffer<int> b = device.AllocateReadWriteBuffer<int>(Length);
        await using ComputeContext context = device.CreateComputeContext();

        context.For(Length, new IncrementAll(a));
        context.Copy(a, b);
        context.For(Length, new IncrementAll(b));
        await context.ExecuteAsync();

        Assert.All(b.ToArray(), value => Assert.Equal(2, value));
    }

    [MetalFact]
    public void SynchronousCopy_IsOrderedAfterPendingAsyncDispatches()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteBuffer<int> b = device.AllocateReadWriteBuffer<int>(Length);
        Task[] pending = new Task[20];

        for (int i = 0; i < pending.Length; i++)
        {
            pending[i] = device.ForAsync(Length, new IncrementAll(a));
        }

        // Committed after the dispatches, so it copies their result without awaiting them first.
        device.Copy(a, b);

        Assert.All(b.ToArray(), value => Assert.Equal(pending.Length, value));
        Task.WaitAll(pending);
    }

    [MetalFact]
    public void BuffersDisposedWhilePending_StayAliveUntilTheCopyHasRun()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<int> source = device.AllocateReadWriteBuffer<int>(Enumerable.Range(0, Length).ToArray());
        ReadWriteBuffer<int> middle = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(Length);
        nint mtlSource = ObjC.Retain(source.Handle.DangerousGetHandle());
        nint mtlMiddle = ObjC.Retain(middle.Handle.DangerousGetHandle());

        try
        {
            ComputeContext context = device.CreateComputeContext();
            context.Copy(source, middle);
            context.Fill(source, 0);
            context.Copy(middle, destination);

            source.Dispose();
            middle.Dispose();
            Assert.True(ComputeContextTests.RetainCount(mtlSource) > 1);
            Assert.True(ComputeContextTests.RetainCount(mtlMiddle) > 1);

            context.Dispose();

            Assert.Equal(Enumerable.Range(0, Length), destination.ToArray());
            ComputeContextTests.WaitForRetainCount(mtlSource, 1);
            ComputeContextTests.WaitForRetainCount(mtlMiddle, 1);
        }
        finally
        {
            ObjC.Release(mtlSource);
            ObjC.Release(mtlMiddle);
        }
    }

    [MetalFact]
    public void AbandonedContext_WithPendingCopies_ReleasesItsLeases()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<int> source = device.AllocateReadWriteBuffer<int>(Length);
        using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(Enumerable.Repeat(9, Length).ToArray());
        nint mtlSource = ObjC.Retain(source.Handle.DangerousGetHandle());

        try
        {
            RecordAndLeak(device, source, destination);
            source.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Never executed: the copy did not run, and the leaked batch gave its leases back.
            Assert.All(destination.ToArray(), value => Assert.Equal(9, value));
            ComputeContextTests.WaitForRetainCount(mtlSource, 1);
        }
        finally
        {
            ObjC.Release(mtlSource);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordAndLeak(GraphicsDevice device, ReadWriteBuffer<int> source, ReadWriteBuffer<int> destination)
    {
        ComputeContext context = device.CreateComputeContext();
        context.Copy(source, destination);
        context.Clear(destination);
    }

    [MetalFact]
    public void DisposedBuffers_AreRejected_AndRecordNothing()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> live = device.AllocateReadWriteBuffer<int>(16);
        ReadWriteBuffer<int> disposed = device.AllocateReadWriteBuffer<int>(16);
        disposed.Dispose();

        Assert.Throws<ObjectDisposedException>(() => device.Copy(disposed, live));
        Assert.Throws<ObjectDisposedException>(() => device.Copy(live, disposed));
        Assert.Throws<ObjectDisposedException>(() => device.Copy(live, 0, disposed, 0, 0));
        Assert.Throws<ObjectDisposedException>(() => device.Fill(disposed, 1));
        Assert.Throws<ObjectDisposedException>(() => device.Clear(disposed));

        using ComputeContext context = device.CreateComputeContext();
        Assert.Throws<ObjectDisposedException>(() => context.Copy(disposed, live));
        Assert.Throws<ObjectDisposedException>(() => context.Copy(live, disposed));
        Assert.Throws<ObjectDisposedException>(() => context.Fill(disposed, 1));
        Assert.Throws<ObjectDisposedException>(() => context.Clear(disposed));
        Assert.Equal(0, context.PendingCount);

        // A failed first operation leaves the context usable.
        context.Fill(live, 0xFF);
        context.Execute();
        Assert.Equal(-1, live[15]);
    }

    [MetalFact]
    public void BuffersOfAnotherDevice_AreRejected()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using GraphicsDevice other = GraphicsDevice.CreateStandInForAnotherDevice();
        using ReadWriteBuffer<int> mine = device.AllocateReadWriteBuffer<int>(16);
        using ReadWriteBuffer<int> foreign = other.AllocateReadWriteBuffer<int>(16);

        Assert.Equal("source", Assert.Throws<ArgumentException>(() => device.Copy(foreign, mine)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(mine, foreign)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(mine, 0, foreign.Slice(4, 4), 0, 1)).ParamName);
        Assert.Equal("buffer", Assert.Throws<ArgumentException>(() => device.Fill(foreign, 1)).ParamName);
        Assert.Equal("buffer", Assert.Throws<ArgumentException>(() => device.Clear(foreign)).ParamName);

        using ComputeContext context = device.CreateComputeContext();
        Assert.Throws<ArgumentException>(() => context.Copy(foreign, mine));
        Assert.Throws<ArgumentException>(() => context.Copy(mine, foreign));
        Assert.Throws<ArgumentException>(() => context.Fill(foreign, 1));
        Assert.Equal(0, context.PendingCount);

        // On its own device the buffer copies fine.
        using ReadWriteBuffer<int> foreignTarget = other.AllocateReadWriteBuffer<int>(16);
        foreign.AsSpan().Fill(3);
        other.Copy(foreign, foreignTarget);
        Assert.All(foreignTarget.ToArray(), value => Assert.Equal(3, value));
    }

    [MetalFact]
    public void DisposedDevice_RejectsCopiesAndFills()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(16);
        ReadWriteBuffer<int> b = device.AllocateReadWriteBuffer<int>(16);
        ComputeContext context = device.CreateComputeContext();
        device.Dispose();

        Assert.Throws<ObjectDisposedException>(() => device.Copy(a, b));
        Assert.Throws<ObjectDisposedException>(() => device.Fill(a, 1));
        Assert.Throws<ObjectDisposedException>(() => context.Copy(a, b));
        Assert.Throws<ObjectDisposedException>(() => context.Clear(a));

        a.Dispose();
        b.Dispose();
        context.Dispose();
    }

    [MetalFact]
    public void MappedReadOnlyMemory_CanBeCopiedFrom_ButNotWritten()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string path = Path.Combine(Path.GetTempPath(), $"sharpcompute-copy-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, MemoryMarshal.AsBytes<int>(Enumerable.Range(0, 1000).ToArray()).ToArray());

        try
        {
            using ReadOnlyBuffer<int> mapped = device.MapReadOnlyBuffer<int>(path);
            using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(1000);

            device.Copy(mapped, buffer);
            Assert.Equal(Enumerable.Range(0, 1000), buffer.ToArray());

            Assert.Throws<NotSupportedException>(() => device.Copy(buffer, mapped));
            Assert.Throws<NotSupportedException>(() => device.Copy(buffer, 0, mapped.Slice(10, 10), 0, 1));
            Assert.Throws<NotSupportedException>(() => device.Fill(mapped, 0));
            Assert.Throws<NotSupportedException>(() => device.Clear(mapped.Slice(0, 4)));

            using ComputeContext context = device.CreateComputeContext();
            Assert.Throws<NotSupportedException>(() => context.Copy(buffer, mapped));
            Assert.Throws<NotSupportedException>(() => context.Clear(mapped));
            Assert.Equal(0, context.PendingCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [MetalFact]
    public void SimulatedGpuError_NamesCopiesInTheMessage()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> a = device.AllocateReadWriteBuffer<int>(16);
        using ReadWriteBuffer<int> b = device.AllocateReadWriteBuffer<int>(16);
        using ComputeContext context = device.CreateComputeContext();

        context.Copy(a, b);
        context.SimulateGpuError("Injected");
        Assert.Contains("a GPU copy or fill: Injected", Assert.Throws<MetalException>(context.Execute).Message);

        context.For(16, new IncrementAll(a));
        context.Copy(a, b);
        context.Clear(a);
        context.SimulateGpuError("Injected");
        string message = Assert.Throws<MetalException>(context.Execute).Message;
        Assert.Contains("a batch of 1 dispatches and 2 copies or fills", message);
        Assert.Contains(typeof(IncrementAll).FullName!, message);
    }

    [MetalFact]
    public void CommandBatch_CommandsStay24Bytes()
    {
        // The recorded commands are the bulk of a batch's memory; the buffer offset fits without growing them.
        Assert.Equal(24, Unsafe.SizeOf<CommandBatch.Command>());
    }

    [MetalTheory]
    [InlineData(64, 64)]
    [InlineData(5, 3)]
    [InlineData(333, 7)]
    public void Copy_TextureToTexture_CopiesEveryPixel(int width, int height)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rgba32[] pixels = Pixels(width, height);
        using ReadOnlyTexture2D<Rgba32> source = device.AllocateReadOnlyTexture2D<Rgba32>(pixels, width, height);
        using ReadWriteTexture2D<Rgba32> destination = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        device.Copy(source, destination);
        Assert.Equal(pixels, destination.ToArray());

        // And back out through a view of the read-write texture, in a context.
        using ReadOnlyTexture2D<Rgba32> again = device.AllocateReadOnlyTexture2D<Rgba32>(width, height);

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.Copy(destination.AsReadOnly(), again);
        }

        Assert.Equal(pixels, again.ToArray());
    }

    [MetalTheory]
    [InlineData(64, 64)]
    [InlineData(5, 3)]
    [InlineData(333, 7)]
    public void Copy_BetweenBufferAndTexture_UsesTightlyPackedRows(int width, int height)
    {
        // 5 and 333 pixels wide: the texture's rows are padded, the buffer's are not.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rgba32[] pixels = Pixels(width, height);
        using ReadWriteBuffer<Rgba32> packed = device.AllocateReadWriteBuffer<Rgba32>(pixels);
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteBuffer<Rgba32> readBack = device.AllocateReadWriteBuffer<Rgba32>((width * height) + 20);

        device.Copy(packed, texture);
        Assert.Equal(pixels, texture.ToArray());

        // Into a view: the pixels land at the view's start and nothing around it is touched.
        device.Copy(texture, readBack.Slice(10));
        Rgba32[] result = readBack.ToArray();
        Assert.Equal(pixels, result[10..^10]);
        Assert.All(result[..10], pixel => Assert.Equal(default, pixel));
        Assert.All(result[^10..], pixel => Assert.Equal(default, pixel));

        // From a view: the first Width × Height pixels after its start.
        using ReadWriteTexture2D<Rgba32> second = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        device.Copy(readBack.Slice(10), second);
        Assert.Equal(pixels, second.ToArray());
    }

    [MetalFact]
    public void Copy_BetweenBufferAndTexture_OfOneAndTwoBytePixels_AtOddWidths()
    {
        // Packed rows of 5 and 10 bytes: not multiples of 4, and shorter than the texture's padded rows.
        const int width = 5, height = 3;
        GraphicsDevice device = GraphicsDevice.GetDefault();

        R8[] bytes = Enumerable.Range(0, width * height).Select(i => new R8((byte)(i * 17))).ToArray();
        using ReadWriteBuffer<R8> packedBytes = device.AllocateReadWriteBuffer<R8>(bytes);
        using ReadWriteTexture2D<R8> byteTexture = device.AllocateReadWriteTexture2D<R8>(width, height);
        using ReadWriteBuffer<R8> bytesBack = device.AllocateReadWriteBuffer<R8>((width * height) + 3);

        device.Copy(packedBytes, byteTexture);
        device.Copy(byteTexture, bytesBack.Slice(3));
        Assert.Equal(bytes, byteTexture.ToArray());
        Assert.Equal(bytes, bytesBack.ToArray()[3..]);
        Assert.All(bytesBack.ToArray()[..3], pixel => Assert.Equal(default, pixel));

        Half[] halves = Enumerable.Range(0, width * height).Select(i => (Half)(i * 0.25f)).ToArray();
        using ReadWriteBuffer<Half> packedHalves = device.AllocateReadWriteBuffer<Half>(halves);
        using ReadWriteTexture2D<Half> halfTexture = device.AllocateReadWriteTexture2D<Half>(width, height);
        using ReadOnlyTexture2D<Half> halfCopy = device.AllocateReadOnlyTexture2D<Half>(width, height);
        using ReadWriteBuffer<Half> halvesBack = device.AllocateReadWriteBuffer<Half>((width * height) + 1);

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.Copy(packedHalves, halfTexture);
            context.Copy(halfTexture, halfCopy);
            context.Copy(halfCopy, halvesBack.Slice(1));
        }

        Assert.Equal(halves, halfCopy.ToArray());
        Assert.Equal(halves, halvesBack.ToArray()[1..]);
    }

    [MetalFact]
    public void Copy_OfFloatTextures_KeepsTheBits()
    {
        const int width = 37, height = 5;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Float4[] pixels = Enumerable.Range(0, width * height).Select(i => new Float4(i, -i * 0.5f, float.NaN, float.PositiveInfinity)).ToArray();
        using ReadWriteBuffer<Float4> packed = device.AllocateReadWriteBuffer<Float4>(pixels);
        using ReadWriteTexture2D<Float4> a = device.AllocateReadWriteTexture2D<Float4>(width, height);
        using ReadWriteTexture2D<Float4> b = device.AllocateReadWriteTexture2D<Float4>(width, height);
        using ReadWriteBuffer<Float4> readBack = device.AllocateReadWriteBuffer<Float4>(width * height);

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.Copy(packed, a);
            context.Copy(a, b);
            context.Copy(b, readBack);
            Assert.Equal(3, context.PendingCount);
        }

        Assert.Equal(MemoryMarshal.AsBytes<Float4>(pixels).ToArray(), MemoryMarshal.AsBytes(readBack.AsReadOnlySpan()).ToArray());
    }

    [MetalFact]
    public void Context_TextureCopies_AreOrderedWithDispatches()
    {
        const int width = 96, height = 40;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(width * height);
        using ReadWriteTexture2D<float> texture = device.AllocateReadWriteTexture2D<float>(width, height);
        using ReadWriteBuffer<float> first = device.AllocateReadWriteBuffer<float>(width * height);
        using ReadWriteBuffer<float> second = device.AllocateReadWriteBuffer<float>(width * height);

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(width, height, new AddToTexture(texture, 1f));  // 1
            context.Copy(texture, first);                               // first = 1
            context.For(width, height, new AddToTexture(texture, 2f));  // 3
            context.Copy(texture, second);                              // second = 3
        }

        Assert.All(first.ToArray(), value => Assert.Equal(1f, value));
        Assert.All(second.ToArray(), value => Assert.Equal(3f, value));
    }

    [MetalFact]
    public void TextureCopies_RejectMismatchedOrForeignResources()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using GraphicsDevice other = GraphicsDevice.CreateStandInForAnotherDevice();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(8, 4);
        using ReadWriteTexture2D<Rgba32> wide = device.AllocateReadWriteTexture2D<Rgba32>(9, 4);
        using ReadWriteTexture2D<Rgba32> foreign = other.AllocateReadWriteTexture2D<Rgba32>(8, 4);
        using ReadWriteBuffer<Rgba32> small = device.AllocateReadWriteBuffer<Rgba32>(31);
        using ReadWriteBuffer<Rgba32> exact = device.AllocateReadWriteBuffer<Rgba32>(32);
        using ReadWriteBuffer<Rgba32> foreignBuffer = other.AllocateReadWriteBuffer<Rgba32>(32);
        ReadWriteTexture2D<Rgba32> disposed = device.AllocateReadWriteTexture2D<Rgba32>(8, 4);
        disposed.Dispose();

        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(texture, wide)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(texture, texture)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(texture.AsReadOnly(), texture)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentException>(() => device.Copy(foreign, texture)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(texture, foreign)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentException>(() => device.Copy(small, texture)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(texture, small)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentException>(() => device.Copy(foreignBuffer, texture)).ParamName);
        Assert.Equal("destination", Assert.Throws<ArgumentException>(() => device.Copy(texture, foreignBuffer)).ParamName);
        Assert.Throws<ObjectDisposedException>(() => device.Copy(disposed, texture));
        Assert.Throws<ObjectDisposedException>(() => device.Copy(texture, disposed));
        Assert.Throws<ObjectDisposedException>(() => device.Copy(exact, disposed));
        Assert.Throws<ObjectDisposedException>(() => device.Copy(disposed, exact));
        Assert.Throws<ArgumentNullException>(() => device.Copy((Texture2D<Rgba32>)null!, texture));
        Assert.Throws<ArgumentNullException>(() => device.Copy(exact, (Texture2D<Rgba32>)null!));

        using ComputeContext context = device.CreateComputeContext();
        Assert.Throws<ArgumentException>(() => context.Copy(texture, wide));
        Assert.Throws<ArgumentException>(() => context.Copy(small, texture));
        Assert.Throws<ArgumentException>(() => context.Copy(texture, small));
        Assert.Throws<ObjectDisposedException>(() => context.Copy(disposed, texture));
        Assert.Equal(0, context.PendingCount);
    }

    [MetalFact]
    public void TextureDisposedWhilePending_StaysAliveUntilTheCopyHasRun()
    {
        const int width = 64, height = 64;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rgba32[] pixels = Pixels(width, height);
        ReadWriteTexture2D<Rgba32> source = device.AllocateReadWriteTexture2D<Rgba32>(pixels, width, height);
        using ReadWriteBuffer<Rgba32> destination = device.AllocateReadWriteBuffer<Rgba32>(width * height);
        nint mtlTexture = ObjC.Retain(source.Handle.DangerousGetHandle());

        try
        {
            ComputeContext context = device.CreateComputeContext();
            context.Copy(source, destination);
            source.Dispose();
            Assert.True(ComputeContextTests.RetainCount(mtlTexture) > 1);

            context.Dispose();

            Assert.Equal(pixels, destination.ToArray());
            ComputeContextTests.WaitForRetainCount(mtlTexture, 1);
        }
        finally
        {
            ObjC.Release(mtlTexture);
        }
    }

    private static Rgba32[] Pixels(int width, int height) =>
        Enumerable.Range(0, width * height).Select(i => new Rgba32((byte)i, (byte)(i >> 8), (byte)(i * 7), (byte)(255 - (i % 200)))).ToArray();
}
