using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>Pixel shaders (<c>IComputeShader&lt;Float4&gt;</c>) and <c>ThreadIds.Normalized</c>.</summary>
public class PixelShaderTests
{
    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    private static GeneratorRun RunPixel(string members, string trailer = "") => GeneratorHarness.Run($$"""
        using SharpCompute;

        namespace Tests;

        [GeneratedComputeShaderDescriptor]
        public readonly partial struct Pixel(float time) : IComputeShader<Float4>
        {
        {{members}}
        }
        {{trailer}}
        """);

    [Fact]
    public void PixelShader_Snapshot()
    {
        GeneratorRun run = RunPixel("""
                private static float Wave(float x)
                {
                    if (x < 0)
                    {
                        return 0;
                    }

                    return Gpu.Sin(x);
                }

                public Float4 Execute()
                {
                    Float2 uv = ThreadIds.Normalized.XY;

                    if (uv.X > 0.5f)
                    {
                        return new Float4(1, 0, 0, 1);
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        if (Wave(time + i) > 0.9f)
                        {
                            return new Float4(0, 1, 0, 1);
                        }
                    }

                    return new(uv, Wave(time), 1f);
                }
            """);

        ShaderDescriptor descriptor = run.Verify("Tests.Pixel");

        Assert.Equal(
            Normalize("""
            #include <metal_stdlib>

            using namespace metal;

            struct __Constants
            {
                float time;
            };
            static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, time) == 0, "SharpCompute: offset of '__Constants.time' does not match its C# layout");

            static float Wave(float x);

            static float Wave(float x)
            {
                if (x < 0.0f)
                {
                    return 0.0f;
                }
                return metal::sin(x);
            }

            kernel void Tests_Pixel(
                texture2d<float, access::write> __output [[texture(0)]],
                constant __Constants& __c [[buffer(0)]],
                uint3 __tid [[thread_position_in_grid]],
                uint3 __grid [[threads_per_grid]])
            {
                float2 uv = float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1));
                if (uv.x > 0.5f)
                {
                    __output.write(float4(1.0f, 0.0f, 0.0f, 1.0f), __tid.xy);
                    return;
                }
                for (int i = 0; i < 4; i++)
                {
                    if (::Wave(__c.time + float(i)) > 0.9f)
                    {
                        __output.write(float4(0.0f, 1.0f, 0.0f, 1.0f), __tid.xy);
                        return;
                    }
                }
                __output.write(float4(uv, ::Wave(__c.time), 1.0f), __tid.xy);
                return;
            }
            """),
            Normalize(descriptor.MslSource));

        // Buffers and constants bind exactly as for IComputeShader: the constants block is at buffer 0.
        Assert.Contains("writer.SetBytes(0, in __constants);", run.Source("Tests.Pixel.SharpCompute.g.cs"));
    }

    [Fact]
    public void ExpressionBodiedExecute_IsRewritten_AndBuiltinsAreAlwaysPresent()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Flat : IComputeShader<Float4>
            {
                public Float4 Execute() => new(0.25f, 0.5f, 0.75f, 1f);
            }
            """).Verify("Flat");

        // No captured state and no use of the thread position, yet __tid and __grid are parameters.
        Assert.Contains(
            Normalize("""
            kernel void Flat(
                texture2d<float, access::write> __output [[texture(0)]],
                uint3 __tid [[thread_position_in_grid]],
                uint3 __grid [[threads_per_grid]])
            {
                __output.write(float4(0.25f, 0.5f, 0.75f, 1.0f), __tid.xy);
                return;
            }
            """),
            descriptor.MslSource);
    }

    [Fact]
    public void ReturnsInHelpersAndLocalFunctions_AreNotRewritten()
    {
        ShaderDescriptor descriptor = RunPixel("""
                private static Float4 Gray(float v) => new(v, v, v, 1f);

                public Float4 Execute()
                {
                    static Float4 Tint(Float4 c) { return c * 0.5f; }

                    return Tint(Gray(time));
                }
            """).Verify("Tests.Pixel");

        Assert.Contains("static float4 Gray(float v)\n{\n    return float4(v, v, v, 1.0f);\n}", descriptor.MslSource);
        Assert.Contains("static float4 Execute_Tint(float4 c)\n{\n    return c * 0.5f;\n}", descriptor.MslSource);
        Assert.Equal("__output.write(::Execute_Tint(::Gray(__c.time)), __tid.xy);\nreturn;", descriptor.KernelBody);
    }

    [Fact]
    public void PixelShader_WithBuffers_BindsThemLikeAComputeShader()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Palette(ReadOnlyBuffer<Float4> colors, float scale) : IComputeShader<Float4>
            {
                public Float4 Execute() => colors[ThreadIds.X % colors.Length] * scale;
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Palette");

        Assert.Contains("texture2d<float, access::write> __output [[texture(0)]],\n    const device packed_float4* colors [[buffer(0)]],\n    constant __Constants& __c [[buffer(1)]],", descriptor.MslSource);
        Assert.Equal("__output.write(colors[int(__tid.x) % int(__c.__length_colors)] * __c.scale, __tid.xy);\nreturn;", descriptor.KernelBody);
    }

    [Theory]
    [InlineData("float v = ThreadIds.Normalized.X;", "float v = float(__tid.x) / float(metal::max(1, int(__grid.x) - 1));")]
    [InlineData("float v = ThreadIds.Normalized.Y;", "float v = float(__tid.y) / float(metal::max(1, int(__grid.y) - 1));")]
    [InlineData("float v = ThreadIds.Normalized.Z;", "float v = float(__tid.z) / float(metal::max(1, int(__grid.z) - 1));")]
    [InlineData("Float2 v = ThreadIds.Normalized.XY;", "float2 v = float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1));")]
    [InlineData("Float3 v = ThreadIds.Normalized.XYZ;", "float3 v = float3(__tid.xyz) / float3(metal::max(int3(1), int3(__grid.xyz) - 1));")]
    public void Normalized_UsesTheInclusiveFormula(string statement, string expected)
    {
        // Also available in IComputeShader kernels, where __tid and __grid are emitted on demand.
        ShaderDescriptor descriptor = GeneratorHarness.Run($$"""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Kernel(ReadWriteBuffer<float> output) : IComputeShader
            {
                public void Execute()
                {
                    {{statement}}
                    output[ThreadIds.X] = 1;
                }
            }
            """).Verify("Kernel");

        Assert.StartsWith(expected + "\n", descriptor.KernelBody);
        Assert.Contains("uint3 __grid [[threads_per_grid]]", descriptor.MslSource);
    }

    [Fact]
    public void Normalized_InAHelper_ForwardsBothBuiltins()
    {
        ShaderDescriptor descriptor = RunPixel("""
                private static float U() => ThreadIds.Normalized.X;

                public Float4 Execute() => new(U(), 0, 0, 1);
            """).Verify("Tests.Pixel");

        Assert.Contains("static float U(uint3 __tid, uint3 __grid);", descriptor.MslSource);
        Assert.Contains("__output.write(float4(::U(__tid, __grid), 0.0f, 0.0f, 1.0f), __tid.xy);", descriptor.KernelBody);
    }

    // ---- diagnostics ------------------------------------------------------------------------------

    [Fact]
    public void MSG0022_BothShaderInterfaces()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Both : IComputeShader, IComputeShader<Float4>
            {
                public void Execute() { }

                Float4 IComputeShader<Float4>.Execute() => default;
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0022", "Both");

        Assert.Equal("Shader struct 'Both' implements both IComputeShader and IComputeShader<Float4>; implement exactly one of them", diagnostic.GetMessage());
        Assert.Empty(run.Sources);
    }

    [Theory]
    [InlineData("float")]
    [InlineData("Float3")]
    [InlineData("Int4")]
    public void MSG0023_PixelTypeMustBeFloat4(string pixelType)
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Wrong : IComputeShader<{{pixelType}}>
            {
                public {{pixelType}} Execute() => default;
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0023", "Wrong");

        Assert.Equal($"Shader struct 'Wrong' implements IComputeShader<{pixelType}>, but pixel shaders must return Float4 (an RGBA color written to the output texture)", diagnostic.GetMessage());
        Assert.Empty(run.Sources);
    }

    [Fact]
    public void MSG0003_NamesBothInterfaces()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Nothing
            {
            }
            """);

        Assert.Equal(
            "Shader struct 'Nothing' is marked [GeneratedComputeShaderDescriptor] but implements neither SharpCompute.IComputeShader nor SharpCompute.IComputeShader<TPixel>",
            run.AssertOnlyDiagnostic("SCG0003", "Nothing").GetMessage());
    }
}
