using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// <c>Slice</c>: a view is the same <c>MTLBuffer</c> bound at an offset, relative to its start
/// everywhere, and lives as long as the buffer it was made from.
/// </summary>
public class BufferViewTests
{
    [MetalFact]
    public void Slice_OnTheCpu_IsRelativeToItsStart()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Enumerable.Range(0, 100).ToArray());
        ReadWriteBuffer<int> view = buffer.Slice(10, 20);

        Assert.Equal(20, view.Length);
        Assert.Same(device, view.Device);
        Assert.True(view.IsCpuWritable);
        Assert.Equal(10, view[0]);
        Assert.Equal(29, view[19]);
        Assert.Equal(Enumerable.Range(10, 20), view.ToArray());
        Assert.Equal(Enumerable.Range(10, 20), view.AsReadOnlySpan().ToArray());

        // Writes through the view land in the source, and the other way round: it is the same memory.
        view[3] = -1;
        view.AsSpan()[4] = -2;
        buffer[15] = -3;
        Assert.Equal(-1, buffer[13]);
        Assert.Equal(-2, buffer[14]);
        Assert.Equal(-3, view[5]);

        int[] copy = new int[25];
        view.CopyTo(copy);
        Assert.Equal(10, copy[0]);
        Assert.Equal(29, copy[19]);
        Assert.Equal(0, copy[20]);

        view.CopyFrom([7, 8]);
        Assert.Equal(7, buffer[10]);
        Assert.Equal(8, buffer[11]);
        Assert.Equal(9, buffer[9]);
        Assert.Equal(12, buffer[12]);
    }

    [MetalFact]
    public void Slice_IndexesAndCopies_AreBoundsCheckedAgainstTheView()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(100);
        ReadWriteBuffer<int> view = buffer.Slice(10, 20);

        // Inside the source but outside the view.
        Assert.Throws<ArgumentOutOfRangeException>(() => view[20]);
        Assert.Throws<ArgumentOutOfRangeException>(() => view[-1]);
        Assert.Throws<ArgumentException>(() => view.CopyTo(new int[19]));
        Assert.Throws<ArgumentException>(() => view.CopyFrom(new int[21]));
    }

    [MetalFact]
    public void Slice_RejectsRangesOutsideTheBuffer()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(100);
        using ReadOnlyBuffer<int> readOnly = device.AllocateReadOnlyBuffer<int>(100);

        Assert.Equal("start", Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(-1, 1)).ParamName);
        Assert.Equal("start", Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(101, 0)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(0, 101)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(90, 11)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(10, -1)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(1, int.MaxValue)).ParamName);
        Assert.Equal("start", Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(-1)).ParamName);
        Assert.Equal("start", Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(101)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => readOnly.Slice(90, 11)).ParamName);
        Assert.Equal("start", Assert.Throws<ArgumentOutOfRangeException>(() => readOnly.Slice(101)).ParamName);

        // The whole buffer, the empty ranges at both ends and the open-ended form are all fine.
        Assert.Equal(100, buffer.Slice(0, 100).Length);
        Assert.Equal(0, buffer.Slice(0, 0).Length);
        Assert.Equal(0, buffer.Slice(100, 0).Length);
        Assert.Equal(0, buffer.Slice(100).Length);
        Assert.Equal(75, buffer.Slice(25).Length);
        Assert.Equal(75, readOnly.Slice(25).Length);
    }

    [MetalFact]
    public void Slice_OfASlice_IsRelativeToTheOuterSlice()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Enumerable.Range(0, 100).ToArray());
        ReadWriteBuffer<int> outer = buffer.Slice(20, 60);
        ReadWriteBuffer<int> inner = outer.Slice(5, 10);

        Assert.Equal(Enumerable.Range(25, 10), inner.ToArray());
        Assert.Same(buffer, inner.Root);
        Assert.Equal(25, inner.StartInRoot);
        Assert.Equal((nuint)(25 * sizeof(int)), inner.ByteOffset);
        Assert.Throws<ArgumentOutOfRangeException>(() => outer.Slice(55, 6));

        // The GPU sees the nested view at its real position too.
        device.For(inner.Length, new WriteIndexAndLength(inner));

        int[] result = buffer.ToArray();

        for (int i = 0; i < result.Length; i++)
        {
            Assert.Equal(i is >= 25 and < 35 ? 10_000 + (i - 25) : i, result[i]);
        }
    }

    [MetalFact]
    public void Slice_InAShader_BindsAtItsOffset_AndReportsItsOwnLength()
    {
        const int length = 1000;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Enumerable.Repeat(-1, length).ToArray());
        ReadWriteBuffer<int> view = buffer.Slice(300, 257);

        device.For(view.Length, new WriteIndexAndLength(view));

        int[] result = buffer.ToArray();

        for (int i = 0; i < length; i++)
        {
            // Nothing outside the view is touched; inside it, index 0 is element 300 and Length is 257.
            Assert.Equal(i is >= 300 and < 557 ? 257_000 + (i - 300) : -1, result[i]);
        }
    }

    [MetalFact]
    public void Slices_OfReadOnlyAndReadWriteBuffers_BindTogether()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> a = device.AllocateReadOnlyBuffer<int>(Enumerable.Range(0, 64).ToArray());
        using ReadOnlyBuffer<int> b = device.AllocateReadOnlyBuffer<int>(Enumerable.Range(0, 64).Select(i => i * 100).ToArray());
        using ReadWriteBuffer<int> sum = device.AllocateReadWriteBuffer<int>(64);

        // Three different offsets in one dispatch; the hidden length constant is that of the view of `a`.
        device.For(8, new AddBuffers(a.Slice(10, 8), b.Slice(20, 8), sum.Slice(40, 8)));

        int[] result = sum.ToArray();

        for (int i = 0; i < 64; i++)
        {
            Assert.Equal(i is >= 40 and < 48 ? (10 + i - 40) + ((20 + i - 40) * 100) + 8 : 0, result[i]);
        }
    }

    [MetalFact]
    public void Slice_AtAnOddIndexOfTwoByteElements_BindsAtAnOffsetThatIsNotAMultipleOfFour()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Half> buffer = device.AllocateReadWriteBuffer<Half>(Enumerable.Range(0, 32).Select(i => (Half)i).ToArray());
        ReadWriteBuffer<Half> view = buffer.Slice(3, 7);

        Assert.Equal((nuint)6, view.ByteOffset);
        device.For(view.Length, new IncrementHalves(view));

        Half[] result = buffer.ToArray();

        for (int i = 0; i < 32; i++)
        {
            Assert.Equal((Half)(i is >= 3 and < 10 ? i + 1 : i), result[i]);
        }
    }

    [MetalFact]
    public void Slice_OfSixteenByteAlignedElements_BindsAtAMultipleOfSixteen()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> buffer = device.AllocateReadWriteBuffer<Float4>(Enumerable.Range(0, 16).Select(i => new Float4(i, i, i, 1)).ToArray());
        ReadWriteBuffer<Float4> view = buffer.Slice(5, 3);

        Assert.Equal((nuint)80, view.ByteOffset);
        device.For(view.Length, new ScaleFloat4(view, 10f));

        Float4[] result = buffer.ToArray();

        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(i is >= 5 and < 8 ? new Float4(i * 10, i * 10, i * 10, 10) : new Float4(i, i, i, 1), result[i]);
        }
    }

    [MetalFact]
    public void EmptySlice_BindsLikeAnEmptyBuffer()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>([1, 2, 3, 4]);
        using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(4);

        // An empty view at the very end would be an offset Metal rejects; it binds the start of the
        // buffer instead, and a shader that respects Length reads nothing from it.
        ReadOnlyBuffer<int> empty = source.Slice(4, 0);
        Assert.Equal((nuint)16, empty.ByteOffset);
        Assert.Equal((nuint)0, empty.BindOffset);
        Assert.Empty(empty.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => empty[0]);

        device.For(4, new CopyUpToLength(empty, destination));
        Assert.Equal([-1, -1, -1, -1], destination.ToArray());

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(4, new CopyUpToLength(source.Slice(1, 2), destination));
            context.For(2, new CopyUpToLength(source.Slice(0, 0), destination.Slice(2, 2)));
        }

        Assert.Equal([2, 3, -1, -1], destination.ToArray());

        device.ForCpu(4, new CopyUpToLength(empty, destination));
        Assert.Equal([-1, -1, -1, -1], destination.ToArray());
    }

    [MetalFact]
    public void Slice_InAComputeContext_AndAsync_CarriesItsOffset()
    {
        const int length = 512;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(length);

        using (ComputeContext context = device.CreateComputeContext())
        {
            // The same pipeline four times: only the recorded offset tells the dispatches apart.
            for (int part = 0; part < 4; part++)
            {
                context.For(128, new IncrementAll(buffer.Slice(part * 128, 128)));
                context.For(64, new IncrementAll(buffer.Slice((part * 128) + 32, 64)));
            }
        }

        int[] result = buffer.ToArray();

        for (int i = 0; i < length; i++)
        {
            Assert.Equal((i % 128) is >= 32 and < 96 ? 2 : 1, result[i]);
        }

        Task first = device.ForAsync(100, new IncrementAll(buffer.Slice(400, 100)));
        Task second = device.ForAsync(50, new IncrementAll(buffer.Slice(450, 50)));
        Task.WaitAll(first, second);

        // 399 and 400 had 1, 450 had 2 (it is in the middle of its 128-element part), 500 had 1.
        Assert.Equal(1, buffer[399]);
        Assert.Equal(2, buffer[400]);
        Assert.Equal(4, buffer[450]);
        Assert.Equal(1, buffer[500]);
    }

    [MetalFact]
    public void Slice_OnTheCpuAndSplit_MatchesTheGpu()
    {
        const int length = 100_000;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(length + 50);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(length + 50);
        using ReadWriteBuffer<int> split = device.AllocateReadWriteBuffer<int>(length + 50);

        device.For(length, new WriteIndexAndLength(gpu.Slice(17, length)));
        device.ForCpu(length, new WriteIndexAndLength(cpu.Slice(17, length)));
        SplitDispatchInfo info = device.ForSplit(length, new WriteIndexAndLength(split.Slice(17, length)), cpuFraction: 0.5);

        Assert.True(info.CpuExtent > 0 && info.GpuExtent > 0);

        int[] expected = gpu.ToArray();
        Assert.Equal(0, expected[16]);
        Assert.Equal(length * 1000, expected[17]);
        Assert.Equal((length * 1000) + length - 1, expected[16 + length]);
        Assert.Equal(0, expected[17 + length]);
        Assert.Equal(expected, cpu.ToArray());
        Assert.Equal(expected, split.ToArray());
    }

    [MetalFact]
    public void Slice_OnTheCpu_IsBoundsCheckedAgainstTheView()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(100);

        // 20 threads over a 10-element view: the GPU would write into the rest of the buffer, the CPU throws.
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForCpu(20, new WriteIndexAndLength(buffer.Slice(0, 10))));
    }

    [MetalFact]
    public void DisposingASlice_ReleasesNothing_AndOnlyDisablesTheSlice()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>([1, 2, 3, 4]);
        ReadWriteBuffer<int> view = buffer.Slice(1, 2);
        ReadWriteBuffer<int> sibling = buffer.Slice(1, 2);

        view.Dispose();
        view.Dispose();

        Assert.True(view.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => view[0]);
        Assert.Throws<ObjectDisposedException>(() => view.AsSpan());
        Assert.Throws<ObjectDisposedException>(() => view.ToArray());
        Assert.Throws<ObjectDisposedException>(() => view.Slice(0, 1));
        Assert.Throws<ObjectDisposedException>(() => device.For(2, new IncrementAll(view)));

        // The source and its other views are untouched.
        Assert.False(buffer.IsDisposed);
        Assert.Equal([1, 2, 3, 4], buffer.ToArray());
        Assert.Equal(2, sibling[0]);
        device.For(2, new IncrementAll(sibling));
        Assert.Equal([1, 3, 4, 4], buffer.ToArray());
    }

    [MetalFact]
    public void DisposingTheSource_DisposesEverySliceOfIt()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(100);
        ReadWriteBuffer<int> view = buffer.Slice(10, 20);
        ReadWriteBuffer<int> nested = view.Slice(5, 5);

        buffer.Dispose();

        foreach (ReadWriteBuffer<int> slice in new[] { view, nested })
        {
            Assert.True(slice.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => slice[0]);
            Assert.Throws<ObjectDisposedException>(() => slice.AsSpan());
            Assert.Throws<ObjectDisposedException>(() => slice.AsReadOnlySpan());
            Assert.Throws<ObjectDisposedException>(() => slice.CopyTo(new int[20]));
            Assert.Throws<ObjectDisposedException>(() => slice.Slice(0, 1));
            Assert.Throws<ObjectDisposedException>(() => device.For(5, new IncrementAll(slice)));
            Assert.Throws<ObjectDisposedException>(() => device.ForCpu(5, new WriteIndexAndLength(slice)));
        }

        Assert.Throws<ObjectDisposedException>(() => buffer.Slice(0, 1));

        // Disposing a view of a disposed source is still fine.
        view.Dispose();
    }

    [MetalFact]
    public void SliceOfASlice_DependsOnTheBufferThatOwnsTheMemory_NotOnTheSliceInBetween()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Enumerable.Range(0, 100).ToArray());
        ReadWriteBuffer<int> outer = buffer.Slice(20, 60);
        ReadWriteBuffer<int> inner = outer.Slice(5, 10);

        outer.Dispose();

        // The inner view is its own window onto the buffer's memory; only the buffer can end it.
        Assert.False(inner.IsDisposed);
        Assert.Equal(25, inner[0]);
        device.For(10, new IncrementAll(inner));
        Assert.Equal(26, buffer[25]);
        Assert.Throws<ObjectDisposedException>(() => outer.Slice(0, 1));

        buffer.Dispose();
        Assert.True(inner.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => inner[0]);
    }

    [MetalFact]
    public void SlicesThatAreDropped_AreNotKeptByTheirSource()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(1024);
        Assert.Equal(0, buffer.RegisteredViewCount);

        // A loop that slices every "frame" and keeps nothing: the source's registry of views must not
        // grow with the number of slices ever made, only with the number still alive.
        ReadWriteBuffer<int> kept = buffer.Slice(0, 16);

        for (int round = 0; round < 20; round++)
        {
            SliceAndDrop(buffer, 1000);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.InRange(buffer.RegisteredViewCount, 1, 2100);

        // The one that was kept is still wired to its source (element 15 was written through a dropped slice).
        Assert.Equal(15, kept[15]);
        buffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => kept[15]);
        Assert.Equal(0, buffer.RegisteredViewCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SliceAndDrop(ReadWriteBuffer<int> buffer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            buffer.Slice(i, 8)[0] = i;
        }
    }

    [MetalFact]
    public void SlicingWhileTheSourceIsDisposed_NeverLeavesAUsableSliceBehind()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        for (int round = 0; round < 200; round++)
        {
            ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(256);
            ReadWriteBuffer<int>?[] slices = new ReadWriteBuffer<int>?[64];
            using Barrier barrier = new(2);

            Task slicing = Task.Run(() =>
            {
                barrier.SignalAndWait();

                for (int i = 0; i < slices.Length; i++)
                {
                    try
                    {
                        slices[i] = buffer.Slice(i, 8);
                    }
                    catch (ObjectDisposedException)
                    {
                        // The dispose won the race for this one.
                    }
                }
            });

            Task disposing = Task.Run(() =>
            {
                barrier.SignalAndWait();
                buffer.Dispose();
            });

            Task.WaitAll(slicing, disposing);

            // Whichever side won each race, every slice that exists is disposed now: none can reach freed memory.
            foreach (ReadWriteBuffer<int>? slice in slices)
            {
                if (slice is not null)
                {
                    Assert.True(slice.IsDisposed);
                    Assert.Throws<ObjectDisposedException>(() => slice[0]);
                }
            }
        }
    }

    [MetalFact]
    public void SliceBoundToAPendingBatch_KeepsTheSourceMemoryAlive_UntilTheGpuIsDone()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(4096);
        using ReadWriteBuffer<int> result = device.AllocateReadWriteBuffer<int>(1024);
        nint mtlBuffer = ObjC.Retain(buffer.Handle.DangerousGetHandle());

        try
        {
            ComputeContext context = device.CreateComputeContext();
            ReadWriteBuffer<int> view = buffer.Slice(1024, 1024);

            context.For(1024, new IncrementAll(view));
            context.Copy(view, result);

            // Disposed while pending, view first and then the source: the batch's lease on the source's
            // handle is what keeps the MTLBuffer.
            view.Dispose();
            buffer.Dispose();
            Assert.True(ComputeContextTests.RetainCount(mtlBuffer) > 1);

            context.Dispose();

            Assert.All(result.ToArray(), value => Assert.Equal(1, value));
            ComputeContextTests.WaitForRetainCount(mtlBuffer, 1);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void Slice_FromAnotherDevice_IsRejected()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using GraphicsDevice other = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<int> foreign = other.AllocateReadWriteBuffer<int>(64);
        ReadWriteBuffer<int> view = foreign.Slice(8, 8);

        Assert.Same(other, view.Device);
        Assert.Throws<ArgumentException>(() => device.For(8, new IncrementAll(view)));
        Assert.Throws<ArgumentException>(() => device.ForCpu(8, new WriteIndexAndLength(view)));

        other.For(8, new IncrementAll(view));
        Assert.Equal(1, foreign[8]);
        Assert.Equal(0, foreign[7]);
    }

    [MetalFact]
    public unsafe void Slice_OfWrappedMemory_SharesIt_AndReleaseRunsOnceWithTheSource()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int length = GraphicsDevice.PageSize / sizeof(int);
        void* memory = NativeMemory.AlignedAlloc((nuint)GraphicsDevice.PageSize, (nuint)GraphicsDevice.PageSize);
        new Span<int>(memory, length).Clear();
        int released = 0;

        try
        {
            ReadWriteBuffer<int> wrapped = device.WrapReadWriteBuffer<int>((nint)memory, length, () => released++);
            ReadWriteBuffer<int> view = wrapped.Slice(100, 50);

            device.For(50, new IncrementAll(view));
            Assert.Equal(1, ((int*)memory)[100]);
            Assert.Equal(0, ((int*)memory)[99]);
            Assert.Equal(0, ((int*)memory)[150]);

            view.Dispose();
            Assert.Equal(0, released);
            wrapped.Dispose();
            Assert.Equal(1, released);
        }
        finally
        {
            NativeMemory.AlignedFree(memory);
        }
    }

    [MetalFact]
    public void Slice_OfAMappedFile_IsReadOnlyOnTheCpuToo()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string path = Path.Combine(Path.GetTempPath(), $"sharpcompute-view-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, MemoryMarshal.AsBytes<int>(Enumerable.Range(0, 1000).ToArray()).ToArray());

        try
        {
            using ReadOnlyBuffer<int> mapped = device.MapReadOnlyBuffer<int>(path);
            using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(10);
            ReadOnlyBuffer<int> view = mapped.Slice(500, 10);

            Assert.False(view.IsCpuWritable);
            Assert.Throws<NotSupportedException>(() => view.AsSpan());
            Assert.Throws<NotSupportedException>(() => view.CopyFrom([1]));
            Assert.Equal(500, view[0]);

            device.For(10, new MultiplyInto(view, output, 3));
            Assert.Equal(Enumerable.Range(500, 10).Select(i => i * 3), output.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
