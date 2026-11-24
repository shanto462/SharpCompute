using System;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

public class GraphicsDeviceTests
{
    [Fact]
    public void IsSupported_NeverThrows_AndGetDefaultAgreesWithIt()
    {
        // Runs on every machine, GPU or not: this is the probe [MetalFact] relies on.
        if (GraphicsDevice.IsSupported)
        {
            Assert.NotNull(GraphicsDevice.GetDefault());
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => GraphicsDevice.GetDefault());
        }
    }

    [MetalFact]
    public void GetDefault_ReturnsTheSameInstance()
    {
        Assert.Same(GraphicsDevice.GetDefault(), GraphicsDevice.GetDefault());
    }

    [MetalFact]
    public void Name_IsTheMetalDeviceName()
    {
        Assert.False(string.IsNullOrWhiteSpace(GraphicsDevice.GetDefault().Name));
    }

    [MetalFact]
    public void DisposingTheDefaultDevice_IsANoOp()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        device.Dispose();
        device.Dispose();

        using (GraphicsDevice scoped = GraphicsDevice.GetDefault())
        {
            Assert.Same(device, scoped);
        }

        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3]);
        device.For(buffer.Length, new MultiplyByTwo(buffer, 2f));

        Assert.Equal([2f, 4f, 6f], buffer.ToArray());
    }

    [MetalFact]
    public void NonDefaultDevice_HasItsOwnPipelineCacheAndDispatches()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3]);

        device.For(buffer.Length, new MultiplyByTwo(buffer, 2f));
        device.For(buffer.Length, new MultiplyByTwo(buffer, 2f));

        Assert.NotSame(GraphicsDevice.GetDefault(), device);
        Assert.Equal(GraphicsDevice.GetDefault().Name, device.Name);
        Assert.Equal([4f, 8f, 12f], buffer.ToArray());
    }

    [MetalFact]
    public void DisposedDevice_AllocationsAndDispatchesThrow()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3]);
        device.For(buffer.Length, new MultiplyByTwo(buffer, 2f));

        device.Dispose();
        device.Dispose();

        Assert.Throws<ObjectDisposedException>(() => device.AllocateReadWriteBuffer<float>(4));
        Assert.Throws<ObjectDisposedException>(() => device.AllocateReadWriteBuffer<float>([1f]));
        Assert.Throws<ObjectDisposedException>(() => device.AllocateReadOnlyBuffer<float>([1f]));
        Assert.Throws<ObjectDisposedException>(() => device.AllocateReadWriteBuffer<float>(0));
        Assert.Throws<ObjectDisposedException>(() => device.For(buffer.Length, new MultiplyByTwo(buffer, 2f)));
        Assert.Throws<ObjectDisposedException>(() => device.For(0, new MultiplyByTwo(buffer, 2f)));

        // A shader this device never compiled must not be compiled now either.
        using ReadWriteBuffer<uint> other = GraphicsDevice.GetDefault().AllocateReadWriteBuffer<uint>(4);
        Assert.Throws<ObjectDisposedException>(() => device.For(4, new EncodePosition3D(other)));

        // Buffers outlive their device: the MTLBuffer keeps the MTLDevice alive.
        Assert.Equal([2f, 4f, 6f], buffer.ToArray());
        Assert.Equal(device.Name, GraphicsDevice.GetDefault().Name);
        buffer.Dispose();
    }

    [MetalFact]
    public void BufferFromAnotherDevice_IsRejected()
    {
        using GraphicsDevice other = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<float> foreign = other.AllocateReadWriteBuffer<float>([1, 2, 3]);

        GraphicsDevice device = GraphicsDevice.GetDefault();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => device.For(foreign.Length, new MultiplyByTwo(foreign, 2f)));

        Assert.Contains("different GraphicsDevice", exception.Message);
        Assert.Equal([1f, 2f, 3f], foreign.ToArray());

        // The failed bind must not have left the device in a bad state.
        using ReadWriteBuffer<float> own = device.AllocateReadWriteBuffer<float>([1, 2, 3]);
        device.For(own.Length, new MultiplyByTwo(own, 2f));

        Assert.Equal([2f, 4f, 6f], own.ToArray());
    }
}
