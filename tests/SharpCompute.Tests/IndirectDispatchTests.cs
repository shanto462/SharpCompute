using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// <c>ForIndirect</c>: the threadgroup counts of a dispatch come from a buffer the GPU reads when the
/// dispatch runs, so a pass recorded earlier in the same context can decide them.
/// </summary>
public class IndirectDispatchTests
{
    [MetalFact]
    public unsafe void DispatchArguments_IsThreeUInts_LikeMetalsIndirectArguments()
    {
        Assert.Equal(12, sizeof(DispatchArguments));

        DispatchArguments arguments = new(3, 4, 5);
        ReadOnlySpan<uint> raw = MemoryMarshal.Cast<DispatchArguments, uint>(new ReadOnlySpan<DispatchArguments>(in arguments));
        Assert.Equal([3u, 4u, 5u], raw.ToArray());

        Assert.Equal(new DispatchArguments(7, 1, 1), new DispatchArguments(7));
        Assert.True(new DispatchArguments(1, 2, 3) == new DispatchArguments(1, 2, 3));
        Assert.True(new DispatchArguments(1, 2, 3) != new DispatchArguments(1, 2, 4));
        Assert.Equal(new DispatchArguments(1, 2, 3).GetHashCode(), new DispatchArguments(1, 2, 3).GetHashCode());
        Assert.Equal("1 × 2 × 3 threadgroups", new DispatchArguments(1, 2, 3).ToString());
    }

    [Theory]
    [InlineData(0, 64, 0u)]
    [InlineData(1, 64, 1u)]
    [InlineData(64, 64, 1u)]
    [InlineData(65, 64, 2u)]
    [InlineData(1000, 256, 4u)]
    [InlineData(int.MaxValue, 1, 2147483647u)]
    [InlineData(int.MaxValue, 256, 8388608u)]
    [InlineData(int.MaxValue, int.MaxValue, 1u)]
    public void FromCount_RoundsUpToWholeThreadGroups(int count, int size, uint groups)
    {
        Assert.Equal(new DispatchArguments(groups, 1, 1), DispatchArguments.FromCount(count, size));
    }

    [Fact]
    public void FromCount_RejectsNegativeCountsAndNonPositiveSizes()
    {
        Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => DispatchArguments.FromCount(-1, 64)).ParamName);
        Assert.Equal("threadGroupSize", Assert.Throws<ArgumentOutOfRangeException>(() => DispatchArguments.FromCount(1, 0)).ParamName);
        Assert.Equal("threadGroupSize", Assert.Throws<ArgumentOutOfRangeException>(() => DispatchArguments.FromCount(1, -4)).ParamName);
    }

    [MetalTheory]
    [InlineData(1000, 16, 1024)]
    [InlineData(64, 1, 64)]
    [InlineData(65, 2, 128)]
    [InlineData(1, 1, 64)]
    public void ForIndirect_RunsWholeThreadGroups_AndDispatchSizeIsTheirProduct(int count, int groups, int threads)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([count]);
        using ReadWriteBuffer<int> touched = device.AllocateReadWriteBuffer<int>(2048);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>([DispatchArguments.FromCount(count, 64)]);

        Assert.Equal((uint)groups, arguments[0].ThreadGroupsX);
        device.ForIndirect(arguments, new IndirectProbe64(counter, touched, stats));

        // More threads ran than there are elements (a whole number of groups), DispatchSize says so,
        // and the shader's own check kept the extra threads away from the data.
        Assert.Equal(threads, stats[0]);
        Assert.Equal(threads, stats[1]);
        int[] result = touched.ToArray();
        Assert.All(result[..count], value => Assert.Equal(1, value));
        Assert.All(result[count..], value => Assert.Equal(0, value));
    }

    [MetalFact]
    public void ForIndirect_WithZeroGroups_RunsNothing()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([0]);
        using ReadWriteBuffer<int> touched = device.AllocateReadWriteBuffer<int>(256);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(
            [new DispatchArguments(0), new DispatchArguments(4, 0, 1), new DispatchArguments(4, 1, 0)]);

        for (int slot = 0; slot < 3; slot++)
        {
            device.ForIndirect(arguments, slot, new IndirectProbe64(counter, touched, stats));
            device.ForIndirectCpu(arguments, slot, new IndirectProbe64(counter, touched, stats));
        }

        Assert.Equal([0, 0], stats.ToArray());
    }

    [MetalFact]
    public void ForIndirect_ReadsTheSlotAtArgumentsIndex_AlsoThroughAView()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([10_000]);
        using ReadWriteBuffer<int> touched = device.AllocateReadWriteBuffer<int>(1024);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(
            Enumerable.Range(1, 8).Select(i => new DispatchArguments((uint)i)).ToArray());

        device.ForIndirect(arguments, 4, new IndirectProbe64(counter, touched, stats));
        Assert.Equal(5 * 64, stats[0]);

        // Slot 1 of a view that starts at slot 5 is slot 6 of the buffer: 7 groups.
        stats[0] = 0;
        device.ForIndirect(arguments.Slice(5, 3), 1, new IndirectProbe64(counter, touched, stats));
        Assert.Equal(7 * 64, stats[0]);

        // A read-only buffer of arguments works too.
        using ReadOnlyBuffer<DispatchArguments> constant = device.AllocateReadOnlyBuffer<DispatchArguments>([new DispatchArguments(2)]);
        stats[0] = 0;
        device.ForIndirect(constant, new IndirectProbe64(counter, touched, stats));
        Assert.Equal(2 * 64, stats[0]);
    }

    [MetalFact]
    public void ForIndirect_WithoutAThreadGroupSize_UsesTheOneDimensionalDefault()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([300]);
        using ReadWriteBuffer<int> touched = device.AllocateReadWriteBuffer<int>(1024);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(1);

        // 300 elements with the default group of 256 threads: 2 groups, 512 threads.
        device.WriteDispatchArguments<IndirectProbeDefault>(counter, arguments);
        Assert.Equal(new DispatchArguments(2, 1, 1), arguments[0]);

        device.ForIndirect(arguments, new IndirectProbeDefault(counter, touched, stats));
        Assert.Equal([512, 512], stats.ToArray());
        Assert.Equal(300, touched.ToArray().Sum());
    }

    [MetalFact]
    public void ForIndirect_TwoDimensional_UsesTheShadersThreadGroupSize()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(3);
        using ReadWriteBuffer<int> cpuStats = device.AllocateReadWriteBuffer<int>(3);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>([new DispatchArguments(5, 3)]);

        // 5 × 3 groups of 8 × 4 threads.
        device.ForIndirect(arguments, new IndirectProbe2D(stats));
        device.ForIndirectCpu(arguments, new IndirectProbe2D(cpuStats));

        Assert.Equal([40 * 12, 40, 12], stats.ToArray());
        Assert.Equal(stats.ToArray(), cpuStats.ToArray());
    }

    [MetalFact]
    public void ForIndirectCpu_MatchesTheGpu()
    {
        const int count = 1000;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([count]);
        using ReadWriteBuffer<int> gpuTouched = device.AllocateReadWriteBuffer<int>(2048);
        using ReadWriteBuffer<int> cpuTouched = device.AllocateReadWriteBuffer<int>(2048);
        using ReadWriteBuffer<int> gpuStats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<int> cpuStats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(2);

        // The arguments are written on the GPU and then read by the CPU dispatch: this call waited for them.
        device.WriteDispatchArguments<IndirectProbe64>(counter, 0, arguments, 1);
        device.ForIndirect(arguments, 1, new IndirectProbe64(counter, gpuTouched, gpuStats));
        device.ForIndirectCpu(arguments, 1, new IndirectProbe64(counter, cpuTouched, cpuStats));

        Assert.Equal([1024, 1024], gpuStats.ToArray());
        Assert.Equal(gpuStats.ToArray(), cpuStats.ToArray());
        Assert.Equal(gpuTouched.ToArray(), cpuTouched.ToArray());
    }

    [MetalFact]
    public void ForIndirectCpu_RejectsAGridThatDoesNotFitInInt()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>(1);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>([new DispatchArguments(uint.MaxValue)]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => device.ForIndirectCpu(arguments, new IndirectProbe64(counter, counter, stats)));
        Assert.Equal("arguments", error.ParamName);
        Assert.Equal(0, stats[0]);
    }

    [MetalTheory]
    [InlineData(0, 0u)]
    [InlineData(1, 1u)]
    [InlineData(64, 1u)]
    [InlineData(65, 2u)]
    [InlineData(1_000_000, 15625u)]
    [InlineData(int.MaxValue, 33554432u)]
    [InlineData(-1, 0u)]
    [InlineData(int.MinValue, 0u)]
    public void WriteDispatchArguments_ComputesTheSameGroupsAsFromCount(int count, uint groups)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([-7, count, -7]);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(
            Enumerable.Repeat(new DispatchArguments(99, 99, 99), 4).ToArray());

        device.WriteDispatchArguments<IndirectProbe64>(counter, 1, arguments, 2);

        // Only the addressed slot is written.
        Assert.Equal(new DispatchArguments(groups, 1, 1), arguments[2]);
        Assert.Equal(new DispatchArguments(99, 99, 99), arguments[0]);
        Assert.Equal(new DispatchArguments(99, 99, 99), arguments[1]);
        Assert.Equal(new DispatchArguments(99, 99, 99), arguments[3]);

        if (count >= 0)
        {
            Assert.Equal(DispatchArguments.FromCount(count, 64), arguments[2]);
        }
    }

    [MetalFact]
    public void WriteDispatchArguments_ReadsUnsignedCountsAsUnsigned()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> counter = device.AllocateReadWriteBuffer<uint>([130u, 0x8000_0001u, uint.MaxValue]);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(3);

        device.WriteDispatchArguments<IndirectProbe64>(counter, arguments);
        device.WriteDispatchArguments<IndirectProbe64>(counter, 1, arguments, 1);
        device.WriteDispatchArguments<IndirectProbe64>(counter.Slice(2), 0, arguments.Slice(1), 1);

        // The same bits in an int buffer would be negative counts (0 groups); here they are large ones.
        Assert.Equal(new DispatchArguments(3), arguments[0]);
        Assert.Equal(new DispatchArguments(0x0200_0001u), arguments[1]);
        Assert.Equal(new DispatchArguments(0x0400_0000u), arguments[2]);
    }

    [MetalFact]
    public void WriteDispatchArguments_RejectsAShaderWhoseThreadGroupIsNotOneDimensional()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>(1);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>([new DispatchArguments(9)]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => device.WriteDispatchArguments<IndirectProbe2D>(counter, arguments));
        Assert.Contains("(8, 4, 1)", error.Message);

        using ComputeContext context = device.CreateComputeContext();
        Assert.Throws<InvalidOperationException>(() => context.WriteDispatchArguments<IndirectProbe2D>(counter, arguments));
        Assert.Equal(0, context.PendingCount);
        Assert.Equal(new DispatchArguments(9), arguments[0]);
    }

    [MetalFact]
    public void Context_CountConvertDispatch_RunsOnTheGpuInOneExecution()
    {
        const int length = 100_000;
        const int cutoff = 61_234;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> values = device.AllocateReadOnlyBuffer<int>(Enumerable.Range(0, length).ToArray());
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([-5]);
        using ReadWriteBuffer<int> touched = device.AllocateReadWriteBuffer<int>(length);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(1);
        int expected = length - cutoff;

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.Clear(counter);
            context.For(length, new CountAbove(values, counter, cutoff));
            context.WriteDispatchArguments<IndirectProbe64>(counter, arguments);
            context.ForIndirect(arguments, new IndirectProbe64(counter, touched, stats));

            // The count is not known to the CPU at any point before this executes.
            Assert.Equal(4, context.PendingCount);
            Assert.Equal(default, arguments[0]);
        }

        int threads = (expected + 63) / 64 * 64;
        Assert.Equal(expected, counter[0]);
        Assert.Equal(DispatchArguments.FromCount(expected, 64), arguments[0]);
        Assert.Equal([threads, threads], stats.ToArray());

        int[] result = touched.ToArray();
        Assert.All(result[..expected], value => Assert.Equal(1, value));
        Assert.All(result[expected..], value => Assert.Equal(0, value));
    }

    [MetalFact]
    public void Context_ArgumentsWrittenByAUserShader_DriveTheNextDispatch()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([200]);
        using ReadWriteBuffer<int> touched = device.AllocateReadWriteBuffer<int>(1024);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(4);

        using (ComputeContext context = device.CreateComputeContext())
        {
            // DispatchArguments is an ordinary struct to a shader: this one fills slot 3.
            context.For(1, new WriteArguments64(counter, arguments, slot: 3));
            context.ForIndirect(arguments, 3, new IndirectProbe64(counter, touched, stats));
        }

        Assert.Equal(new DispatchArguments(4, 1, 1), arguments[3]);
        Assert.Equal([256, 256], stats.ToArray());
        Assert.Equal(200, touched.ToArray().Sum());
    }

    [MetalFact]
    public void Context_RepeatedIndirectChains_EachSeeTheirOwnCount()
    {
        // The same arguments slot is rewritten between dispatches: each dispatch must read the value
        // that was current when it ran, in recorded order.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>([64, 640, 0, 6400, 1]);
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>(1);
        using ReadWriteBuffer<int> touched = device.AllocateReadWriteBuffer<int>(8192);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<int> log = device.AllocateReadWriteBuffer<int>(5);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(1);

        using (ComputeContext context = device.CreateComputeContext())
        {
            for (int i = 0; i < 5; i++)
            {
                context.Copy(counts, i, counter, 0, 1);
                context.Clear(stats);
                context.WriteDispatchArguments<IndirectProbe64>(counter, arguments);
                context.ForIndirect(arguments, new IndirectProbe64(counter, touched, stats));
                context.Copy(stats, 0, log, i, 1);
            }
        }

        Assert.Equal([64, 640, 0, 6400, 64], log.ToArray());
    }

    [MetalFact]
    public void ArgumentsDisposedWhilePending_StayAliveUntilTheDispatchHasRun()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([128]);
        using ReadWriteBuffer<int> touched = device.AllocateReadWriteBuffer<int>(256);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>([new DispatchArguments(2)]);
        nint mtlArguments = ObjC.Retain(arguments.Handle.DangerousGetHandle());

        try
        {
            ComputeContext context = device.CreateComputeContext();
            context.ForIndirect(arguments, new IndirectProbe64(counter, touched, stats));

            arguments.Dispose();
            Assert.True(ComputeContextTests.RetainCount(mtlArguments) > 1);

            context.Dispose();

            Assert.Equal(128, stats[0]);
            ComputeContextTests.WaitForRetainCount(mtlArguments, 1);
        }
        finally
        {
            ObjC.Release(mtlArguments);
        }
    }

    [MetalFact]
    public void InvalidArguments_AreRejected_AndRecordNothing()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using GraphicsDevice other = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>(1);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>(2);
        using ReadWriteBuffer<DispatchArguments> empty = device.AllocateReadWriteBuffer<DispatchArguments>(0);
        using ReadWriteBuffer<DispatchArguments> foreign = other.AllocateReadWriteBuffer<DispatchArguments>(2);
        using ReadWriteBuffer<int> foreignCounter = other.AllocateReadWriteBuffer<int>(1);
        ReadWriteBuffer<DispatchArguments> disposed = device.AllocateReadWriteBuffer<DispatchArguments>(2);
        disposed.Dispose();
        IndirectProbe64 shader = new(counter, counter, stats);

        using ComputeContext context = device.CreateComputeContext();

        Assert.Equal("argumentsIndex", Assert.Throws<ArgumentOutOfRangeException>(() => device.ForIndirect(arguments, 2, shader)).ParamName);
        Assert.Equal("argumentsIndex", Assert.Throws<ArgumentOutOfRangeException>(() => device.ForIndirect(arguments, -1, shader)).ParamName);
        Assert.Equal("argumentsIndex", Assert.Throws<ArgumentOutOfRangeException>(() => device.ForIndirect(empty, shader)).ParamName);
        Assert.Equal("argumentsIndex", Assert.Throws<ArgumentOutOfRangeException>(() => device.ForIndirectCpu(arguments, 2, shader)).ParamName);
        Assert.Equal("argumentsIndex", Assert.Throws<ArgumentOutOfRangeException>(() => context.ForIndirect(arguments, 2, shader)).ParamName);
        Assert.Equal("arguments", Assert.Throws<ArgumentNullException>(() => device.ForIndirect(null!, shader)).ParamName);
        Assert.Equal("arguments", Assert.Throws<ArgumentNullException>(() => context.ForIndirect(null!, shader)).ParamName);
        Assert.Equal("arguments", Assert.Throws<ArgumentException>(() => device.ForIndirect(foreign, shader)).ParamName);
        Assert.Equal("arguments", Assert.Throws<ArgumentException>(() => device.ForIndirectCpu(foreign, shader)).ParamName);
        Assert.Equal("arguments", Assert.Throws<ArgumentException>(() => context.ForIndirect(foreign, shader)).ParamName);
        Assert.Throws<ObjectDisposedException>(() => device.ForIndirect(disposed, shader));
        Assert.Throws<ObjectDisposedException>(() => device.ForIndirectCpu(disposed, shader));
        Assert.Throws<ObjectDisposedException>(() => context.ForIndirect(disposed, shader));

        // The shader's own resources are checked as for any dispatch.
        Assert.Throws<ArgumentNullException>(() => device.ForIndirect(arguments, default(IndirectProbe64)));
        Assert.Throws<ArgumentNullException>(() => context.ForIndirect(arguments, default(IndirectProbe64)));
        Assert.Throws<ArgumentNullException>(() => device.ForIndirectCpu(arguments, default(IndirectProbe64)));

        Assert.Equal("countIndex", Assert.Throws<ArgumentOutOfRangeException>(() => device.WriteDispatchArguments<IndirectProbe64>(counter, 1, arguments, 0)).ParamName);
        Assert.Equal("argumentsIndex", Assert.Throws<ArgumentOutOfRangeException>(() => device.WriteDispatchArguments<IndirectProbe64>(counter, 0, arguments, 2)).ParamName);
        Assert.Equal("argumentsIndex", Assert.Throws<ArgumentOutOfRangeException>(() => context.WriteDispatchArguments<IndirectProbe64>(counter, 0, arguments, -1)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentException>(() => device.WriteDispatchArguments<IndirectProbe64>(foreignCounter, arguments)).ParamName);
        Assert.Equal("arguments", Assert.Throws<ArgumentException>(() => device.WriteDispatchArguments<IndirectProbe64>(counter, foreign)).ParamName);
        Assert.Equal("count", Assert.Throws<ArgumentNullException>(() => device.WriteDispatchArguments<IndirectProbe64>((Buffer<int>)null!, arguments)).ParamName);
        Assert.Throws<ObjectDisposedException>(() => device.WriteDispatchArguments<IndirectProbe64>(counter, disposed));
        Assert.Throws<ObjectDisposedException>(() => context.WriteDispatchArguments<IndirectProbe64>(counter, disposed));

        Assert.Equal(0, context.PendingCount);
        Assert.Equal(0, stats[0]);
    }

    [MetalFact]
    public void WriteDispatchArguments_IntoMappedReadOnlyMemory_IsRejected()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string path = Path.Combine(Path.GetTempPath(), $"sharpcompute-arguments-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[24]);

        try
        {
            using ReadOnlyBuffer<DispatchArguments> mapped = device.MapReadOnlyBuffer<DispatchArguments>(path);
            using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([5]);

            Assert.Throws<NotSupportedException>(() => device.WriteDispatchArguments<IndirectProbe64>(counter, mapped));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [MetalFact]
    public void SimulatedGpuError_NamesTheIndirectlyDispatchedShader()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([1]);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>([new DispatchArguments(1)]);
        using ComputeContext context = device.CreateComputeContext();

        context.ForIndirect(arguments, new IndirectProbe64(counter, counter, stats));
        context.SimulateGpuError("Injected");

        Assert.Contains($"shader '{typeof(IndirectProbe64).FullName}'", Assert.Throws<MetalException>(context.Execute).Message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordAndLeak(GraphicsDevice device, ReadWriteBuffer<DispatchArguments> arguments, ReadWriteBuffer<int> counter, ReadWriteBuffer<int> stats)
    {
        ComputeContext context = device.CreateComputeContext();
        context.ForIndirect(arguments, new IndirectProbe64(counter, counter, stats));
    }

    [MetalFact]
    public void AbandonedContext_WithAPendingIndirectDispatch_ReleasesTheArguments()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> counter = device.AllocateReadWriteBuffer<int>([1]);
        using ReadWriteBuffer<int> stats = device.AllocateReadWriteBuffer<int>(2);
        ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>([new DispatchArguments(1)]);
        nint mtlArguments = ObjC.Retain(arguments.Handle.DangerousGetHandle());

        try
        {
            RecordAndLeak(device, arguments, counter, stats);
            arguments.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.Equal(0, stats[0]);
            ComputeContextTests.WaitForRetainCount(mtlArguments, 1);
        }
        finally
        {
            ObjC.Release(mtlArguments);
        }
    }
}
