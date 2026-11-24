using System;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

public class ThreadGroupSizeTests
{
    // --- Pure size resolution: no GPU needed, so these run everywhere. ---

    [Theory]
    [InlineData(1, 256, 1, 1)]
    [InlineData(2, 16, 16, 1)]
    [InlineData(3, 8, 8, 4)]
    public void RuntimePicked_UsesThePlannedSizePerRank(int rank, int x, int y, int z)
    {
        MTLSize size = GraphicsDevice.ResolveThreadGroupSize(0, 0, 0, rank, maxTotalThreads: 1024, typeof(object));

        Assert.Equal(((nuint)x, (nuint)y, (nuint)z), (size.Width, size.Height, size.Depth));
    }

    [Theory]
    [InlineData(1, 256, 256, 1, 1)]
    [InlineData(1, 128, 128, 1, 1)]
    [InlineData(1, 100, 64, 1, 1)]
    [InlineData(2, 128, 8, 16, 1)]
    [InlineData(2, 64, 8, 8, 1)]
    [InlineData(3, 128, 4, 8, 4)]
    [InlineData(3, 64, 4, 4, 4)]
    [InlineData(3, 1, 1, 1, 1)]
    [InlineData(1, 1, 1, 1, 1)]
    public void RuntimePicked_IsClampedToThePipelineMaximum(int rank, int max, int x, int y, int z)
    {
        MTLSize size = GraphicsDevice.ResolveThreadGroupSize(0, 0, 0, rank, (nuint)max, typeof(object));

        Assert.Equal(((nuint)x, (nuint)y, (nuint)z), (size.Width, size.Height, size.Depth));
        Assert.True(size.Width * size.Height * size.Depth <= (nuint)max);
    }

    [Fact]
    public void Explicit_IsPassedThroughUnchangedWhenItFits()
    {
        MTLSize size = GraphicsDevice.ResolveThreadGroupSize(32, 8, 4, rank: 1, maxTotalThreads: 1024, typeof(object));

        Assert.Equal(((nuint)32, (nuint)8, (nuint)4), (size.Width, size.Height, size.Depth));
    }

    [Theory]
    [InlineData(1025, 1, 1)]
    [InlineData(33, 32, 1)]
    [InlineData(16, 16, 5)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    public void Explicit_LargerThanThePipelineMaximum_Throws(int x, int y, int z)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => GraphicsDevice.ResolveThreadGroupSize(x, y, z, rank: 1, maxTotalThreads: 1024, typeof(ThreadGroupSizeTests)));

        Assert.Contains("1024", exception.Message);
        Assert.Contains(nameof(ThreadGroupSizeTests), exception.Message);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(64, 0, 1)]
    [InlineData(64, 1, 0)]
    [InlineData(-1, 1, 1)]
    [InlineData(64, -1, 1)]
    public void Explicit_WithANonPositiveDimension_Throws(int x, int y, int z)
    {
        Assert.Throws<InvalidOperationException>(
            () => GraphicsDevice.ResolveThreadGroupSize(x, y, z, rank: 1, maxTotalThreads: 1024, typeof(object)));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(1000, 1, 2)]
    [InlineData(1, 2, 3)]
    [InlineData(2, 2, 3)]
    [InlineData(1000, 1000, 3)]
    public void Rank_IsInferredFromTheExtents(int y, int z, int expectedRank)
    {
        Assert.Equal(expectedRank, GraphicsDevice.InferRank(y, z));
    }

    // --- Observed on the GPU: the largest thread_position_in_threadgroup per axis is size - 1. ---

    [MetalFact]
    public void RuntimePicked_1D_Dispatches256x1x1()
    {
        Assert.Equal((255u, 0u, 0u), ObserveMaxGroupIds<RuntimePicked>(1000, 1, 1, overload: 1));
    }

    [MetalFact]
    public void RuntimePicked_2D_Dispatches16x16x1()
    {
        Assert.Equal((15u, 15u, 0u), ObserveMaxGroupIds<RuntimePicked>(50, 40, 1, overload: 2));
    }

    [MetalFact]
    public void RuntimePicked_3D_Dispatches8x8x4()
    {
        Assert.Equal((7u, 7u, 3u), ObserveMaxGroupIds<RuntimePicked>(20, 18, 10, overload: 3));
    }

    [MetalFact]
    public void RuntimePicked_RankComesFromTheExtentsNotTheOverload()
    {
        // The 2- and 3-argument overloads with trailing extents of 1 are 1D dispatches: (256, 1, 1).
        Assert.Equal((255u, 0u, 0u), ObserveMaxGroupIds<RuntimePicked>(1000, 1, 1, overload: 2));
        Assert.Equal((255u, 0u, 0u), ObserveMaxGroupIds<RuntimePicked>(1000, 1, 1, overload: 3));

        // The 3-argument overload with z == 1 is a 2D dispatch: (16, 16, 1).
        Assert.Equal((15u, 15u, 0u), ObserveMaxGroupIds<RuntimePicked>(50, 40, 1, overload: 3));

        // y == 1 with z > 1 is neither 1D nor 2D by the rule, so it is 3D: (8, 8, 4).
        Assert.Equal((7u, 0u, 3u), ObserveMaxGroupIds<RuntimePicked>(100, 1, 10, overload: 3));
    }

    [MetalFact]
    public void Explicit_DispatchesTheDescriptorSize()
    {
        Assert.Equal((63u, 0u, 0u), ObserveMaxGroupIds<Size64x1x1>(1000, 1, 1, overload: 1));
        Assert.Equal((3u, 1u, 2u), ObserveMaxGroupIds<Size4x2x3>(9, 9, 9, overload: 3));
    }

    [MetalFact]
    public void Explicit_LargerThanMaxTotalThreadsPerThreadgroup_ThrowsInsteadOfDispatching()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(3 * 16);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => device.For(16, new RecordGroupIds<Size2048x1x1>(buffer)));

        Assert.Contains("2048", exception.Message);
        Assert.All(buffer.ToArray(), value => Assert.Equal(0u, value));
    }

    [MetalFact]
    public void Explicit_PartiallyZero_Throws()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(3 * 16);

        Assert.Throws<InvalidOperationException>(() => device.For(16, new RecordGroupIds<Size0x4x1>(buffer)));
    }

    private static (uint X, uint Y, uint Z) ObserveMaxGroupIds<TSize>(int x, int y, int z, int overload)
        where TSize : struct, IThreadGroupSize
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(x * y * z * 3);
        RecordGroupIds<TSize> shader = new(buffer);

        switch (overload)
        {
            case 1:
                device.For(x, shader);
                break;
            case 2:
                device.For(x, y, shader);
                break;
            default:
                device.For(x, y, z, shader);
                break;
        }

        (uint X, uint Y, uint Z) max = default;
        ReadOnlySpan<uint> ids = buffer.AsSpan();

        for (int i = 0; i < ids.Length; i += 3)
        {
            max = (Math.Max(max.X, ids[i]), Math.Max(max.Y, ids[i + 1]), Math.Max(max.Z, ids[i + 2]));
        }

        return max;
    }
}
