using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The hidden grid window that lets a kernel run one part of a larger grid (the GPU part of a CPU+GPU
/// split): its buffer index, the <c>__GridWindow</c> struct and its drift guards, the function constant
/// that selects the windowed specialization, and the prologue that rebases the built-ins.
/// </summary>
public class GridWindowTests
{
    private const string ThreadPrologue = "const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;";
    private const string GridPrologue = "const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;";

    private static string ManyBuffers(int count, bool withConstant, string body = "b0[ThreadIds.X] = 1;")
    {
        var builder = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            builder.AppendLine($"    public readonly ReadWriteBuffer<float> b{i};");
        }

        if (withConstant)
        {
            builder.AppendLine("    public readonly float scale;");
        }

        builder.AppendLine($"    public void Execute() {{ {body} }}");
        return TestSources.Shader(builder.ToString());
    }

    private static string GeneratedCode(GeneratorRun run) => Assert.Single(run.Sources).SourceText.ToString();

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 2)]
    [InlineData(3, true, 4)]
    [InlineData(30, false, 30)]
    [InlineData(29, true, 30)]
    public void WindowIndex_IsTheFirstIndexAfterTheBuffersAndConstants(int buffers, bool withConstant, int expected)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, ManyBuffers(buffers, withConstant));
        ShaderDescriptor descriptor = run.Verify("Tests.TestShader");

        Assert.Equal(expected, descriptor.GridWindowIndex);
        Assert.Contains($"constant __GridWindow& __window [[buffer({expected}), function_constant(__windowed)]],", descriptor.MslSource);
        Assert.Contains($"static int global::SharpCompute.IComputeShaderDescriptor<global::Tests.TestShader>.GridWindowIndex => {expected};", GeneratedCode(run));
        Assert.Equal(ThreadPrologue, descriptor.KernelPrologue);
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(30, true)]
    public void NoFreeIndex_MeansNoWindow_AndTheBuiltinsAreUsedDirectly(int buffers, bool withConstant)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, ManyBuffers(buffers, withConstant));
        ShaderDescriptor descriptor = run.Verify("Tests.TestShader");

        // The interface default: the runtime's ForSplit rejects the shader, whole-grid dispatches work as before.
        Assert.Equal(-1, descriptor.GridWindowIndex);
        Assert.DoesNotContain("GridWindowIndex", GeneratedCode(run));
        Assert.DoesNotContain("__window", descriptor.MslSource);
        Assert.Contains("uint3 __tid [[thread_position_in_grid]])", descriptor.MslSource);
        Assert.Empty(descriptor.KernelPrologue);
    }

    [Fact]
    public void NoGridBuiltins_MeansNoWindowCode_ButTheIndexIsStillReserved()
    {
        // Every thread does the same thing, so a part of the grid needs no rebasing; the runtime may
        // still bind a window at the index, which the kernel simply does not declare.
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, ManyBuffers(2, withConstant: true, body: "b1[0] = scale;"));
        ShaderDescriptor descriptor = run.Verify("Tests.TestShader");

        Assert.Equal(3, descriptor.GridWindowIndex);
        Assert.DoesNotContain("__GridWindow", descriptor.MslSource);
        Assert.DoesNotContain("__windowed", descriptor.MslSource);
    }

    [Fact]
    public void WindowStruct_HasDriftGuards_AndTheFunctionConstantIsIndexZero()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, ManyBuffers(1, withConstant: false)).Verify("Tests.TestShader");

        Assert.Contains(
            """
            struct __GridWindow
            {
                packed_uint3 offset;
                packed_uint3 size;
                packed_uint3 groupOffset;
            };
            static_assert(sizeof(__GridWindow) == 36, "SharpCompute: size of '__GridWindow' does not match its C# layout");
            static_assert(__builtin_offsetof(__GridWindow, offset) == 0, "SharpCompute: offset of '__GridWindow.offset' does not match its C# layout");
            static_assert(__builtin_offsetof(__GridWindow, size) == 12, "SharpCompute: offset of '__GridWindow.size' does not match its C# layout");
            static_assert(__builtin_offsetof(__GridWindow, groupOffset) == 24, "SharpCompute: offset of '__GridWindow.groupOffset' does not match its C# layout");

            constant bool __windowed [[function_constant(0)]];
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
    }

    [Fact]
    public void PixelShaders_AlwaysRebaseThePixelAndTheGrid()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Flat : IComputeShader<Float4>
            {
                public Float4 Execute() => new(0.25f, 0.5f, 0.75f, 1f);
            }
            """);
        ShaderDescriptor descriptor = run.Verify("Flat");

        // Index 0: a pixel shader binds its output texture in the texture table, not the buffer table.
        Assert.Equal(0, descriptor.GridWindowIndex);
        Assert.Equal(ThreadPrologue + "\n" + GridPrologue, descriptor.KernelPrologue);
        Assert.Contains("__output.write(float4(0.25f, 0.5f, 0.75f, 1.0f), __tid.xy);", descriptor.KernelBody);
    }

    [Fact]
    public void Helpers_ReceiveTheRebasedBuiltins()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            "buffer[Index()] = factor;",
            "private static int Index() => ThreadIds.X + DispatchSize.X;")).Verify("Tests.TestShader");

        // Helpers take the kernel's (rebased) locals as hidden parameters; only the kernel sees the raw values.
        Assert.Contains("static int Index(uint3 __tid, uint3 __grid);", descriptor.MslSource);
        Assert.Contains("buffer[::Index(__tid, __grid)] = __c.factor;", descriptor.KernelBody);
        Assert.Equal(ThreadPrologue + "\n" + GridPrologue, descriptor.KernelPrologue);
        Assert.Equal(3, descriptor.MslSource.Split("__tid_in").Length - 1);
    }

    [Fact]
    public void ThreadGroupPositions_AreRebasedByTheGroupOffset()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body("buffer[ThreadGroupIds.X] = factor;")).Verify("Tests.TestShader");

        Assert.Contains("uint3 __gid_in [[threadgroup_position_in_grid]])", descriptor.MslSource);
        Assert.Equal("const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;", descriptor.KernelPrologue);
    }

    [Fact]
    public void Stubs_DoNotDeclareAGridWindow()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, ManyBuffers(32, withConstant: false));

        Assert.Contains(run.Diagnostics, static d => d.Id == "SCG0011");
        Assert.DoesNotContain("GridWindowIndex", GeneratedCode(run));
        Assert.DoesNotContain(run.Output.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }
}
