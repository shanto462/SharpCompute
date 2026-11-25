using System;
using System.Linq;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// SIMD-group shaders through the other ways to dispatch and compile a shader: indirect dispatch, whose
/// grid is whole threadgroups (on the GPU and, through the same executor selection as <c>ForCpu</c>, on
/// the CPU), and the compile options of <see cref="ShaderOptionsAttribute"/>.
/// </summary>
public class SimdGroupDispatchTests
{
    private static ShaderCompileOptions CompileOptions<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.CompileOptions;

    private static bool UsesSimdGroups<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.UsesSimdGroups;

    // ---- indirect dispatch ------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(150)] // three threadgroups of 64: the last SIMD group has 22 active lanes
    [InlineData(64)]
    [InlineData(1)]
    [InlineData(321)]
    public void ForIndirect_RunsWholeThreadgroups_AndTheOperationsSeeTheLanesThatDidNotReturn(int count)
    {
        int threads = (count + 63) / 64 * 64;
        int[] input = Enumerable.Range(0, threads).Select(static i => (i * 37 % 23) - 11).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadOnlyBuffer<int> countBuffer = device.AllocateReadOnlyBuffer<int>([count]);
        using ReadOnlyBuffer<DispatchArguments> arguments = device.AllocateReadOnlyBuffer<DispatchArguments>([default, DispatchArguments.FromCount(count, 64)]);
        using ReadWriteBuffer<int> direct = device.AllocateReadWriteBuffer<int>(threads * SimdIndirectSums.Stride);
        using ReadWriteBuffer<int> indirect = device.AllocateReadWriteBuffer<int>(threads * SimdIndirectSums.Stride);
        using ReadWriteBuffer<int> batched = device.AllocateReadWriteBuffer<int>(threads * SimdIndirectSums.Stride);
        using ReadWriteBuffer<int> onCpu = device.AllocateReadWriteBuffer<int>(threads * SimdIndirectSums.Stride);

        foreach (ReadWriteBuffer<int> buffer in new[] { direct, indirect, batched, onCpu })
        {
            buffer.AsSpan().Fill(int.MinValue);
        }

        device.For(threads, new SimdIndirectSums(inputBuffer, countBuffer, direct));
        device.ForIndirect(arguments, 1, new SimdIndirectSums(inputBuffer, countBuffer, indirect));

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.ForIndirect(arguments, 1, new SimdIndirectSums(inputBuffer, countBuffer, batched));
        }

        // The CPU counterpart goes through the same choice of executor as ForCpu: lanes on threads.
        device.ForIndirectCpu(arguments, 1, new SimdIndirectSums(inputBuffer, countBuffer, onCpu));

        int[] expected = new int[threads * SimdIndirectSums.Stride];
        Array.Fill(expected, int.MinValue);

        for (int thread = 0; thread < count; thread++)
        {
            // The lanes of this thread's SIMD group that are below the count: the others returned.
            int first = thread / 32 * 32;
            int[] lanes = Enumerable.Range(first, 32).Where(t => t < count).ToArray();

            expected[(thread * 4) + 0] = lanes.Sum(t => input[t]);
            expected[(thread * 4) + 1] = lanes.Where(t => t < thread).Sum(t => input[t]);
            expected[(thread * 4) + 2] = unchecked((int)(uint)((1UL << lanes.Length) - 1));
            expected[(thread * 4) + 3] = (threads * 1000) + (thread % 64 / 32 * 100) + (thread % 32);
        }

        Assert.Equal(expected, indirect.ToArray());
        Assert.Equal(expected, direct.ToArray());
        Assert.Equal(expected, batched.ToArray());
        Assert.Equal(expected, onCpu.ToArray());
    }

    [MetalFact]
    public void ForIndirectCpu_OfAShaderWithBarriersToo_RunsWholeThreadgroups_LikeTheGpu()
    {
        // Threadgroups of 96 (three SIMD groups each), two of them.
        const int threads = 192;
        int[] input = Enumerable.Range(0, threads).Select(static i => (i * 13 % 17) - 8).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<DispatchArguments> arguments = device.AllocateReadWriteBuffer<DispatchArguments>([new DispatchArguments(2)]);
        using ReadWriteBuffer<int> onGpu = device.AllocateReadWriteBuffer<int>(threads * SimdWithSharedMemory.Stride);
        using ReadWriteBuffer<int> onCpu = device.AllocateReadWriteBuffer<int>(threads * SimdWithSharedMemory.Stride);

        device.ForIndirect(arguments, new SimdWithSharedMemory(inputBuffer, onGpu));
        device.ForIndirectCpu(arguments, new SimdWithSharedMemory(inputBuffer, onCpu));

        Assert.Equal(onGpu.ToArray(), onCpu.ToArray());
        Assert.Equal(input[..96].Sum(), onCpu[0]);
        Assert.Equal(input[96..].Sum(), onCpu[96 * SimdWithSharedMemory.Stride]);
        Assert.Equal(96, onCpu[2]);
    }

    [MetalFact]
    public void ForIndirectCpu_RefusesDivergentControlFlow_LikeForCpu()
    {
        int[] input = Enumerable.Range(1, 64).ToArray();
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadOnlyBuffer<DispatchArguments> arguments = device.AllocateReadOnlyBuffer<DispatchArguments>([new DispatchArguments(2)]);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(64);

        // Threadgroups of 32: the indirect grid is 64 threads. On the GPU each branch sees its own lanes.
        device.ForIndirect(arguments, new SimdDivergent(inputBuffer, output));
        Assert.Equal((256, 32), (output[0], output[1]));

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForIndirectCpu(arguments, new SimdDivergent(inputBuffer, output)));
        Assert.Contains("SIMD-group operation in divergent control flow is not supported on the CPU", exception.Message);
    }

    // ---- compile options --------------------------------------------------------------------------

    [MetalFact]
    public void WithFastMath_TheOperationsWorkAsBefore_OnTheGpuAndOnTheCpu()
    {
        Assert.Equal(ShaderCompileOptions.FastMath, CompileOptions<SimdFastMath>());
        Assert.True(UsesSimdGroups<SimdFastMath>());
        Assert.Equal(ShaderCompileOptions.None, CompileOptions<SimdReduceFloat>());

        // Halves and small integers: every sum is exact, so fast math has nothing to reorder into a
        // different result. 40 threads in threadgroups of 64: a full SIMD group and one of 8 lanes.
        float[] input = Enumerable.Range(0, 40).Select(static i => (i % 9) - 4 + (i % 2 == 0 ? 0.5f : 0f)).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<float> onGpu = device.AllocateReadWriteBuffer<float>(input.Length * SimdFastMath.Stride);
        using ReadWriteBuffer<float> onCpu = device.AllocateReadWriteBuffer<float>(input.Length * SimdFastMath.Stride);

        device.For(input.Length, new SimdFastMath(inputBuffer, onGpu));
        device.ForCpu(input.Length, new SimdFastMath(inputBuffer, onCpu));

        float[] expected = new float[input.Length * SimdFastMath.Stride];

        for (int thread = 0; thread < input.Length; thread++)
        {
            int first = thread / 32 * 32;
            float[] lanes = input[first..Math.Min(input.Length, first + 32)];

            // sum, max of v / 2 + 1, exclusive prefix sum, count of positive values, the neighbour's value
            expected[(thread * 5) + 0] = lanes.Sum();
            expected[(thread * 5) + 1] = lanes.Max(static v => (v * 0.5f) + 1f);
            expected[(thread * 5) + 2] = input[first..thread].Sum();
            expected[(thread * 5) + 3] = lanes.Count(static v => v > 0f);
            expected[(thread * 5) + 4] = input[thread ^ 1];
        }

        Assert.Equal(expected, onGpu.ToArray());
        Assert.Equal(expected, onCpu.ToArray());

        // The fast-math pipeline is a pipeline like any other: the SIMD width check applies, and a
        // second dispatch reuses it.
        Assert.True(device.IsPipelineCached<SimdFastMath>());
        device.For(input.Length, new SimdFastMath(inputBuffer, onGpu));
        Assert.Equal(expected, onGpu.ToArray());
    }

    [MetalFact]
    public void WithFastMath_SplitAndIndirectDispatches_GiveTheSameResult()
    {
        const int threads = 256;
        float[] input = Enumerable.Range(0, threads).Select(static i => (i % 7) - 3f).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadOnlyBuffer<DispatchArguments> arguments = device.AllocateReadOnlyBuffer<DispatchArguments>([new DispatchArguments(4)]);
        using ReadWriteBuffer<float> whole = device.AllocateReadWriteBuffer<float>(threads * SimdFastMath.Stride);
        using ReadWriteBuffer<float> split = device.AllocateReadWriteBuffer<float>(threads * SimdFastMath.Stride);
        using ReadWriteBuffer<float> indirect = device.AllocateReadWriteBuffer<float>(threads * SimdFastMath.Stride);
        using ReadWriteBuffer<float> indirectCpu = device.AllocateReadWriteBuffer<float>(threads * SimdFastMath.Stride);

        device.For(threads, new SimdFastMath(inputBuffer, whole));
        device.ForSplit(threads, new SimdFastMath(inputBuffer, split), cpuFraction: 0.5);
        device.ForIndirect(arguments, new SimdFastMath(inputBuffer, indirect));
        device.ForIndirectCpu(arguments, new SimdFastMath(inputBuffer, indirectCpu));

        Assert.Equal(whole.ToArray(), split.ToArray());
        Assert.Equal(whole.ToArray(), indirect.ToArray());
        Assert.Equal(whole.ToArray(), indirectCpu.ToArray());
    }
}
