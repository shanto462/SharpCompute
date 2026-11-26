using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Samples.Shaders;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// Pipelines from a precompiled library: when the runtime takes a kernel from the <c>.metallib</c>
/// the build made of an assembly's shaders, when it compiles the MSL instead, and that the two give
/// the same bits. Every test uses a device of its own, so "first use" is first use.
/// </summary>
public class PrecompiledShaderTests
{
    private const int Length = 4096;

    [PrecompiledFact]
    public void GeneratedShader_ComesFromThePrecompiledLibrary_WithoutCompilingAnything()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);

        Assert.Equal(PipelineOrigin.None, device.GetPipelineOrigin<PrecompiledProbeA>());

        device.For(Length, new PrecompiledProbeA(buffer));

        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<PrecompiledProbeA>());
        Assert.Equal(1, device.PrecompiledPipelineCount);
        Assert.Equal(0, device.PipelineCompileCount);
        Assert.Equal(Enumerable.Range(101, Length), buffer.ToArray());

        // The pipeline is cached like any other: nothing is created again.
        device.For(Length, new PrecompiledProbeA(buffer));
        device.Prepare<PrecompiledProbeA>();
        Assert.Equal(1, device.PipelineCreationCount);

        // Another shader of the assembly comes out of the same library.
        device.Prepare<PrecompiledProbeFast>();
        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<PrecompiledProbeFast>());
        Assert.Equal(PipelineOrigin.None, device.GetPipelineOrigin<PrecompiledProbeB>());
        Assert.Equal(0, device.PipelineCompileCount);
    }

    [PrecompiledFact]
    public void TheWindowedSpecialization_ComesFromTheSameKernelOfTheLibrary()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        // Function constants are specialised when the pipeline is created, so the one precompiled
        // kernel serves the ordinary pipeline and the one a split dispatch runs on the GPU.
        device.Prepare<PrecompiledProbeA>(PrepareOptions.IncludeSplit);

        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<PrecompiledProbeA>(windowed: false));
        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<PrecompiledProbeA>(windowed: true));
        Assert.Equal(2, device.PrecompiledPipelineCount);
        Assert.Equal(0, device.PipelineCompileCount);

        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
        SplitDispatchInfo info = device.ForSplit(Length, new PrecompiledProbeA(buffer), cpuFraction: 0.5);

        Assert.True(info.CpuExtent > 0 && info.GpuExtent > 0);
        Assert.Equal(Enumerable.Range(101, Length), buffer.ToArray());
        Assert.Equal(2, device.PipelineCreationCount);
    }

    [PrecompiledFact]
    public void ShaderOfAReferencedAssembly_ComesFromThatAssemblysLibrary()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(64, 64);

        // SharpCompute.Samples.Shaders.metallib is copied here with the assembly it belongs to.
        device.ForEach(texture, new HelloWorld(1.5f));

        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<HelloWorld>());
        Assert.Equal(0, device.PipelineCompileCount);
    }

    [MetalFact]
    public void HandWrittenDescriptor_IsAlwaysCompiledFromSource()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(Enumerable.Repeat(3f, 64).ToArray());

        device.For(64, new MultiplyByTwo(buffer, 2f));

        Assert.Equal(PipelineOrigin.CompiledAtRunTime, device.GetPipelineOrigin<MultiplyByTwo>());
        Assert.Equal(0, device.PrecompiledPipelineCount);
        Assert.Equal(1, device.PipelineCompileCount);
        Assert.All(buffer.ToArray(), value => Assert.Equal(6f, value));
    }

    [MetalFact]
    public void UsePrecompiledShaders_False_CompilesEverythingFromSource()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.UsePrecompiledShaders = false;
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);

        device.For(Length, new PrecompiledProbeA(buffer));

        Assert.Equal(PipelineOrigin.CompiledAtRunTime, device.GetPipelineOrigin<PrecompiledProbeA>());
        Assert.Equal(0, device.PrecompiledPipelineCount);
        Assert.Equal(Enumerable.Range(101, Length), buffer.ToArray());
    }

    [MetalFact]
    public void TheAppContextSwitch_TurnsPrecompiledShadersOff_ForDevicesCreatedAfterIt()
    {
        // The shared default device is created first, so that it never sees the switch.
        _ = GraphicsDevice.GetDefault();

        using GraphicsDevice before = GraphicsDevice.CreateSystemDefault();
        Assert.True(before.UsePrecompiledShaders);

        AppContext.SetSwitch(GraphicsDevice.PrecompiledShadersSwitch, false);

        try
        {
            using GraphicsDevice off = GraphicsDevice.CreateSystemDefault();
            using ReadWriteBuffer<int> buffer = off.AllocateReadWriteBuffer<int>(Length);

            Assert.False(off.UsePrecompiledShaders);
            off.For(Length, new PrecompiledProbeA(buffer));
            Assert.Equal(PipelineOrigin.CompiledAtRunTime, off.GetPipelineOrigin<PrecompiledProbeA>());

            // A device that already exists keeps what it was created with.
            Assert.True(before.UsePrecompiledShaders);
        }
        finally
        {
            AppContext.SetSwitch(GraphicsDevice.PrecompiledShadersSwitch, true);
        }

        using GraphicsDevice after = GraphicsDevice.CreateSystemDefault();
        Assert.True(after.UsePrecompiledShaders);
    }

    [MetalFact]
    public void MissingLibrary_FallsBackToTheSource()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.PrecompiledLibraryLocator = static _ => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".metallib");

        AssertCompiledFromSource(device);

        // No library at all, which is what the default locator reports for an assembly without one.
        using GraphicsDevice none = GraphicsDevice.CreateSystemDefault();
        none.PrecompiledLibraryLocator = static _ => null;

        AssertCompiledFromSource(none);
    }

    [MetalFact]
    public void FileThatIsNotALibrary_FallsBackToTheSource()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "garbage.metallib");
        File.WriteAllBytes(path, Enumerable.Range(0, 4096).Select(static i => (byte)(i * 31)).ToArray());

        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.PrecompiledLibraryLocator = _ => path;

        AssertCompiledFromSource(device);

        // An empty file, too.
        File.WriteAllBytes(path, []);
        using GraphicsDevice empty = GraphicsDevice.CreateSystemDefault();
        empty.PrecompiledLibraryLocator = _ => path;

        AssertCompiledFromSource(empty);
    }

    [PrecompiledFact]
    public void LibraryWithoutTheEntryPoint_FallsBackToTheSource()
    {
        // A real library, of another assembly: none of this assembly's kernels is in it.
        string other = Path.Combine(Path.GetDirectoryName(PrecompiledFactAttribute.LibraryPath)!, "SharpCompute.Samples.Shaders.metallib");
        Assert.True(File.Exists(other), $"'{other}' was not copied next to the tests.");

        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.PrecompiledLibraryLocator = _ => other;

        AssertCompiledFromSource(device);
    }

    [MetalToolchainFact]
    public void LibraryBuiltFromTheCurrentSource_IsUsed()
    {
        // The control for the three tests below: a library built here, the way they build theirs, is
        // taken when it matches. Without it they would pass for a library the runtime can never use.
        using TemporaryDirectory directory = new();
        string path = MetalToolchain.BuildLibrary(directory.Path, MetalToolchain.Kernel.Of<PrecompiledProbeA>(), MetalToolchain.Kernel.Of<PrecompiledProbeFast>());

        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.PrecompiledLibraryLocator = _ => path;
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);

        device.For(Length, new PrecompiledProbeA(buffer));
        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<PrecompiledProbeA>());
        Assert.Equal(Enumerable.Range(101, Length), buffer.ToArray());

        device.For(Length, new PrecompiledProbeFast(buffer));
        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<PrecompiledProbeFast>());
        Assert.Equal(Enumerable.Range(404, Length), buffer.ToArray());

        // A shader the library does not hold is compiled, next to the ones it does hold.
        device.For(Length, new PrecompiledProbeB(buffer));
        Assert.Equal(PipelineOrigin.CompiledAtRunTime, device.GetPipelineOrigin<PrecompiledProbeB>());
        Assert.Equal(Enumerable.Range(202, Length), buffer.ToArray());
    }

    [MetalToolchainFact]
    public void LibraryBuiltFromAnotherVersionOfTheSource_IsNotUsed()
    {
        // What an outdated library is: the kernel is there, under the entry point, but it was compiled
        // from MSL that is no longer the shader's. This one would even write other values.
        MetalToolchain.Kernel current = MetalToolchain.Kernel.Of<PrecompiledProbeA>();
        string oldSource = current.Source.Replace("101", "999", StringComparison.Ordinal);
        Assert.NotEqual(current.Source, oldSource);

        MetalToolchain.Kernel outdated = current with
        {
            Source = oldSource,
            FunctionName = PrecompiledShaderContract.FunctionName(current.EntryPoint, oldSource, (int)current.Options),
        };

        using TemporaryDirectory directory = new();
        string path = MetalToolchain.BuildLibrary(directory.Path, outdated);

        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.PrecompiledLibraryLocator = _ => path;

        // Compiled from the current source, and so the current values.
        AssertCompiledFromSource(device);
    }

    [MetalToolchainFact]
    public void LibraryBuiltWithOtherOptions_IsNotUsed()
    {
        // The same MSL compiled with fast math is another kernel: the options are part of its name.
        MetalToolchain.Kernel current = MetalToolchain.Kernel.Of<PrecompiledProbeA>();
        MetalToolchain.Kernel fast = current with
        {
            Options = ShaderCompileOptions.FastMath,
            FunctionName = PrecompiledShaderContract.FunctionName(current.EntryPoint, current.Source, (int)ShaderCompileOptions.FastMath),
        };

        Assert.NotEqual(current.FunctionName, fast.FunctionName);

        using TemporaryDirectory directory = new();
        string path = MetalToolchain.BuildLibrary(directory.Path, fast);

        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.PrecompiledLibraryLocator = _ => path;

        AssertCompiledFromSource(device);
    }

    [MetalToolchainFact]
    public void KernelStoredUnderItsBareEntryPoint_IsNotUsed()
    {
        // A library someone made with the Metal tools directly holds the kernel under its own name,
        // with nothing that says what it was compiled from, so it is not trusted.
        MetalToolchain.Kernel current = MetalToolchain.Kernel.Of<PrecompiledProbeA>();

        using TemporaryDirectory directory = new();
        string path = MetalToolchain.BuildLibrary(directory.Path, current with { FunctionName = current.EntryPoint });

        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.PrecompiledLibraryLocator = _ => path;

        AssertCompiledFromSource(device);
    }

    [PrecompiledFact]
    public void MathKernels_GiveTheSameBits_PrecompiledAndCompiledFromSource()
    {
        float[] input = MathInputs();

        AssertSameBits<MathSweep>((device, source, output) => device.For(Length, new MathSweep(source, output)), input);
        AssertSameBits<MathSweepFast>((device, source, output) => device.For(Length, new MathSweepFast(source, output)), input);
        AssertSameBits<MathSweepNoContract>((device, source, output) => device.For(Length, new MathSweepNoContract(source, output)), input);
        AssertSameBits<MathSweepFastNoContract>((device, source, output) => device.For(Length, new MathSweepFastNoContract(source, output)), input);
    }

    [PrecompiledFact]
    public void TheOptionsOfAPrecompiledKernel_AreTheShadersOwn()
    {
        // The four shaders have one body, so any difference between two of them is the options at
        // work in the precompiled kernel: had they all been compiled alike, these would be equal.
        float[] input = MathInputs();
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<float> output = device.AllocateReadWriteBuffer<float>(Length * MathSweep.Outputs);

        device.For(Length, new MathSweep(source, output));
        int[] safe = Bits(output);
        device.For(Length, new MathSweepFast(source, output));
        int[] fast = Bits(output);
        device.For(Length, new MathSweepNoContract(source, output));
        int[] noContract = Bits(output);
        device.For(Length, new MathSweepFastNoContract(source, output));
        int[] fastNoContract = Bits(output);

        Assert.Equal(4, device.PrecompiledPipelineCount);
        Assert.Equal(0, device.PipelineCompileCount);
        Assert.NotEqual(safe, fast);
        Assert.NotEqual(safe, noContract);
        Assert.NotEqual(fast, fastNoContract);

        // With contraction off, a * b + c is a rounded multiply and a rounded add, as on the CPU
        // (NaN payloads and subnormals aside, which the GPU does not keep).
        int compared = 0;

        for (int i = 0; i < Length; i++)
        {
            float expected = (input[i * MathSweep.Inputs] * input[(i * MathSweep.Inputs) + 1]) + input[(i * MathSweep.Inputs) + 2];

            if (float.IsNormal(expected))
            {
                Assert.Equal(BitConverter.SingleToInt32Bits(expected), noContract[i * MathSweep.Outputs]);
                compared++;
            }
        }

        Assert.True(compared > Length * 9 / 10);
    }

    [PrecompiledFact]
    public void ThreadgroupMemoryKernel_GivesTheSameBits_PrecompiledAndCompiledFromSource()
    {
        float[] input = MathInputs();
        float[] a = input[..Length], b = input[Length..(2 * Length)], c = input[(2 * Length)..(3 * Length)];

        int[] Run(GraphicsDevice device)
        {
            using ReadOnlyBuffer<float> bufferA = device.AllocateReadOnlyBuffer<float>(a);
            using ReadOnlyBuffer<float> bufferB = device.AllocateReadOnlyBuffer<float>(b);
            using ReadOnlyBuffer<float> bufferC = device.AllocateReadOnlyBuffer<float>(c);
            using ReadWriteBuffer<float> sums = device.AllocateReadWriteBuffer<float>(Length / BlockMulAddSum.BlockSize);

            device.For(Length, new BlockMulAddSum(bufferA, bufferB, bufferC, sums, Length));
            return Bits(sums);
        }

        AssertSameBits<BlockMulAddSum>(Run, windowed: false);
    }

    [PrecompiledFact]
    public void SmallAndWideIntegerKernel_GivesTheSameBits_PrecompiledAndCompiledFromSource()
    {
        const byte Tag = 0xA5;
        const long Key = unchecked((long)0x8877665544332211);
        const short Delta = -12345;
        const ulong Hash = 0xFEDCBA9876543210;
        float[] input = MathInputs();

        // What the build tool had to get through the offline compiler for this kernel: the 64-bit
        // and 8- and 16-bit types, a constants struct with explicit offsets, and the prelude's helper
        // for the float to long conversion.
        string msl = Source<IntegerSweep>();
        Assert.Contains("__ms_sat_long(", msl);
        Assert.Contains("static_assert(__builtin_offsetof(__Constants, ", msl);
        Assert.Matches(@"\bulong\b", msl);
        Assert.Matches(@"\buchar\b", msl);

        long[] RunLongs(GraphicsDevice device, bool onCpu = false)
        {
            using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer<float>(input);
            using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(Length * IntegerSweep.Outputs);
            IntegerSweep shader = new(Tag, Key, Delta, Hash, source, output);

            if (onCpu)
            {
                device.ForCpu(Length, shader);
            }
            else
            {
                device.For(Length, shader);
            }

            return output.ToArray();
        }

        AssertSameBits<IntegerSweep>(device => MemoryMarshal.Cast<long, int>(RunLongs(device)).ToArray(), windowed: false);

        // And the precompiled kernel computes what the C# does, constants and conversions included.
        using GraphicsDevice precompiled = GraphicsDevice.CreateSystemDefault();
        long[] gpu = RunLongs(precompiled);
        long[] cpu = RunLongs(precompiled, onCpu: true);

        Assert.Equal(PipelineOrigin.Precompiled, precompiled.GetPipelineOrigin<IntegerSweep>());
        Assert.Equal(cpu, gpu);
        Assert.Contains(long.MaxValue, gpu);
        Assert.Contains(long.MinValue, gpu);
    }

    [PrecompiledFact]
    public void PixelShader_GivesTheSamePixels_PrecompiledAndCompiledFromSource()
    {
        int[] Run(GraphicsDevice device)
        {
            using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(160, 90);
            device.ForEach(texture, new HelloWorld(2.25f));
            return MemoryMarshal.Cast<Rgba32, int>(texture.ToArray()).ToArray();
        }

        AssertSameBits<HelloWorld>(Run, windowed: false);
    }

    [PrecompiledFact]
    public void SplitDispatch_GivesTheSameBits_PrecompiledAndCompiledFromSource()
    {
        const int width = 192, height = 128;

        int[] Run(GraphicsDevice device)
        {
            using ReadWriteBuffer<float> field = device.AllocateReadWriteBuffer<float>(width * height);
            SplitDispatchInfo info = device.ForSplit(width, height, new WindowedField(field, 0.75f), cpuFraction: 0.25);

            // The GPU ran the windowed specialization over its part of the grid, and that part is
            // the same on both devices because the fraction is given.
            Assert.True(info.CpuExtent > 0 && info.GpuExtent > 0);
            return Bits(field);
        }

        AssertSameBits<WindowedField>(Run, windowed: true);

        // And the whole grid on the GPU, through the ordinary pipeline of the same kernel.
        AssertSameBits<WindowedField>(
            device =>
            {
                using ReadWriteBuffer<float> field = device.AllocateReadWriteBuffer<float>(width * height);
                device.For(width, height, new WindowedField(field, 0.75f));
                return Bits(field);
            },
            windowed: false);
    }

    [PrecompiledFact]
    public async Task ConcurrentFirstUses_OpenTheLibraryOnce_AndBuildEachPipelineOnce()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        int locatorCalls = 0;
        Func<System.Reflection.Assembly, string?> locate = device.PrecompiledLibraryLocator;

        device.PrecompiledLibraryLocator = assembly =>
        {
            Interlocked.Increment(ref locatorCalls);
            return locate(assembly);
        };

        using Barrier start = new(12);

        Task[] tasks = Enumerable.Range(0, 12).Select(i => Task.Factory.StartNew(
            () =>
            {
                using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);
                start.SignalAndWait();

                switch (i % 3)
                {
                    case 0:
                        device.For(Length, new PrecompiledProbeA(buffer));
                        Assert.Equal(Enumerable.Range(101, Length), buffer.ToArray());
                        break;
                    case 1:
                        device.For(Length, new PrecompiledProbeB(buffer));
                        Assert.Equal(Enumerable.Range(202, Length), buffer.ToArray());
                        break;
                    default:
                        device.For(Length, new PrecompiledProbeC(buffer));
                        Assert.Equal(Enumerable.Range(303, Length), buffer.ToArray());
                        break;
                }
            },
            TaskCreationOptions.LongRunning)).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, locatorCalls);
        Assert.Equal(3, device.PrecompiledPipelineCount);
        Assert.Equal(0, device.PipelineCompileCount);
        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<PrecompiledProbeA>());
        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<PrecompiledProbeB>());
        Assert.Equal(PipelineOrigin.Precompiled, device.GetPipelineOrigin<PrecompiledProbeC>());
    }

    [PrecompiledFact]
    public void DisposingTheDevice_ReleasesTheLibrary_AndANewDeviceOpensItAgain()
    {
        GraphicsDevice first = GraphicsDevice.CreateSystemDefault();
        first.Prepare<PrecompiledProbeA>();
        Assert.Equal(PipelineOrigin.Precompiled, first.GetPipelineOrigin<PrecompiledProbeA>());

        first.Dispose();
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.Prepare<PrecompiledProbeB>());

        using GraphicsDevice second = GraphicsDevice.CreateSystemDefault();
        second.Prepare<PrecompiledProbeA>();
        Assert.Equal(PipelineOrigin.Precompiled, second.GetPipelineOrigin<PrecompiledProbeA>());
    }

    [PrecompiledFact]
    public void DefaultLocator_FindsTheLibraryNextToTheAssembly_AndNothingForAnAssemblyWithoutOne()
    {
        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();

        Assert.Equal(PrecompiledFactAttribute.LibraryPath, device.PrecompiledLibraryLocator(typeof(PrecompiledShaderTests).Assembly));
        Assert.EndsWith("SharpCompute.Samples.Shaders.metallib", device.PrecompiledLibraryLocator(typeof(HelloWorld).Assembly));

        // SharpCompute itself has no shaders of the generator's, so the build makes it no library.
        Assert.Null(device.PrecompiledLibraryLocator(typeof(GraphicsDevice).Assembly));
    }

    [Fact]
    public void Locator_CannotBeSetToNull()
    {
        if (!GraphicsDevice.IsSupported)
        {
            return;
        }

        using GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        Assert.Throws<ArgumentNullException>(() => device.PrecompiledLibraryLocator = null!);
    }

    [Fact]
    public void FunctionName_IsTheEntryPointAndAHashOfEverythingThatDecidesTheCode()
    {
        const string source = "kernel void Entry() { }\n";
        string name = PrecompiledShaderContract.FunctionName("Entry", source, 0);

        // Pinned: the build tool and the runtime compute this separately, so a change to the hash
        // has to be a decision (and a change of the scheme string), not an accident.
        Assert.Equal("Entry__sc", name[..9]);
        Assert.Equal(9 + 32, name.Length);
        Assert.All(name[9..], static c => Assert.True(c is (>= '0' and <= '9') or (>= 'a' and <= 'f')));
        Assert.Equal(name, PrecompiledShaderContract.FunctionName("Entry", new string(source.AsSpan()), 0));

        string[] others =
        [
            PrecompiledShaderContract.FunctionName("Entry", source + " ", 0),
            PrecompiledShaderContract.FunctionName("Entry", source.Replace("{ }", "{}", StringComparison.Ordinal), 0),
            PrecompiledShaderContract.FunctionName("Entry", source, (int)ShaderCompileOptions.FastMath),
            PrecompiledShaderContract.FunctionName("Entry", source, (int)ShaderCompileOptions.NoFpContract),
            PrecompiledShaderContract.FunctionName("Entry", source, (int)(ShaderCompileOptions.FastMath | ShaderCompileOptions.NoFpContract)),
            PrecompiledShaderContract.FunctionName("Entry", string.Empty, 0),
        ];

        Assert.Equal(others.Length + 1, others.Append(name).Select(static other => other[9..]).Distinct().Count());

        // The entry point is hashed too, and its length with it: moving a character between the
        // entry point and the source is another shader.
        Assert.NotEqual(
            PrecompiledShaderContract.FunctionName("Entry", "x" + source, 0)[^32..],
            PrecompiledShaderContract.FunctionName("Entryx", source, 0)[^32..]);
    }

    [Fact]
    public void CompiledSource_PutsThePragmaAheadOfTheSource_OnlyForNoFpContract()
    {
        Assert.Equal("kernel", PrecompiledShaderContract.CompiledSource("kernel", 0));
        Assert.Equal("kernel", PrecompiledShaderContract.CompiledSource("kernel", (int)ShaderCompileOptions.FastMath));
        Assert.Equal("#pragma clang fp contract(off)\nkernel", PrecompiledShaderContract.CompiledSource("kernel", (int)ShaderCompileOptions.NoFpContract));
        Assert.Equal(GraphicsDevice.FpContractOffPragma, PrecompiledShaderContract.FpContractOffPragma);

        // The contract's copies of the option bits are the enum's.
        Assert.Equal((int)ShaderCompileOptions.FastMath, PrecompiledShaderContract.FastMath);
        Assert.Equal((int)ShaderCompileOptions.NoFpContract, PrecompiledShaderContract.NoFpContract);
    }

    [Fact]
    public void GeneratedShaders_CarryTheirRecord()
    {
        GeneratedShaderRecordAttribute record = Assert.Single(
            typeof(MathSweepFastNoContract).GetCustomAttributes(typeof(GeneratedShaderRecordAttribute), inherit: false).Cast<GeneratedShaderRecordAttribute>());

        Assert.Equal(EntryPoint<MathSweepFastNoContract>(), record.EntryPoint);
        Assert.Equal(ShaderCompileOptions.FastMath | ShaderCompileOptions.NoFpContract, record.CompileOptions);

        // A hand-written descriptor has none.
        Assert.Empty(typeof(MultiplyByTwo).GetCustomAttributes(typeof(GeneratedShaderRecordAttribute), inherit: false));
    }

    private static string EntryPoint<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.EntryPoint;

    private static string Source<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.MslSource;

    /// <summary>Runs <see cref="PrecompiledProbeA"/> on <paramref name="device"/> and checks that it was compiled from its MSL and still gives its values.</summary>
    private static void AssertCompiledFromSource(GraphicsDevice device)
    {
        using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(Length);

        device.For(Length, new PrecompiledProbeA(buffer));

        Assert.Equal(PipelineOrigin.CompiledAtRunTime, device.GetPipelineOrigin<PrecompiledProbeA>());
        Assert.Equal(0, device.PrecompiledPipelineCount);
        Assert.Equal(1, device.PipelineCompileCount);
        Assert.Equal(Enumerable.Range(101, Length), buffer.ToArray());
    }

    private delegate void MathDispatch(GraphicsDevice device, ReadOnlyBuffer<float> source, ReadWriteBuffer<float> output);

    private static void AssertSameBits<T>(MathDispatch dispatch, float[] input)
        where T : struct, IComputeShaderDescriptor<T> =>
        AssertSameBits<T>(
            device =>
            {
                using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer<float>(input);
                using ReadWriteBuffer<float> output = device.AllocateReadWriteBuffer<float>(Length * MathSweep.Outputs);
                dispatch(device, source, output);
                return Bits(output);
            },
            windowed: false);

    /// <summary>
    /// Runs <paramref name="run"/> on a device that compiles from source and on one that uses the
    /// precompiled library, checks that each really did, and compares the results bit for bit.
    /// </summary>
    private static void AssertSameBits<T>(Func<GraphicsDevice, int[]> run, bool windowed)
        where T : struct, IComputeShaderDescriptor<T>
    {
        using GraphicsDevice compiled = GraphicsDevice.CreateSystemDefault();
        compiled.UsePrecompiledShaders = false;
        using GraphicsDevice precompiled = GraphicsDevice.CreateSystemDefault();

        int[] expected = run(compiled);
        int[] actual = run(precompiled);

        Assert.Equal(PipelineOrigin.CompiledAtRunTime, compiled.GetPipelineOrigin<T>(windowed));
        Assert.Equal(PipelineOrigin.Precompiled, precompiled.GetPipelineOrigin<T>(windowed));
        Assert.Equal(0, precompiled.PipelineCompileCount);
        Assert.Equal(0, compiled.PrecompiledPipelineCount);

        int different = expected.Zip(actual).Count(static pair => pair.First != pair.Second);
        Assert.True(different == 0, $"{typeof(T).Name}: {different} of {expected.Length} values differ between the precompiled kernel and the one compiled from source.");
        Assert.Contains(actual, static value => value != 0);
    }

    private static int[] Bits(ReadWriteBuffer<float> buffer) => buffer.ToArray().Select(BitConverter.SingleToInt32Bits).ToArray();

    /// <summary>
    /// Four inputs per thread with full mantissas, so that products need rounding, and now and then a
    /// value that fast math is allowed to mishandle: zero, a huge one, infinity, NaN, a subnormal.
    /// </summary>
    private static float[] MathInputs()
    {
        Random random = new(20261126);
        float[] specials = [0f, -0f, 1e30f, -1e30f, float.PositiveInfinity, float.NaN, 1e-40f, float.Epsilon];
        float[] input = new float[Length * MathSweep.Inputs];

        for (int i = 0; i < input.Length; i++)
        {
            input[i] = i % 97 == 0 ? specials[i / 97 % specials.Length] : (random.NextSingle() * 16f) - 8f;
        }

        return input;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sharpcompute-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
