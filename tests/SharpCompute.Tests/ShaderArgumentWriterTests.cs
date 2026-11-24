using System;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

public class ShaderArgumentWriterTests
{
    [MetalTheory]
    [InlineData(0, 1)]
    [InlineData(30, 29)]
    [InlineData(29, 30)]
    public void IndicesInRange_AreAccepted(int bufferIndex, int bytesIndex)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(4);

        device.For(4, new BindAtIndex(buffer, bufferIndex, bytesIndex));
    }

    [MetalTheory]
    [InlineData(-1, 0)]
    [InlineData(31, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 31)]
    [InlineData(int.MaxValue, 0)]
    public void IndicesOutsideTheArgumentTable_Throw(int bufferIndex, int bytesIndex)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(4);

        Assert.Throws<ArgumentOutOfRangeException>(() => device.For(4, new BindAtIndex(buffer, bufferIndex, bytesIndex)));

        // Still usable afterwards: the encoder of the failed dispatch was ended properly.
        device.For(4, new BindAtIndex(buffer, 0, 1));
    }

    [MetalFact]
    public void NullBuffer_Throws()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        Assert.Throws<ArgumentNullException>(() => device.For(4, new BindAtIndex(null!, 0, 1)));
    }

    [Fact]
    public void DefaultWriter_IsRejectedInsteadOfMessagingNil()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            ShaderArgumentWriter writer = default;
            writer.SetBytes(0, 1.0f);
        });
    }
}
