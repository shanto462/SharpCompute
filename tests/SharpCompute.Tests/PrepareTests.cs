using System;
using System.Linq;
using System.Threading.Tasks;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// <c>Prepare</c> / <c>PrepareAsync</c>: the pipeline is compiled and cached ahead of the first
/// dispatch. Every test uses its own device, so "not compiled yet" is known to be true at the start.
/// </summary>
public class PrepareTests
{
    [MetalFact]
    public void Prepare_CompilesAndCachesThePipeline_BeforeAnyDispatch()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        Assert.False(device.IsPipelineCached<WarmupA>());
        device.Prepare<WarmupA>();
        Assert.True(device.IsPipelineCached<WarmupA>());
        Assert.False(device.IsPipelineCached<WarmupB>());

        // Preparing again is a no-op, and the dispatch uses the prepared pipeline.
        device.Prepare<WarmupA>();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(16);
        device.For(16, new WarmupA(buffer));
        Assert.All(buffer.ToArray(), value => Assert.Equal(11, value));
    }

    [MetalFact]
    public async Task PrepareAsync_CompletesWhenThePipelineIsCached()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        Task first = device.PrepareAsync<WarmupA>();
        await first;
        Assert.True(device.IsPipelineCached<WarmupA>());

        // Already compiled: no work is queued at all.
        Assert.Same(Task.CompletedTask, device.PrepareAsync<WarmupA>());

        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(16);
        device.For(16, new WarmupA(buffer));
        Assert.Equal(11, buffer[15]);
    }

    [MetalFact]
    public void Prepare_OfSeveralShaders_CachesEachOne()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        device.Prepare(ShaderReference.Of<WarmupA>(), ShaderReference.Of<WarmupB>());

        Assert.True(device.IsPipelineCached<WarmupA>());
        Assert.True(device.IsPipelineCached<WarmupB>());
        Assert.False(device.IsPipelineCached<WarmupC>());

        // No shaders is allowed and does nothing.
        device.Prepare();
    }

    [MetalFact]
    public async Task PrepareAsync_OfSeveralShaders_CompilesThemAll()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        await device.PrepareAsync(ShaderReference.Of<WarmupA>(), ShaderReference.Of<WarmupB>(), ShaderReference.Of<WarmupC>(), ShaderReference.Of<WarmupA>());

        Assert.True(device.IsPipelineCached<WarmupA>());
        Assert.True(device.IsPipelineCached<WarmupB>());
        Assert.True(device.IsPipelineCached<WarmupC>());
        Assert.Same(Task.CompletedTask, device.PrepareAsync());
    }

    [MetalFact]
    public void Prepare_RacingWithDispatches_CompilesOnce_AndEveryDispatchIsCorrect()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        ReadWriteBuffer<int>[] buffers = Enumerable.Range(0, 8).Select(_ => device.AllocateReadWriteBuffer<int>(64)).ToArray();

        try
        {
            Task[] tasks = new Task[buffers.Length + 2];
            tasks[0] = device.PrepareAsync<WarmupB>();
            tasks[1] = Task.Run(device.Prepare<WarmupB>);

            for (int i = 0; i < buffers.Length; i++)
            {
                ReadWriteBuffer<int> buffer = buffers[i];
                tasks[i + 2] = Task.Run(() => device.For(64, new WarmupB(buffer)));
            }

            Assert.True(Task.WaitAll(tasks, TimeSpan.FromSeconds(60)));
            Assert.All(buffers, buffer => Assert.All(buffer.ToArray(), value => Assert.Equal(22, value)));
        }
        finally
        {
            Array.ForEach(buffers, buffer => buffer.Dispose());
        }
    }

    [MetalFact]
    public async Task Prepare_SurfacesCompileErrors_AndKeepsReportingThem()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        ShaderCompilationException error = Assert.Throws<ShaderCompilationException>(device.Prepare<WarmupBroken>);
        Assert.Contains(nameof(WarmupBroken), error.Message);
        Assert.False(device.IsPipelineCached<WarmupBroken>());

        // Thrown synchronously by Prepare, stored in the task by PrepareAsync.
        Task task = device.PrepareAsync<WarmupBroken>();
        await Assert.ThrowsAsync<ShaderCompilationException>(() => task);

        // With several shaders the failure does not stop the others from being prepared.
        Task many = device.PrepareAsync(ShaderReference.Of<WarmupBroken>(), ShaderReference.Of<WarmupC>());
        await Assert.ThrowsAsync<ShaderCompilationException>(() => many);
        Assert.True(device.IsPipelineCached<WarmupC>());

        // Sequentially, the ones after the failure are not reached.
        Assert.Throws<ShaderCompilationException>(() => device.Prepare(ShaderReference.Of<WarmupBroken>(), ShaderReference.Of<WarmupA>()));
        Assert.False(device.IsPipelineCached<WarmupA>());
    }

    [MetalFact]
    public void Prepare_OnADisposedDevice_Throws()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.Dispose();

        Assert.Throws<ObjectDisposedException>(device.Prepare<WarmupA>);
        ThrowsSynchronously<ObjectDisposedException>(device.PrepareAsync<WarmupA>);
        Assert.Throws<ObjectDisposedException>(() => device.Prepare(ShaderReference.Of<WarmupA>()));
        ThrowsSynchronously<ObjectDisposedException>(() => device.PrepareAsync(ShaderReference.Of<WarmupA>()));
    }

    [MetalFact]
    public void Prepare_RejectsNullReferences_BeforeCompilingAnything()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        Assert.Throws<ArgumentNullException>(() => device.Prepare(ShaderReference.Of<WarmupA>(), null!));
        ThrowsSynchronously<ArgumentNullException>(() => device.PrepareAsync(ShaderReference.Of<WarmupA>(), null!));
        ThrowsSynchronously<ArgumentNullException>(() => device.PrepareAsync((ShaderReference[])null!));
        Assert.False(device.IsPipelineCached<WarmupA>());
    }

    [MetalFact]
    public void Prepare_CachesPerDevice()
    {
        using GraphicsDevice first = GraphicsDevice.CreateSystemDefault();
        using GraphicsDevice second = GraphicsDevice.CreateSystemDefault();

        first.Prepare<WarmupA>();

        Assert.True(first.IsPipelineCached<WarmupA>());
        Assert.False(second.IsPipelineCached<WarmupA>());
    }

    [MetalFact]
    public void Prepare_OfAShaderWithOptions_IsTheOneItsDispatchesUse()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        device.Prepare<MulAddNoContract>();

        Assert.True(device.IsPipelineCached<MulAddNoContract>());
        Assert.False(device.IsPipelineCached<MulAdd>());
    }

    [MetalFact]
    public void Prepare_WithIncludeSplit_CompilesTheSplitSpecialization_SoASplitDispatchDoesNotCompile()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(4096);

        // Without the option only the ordinary pipeline is compiled.
        device.Prepare<WarmupA>();
        Assert.Equal(1, device.PipelineCreationCount);
        Assert.True(device.IsPipelineCached<WarmupA>());
        Assert.False(device.IsPipelineCached<WarmupA>(windowed: true));

        // With it, the specialization ForSplit runs on the GPU is compiled too (and only that: the
        // ordinary one is already there).
        device.Prepare<WarmupA>(PrepareOptions.IncludeSplit);
        Assert.Equal(2, device.PipelineCreationCount);
        Assert.True(device.IsPipelineCached<WarmupA>(windowed: true));

        // Neither a split dispatch nor an ordinary one compiles anything now.
        SplitDispatchInfo info = device.ForSplit(4096, new WarmupA(buffer), cpuFraction: 0.5);
        Assert.True(info.CpuExtent > 0 && info.GpuExtent > 0);
        device.For(4096, new WarmupA(buffer));
        device.Prepare<WarmupA>(PrepareOptions.IncludeSplit);
        Assert.Equal(2, device.PipelineCreationCount);
        Assert.All(buffer.ToArray(), value => Assert.Equal(11, value));

        // The control: a shader that was not prepared compiles its specialization in the split dispatch.
        device.ForSplit(4096, new WarmupB(buffer), cpuFraction: 0.5);
        Assert.Equal(3, device.PipelineCreationCount);
        Assert.True(device.IsPipelineCached<WarmupB>(windowed: true));
        Assert.False(device.IsPipelineCached<WarmupB>());
    }

    [MetalFact]
    public async Task PrepareAsync_AndTheMultiShaderForms_TakeTheOptionToo()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        await device.PrepareAsync<WarmupA>(PrepareOptions.IncludeSplit);
        Assert.Equal(2, device.PipelineCreationCount);
        Assert.True(device.IsPipelineCached<WarmupA>() && device.IsPipelineCached<WarmupA>(windowed: true));
        Assert.Same(Task.CompletedTask, device.PrepareAsync<WarmupA>(PrepareOptions.IncludeSplit));

        // The ordinary pipeline alone is not "already prepared" when the split one is asked for.
        device.Prepare<WarmupB>();
        Task both = device.PrepareAsync<WarmupB>(PrepareOptions.IncludeSplit);
        Assert.NotSame(Task.CompletedTask, both);
        await both;
        Assert.True(device.IsPipelineCached<WarmupB>(windowed: true));
        Assert.Equal(4, device.PipelineCreationCount);

        device.Prepare(PrepareOptions.IncludeSplit, ShaderReference.Of<WarmupA>(), ShaderReference.Of<WarmupC>());
        Assert.True(device.IsPipelineCached<WarmupC>() && device.IsPipelineCached<WarmupC>(windowed: true));
        Assert.Equal(6, device.PipelineCreationCount);

        await device.PrepareAsync(PrepareOptions.IncludeSplit, ShaderReference.Of<MulAdd>(), ShaderReference.Of<WarmupC>());
        Assert.True(device.IsPipelineCached<MulAdd>() && device.IsPipelineCached<MulAdd>(windowed: true));
        Assert.Equal(8, device.PipelineCreationCount);

        // No shaders, with or without the option, is allowed and does nothing.
        device.Prepare(PrepareOptions.IncludeSplit);
        await device.PrepareAsync(PrepareOptions.IncludeSplit);
        Assert.Equal(8, device.PipelineCreationCount);
    }

    [MetalFact]
    public void Prepare_WithIncludeSplit_SkipsShadersThatCannotBeSplit()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(64);

        // A hand-written descriptor has no grid window, so ForSplit rejects it and there is no
        // specialization to prepare: only the ordinary pipeline is compiled.
        device.Prepare<IncrementAll>(PrepareOptions.IncludeSplit);
        Assert.Equal(1, device.PipelineCreationCount);
        Assert.True(device.IsPipelineCached<IncrementAll>());
        Assert.False(device.IsPipelineCached<IncrementAll>(windowed: true));
        Assert.Throws<NotSupportedException>(() => device.ForSplit(64, new IncrementAll(buffer), cpuFraction: 0.5));
    }

    [MetalFact]
    public void Prepare_OfAShaderWithThreadgroupMemoryAndOptions_CoversItsDispatchesAndItsSplit()
    {
        const int length = 4096;
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        float[] ones = Enumerable.Repeat(1f, length).ToArray();
        using ReadOnlyBuffer<float> a = device.AllocateReadOnlyBuffer<float>(ones);
        using ReadOnlyBuffer<float> b = device.AllocateReadOnlyBuffer<float>(ones);
        using ReadOnlyBuffer<float> c = device.AllocateReadOnlyBuffer<float>(ones);
        using ReadWriteBuffer<float> sums = device.AllocateReadWriteBuffer<float>(length / BlockMulAddSum.BlockSize);

        // [GroupShared] memory and [ShaderOptions(FpContract = false)] on one shader: both pipelines
        // are compiled by Prepare, and nothing after it compiles.
        device.Prepare<BlockMulAddSum>(PrepareOptions.IncludeSplit);
        Assert.Equal(2, device.PipelineCreationCount);
        Assert.True(device.IsPipelineCached<BlockMulAddSum>() && device.IsPipelineCached<BlockMulAddSum>(windowed: true));

        device.For(length, new BlockMulAddSum(a, b, c, sums, length));
        Assert.All(sums.ToArray(), sum => Assert.Equal(2f * BlockMulAddSum.BlockSize, sum));

        sums.AsSpan().Clear();
        device.ForSplit(length, new BlockMulAddSum(a, b, c, sums, length), cpuFraction: 0.5);
        Assert.All(sums.ToArray(), sum => Assert.Equal(2f * BlockMulAddSum.BlockSize, sum));

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(length, new BlockMulAddSum(a, b, c, sums, length));
        }

        Assert.Equal(2, device.PipelineCreationCount);
    }

    [Fact]
    public void ShaderReference_IsOnePerShaderType()
    {
        ShaderReference a = ShaderReference.Of<WarmupA>();

        Assert.Same(a, ShaderReference.Of<WarmupA>());
        Assert.NotSame(a, ShaderReference.Of<WarmupB>());
        Assert.Equal(typeof(WarmupA), a.ShaderType);
        Assert.Equal(typeof(WarmupA).ToString(), a.ToString());

        // Hand-written descriptors can be referenced too.
        Assert.Equal(typeof(IncrementAll), ShaderReference.Of<IncrementAll>().ShaderType);
    }

    /// <summary>The exception is thrown by the call itself, not stored in the task it returns.</summary>
    private static void ThrowsSynchronously<T>(Func<Task> start)
        where T : Exception => Assert.Throws<T>(() => { _ = start(); });
}
