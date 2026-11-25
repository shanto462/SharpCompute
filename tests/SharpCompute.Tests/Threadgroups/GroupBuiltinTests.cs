using System;

namespace SharpCompute.Tests.Threadgroups;

/// <summary>
/// <see cref="GroupSize"/>, <see cref="GroupIds.Index"/> and <see cref="ThreadGroupCount"/>: the values
/// every thread sees on the GPU, on the CPU and in each part of a split dispatch, for grids that are
/// and are not multiples of the threadgroup size.
/// </summary>
public class GroupBuiltinTests
{
    [Fact]
    public void OutsideADispatch_TheyThrowAndNameTheCpuEntryPoints()
    {
        Func<object>[] reads =
        [
            () => GroupSize.X, () => GroupSize.Y, () => GroupSize.Z, () => GroupSize.XY, () => GroupSize.XYZ, () => GroupSize.Count,
            () => GroupIds.Index,
            () => ThreadGroupCount.X, () => ThreadGroupCount.Y, () => ThreadGroupCount.Z, () => ThreadGroupCount.XY, () => ThreadGroupCount.XYZ,
        ];

        foreach (Func<object> read in reads)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);
            Assert.Contains("Execute", exception.Message);
            Assert.Contains("ForCpu", exception.Message);
        }
    }

    /// <summary>What the definitions give for one thread: the group it is in is cut off at the edge of the grid.</summary>
    private static int[] Expected3D(int x, int y, int z, int gridX, int gridY, int gridZ, int sizeX, int sizeY, int sizeZ)
    {
        int extentX = Math.Min(sizeX, gridX - (x / sizeX * sizeX));
        int extentY = Math.Min(sizeY, gridY - (y / sizeY * sizeY));
        int extentZ = Math.Min(sizeZ, gridZ - (z / sizeZ * sizeZ));
        int index = (((z % sizeZ * extentY) + (y % sizeY)) * extentX) + (x % sizeX);

        return
        [
            extentX, extentY, extentZ, extentX * extentY * extentZ, index,
            (gridX + sizeX - 1) / sizeX, (gridY + sizeY - 1) / sizeY, (gridZ + sizeZ - 1) / sizeZ,
        ];
    }

    private static void AssertExpected(int[] values, int gridX, int gridY, int gridZ, int sizeX, int sizeY, int sizeZ, string device)
    {
        for (int z = 0; z < gridZ; z++)
        {
            for (int y = 0; y < gridY; y++)
            {
                for (int x = 0; x < gridX; x++)
                {
                    int i = ((((z * gridY) + y) * gridX) + x) * 8;
                    int[] expected = Expected3D(x, y, z, gridX, gridY, gridZ, sizeX, sizeY, sizeZ);

                    Assert.True(
                        values.AsSpan(i, 8).SequenceEqual(expected),
                        $"{device}, thread ({x}, {y}, {z}) of {gridX} x {gridY} x {gridZ}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", values[i..(i + 8)])}].");
                }
            }
        }
    }

    [MetalTheory]
    [InlineData(8, 4, 4)]
    [InlineData(9, 5, 3)]
    [InlineData(1, 1, 1)]
    [InlineData(3, 1, 7)]
    [InlineData(17, 6, 1)]
    public void Builtins3D_HaveTheDefinedValues_OnTheGpuAndTheCpu(int x, int y, int z)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(x * y * z * RecordGroupBuiltins3D.Stride);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(x * y * z * RecordGroupBuiltins3D.Stride);

        device.For(x, y, z, new RecordGroupBuiltins3D(gpu));
        device.ForCpu(x, y, z, new RecordGroupBuiltins3D(cpu));

        // For(17, 6, 1) is a 2D dispatch, but the shader's explicit ThreadGroupSize(4, 2, 2) still applies.
        AssertExpected(gpu.ToArray(), x, y, z, 4, 2, 2, "GPU");
        AssertExpected(cpu.ToArray(), x, y, z, 4, 2, 2, "CPU");
    }

    [MetalTheory]
    [InlineData(32, 48)]
    [InlineData(37, 45)]
    [InlineData(5, 3)]
    [InlineData(16, 17)]
    public void Builtins2D_HaveTheDefinedValues_OnTheGpuAndTheCpu(int x, int y)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(x * y * RecordGroupBuiltins2D.Stride);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(x * y * RecordGroupBuiltins2D.Stride);

        device.For(x, y, new RecordGroupBuiltins2D(gpu));
        device.ForCpu(x, y, new RecordGroupBuiltins2D(cpu));

        AssertExpected(gpu.ToArray(), x, y, 1, 16, 16, 1, "GPU");
        AssertExpected(cpu.ToArray(), x, y, 1, 16, 16, 1, "CPU");
    }

    [MetalTheory]
    [InlineData(64)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(1000)]
    public void Builtins1D_HaveTheDefinedValues_OnTheGpuAndTheCpu(int x)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(x * RecordGroupBuiltins1D.Stride);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(x * RecordGroupBuiltins1D.Stride);

        device.For(x, new RecordGroupBuiltins1D(gpu));
        device.ForCpu(x, new RecordGroupBuiltins1D(cpu));

        int[] expected = new int[x * RecordGroupBuiltins1D.Stride];

        for (int i = 0; i < x; i++)
        {
            int extent = Math.Min(64, x - (i / 64 * 64));
            expected[(i * 4) + 0] = extent;
            expected[(i * 4) + 1] = extent;
            expected[(i * 4) + 2] = i % 64;
            expected[(i * 4) + 3] = (x + 63) / 64;
        }

        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
    }

    public static TheoryData<double> Fractions => new(0.0, 0.01, 0.3, 0.5, 0.8, 0.99, 1.0);

    [MetalTheory]
    [MemberData(nameof(Fractions))]
    public void Split3D_EveryPartSeesTheWholeGrid(double fraction)
    {
        // The GPU part is a windowed dispatch: its threadgroup count must be the whole grid's, not the part's.
        const int x = 9, y = 5, z = 7;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> split = device.AllocateReadWriteBuffer<int>(x * y * z * RecordGroupBuiltins3D.Stride);

        SplitDispatchInfo info = device.ForSplit(x, y, z, new RecordGroupBuiltins3D(split), fraction);

        Assert.Equal(z, info.CpuExtent + info.GpuExtent);
        AssertExpected(split.ToArray(), x, y, z, 4, 2, 2, $"split ({info.CpuExtent} slices on the CPU)");
    }

    [MetalTheory]
    [MemberData(nameof(Fractions))]
    public void Split2D_EveryPartSeesTheWholeGrid(double fraction)
    {
        const int x = 37, y = 70;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> split = device.AllocateReadWriteBuffer<int>(x * y * RecordGroupBuiltins2D.Stride);

        SplitDispatchInfo info = device.ForSplit(x, y, new RecordGroupBuiltins2D(split), fraction);

        Assert.Equal(y, info.CpuExtent + info.GpuExtent);
        AssertExpected(split.ToArray(), x, y, 1, 16, 16, 1, $"split ({info.CpuExtent} rows on the CPU)");
    }

    [MetalTheory]
    [MemberData(nameof(Fractions))]
    public void Split1D_EveryPartSeesTheWholeGrid(double fraction)
    {
        const int x = 1000;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> split = device.AllocateReadWriteBuffer<int>(x * RecordGroupBuiltins1D.Stride);
        using ReadWriteBuffer<int> whole = device.AllocateReadWriteBuffer<int>(x * RecordGroupBuiltins1D.Stride);

        device.For(x, new RecordGroupBuiltins1D(whole));
        SplitDispatchInfo info = device.ForSplit(x, new RecordGroupBuiltins1D(split), fraction);

        Assert.Equal(x, info.CpuExtent + info.GpuExtent);
        Assert.True(info.CpuExtent % 64 == 0 || info.CpuExtent == x, $"CPU extent {info.CpuExtent}");
        Assert.Equal(whole.ToArray(), split.ToArray());
    }
}
