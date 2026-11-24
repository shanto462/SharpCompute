using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>Buffers over existing memory: <see cref="GraphicsDevice.WrapReadWriteBuffer{T}"/>, <see cref="GraphicsDevice.WrapReadOnlyBuffer{T}"/> and <see cref="GraphicsDevice.MapReadOnlyBuffer{T}"/>.</summary>
public sealed unsafe class ZeroCopyBufferTests
{
    [MetalFact]
    public void PageSize_IsTheSystemPageSize()
    {
        Assert.Equal(Environment.SystemPageSize, GraphicsDevice.PageSize);
        Assert.True(GraphicsDevice.PageSize > 0 && (GraphicsDevice.PageSize & (GraphicsDevice.PageSize - 1)) == 0);
    }

    [MetalFact]
    public void WrapReadWriteBuffer_SharesTheMemoryWithTheCpuAndTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int length = 1000; // not a whole number of pages
        float* memory = (float*)NativeMemory.AlignedAlloc((nuint)(length * sizeof(float)), (nuint)GraphicsDevice.PageSize);
        int released = 0;

        try
        {
            for (int i = 0; i < length; i++)
            {
                memory[i] = i;
            }

            using (ReadWriteBuffer<float> buffer = device.WrapReadWriteBuffer<float>((nint)memory, length, () => Interlocked.Increment(ref released)))
            {
                Assert.Equal(length, buffer.Length);
                Assert.True(buffer.IsCpuWritable);
                Assert.Equal(999f, buffer[999]);

                device.For(length, new MultiplyByTwo(buffer, 2f));

                // The GPU wrote the caller's memory itself, not a copy of it.
                Assert.Equal(2f * 999f, memory[999]);

                buffer[0] = 5f;
                Assert.Equal(5f, memory[0]);
                Assert.Equal(0, Volatile.Read(ref released));
            }

            Assert.Equal(1, Volatile.Read(ref released));
        }
        finally
        {
            NativeMemory.AlignedFree(memory);
        }
    }

    [MetalFact]
    public void WrapReadOnlyBuffer_BindsAsAShaderInput()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int length = 4097;
        int* memory = (int*)NativeMemory.AlignedAlloc((nuint)(length * sizeof(int)), (nuint)GraphicsDevice.PageSize);

        try
        {
            for (int i = 0; i < length; i++)
            {
                memory[i] = i - 2000;
            }

            using ReadOnlyBuffer<int> source = device.WrapReadOnlyBuffer<int>((nint)memory, length);
            using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(length);

            device.For(length, new Square(source, destination));

            Assert.Equal(Enumerable.Range(-2000, length).Select(static v => v * v), destination.ToArray());
            Assert.True(source.IsCpuWritable);

            using ReadOnlyBuffer<int> readOnlyMemory = device.WrapReadOnlyBuffer<int>((nint)memory, length, isCpuWritable: false);
            Assert.False(readOnlyMemory.IsCpuWritable);
            Assert.Throws<NotSupportedException>(() => readOnlyMemory.AsSpan());
            Assert.Equal(-2000, readOnlyMemory.AsReadOnlySpan()[0]);
        }
        finally
        {
            NativeMemory.AlignedFree(memory);
        }
    }

    [MetalFact]
    public void AllocateReadOnlyBufferWithLength_IsZeroFilledAndFilledFromTheCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>(300);
        using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(300);

        Assert.All(source.ToArray(), static v => Assert.Equal(0, v));
        Enumerable.Range(0, 300).ToArray().CopyTo(source.AsSpan());
        device.For(300, new Square(source, destination));

        Assert.Equal(Enumerable.Range(0, 300).Select(static v => v * v), destination.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => device.AllocateReadOnlyBuffer<int>(-1));
    }

    [MetalFact]
    public void Wrap_RejectsMisalignedNullAndEmptyMemory()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        byte* memory = (byte*)NativeMemory.AlignedAlloc((nuint)(2 * GraphicsDevice.PageSize), (nuint)GraphicsDevice.PageSize);
        bool released = false;

        try
        {
            Assert.Throws<ArgumentException>(() => device.WrapReadWriteBuffer<int>((nint)(memory + 4), 16, () => released = true));
            Assert.Throws<ArgumentException>(() => device.WrapReadOnlyBuffer<int>(0, 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => device.WrapReadWriteBuffer<int>((nint)memory, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => device.WrapReadWriteBuffer<int>((nint)memory, -1));

            // A failed wrap never calls release: the caller still owns the memory.
            Assert.False(released);
        }
        finally
        {
            NativeMemory.AlignedFree(memory);
        }
    }

    [MetalFact]
    public void Wrap_ReleaseRunsOnceEvenWhenDisposedTwice()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        void* memory = NativeMemory.AlignedAlloc((nuint)GraphicsDevice.PageSize, (nuint)GraphicsDevice.PageSize);
        int released = 0;

        ReadWriteBuffer<byte> buffer = device.WrapReadWriteBuffer<byte>((nint)memory, GraphicsDevice.PageSize, () =>
        {
            Interlocked.Increment(ref released);
            NativeMemory.AlignedFree(memory);
        });

        buffer.Dispose();
        buffer.Dispose();

        Assert.Equal(1, Volatile.Read(ref released));
        Assert.Throws<ObjectDisposedException>(() => buffer.AsSpan());
    }

    [MetalFact]
    public void Wrap_DisposeDuringADispatch_ReleasesOnlyAfterTheGpuIsDone()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int length = 1 << 20;

        // Compile the pipeline first, so the timed For binds within microseconds of starting.
        using (ReadWriteBuffer<float> warmUp = device.AllocateReadWriteBuffer<float>(1))
        {
            device.For(1, new SpinOnBuffer(warmUp, 1));
        }

        // A descheduled thread can still lose the race and dispose before the bind; then For throws
        // ObjectDisposedException, which is correct but proves nothing here, so try again.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (TryDisposeDuringDispatch(device, length, out long disposedAt, out long finished, out bool? completeAtRelease))
            {
                // The dispose must happen while the dispatch is still running for the test to mean anything.
                Assert.True(disposedAt < finished, "The dispatch finished before the buffer was disposed.");
                Assert.NotNull(completeAtRelease);

                // No timing window: when the memory is released, every thread must already have written
                // its final value. A release during the dispatch would still see zeros.
                Assert.True(completeAtRelease.Value, "The memory was released while the GPU was still writing it.");
                return;
            }
        }

        Assert.Fail("The buffer was disposed before the dispatch bound it in every attempt.");
    }

    private static bool TryDisposeDuringDispatch(GraphicsDevice device, int length, out long disposedAt, out long finished, out bool? completeAtRelease)
    {
        float* memory = (float*)NativeMemory.AlignedAlloc((nuint)(length * sizeof(float)), (nuint)GraphicsDevice.PageSize);
        NativeMemory.Clear(memory, (nuint)(length * sizeof(float)));
        int complete = -1;

        ReadWriteBuffer<float> buffer = device.WrapReadWriteBuffer<float>((nint)memory, length, () =>
        {
            // Every element starts at 0 and the kernel writes the same nonzero value to all of them.
            bool done = memory[0] != 0f;

            for (int i = 1; i < length && done; i++)
            {
                done = memory[i] == memory[0];
            }

            Volatile.Write(ref complete, done ? 1 : 0);
            NativeMemory.AlignedFree(memory);
        });

        long started = 0;
        long disposed = 0;
        Thread disposer = new(() =>
        {
            // Dispose while the kernel below is running, as another thread might.
            while (Volatile.Read(ref started) == 0)
            {
                Thread.Yield();
            }

            Thread.Sleep(10);
            buffer.Dispose();
            Volatile.Write(ref disposed, Stopwatch.GetTimestamp());
        });

        disposer.Start();
        bool bound = true;

        try
        {
            Volatile.Write(ref started, Stopwatch.GetTimestamp());
            device.For(length, new SpinOnBuffer(buffer, 200_000));
        }
        catch (ObjectDisposedException)
        {
            bound = false;
        }

        finished = Stopwatch.GetTimestamp();
        disposer.Join();
        disposedAt = Volatile.Read(ref disposed);
        int state = Volatile.Read(ref complete);
        completeAtRelease = state < 0 ? null : state == 1;
        return bound;
    }

    [MetalFact]
    public void Wrap_UnreachableDuringADispatch_IsNotFinalizedUnderTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int length = 1 << 20;
        void* memory = NativeMemory.AlignedAlloc((nuint)(length * sizeof(float)), (nuint)GraphicsDevice.PageSize);
        int released = 0;
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
            // Nothing but the dispatch references the buffer.
            DispatchOverUnreferencedBuffer(device, (nint)memory, length, () => Interlocked.Exchange(ref released, 1));
            Assert.Equal(0, Volatile.Read(ref released));
        }
        finally
        {
            stop.Cancel();
            collector.Join();
        }

        // Once the dispatch is over the buffer may be finalized and the memory released.
        for (int i = 0; i < 50 && Volatile.Read(ref released) == 0; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(1, Volatile.Read(ref released));
        NativeMemory.AlignedFree(memory);
    }

    // Not inlined, so the buffer is unreachable once this returns, even in a Debug build.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void DispatchOverUnreferencedBuffer(GraphicsDevice device, nint memory, int length, Action release) =>
        device.For(length, new SpinOnBuffer(device.WrapReadWriteBuffer<float>(memory, length, release), 5_000));

    [MetalFact]
    public void MapReadOnlyBuffer_ReadsTheFileOnTheGpuWithoutCopying()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string path = Path.GetTempFileName();

        try
        {
            int[] data = Enumerable.Range(-5000, 12_345).ToArray();
            File.WriteAllBytes(path, MemoryMarshal.AsBytes(data.AsSpan()).ToArray());

            using ReadOnlyBuffer<int> mapped = device.MapReadOnlyBuffer<int>(path);
            using ReadWriteBuffer<int> squares = device.AllocateReadWriteBuffer<int>(data.Length);

            Assert.Equal(data.Length, mapped.Length);
            Assert.False(mapped.IsCpuWritable);
            Assert.Equal(data, mapped.AsReadOnlySpan().ToArray());
            Assert.Equal(data, mapped.ToArray());
            Assert.Equal(data[^1], mapped[data.Length - 1]);

            device.For(data.Length, new Square(mapped, squares));

            Assert.Equal(data.Select(static v => v * v), squares.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [MetalFact]
    public void MapReadOnlyBuffer_RefusesWritableAccess()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, new byte[64]);
            using ReadOnlyBuffer<float> mapped = device.MapReadOnlyBuffer<float>(path);

            Assert.Throws<NotSupportedException>(() => mapped.AsSpan());
            Assert.Throws<NotSupportedException>(() => mapped.CopyFrom([1f]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [MetalFact]
    public void MapReadOnlyBuffer_ValidatesTheFile()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, new byte[6]);
            Assert.Throws<ArgumentException>(() => device.MapReadOnlyBuffer<int>(path));

            File.WriteAllBytes(path, []);
            using ReadOnlyBuffer<int> empty = device.MapReadOnlyBuffer<int>(path);
            Assert.Equal(0, empty.Length);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Throws<FileNotFoundException>(() => device.MapReadOnlyBuffer<int>(path));
    }

    [MetalFact]
    public void MapReadOnlyBuffer_ReleasesTheMappingOnDispose()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string path = Path.GetTempFileName();

        try
        {
            File.WriteAllBytes(path, new byte[3 * 16384 + 8]);

            for (int i = 0; i < 200; i++)
            {
                using ReadOnlyBuffer<long> mapped = device.MapReadOnlyBuffer<long>(path);
                Assert.Equal(0L, mapped[mapped.Length - 1]);
            }
        }
        finally
        {
            // Fails on a leaked mapping only on platforms that lock mapped files; on macOS this
            // mostly checks that disposal doesn't throw.
            File.Delete(path);
        }
    }
}
