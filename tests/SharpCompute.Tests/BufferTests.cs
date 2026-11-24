using System;
using System.Linq;
using System.Runtime.CompilerServices;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

public class BufferTests
{
    [MetalFact]
    public void AllocateWithLength_IsZeroFilledAndBelongsToTheDevice()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(1000);

        Assert.Equal(1000, buffer.Length);
        Assert.Same(device, buffer.Device);
        Assert.Equal(1000, buffer.AsSpan().Length);
        Assert.All(buffer.ToArray(), value => Assert.Equal(0, value));
    }

    [MetalFact]
    public void AllocateFromData_CopiesTheData()
    {
        int[] data = Enumerable.Range(0, 257).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> readWrite = device.AllocateReadWriteBuffer<int>(data);
        using ReadOnlyBuffer<int> readOnly = device.AllocateReadOnlyBuffer<int>(data);

        data[0] = -1;

        Assert.Equal(Enumerable.Range(0, 257), readWrite.ToArray());
        Assert.Equal(Enumerable.Range(0, 257), readOnly.ToArray());
        Assert.Equal(257, readOnly.Length);
    }

    [MetalFact]
    public void AsSpan_IsAViewOfTheSharedMemoryNotACopy()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(4);

        Span<float> span = buffer.AsSpan();
        span[2] = 21f;

        Assert.Equal(21f, buffer[2]);

        // The GPU writes through the same memory: no readback step.
        device.For(buffer.Length, new MultiplyByTwo(buffer, 2f));

        Assert.Equal(42f, span[2]);
    }

    [MetalFact]
    public void Indexer_ReadsAndWritesByReference()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(3);

        buffer[0] = 10;
        buffer[2] = 30;
        ref int middle = ref buffer[1];
        middle = 20;
        buffer[1]++;

        Assert.Equal([10, 21, 30], buffer.ToArray());

        using ReadOnlyBuffer<int> readOnly = device.AllocateReadOnlyBuffer<int>([7, 8, 9]);

        Assert.Equal(8, readOnly[1]);
    }

    [MetalFact]
    public void Indexer_IsBoundsChecked()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> readWrite = device.AllocateReadWriteBuffer<int>(3);
        using ReadOnlyBuffer<int> readOnly = device.AllocateReadOnlyBuffer<int>([1, 2, 3]);

        Assert.Throws<ArgumentOutOfRangeException>(() => readWrite[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => readWrite[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => readWrite[int.MaxValue]);
        Assert.Throws<ArgumentOutOfRangeException>(() => readOnly[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => readOnly[-1]);
    }

    [MetalFact]
    public void CopyTo_RequiresADestinationAtLeastAsLongAsTheBuffer()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>([1, 2, 3]);

        int[] exact = new int[3];
        int[] larger = [9, 9, 9, 9, 9];
        buffer.CopyTo(exact);
        buffer.CopyTo(larger);

        Assert.Equal([1, 2, 3], exact);
        Assert.Equal([1, 2, 3, 9, 9], larger);
        Assert.Throws<ArgumentException>(() => buffer.CopyTo(new int[2]));
    }

    [MetalFact]
    public void CopyFrom_AcceptsUpToLengthElements()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>([1, 2, 3]);

        buffer.CopyFrom([7, 8]);
        Assert.Equal([7, 8, 3], buffer.ToArray());

        buffer.CopyFrom([4, 5, 6]);
        Assert.Equal([4, 5, 6], buffer.ToArray());

        Assert.Throws<ArgumentException>(() => buffer.CopyFrom([1, 2, 3, 4]));
        Assert.Equal([4, 5, 6], buffer.ToArray());
    }

    [MetalFact]
    public void NegativeLength_Throws()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        Assert.Throws<ArgumentOutOfRangeException>(() => device.AllocateReadWriteBuffer<int>(-1));
    }

    [MetalFact]
    public void LengthBeyondTheDeviceMaximum_Throws()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        // Only meaningful when the device limit is below what int.MaxValue 64-byte elements need
        // (~128 GiB); on a device that could really hold that, there is nothing to assert.
        if (device.MaxBufferLength < (ulong)int.MaxValue * 64)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => device.AllocateReadWriteBuffer<Block64>(int.MaxValue));
        }
    }

    // --- Zero length ---

    [MetalFact]
    public void ZeroLength_ReportsLengthZeroAndExposesNoElements()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> fromLength = device.AllocateReadWriteBuffer<float>(0);
        using ReadWriteBuffer<float> fromData = device.AllocateReadWriteBuffer<float>(ReadOnlySpan<float>.Empty);
        using ReadOnlyBuffer<float> readOnly = device.AllocateReadOnlyBuffer<float>(ReadOnlySpan<float>.Empty);

        Assert.Equal(0, fromLength.Length);
        Assert.Equal(0, fromData.Length);
        Assert.Equal(0, readOnly.Length);
        Assert.True(fromLength.AsSpan().IsEmpty);
        Assert.Empty(fromLength.ToArray());
        Assert.Empty(readOnly.ToArray());

        fromLength.CopyTo(Span<float>.Empty);
        fromLength.CopyFrom(ReadOnlySpan<float>.Empty);

        // The one element of backing storage Metal forces us to allocate is not reachable.
        Assert.Throws<ArgumentOutOfRangeException>(() => fromLength[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => readOnly[0]);
        Assert.Throws<ArgumentException>(() => fromLength.CopyFrom([1f]));
    }

    [MetalFact]
    public void ZeroLength_CanStillBeBoundToAShader()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> empty = device.AllocateReadOnlyBuffer<int>(ReadOnlySpan<int>.Empty);
        using ReadWriteBuffer<int> emptyOutput = device.AllocateReadWriteBuffer<int>(0);

        // The natural call: dispatch over the buffer's length, which is a no-op.
        device.For(empty.Length, new Square(empty, emptyOutput));

        // And a real dispatch with an empty buffer bound: Metal must get a valid MTLBuffer.
        // The single thread reads and writes the one hidden backing element of each.
        device.For(1, new Square(empty, emptyOutput));

        Assert.Equal(0, emptyOutput.Length);
    }

    // --- Disposal ---

    [MetalFact]
    public void Disposed_EveryAccessorThrowsObjectDisposedException()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<int> readWrite = device.AllocateReadWriteBuffer<int>([1, 2, 3]);
        ReadOnlyBuffer<int> readOnly = device.AllocateReadOnlyBuffer<int>([1, 2, 3]);

        readWrite.Dispose();
        readOnly.Dispose();

        Assert.Throws<ObjectDisposedException>(() => readWrite.AsSpan());
        Assert.Throws<ObjectDisposedException>(() => readWrite.ToArray());
        Assert.Throws<ObjectDisposedException>(() => readWrite.CopyTo(new int[3]));
        Assert.Throws<ObjectDisposedException>(() => readWrite.CopyFrom([1]));
        Assert.Throws<ObjectDisposedException>(() => readWrite[0]);
        Assert.Throws<ObjectDisposedException>(() => readWrite[0] = 5);
        Assert.Throws<ObjectDisposedException>(() => readOnly[0]);
        Assert.Throws<ObjectDisposedException>(() => readOnly.AsSpan());

        // Out of range on a disposed buffer still reports the disposal, the more useful diagnosis.
        Assert.Throws<ObjectDisposedException>(() => readWrite[100]);

        // Metadata stays readable so a disposed buffer can still be described in logs.
        Assert.Equal(3, readWrite.Length);
        Assert.Same(device, readWrite.Device);
    }

    [MetalFact]
    public void Disposed_ZeroLengthBufferAlsoThrows()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(0);

        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.AsSpan());
        Assert.Throws<ObjectDisposedException>(() => buffer.ToArray());
        Assert.Throws<ObjectDisposedException>(() => buffer[0]);
    }

    [MetalFact]
    public void Dispose_IsIdempotent()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(8);

        buffer.Dispose();
        buffer.Dispose();
        buffer.Dispose();
    }

    [MetalFact]
    public void Disposed_BindingToADispatchThrowsAndLeavesTheDeviceUsable()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>([1, 2, 3]);
        using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(3);

        source.Dispose();

        // SetArguments throws after the encoder exists. If the runtime skipped endEncoding on that
        // path, Metal would abort the whole test process rather than fail this assertion.
        Assert.Throws<ObjectDisposedException>(() => device.For(3, new Square(source, destination)));

        using ReadOnlyBuffer<int> replacement = device.AllocateReadOnlyBuffer<int>([1, 2, 3]);
        device.For(3, new Square(replacement, destination));

        Assert.Equal([1, 4, 9], destination.ToArray());
    }

    [MetalFact]
    public void Leaked_BuffersAreReleasedByFinalization()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        for (int round = 0; round < 3; round++)
        {
            AllocateAndAbandon(device, count: 200);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // The device is still healthy after 600 buffers were released from the finalizer thread.
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2]);
        device.For(buffer.Length, new MultiplyByTwo(buffer, 2f));

        Assert.Equal([2f, 4f], buffer.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateAndAbandon(GraphicsDevice device, int count)
    {
        for (int i = 0; i < count; i++)
        {
            ReadWriteBuffer<long> buffer = device.AllocateReadWriteBuffer<long>(4096);
            buffer[0] = i;
        }
    }

    [InlineArray(8)]
    private struct Block64
    {
        private long _element0;
    }
}
