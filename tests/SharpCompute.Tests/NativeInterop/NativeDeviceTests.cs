using System;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;
using MultiplyByTwo = SharpCompute.Tests.EndToEnd.MultiplyByTwo;

namespace SharpCompute.Tests.NativeInterop;

public class NativeDeviceTests
{
    [MetalFact]
    public void SystemDefaultDevice_IsTheSameObjectEveryTime()
    {
        // What "the same device" rests on: MTLCreateSystemDefaultDevice hands out one object, so the
        // app's device and GetDefault() are the same MTLDevice whenever the app uses the default.
        nint first = Metal.CreateSystemDefaultDevice();
        nint second = Metal.CreateSystemDefaultDevice();

        try
        {
            Assert.Equal(first, second);
            Assert.Equal(first, GraphicsDevice.GetDefault().NativeHandle);
        }
        finally
        {
            ObjC.Release(first);
            ObjC.Release(second);
        }
    }

    [MetalFact]
    public void FromNative_WrapsTheDevice_AndInteroperatesWithGetDefault()
    {
        nint mtlDevice = Metal.CreateSystemDefaultDevice();

        try
        {
            GraphicsDevice device = GraphicsDevice.FromNative(mtlDevice);

            Assert.Equal(mtlDevice, device.NativeHandle);
            Assert.NotEqual(GraphicsDevice.GetDefault().NativeCommandQueueHandle, device.NativeCommandQueueHandle);
            Assert.Equal(GraphicsDevice.GetDefault().Name, device.Name);

            // Resources belong to the MTLDevice, not to the wrapper: both ways work.
            using ReadWriteBuffer<float> fromDefault = GraphicsDevice.GetDefault().AllocateReadWriteBuffer<float>([1, 2, 3]);
            using ReadWriteBuffer<float> fromNative = device.AllocateReadWriteBuffer<float>([4, 5, 6]);
            device.For(3, new MultiplyByTwo(fromDefault, 2f));
            GraphicsDevice.GetDefault().For(3, new MultiplyByTwo(fromNative, 2f));
            GraphicsDevice.GetDefault().Copy(fromNative, fromDefault);

            Assert.Equal([8f, 10f, 12f], fromDefault.ToArray());

            device.Dispose();
            device.Dispose();

            Assert.Throws<ObjectDisposedException>(() => device.NativeHandle);
            Assert.Throws<ObjectDisposedException>(() => device.NativeCommandQueueHandle);

            // Dispose released SharpCompute's reference only. (The device's own retain count is not
            // checked: every buffer, queue and pipeline of every test retains the one system device;
            // the queue test below checks the retain and release on an object no one else shares.)
            Assert.NotEqual(0, ObjC.SendIntPtr(mtlDevice, Selectors.Name));

            // The buffer outlives the wrapper it was allocated through.
            Assert.Equal([8f, 10f, 12f], fromNative.ToArray());
        }
        finally
        {
            ObjC.Release(mtlDevice);
        }
    }

    [MetalFact]
    public void FromNative_WithTheAppsQueue_CommitsToIt_AndReleasesIt()
    {
        nint mtlDevice = GraphicsDevice.GetDefault().NativeHandle;
        nint queue = ObjC.SendIntPtr(mtlDevice, Selectors.NewCommandQueue);

        try
        {
            nuint before = NativeMetal.RetainCount(queue);

            using (GraphicsDevice device = GraphicsDevice.FromNative(mtlDevice, queue))
            {
                Assert.Equal(queue, device.NativeCommandQueueHandle);
                Assert.True(NativeMetal.RetainCount(queue) > before);

                using ReadWriteBuffer<int> data = device.AllocateReadWriteBuffer<int>(16);
                device.For(16, new WriteIndexAndLength(data));
                Assert.Equal(16_005, data[5]);
            }

            ComputeContextTests.WaitForRetainCount(queue, before);
        }
        finally
        {
            ObjC.Release(queue);
        }
    }

    [MetalFact]
    public void FromNative_RejectsWhatIsNotADeviceOrAQueueOfIt()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint buffer = NativeMetal.NewBuffer(device, 64);

        try
        {
            Assert.Equal("mtlDevice", Assert.Throws<ArgumentException>(() => GraphicsDevice.FromNative(0)).ParamName);

            // A buffer is not a device; nothing is sent to it but conformsToProtocol:.
            ArgumentException notADevice = Assert.Throws<ArgumentException>(() => GraphicsDevice.FromNative(buffer));
            Assert.Equal("mtlDevice", notADevice.ParamName);
            Assert.Contains("id<MTLDevice>", notADevice.Message);

            ArgumentException notAQueue = Assert.Throws<ArgumentException>(() => GraphicsDevice.FromNative(device.NativeHandle, buffer));
            Assert.Equal("mtlCommandQueue", notAQueue.ParamName);
            Assert.Contains("id<MTLCommandQueue>", notAQueue.Message);

            Assert.Equal(1u, NativeMetal.RetainCount(buffer));
        }
        finally
        {
            ObjC.Release(buffer);
        }
    }

    [MetalFact]
    public void SeparateWrappersOfOneDevice_ShareResources()
    {
        // Before native interop, each wrapper counted as its own device; now "same device" is the MTLDevice.
        using GraphicsDevice other = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<float> buffer = other.AllocateReadWriteBuffer<float>([1, 2]);
        using ReadWriteTexture2D<Rgba32> texture = other.AllocateReadWriteTexture2D<Rgba32>(4, 4);

        GraphicsDevice.GetDefault().For(2, new MultiplyByTwo(buffer, 3f));
        GraphicsDevice.GetDefault().For(4, 4, new WritePositionRgba32(texture));

        Assert.Equal([3f, 6f], buffer.ToArray());
        Assert.Equal(new Rgba32(7, 248, 7 ^ 85, 255), texture.ToArray()[1]);
    }

    [MetalFact]
    public void StandInForAnotherDevice_IsRejectedEverywhere()
    {
        // Apple silicon has one GPU, so a second MTLDevice cannot be made; the stand-in compares as one.
        using GraphicsDevice other = GraphicsDevice.CreateStandInForAnotherDevice();
        using ReadWriteBuffer<float> foreign = other.AllocateReadWriteBuffer<float>([1, 2]);
        nint mtlBuffer = NativeMetal.NewBuffer(GraphicsDevice.GetDefault(), 64);
        nint mtlTexture = NativeMetal.NewTexture(GraphicsDevice.GetDefault(), 4, 4);

        try
        {
            Assert.Contains("different GraphicsDevice", Assert.Throws<ArgumentException>(() => GraphicsDevice.GetDefault().For(2, new MultiplyByTwo(foreign, 2f))).Message);
            Assert.Contains("different MTLDevice", Assert.Throws<ArgumentException>(() => other.WrapNativeReadWriteBuffer<int>(mtlBuffer)).Message);
            Assert.Contains("different MTLDevice", Assert.Throws<ArgumentException>(() => other.WrapNativeReadOnlyTexture2D<Rgba32>(mtlTexture)).Message);

            // Rejected before anything was retained.
            Assert.Equal(1u, NativeMetal.RetainCount(mtlBuffer));
            Assert.Equal(1u, NativeMetal.RetainCount(mtlTexture));
        }
        finally
        {
            ObjC.Release(mtlBuffer);
            ObjC.Release(mtlTexture);
        }
    }
}
