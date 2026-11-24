using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        }
        finally
        {
            NativeMemory.AlignedFree(memory);
        }
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
