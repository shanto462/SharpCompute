using System;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;
using MultiplyByTwo = SharpCompute.Tests.EndToEnd.MultiplyByTwo;
using Saxpy = SharpCompute.Tests.EndToEnd.Saxpy;

namespace SharpCompute.Tests.NativeInterop;

public unsafe class NativeBufferTests
{
    [MetalTheory]
    [InlineData(0)] // MTLResourceStorageModeShared
    [InlineData(16)] // MTLResourceStorageModeManaged
    public void SharedOrManagedBuffer_HasFullCpuAccess_OverTheSameMemory(int options)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, 64 * sizeof(int), (nuint)options);

        try
        {
            int* contents = (int*)ObjC.SendIntPtr(mtlBuffer, Selectors.Contents);
            contents[3] = 42;

            using ReadWriteBuffer<int> buffer = device.WrapNativeReadWriteBuffer<int>(mtlBuffer);

            Assert.Equal(64, buffer.Length);
            Assert.True(buffer.IsCpuAccessible);
            Assert.True(buffer.IsCpuWritable);
            Assert.Equal(mtlBuffer, buffer.NativeHandle);
            Assert.Equal(42, buffer[3]);

            // The CPU and the GPU both use the app's memory directly.
            buffer[4] = 7;
            Assert.Equal(7, contents[4]);

            device.For(64, new WriteIndexAndLength(buffer));
            Assert.Equal(64_010, contents[10]);
            Assert.Equal(64_063, buffer.ToArray()[63]);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void ReadOnlyWrap_BindsReadOnly_AndKeepsCpuWritableMemory()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, 8 * sizeof(float));

        try
        {
            using ReadOnlyBuffer<float> x = device.WrapNativeReadOnlyBuffer<float>(mtlBuffer);
            x.AsSpan().Fill(2f);

            using ReadWriteBuffer<float> y = device.AllocateReadWriteBuffer<float>([1, 1, 1, 1, 1, 1, 1, 1]);
            device.For(8, new Saxpy(x, y, 3f, new Float3(0f, 0f, 0f)));

            Assert.True(x.IsCpuWritable);
            Assert.All(y.ToArray(), value => Assert.Equal(7f, value));
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void PrivateBuffer_IsGpuOnly_ButCopiesFillsAndShadersWork()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, 32 * sizeof(int), NativeMetal.StoragePrivate);

        try
        {
            using ReadWriteBuffer<int> gpuOnly = device.WrapNativeReadWriteBuffer<int>(mtlBuffer);
            ReadWriteBuffer<int> view = gpuOnly.Slice(8, 8);
            int[] scratch = new int[32];

            Assert.Equal(32, gpuOnly.Length);
            Assert.False(gpuOnly.IsCpuAccessible);
            Assert.False(gpuOnly.IsCpuWritable);
            Assert.False(view.IsCpuAccessible);

            NotSupportedException exception = Assert.Throws<NotSupportedException>(() => gpuOnly.AsSpan());
            Assert.Contains("private (GPU-only) storage", exception.Message);
            Assert.Throws<NotSupportedException>(() => gpuOnly.AsReadOnlySpan());
            Assert.Throws<NotSupportedException>(() => gpuOnly[0]);
            Assert.Throws<NotSupportedException>(() => gpuOnly[0] = 1);
            Assert.Throws<NotSupportedException>(() => view[0]);
            Assert.Throws<NotSupportedException>(() => gpuOnly.CopyTo(scratch));
            Assert.Throws<NotSupportedException>(() => gpuOnly.CopyFrom(scratch));
            Assert.Throws<NotSupportedException>(() => gpuOnly.ToArray());
            Assert.Throws<NotSupportedException>(() => device.ForCpu(8, new WriteIndexAndLength(view)));

            // An index out of range is still reported as GPU-only, not as a range error.
            Assert.Throws<NotSupportedException>(() => gpuOnly[-1]);

            using ReadWriteBuffer<int> readback = device.AllocateReadWriteBuffer<int>(32);

            // Fill, then a shader on a view, then a copy back to shared memory.
            device.Fill(gpuOnly, 0);
            device.For(8, new WriteIndexAndLength(view));
            device.Copy(gpuOnly, readback);

            int[] values = readback.ToArray();
            Assert.Equal(0, values[7]);
            Assert.Equal(8_000, values[8]);
            Assert.Equal(8_007, values[15]);
            Assert.Equal(0, values[16]);

            // And the other way.
            readback.AsSpan().Fill(5);
            device.Copy(readback, gpuOnly);
            readback.AsSpan().Clear();
            device.Copy(gpuOnly, readback);
            Assert.All(readback.ToArray(), value => Assert.Equal(5, value));
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalTheory]
    [InlineData(10, 4)] // 2.5 ints
    [InlineData(12, 16)] // less than one Float4
    [InlineData(20, 8)] // 2.5 Float2
    public void LengthThatIsNotAWholeNumberOfElements_IsRejected(int byteLength, int elementSize)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, (nuint)byteLength);

        try
        {
            ArgumentException exception = elementSize switch
            {
                4 => Assert.Throws<ArgumentException>(() => device.WrapNativeReadWriteBuffer<int>(mtlBuffer)),
                8 => Assert.Throws<ArgumentException>(() => device.WrapNativeReadOnlyBuffer<Float2>(mtlBuffer)),
                _ => Assert.Throws<ArgumentException>(() => device.WrapNativeReadWriteBuffer<Float4>(mtlBuffer)),
            };

            Assert.Equal("mtlBuffer", exception.ParamName);
            Assert.Contains($"{byteLength} bytes", exception.Message);
            Assert.Equal(1u, NativeMetal.RetainCount(mtlBuffer));
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void WhatIsNotATrackedBufferOfThisDevice_IsRejected()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint untracked = NativeMetal.NewBuffer(device, 64, NativeMetal.StorageShared | NativeMetal.HazardUntracked);
        nint texture = NativeMetal.NewTexture(device, 4, 4);

        try
        {
            Assert.Equal("mtlBuffer", Assert.Throws<ArgumentException>(() => device.WrapNativeReadWriteBuffer<int>(0)).ParamName);
            Assert.Contains("id<MTLBuffer>", Assert.Throws<ArgumentException>(() => device.WrapNativeReadWriteBuffer<int>(texture)).Message);
            Assert.Contains("id<MTLBuffer>", Assert.Throws<ArgumentException>(() => device.WrapNativeReadOnlyBuffer<int>(device.NativeHandle)).Message);
            Assert.Contains("Untracked", Assert.Throws<ArgumentException>(() => device.WrapNativeReadWriteBuffer<int>(untracked)).Message);

            Assert.Equal(1u, NativeMetal.RetainCount(untracked));
            Assert.Equal(1u, NativeMetal.RetainCount(texture));
        }
        finally
        {
            ObjC.Release(untracked);
            ObjC.Release(texture);
        }
    }

    [MetalFact]
    public void DisposedDevice_CannotWrap()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(GraphicsDevice.GetDefault(), 64);

        try
        {
            device.Dispose();
            Assert.Throws<ObjectDisposedException>(() => device.WrapNativeReadWriteBuffer<int>(mtlBuffer));
            Assert.Throws<ObjectDisposedException>(() => device.WrapNativeReadWriteTexture2D<Rgba32>(mtlBuffer));
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void Wrap_RetainsTheBuffer_AndDisposeReleasesIt()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, 64);

        try
        {
            ReadWriteBuffer<int> buffer = device.WrapNativeReadWriteBuffer<int>(mtlBuffer);
            ReadOnlyBuffer<int> second = device.WrapNativeReadOnlyBuffer<int>(mtlBuffer);
            Assert.Equal(3u, NativeMetal.RetainCount(mtlBuffer));

            buffer.Dispose();
            buffer.Dispose();
            second.Dispose();

            Assert.Equal(1u, NativeMetal.RetainCount(mtlBuffer));
            Assert.Throws<ObjectDisposedException>(() => buffer.NativeHandle);
            Assert.Throws<ObjectDisposedException>(() => buffer[0]);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void DisposedWhilePending_StaysAliveUntilTheGpuIsDone()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, 1024 * sizeof(int), NativeMetal.StoragePrivate);

        try
        {
            using ReadWriteBuffer<int> readback = device.AllocateReadWriteBuffer<int>(1024);
            ReadWriteBuffer<int> buffer = device.WrapNativeReadWriteBuffer<int>(mtlBuffer);
            ComputeContext context = device.CreateComputeContext();
            context.For(1024, new WriteIndexAndLength(buffer));
            context.Copy(buffer, readback);

            buffer.Dispose();
            Assert.True(NativeMetal.RetainCount(mtlBuffer) > 1);

            context.Dispose();

            Assert.Equal(1_024_500, readback[500]);
            ComputeContextTests.WaitForRetainCount(mtlBuffer, 1);
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void SliceOfAWrappedBuffer_IsAViewOfTheAppsMemory()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, 64 * sizeof(int));

        try
        {
            int* contents = (int*)ObjC.SendIntPtr(mtlBuffer, Selectors.Contents);
            ReadWriteBuffer<int> view;

            using (ReadWriteBuffer<int> buffer = device.WrapNativeReadWriteBuffer<int>(mtlBuffer))
            {
                view = buffer.Slice(16, 8);
                ReadWriteBuffer<int> inner = view.Slice(2);

                Assert.Equal(mtlBuffer, view.NativeHandle);
                Assert.Equal((nuint)(16 * sizeof(int)), view.NativeByteOffset);
                Assert.Equal((nuint)(18 * sizeof(int)), inner.NativeByteOffset);
                Assert.Equal((nuint)0, buffer.NativeByteOffset);

                device.For(8, new WriteIndexAndLength(view));
                device.ForCpu(6, new WriteIndexAndLength(inner));

                Assert.Equal(0, contents[15]);
                Assert.Equal(8_000, contents[16]);
                Assert.Equal(8_001, contents[17]);
                Assert.Equal(6_000, contents[18]);
                Assert.Equal(6_005, contents[23]);
                Assert.Equal(0, contents[24]);
            }

            // Disposing the wrapped buffer disposes its views, and only releases SharpCompute's reference.
            Assert.Throws<ObjectDisposedException>(() => view[0]);
            Assert.Equal(1u, NativeMetal.RetainCount(mtlBuffer));
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void TwoWrappersOfOneBuffer_AreOneBufferToTheCopyChecks()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, 64 * sizeof(int));

        try
        {
            using ReadWriteBuffer<int> first = device.WrapNativeReadWriteBuffer<int>(mtlBuffer);
            using ReadWriteBuffer<int> second = device.WrapNativeReadWriteBuffer<int>(mtlBuffer);

            Assert.Contains("overlap", Assert.Throws<ArgumentException>(() => device.Copy(first, 0, second, 8, 16)).Message);

            // Disjoint ranges of the one MTLBuffer are fine.
            first.AsSpan().Fill(3);
            first.AsSpan()[32..].Clear();
            device.Copy(first, 0, second, 32, 32);
            Assert.All(second.ToArray(), value => Assert.Equal(3, value));
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void NativeHandle_RoundTrips_ToTheSameMemory()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ours = device.AllocateReadWriteBuffer<int>(32);
        ReadWriteBuffer<int> view = ours.Slice(4, 4);

        Assert.Equal(ours.NativeHandle, view.NativeHandle);
        Assert.Equal((nuint)(4 * sizeof(int)), view.NativeByteOffset);
        Assert.Equal((nuint)(32 * sizeof(int)), ObjC.SendNUInt(ours.NativeHandle, Selectors.Length));

        using (ReadOnlyBuffer<int> again = device.WrapNativeReadOnlyBuffer<int>(ours.NativeHandle))
        {
            ours[5] = 99;
            Assert.Equal(99, again[5]);
            Assert.Equal(32, again.Length);

            device.For(4, new WriteIndexAndLength(view));
            Assert.Equal(4_001, again[5]);
        }

        // The second wrapper released only its own reference.
        Assert.Equal(4_001, ours[5]);
    }
}
