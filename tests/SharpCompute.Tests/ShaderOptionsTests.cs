using System;
using System.Linq;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// <c>[ShaderOptions]</c>: the options reach the descriptor, the pipeline cache and the Metal compiler.
/// Each probe is a pair of shaders with the same body, so only the option differs.
/// </summary>
public class ShaderOptionsTests
{
    private const int Length = 8192;

    [Fact]
    public void Descriptor_ReportsTheAttribute()
    {
        Assert.Equal(ShaderCompileOptions.None, Options<MulAdd>());
        Assert.Equal(ShaderCompileOptions.None, Options<DefaultOptionsSpelledOut>());
        Assert.Equal(ShaderCompileOptions.NoFpContract, Options<MulAddNoContract>());
        Assert.Equal(ShaderCompileOptions.FastMath, Options<NaNProbeFast>());
        Assert.Equal(ShaderCompileOptions.FastMath | ShaderCompileOptions.NoFpContract, Options<MulAddFastNoContract>());

        // Hand-written descriptors get the interface's default.
        Assert.Equal(ShaderCompileOptions.None, Options<IncrementAll>());
    }

    [Fact]
    public void Attribute_DefaultsToMetalsBehaviour()
    {
        ShaderOptionsAttribute attribute = new();

        Assert.False(attribute.FastMath);
        Assert.True(attribute.FpContract);
    }

    [Fact]
    public void MslSource_IsTheSameWithAndWithoutOptions()
    {
        // The options are applied by the runtime at compile time; the generated MSL does not carry them.
        Assert.DoesNotContain("pragma", Source<MulAddNoContract>());
        Assert.Equal(
            Source<MulAdd>().Replace(nameof(MulAdd), "X"),
            Source<MulAddNoContract>().Replace(nameof(MulAddNoContract), "X"));
    }

    [MetalFact]
    public void FpContractOff_RestoresBitParityWithTheCpu_ForMultiplyAdd()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        (float[] a, float[] b, float[] c) = Operands();
        using ReadOnlyBuffer<float> bufferA = device.AllocateReadOnlyBuffer<float>(a);
        using ReadOnlyBuffer<float> bufferB = device.AllocateReadOnlyBuffer<float>(b);
        using ReadOnlyBuffer<float> bufferC = device.AllocateReadOnlyBuffer<float>(c);
        using ReadWriteBuffer<float> contracted = device.AllocateReadWriteBuffer<float>(Length);
        using ReadWriteBuffer<float> exact = device.AllocateReadWriteBuffer<float>(Length);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(Length);

        device.For(Length, new MulAdd(bufferA, bufferB, bufferC, contracted));
        device.For(Length, new MulAddNoContract(bufferA, bufferB, bufferC, exact));
        device.ForCpu(Length, new MulAddNoContract(bufferA, bufferB, bufferC, cpu));

        int[] expected = new int[Length];

        for (int i = 0; i < Length; i++)
        {
            // Two rounded operations: the product is stored to a float before the add.
            float product = a[i] * b[i];
            expected[i] = BitConverter.SingleToInt32Bits(product + c[i]);
        }

        // With contraction off the GPU matches C# bit for bit, on every element.
        Assert.Equal(expected, Bits(exact));
        Assert.Equal(expected, Bits(cpu));

        // With Metal's default the compiler fuses the multiply and the add, which rounds once instead of
        // twice. That is what makes the option observable: the same kernel without it differs from C#
        // on a large share of these operands (about a quarter on an M4 Pro).
        int different = expected.Zip(Bits(contracted)).Count(pair => pair.First != pair.Second);
        Assert.True(different > Length / 20, $"Only {different} of {Length} results of the default kernel differ from the unfused C# result.");
    }

    [MetalFact]
    public void FastMath_FoldsANaNComparisonThatSafeMathKeeps()
    {
        float[] input = [1f, float.NaN, float.PositiveInfinity, 0f, -float.NaN, -2.5f, float.Epsilon, float.NegativeInfinity];
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> buffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<int> safe = device.AllocateReadWriteBuffer<int>(input.Length);
        using ReadWriteBuffer<int> fast = device.AllocateReadWriteBuffer<int>(Enumerable.Repeat(-1, input.Length).ToArray());
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(input.Length);

        device.For(input.Length, new NaNProbe(buffer, safe));
        device.For(input.Length, new NaNProbeFast(buffer, fast));
        device.ForCpu(input.Length, new NaNProbeFast(buffer, cpu));

        // Safe math: both tests hold for the NaNs and for nothing else.
        Assert.Equal([0, 3, 0, 0, 3, 0, 0, 0], safe.ToArray());

        // Fast math: the compiler assumes there are no NaNs and folds `x != x` to false, for the NaNs
        // too. (What isnan() does under fast math is Metal's business: on an M4 Pro it still reports
        // them, so only the comparison is asserted.)
        Assert.All(fast.ToArray(), flags => Assert.Equal(0, flags & 2));

        // The options are GPU compile options: the CPU runs the same C# either way.
        Assert.Equal(safe.ToArray(), cpu.ToArray());
    }

    [MetalFact]
    public void FastMathWithFpContractOff_StillDoesNotContract()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        (float[] a, float[] b, float[] c) = Operands();
        using ReadOnlyBuffer<float> bufferA = device.AllocateReadOnlyBuffer<float>(a);
        using ReadOnlyBuffer<float> bufferB = device.AllocateReadOnlyBuffer<float>(b);
        using ReadOnlyBuffer<float> bufferC = device.AllocateReadOnlyBuffer<float>(c);
        using ReadWriteBuffer<float> result = device.AllocateReadWriteBuffer<float>(Length);

        device.For(Length, new MulAddFastNoContract(bufferA, bufferB, bufferC, result));

        int[] expected = new int[Length];

        for (int i = 0; i < Length; i++)
        {
            float product = a[i] * b[i];
            expected[i] = BitConverter.SingleToInt32Bits(product + c[i]);
        }

        // The pragma wins over the contraction fast math would otherwise allow.
        Assert.Equal(expected, Bits(result));
    }

    [MetalFact]
    public void Options_ApplyInContextsAsyncAndSplitDispatchesToo()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        (float[] a, float[] b, float[] c) = Operands();
        using ReadOnlyBuffer<float> bufferA = device.AllocateReadOnlyBuffer<float>(a);
        using ReadOnlyBuffer<float> bufferB = device.AllocateReadOnlyBuffer<float>(b);
        using ReadOnlyBuffer<float> bufferC = device.AllocateReadOnlyBuffer<float>(c);
        using ReadWriteBuffer<float> reference = device.AllocateReadWriteBuffer<float>(Length);
        using ReadWriteBuffer<float> batched = device.AllocateReadWriteBuffer<float>(Length);
        using ReadWriteBuffer<float> awaited = device.AllocateReadWriteBuffer<float>(Length);
        using ReadWriteBuffer<float> split = device.AllocateReadWriteBuffer<float>(Length);

        device.ForCpu(Length, new MulAddNoContract(bufferA, bufferB, bufferC, reference));

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(Length, new MulAddNoContract(bufferA, bufferB, bufferC, batched));
        }

        device.ForAsync(Length, new MulAddNoContract(bufferA, bufferB, bufferC, awaited)).Wait();

        // The windowed specialization that ForSplit compiles gets the same options, so the GPU's part
        // and the CPU's part agree bit for bit: a split of this shader is reproducible.
        device.ForSplit(Length, new MulAddNoContract(bufferA, bufferB, bufferC, split), cpuFraction: 0.5);

        Assert.Equal(Bits(reference), Bits(batched));
        Assert.Equal(Bits(reference), Bits(awaited));
        Assert.Equal(Bits(reference), Bits(split));
    }

    [MetalFact]
    public void Options_ApplyToAShaderThatUsesThreadgroupMemory()
    {
        // Both descriptor members come from the generator for the same shader: its options and its
        // use of group-shared memory.
        Assert.Equal(ShaderCompileOptions.NoFpContract, Options<BlockMulAddSum>());
        Assert.True(UsesThreadgroupMemory<BlockMulAddSum>());

        const int blocks = Length / BlockMulAddSum.BlockSize;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        (float[] a, float[] b, float[] c) = Operands();
        using ReadOnlyBuffer<float> bufferA = device.AllocateReadOnlyBuffer<float>(a);
        using ReadOnlyBuffer<float> bufferB = device.AllocateReadOnlyBuffer<float>(b);
        using ReadOnlyBuffer<float> bufferC = device.AllocateReadOnlyBuffer<float>(c);
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(blocks);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(blocks);
        using ReadWriteBuffer<float> split = device.AllocateReadWriteBuffer<float>(blocks);

        device.For(Length, new BlockMulAddSum(bufferA, bufferB, bufferC, gpu, Length));
        device.ForCpu(Length, new BlockMulAddSum(bufferA, bufferB, bufferC, cpu, Length));
        device.ForSplit(Length, new BlockMulAddSum(bufferA, bufferB, bufferC, split, Length), cpuFraction: 0.5);

        // Plain C#: an unfused multiply-add per element, then the same tree of sums.
        int[] expected = new int[blocks];
        float[] tile = new float[BlockMulAddSum.BlockSize];
        int fusedLoadsThatDiffer = 0;

        for (int block = 0; block < blocks; block++)
        {
            for (int j = 0; j < tile.Length; j++)
            {
                int i = (block * tile.Length) + j;
                float product = a[i] * b[i];
                tile[j] = product + c[i];
                fusedLoadsThatDiffer += tile[j] != MathF.FusedMultiplyAdd(a[i], b[i], c[i]) ? 1 : 0;
            }

            for (int half = tile.Length / 2; half > 0; half >>= 1)
            {
                for (int j = 0; j < half; j++)
                {
                    tile[j] += tile[j + half];
                }
            }

            expected[block] = BitConverter.SingleToInt32Bits(tile[0]);
        }

        // With contraction off every block sum has C#'s bits, on the GPU, on the CPU's group executor
        // and split between the two. (A fused load would differ in many of the 8192 elements, so the
        // parity is the option at work, not luck.)
        Assert.True(fusedLoadsThatDiffer > Length / 20);
        Assert.Equal(expected, Bits(gpu));
        Assert.Equal(expected, Bits(cpu));
        Assert.Equal(expected, Bits(split));
    }

    [MetalFact]
    public void CompileError_ReportsTheSourceAsItWasCompiled()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        ShaderCompilationException plain = Assert.Throws<ShaderCompilationException>(
            () => device.CompilePipeline("kernel void k() { nonsense }", "k", typeof(ShaderOptionsTests)));
        ShaderCompilationException pragma = Assert.Throws<ShaderCompilationException>(
            () => device.CompilePipeline("kernel void k() { nonsense }", "k", typeof(ShaderOptionsTests), options: ShaderCompileOptions.NoFpContract));

        Assert.Equal("kernel void k() { nonsense }", plain.MslSource);
        Assert.Equal(GraphicsDevice.FpContractOffPragma + "kernel void k() { nonsense }", pragma.MslSource);

        // The error's line number is that of the reported source: line 2 once the pragma is ahead of it.
        Assert.Contains(":1:", plain.Message);
        Assert.Contains(":2:", pragma.Message);
    }

    private static ShaderCompileOptions Options<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.CompileOptions;

    private static string Source<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.MslSource;

    private static bool UsesThreadgroupMemory<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.UsesThreadgroupMemory;

    private static int[] Bits(ReadWriteBuffer<float> buffer) => buffer.ToArray().Select(BitConverter.SingleToInt32Bits).ToArray();

    /// <summary>Operands with full mantissas, so that most products need rounding and a fused add shows.</summary>
    private static (float[] A, float[] B, float[] C) Operands()
    {
        Random random = new(20261125);
        float[] a = new float[Length], b = new float[Length], c = new float[Length];

        for (int i = 0; i < Length; i++)
        {
            a[i] = (random.NextSingle() * 8f) - 4f;
            b[i] = (random.NextSingle() * 8f) - 4f;
            c[i] = (random.NextSingle() * 8f) - 4f;
        }

        return (a, b, c);
    }
}
