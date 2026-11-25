using System;
using System.Threading.Tasks;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;
using MultiplyByTwo = SharpCompute.Tests.EndToEnd.MultiplyByTwo;

namespace SharpCompute.Tests.NativeInterop;

public class EncodeIntoTests
{
    private const int Length = 4096;

    [MetalFact]
    public async Task RecordedWork_RunsBetweenTheHostsOwnPasses_InOneSubmission()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nuint byteLength = Length * sizeof(float);
        nint hostBuffer = NativeMetal.NewBuffer(device, byteLength);
        nint hostResult = NativeMetal.NewBuffer(device, byteLength);
        nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);

        try
        {
            using ReadWriteBuffer<float> buffer = device.WrapNativeReadWriteBuffer<float>(hostBuffer);
            using ReadWriteBuffer<float> result = device.WrapNativeReadWriteBuffer<float>(hostResult);
            using ComputeContext context = device.CreateComputeContext();

            // Host blit -> SharpCompute dispatch, copy and dispatch -> host blit, all in one command buffer.
            NativeMetal.EncodeFill(commandBuffer, hostBuffer, byteLength, 0x3F);
            context.For(Length, new MultiplyByTwo(buffer, 2f));
            context.Copy(buffer, result);
            context.For(Length, new MultiplyByTwo(result, 10f));
            Task done = context.EncodeInto(commandBuffer);
            NativeMetal.EncodeCopy(commandBuffer, hostResult, hostBuffer, byteLength);

            Assert.Equal(0, context.PendingCount);
            Assert.False(done.IsCompleted);

            NativeMetal.CommitAndWait(commandBuffer);
            await done.WaitAsync(TimeSpan.FromSeconds(10));

            // 0x3F3F3F3F, doubled, then ten times that; the host's final copy saw it.
            float filled = BitConverter.Int32BitsToSingle(0x3F3F3F3F);
            Assert.All(buffer.ToArray(), value => Assert.Equal(filled * 20f, value));
            Assert.All(result.ToArray(), value => Assert.Equal(filled * 20f, value));
        }
        finally
        {
            ObjC.Release(commandBuffer);
            ObjC.Release(hostBuffer);
            ObjC.Release(hostResult);
        }
    }

    [MetalFact]
    public async Task EncodingTwiceIntoOneCommandBuffer_AppendsInOrder()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);

        try
        {
            using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(Length);
            using ComputeContext context = device.CreateComputeContext();
            buffer.AsSpan().Fill(1f);

            context.For(Length, new MultiplyByTwo(buffer, 3f));
            Task first = context.EncodeInto(commandBuffer);

            // An encode with nothing recorded encodes nothing.
            Assert.True(context.EncodeInto(commandBuffer).IsCompletedSuccessfully);

            context.Copy(buffer, 0, buffer, Length / 2, Length / 2);
            context.For(Length, new MultiplyByTwo(buffer, 5f));
            Task second = context.EncodeInto(commandBuffer);

            NativeMetal.CommitAndWait(commandBuffer);
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.All(buffer.ToArray(), value => Assert.Equal(15f, value));
        }
        finally
        {
            ObjC.Release(commandBuffer);
        }
    }

    [MetalFact]
    public async Task Leases_AreReleasedWhenTheCommandBufferCompletes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint hostBuffer = NativeMetal.NewBuffer(device, Length * sizeof(int), NativeMetal.StoragePrivate);
        nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);

        try
        {
            using ReadWriteBuffer<int> readback = device.AllocateReadWriteBuffer<int>(Length);
            ReadWriteBuffer<int> buffer = device.WrapNativeReadWriteBuffer<int>(hostBuffer);
            ComputeContext context = device.CreateComputeContext();
            context.For(Length, new WriteIndexAndLength(buffer));
            context.Copy(buffer, readback);
            Task done = context.EncodeInto(commandBuffer);

            // Disposed before the app commits: the lease (and the command buffer's own retain) keep it.
            buffer.Dispose();
            context.Dispose();
            Assert.True(NativeMetal.RetainCount(hostBuffer) > 1);

            ObjC.SendVoid(commandBuffer, Selectors.Commit);
            await done.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(4_096_123, readback[123]);
            ObjC.Release(commandBuffer);
            commandBuffer = 0;
            ComputeContextTests.WaitForRetainCount(hostBuffer, 1);
        }
        finally
        {
            ObjC.Release(commandBuffer);
            ObjC.Release(hostBuffer);
        }
    }

    [MetalFact]
    public void CommandBufferThatCannotBeEncodedInto_IsRejected_AndKeepsTheWork()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint committed = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);
        nint buffer = NativeMetal.NewBuffer(device, 64);

        try
        {
            NativeMetal.CommitAndWait(committed);

            using ReadWriteBuffer<int> data = device.AllocateReadWriteBuffer<int>(16);
            using ComputeContext context = device.CreateComputeContext();
            context.For(16, new WriteIndexAndLength(data));

            Assert.Equal("mtlCommandBuffer", Assert.Throws<ArgumentException>(() => { _ = context.EncodeInto(0); }).ParamName);
            Assert.Contains("id<MTLCommandBuffer>", Assert.Throws<ArgumentException>(() => { _ = context.EncodeInto(buffer); }).Message);
            Assert.Contains("id<MTLCommandBuffer>", Assert.Throws<ArgumentException>(() => { _ = context.EncodeInto(device.NativeCommandQueueHandle); }).Message);
            Assert.Contains("already been committed", Assert.Throws<InvalidOperationException>(() => { _ = context.EncodeInto(committed); }).Message);

            // Nothing was encoded or dropped: the work is still pending, and runs.
            Assert.Equal(1, context.PendingCount);
            context.Execute();
            Assert.Equal(16_015, data[15]);
        }
        finally
        {
            ObjC.Release(committed);
            ObjC.Release(buffer);
        }
    }

    [MetalFact]
    public void CommandBufferOfAnotherDevice_IsRejected()
    {
        using GraphicsDevice other = GraphicsDevice.CreateStandInForAnotherDevice();
        nint commandBuffer = NativeMetal.NewCommandBuffer(GraphicsDevice.GetDefault().NativeCommandQueueHandle);

        try
        {
            using ReadWriteBuffer<int> data = other.AllocateReadWriteBuffer<int>(16);
            using ComputeContext context = other.CreateComputeContext();
            context.For(16, new WriteIndexAndLength(data));

            Assert.Contains("different MTLDevice", Assert.Throws<ArgumentException>(() => { _ = context.EncodeInto(commandBuffer); }).Message);
            Assert.Equal(1, context.PendingCount);
        }
        finally
        {
            ObjC.Release(commandBuffer);
        }
    }

    [MetalFact]
    public void DisposedContextOrDevice_Throws()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        nint commandBuffer = NativeMetal.NewCommandBuffer(GraphicsDevice.GetDefault().NativeCommandQueueHandle);

        try
        {
            ComputeContext disposed = device.CreateComputeContext();
            disposed.Dispose();
            Assert.Throws<ObjectDisposedException>(() => { _ = disposed.EncodeInto(commandBuffer); });

            ComputeContext context = device.CreateComputeContext();
            device.Dispose();
            Assert.Throws<ObjectDisposedException>(() => { _ = context.EncodeInto(commandBuffer); });
        }
        finally
        {
            ObjC.Release(commandBuffer);
        }
    }

    [MetalFact]
    public async Task FailedCommandBuffer_FaultsTheTask()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);

        try
        {
            using ReadWriteBuffer<int> data = device.AllocateReadWriteBuffer<int>(16);
            using ComputeContext context = device.CreateComputeContext();
            context.For(16, new WriteIndexAndLength(data));
            context.SimulateGpuError("Simulated fault");
            Task done = context.EncodeInto(commandBuffer);

            NativeMetal.CommitAndWait(commandBuffer);

            MetalException error = await Assert.ThrowsAsync<MetalException>(() => done.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Contains("Simulated fault", error.Message);
        }
        finally
        {
            ObjC.Release(commandBuffer);
        }
    }

    [MetalFact]
    public async Task EncodeInto_ReusesBatchesFrameAfterFrame()
    {
        // The shape of a render loop: one context, one EncodeInto per frame's command buffer.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(Length);
        using ComputeContext context = device.CreateComputeContext();
        buffer.AsSpan().Fill(1f);

        for (int frame = 0; frame < 8; frame++)
        {
            nint commandBuffer = NativeMetal.NewCommandBuffer(device.NativeCommandQueueHandle);

            try
            {
                context.For(Length, new MultiplyByTwo(buffer, 2f));
                Task done = context.EncodeInto(commandBuffer);
                NativeMetal.CommitAndWait(commandBuffer);
                await done.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                ObjC.Release(commandBuffer);
            }
        }

        Assert.True(context.HasSpareBatch);
        Assert.All(buffer.ToArray(), value => Assert.Equal(256f, value));
    }
}
