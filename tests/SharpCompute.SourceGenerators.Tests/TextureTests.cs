using System;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Textures as captured state: <c>ReadOnlyTexture2D&lt;T&gt;</c> (<c>access::sample</c>) and
/// <c>ReadWriteTexture2D&lt;T&gt;</c> (<c>access::read_write</c>), their kernel parameters and binding
/// indices, the translation of reads, writes, sizes and <c>Sample</c>, the sampler globals, and SCG0027 to SCG0033.
/// </summary>
public class TextureTests
{
    private const string Shader = "Tests.TestShader";

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    /// <summary>A compute shader with a sampled texture, a read-write texture, a buffer, and the given <c>Execute</c> body.</summary>
    private static GeneratorRun RunBody(string body, string extraMembers = "", string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader($$"""
                public readonly ReadOnlyTexture2D<Rgba32> source;
                public readonly ReadWriteTexture2D<Float4> target;
                public readonly ReadWriteBuffer<Float4> output;
                {{extraMembers}}

                public void Execute()
                {
                    {{body}}
                }
            """) + trailer);

    private static GeneratorRun RunShader(string members, string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(members) + trailer);

    private static void AssertStub(GeneratorRun run)
    {
        GeneratedSourceResult source = Assert.Single(run.Sources);

        Assert.Contains("This shader has SharpCompute errors", source.SourceText.ToString());
        Assert.DoesNotContain("metal_stdlib", source.SourceText.ToString());
        Assert.DoesNotContain(run.Output.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }

    // ---- kernel parameters and binding ------------------------------------------------------------

    [Fact]
    public void ComputeShader_Snapshot()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace MyApp;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Blend(ReadOnlyTexture2D<Rgba32> source, ReadWriteTexture2D<Float4> target, ReadWriteBuffer<float> luma, float gain) : IComputeShader
            {
                public void Execute()
                {
                    Int2 xy = ThreadIds.XY;
                    Float2 uv = (new Float2(xy.X, xy.Y) + 0.5f) / new Float2(source.Width, source.Height);
                    Float4 color = source.Sample(Sampler.LinearClamp, uv) * gain;
                    Float4 previous = target[xy];
                    target[xy.X, xy.Y] = color + previous + source[xy];
                    luma[xy.Y * target.Width + xy.X] = color.X;
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("MyApp.Blend");

        Assert.Equal(
            Normalize("""
            #include <metal_stdlib>

            using namespace metal;

            constexpr sampler __ms_sampler_linear_clamp(coord::normalized, filter::linear, address::clamp_to_edge);

            struct __Constants
            {
                float gain;
            };
            static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, gain) == 0, "SharpCompute: offset of '__Constants.gain' does not match its C# layout");

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

            kernel void MyApp_Blend(
                texture2d<float, access::sample> source [[texture(0)]],
                texture2d<float, access::read_write> target [[texture(1)]],
                device float* luma [[buffer(0)]],
                constant __Constants& __c [[buffer(1)]],
                constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
                uint3 __tid_in [[thread_position_in_grid]])
            {
                const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
                int2 xy = int2(__tid.xy);
                float2 uv = (float2(float(xy.x), float(xy.y)) + 0.5f) / float2(float(int(source.get_width())), float(int(source.get_height())));
                float4 color = source.sample(__ms_sampler_linear_clamp, uv, metal::level(0)) * __c.gain;
                float4 previous = target.read(uint2(xy));
                target.write(color + previous + source.read(uint2(xy)), uint2(xy.x, xy.y));
                luma[xy.y * int(target.get_width()) + xy.x] = color.x;
            }
            """),
            Normalize(descriptor.MslSource));

        // Textures are neither buffers nor constants: they bind with SetTexture, at their own indices.
        Assert.Contains(
            Normalize("""
                    private readonly void __SharpCompute_SetArguments(ref global::SharpCompute.ShaderArgumentWriter __writer)
                    {
                        __writer.SetBuffer(0, luma ?? throw new global::System.ArgumentNullException("Blend.luma", "The buffer 'Blend.luma' is null. Create the shader with its constructor (not 'default') and pass a buffer."));
                        __writer.SetTexture(0, source ?? throw new global::System.ArgumentNullException("Blend.source", "The texture 'Blend.source' is null. Create the shader with its constructor (not 'default') and pass a texture."));
                        __writer.SetTexture(1, target ?? throw new global::System.ArgumentNullException("Blend.target", "The texture 'Blend.target' is null. Create the shader with its constructor (not 'default') and pass a texture."));
                        __Constants __constants = default;
                        __constants.gain = gain;
                        __writer.SetBytes(1, in __constants);
                    }

                    /// <summary>The C# mirror of the MSL <c>__Constants</c> struct bound after the buffers.</summary>
                    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
                    private struct __Constants
                    {
                        public float gain;
                    }
            """),
            run.Source("MyApp.Blend.SharpCompute.g.cs"));
    }

    [Fact]
    public void PixelShader_TexturesStartAtIndexOne_AfterTheOutput()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Composite : IComputeShader<Float4>
            {
                public readonly ReadOnlyTexture2D<Rgba32Srgb> background;
                public readonly float opacity;
                public readonly ReadWriteTexture2D<R8> mask;

                public Float4 Execute() =>
                    background.Sample(Sampler.LinearRepeat, ThreadIds.Normalized.XY) * mask[ThreadIds.XY].X * opacity;
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Composite");

        Assert.Contains(
            Normalize("""
            kernel void Composite(
                texture2d<float, access::write> __output [[texture(0)]],
                texture2d<float, access::sample> background [[texture(1)]],
                texture2d<float, access::read_write> mask [[texture(2)]],
                constant __Constants& __c [[buffer(0)]],
            """),
            descriptor.MslSource);
        Assert.Equal(
            "__output.write(background.sample(__ms_sampler_linear_repeat, float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1)), metal::level(0)) * mask.read(uint2(int2(__tid.xy))).x * __c.opacity, __tid.xy);\nreturn;",
            descriptor.KernelBody);

        string code = run.Source("Composite.SharpCompute.g.cs");

        // Index 0 is the output, which the runtime binds; the shader's own textures follow in declaration order.
        Assert.Contains("writer.SetTexture(1, shader.background ?? throw", code);
        Assert.Contains("writer.SetTexture(2, shader.mask ?? throw", code);
        Assert.DoesNotContain("SetTexture(0,", code);
        Assert.Contains("writer.SetBytes(0, in __constants);", code);
    }

    [Theory]
    [InlineData("Rgba32", true)]
    [InlineData("Rgba32Srgb", false)]
    [InlineData("R8", true)]
    [InlineData("Rg16", false)]
    [InlineData("Half", true)]
    [InlineData("Rgba16Float", true)]
    [InlineData("float", true)]
    [InlineData("Float4", true)]
    public void EveryElementType_IsATexture2dOfFloat(string element, bool readWrite)
    {
        string members = $$"""
                public readonly ReadOnlyTexture2D<{{element}}> input;
                {{(readWrite ? $"public readonly ReadWriteTexture2D<{element}> result;" : string.Empty)}}
                public readonly ReadWriteBuffer<Float4> output;

                public void Execute()
                {
                    output[ThreadIds.X] = input[ThreadIds.X, 0];
                    {{(readWrite ? "result[ThreadIds.X, 0] = input[ThreadIds.X, 0];" : string.Empty)}}
                }
            """;

        ShaderDescriptor descriptor = RunShader(members).Verify(Shader);

        // The pixel type picks the Metal pixel format at allocation; in MSL every format reads and writes float4.
        Assert.Contains("texture2d<float, access::sample> input [[texture(0)]]", descriptor.MslSource);
        Assert.Equal(readWrite, descriptor.MslSource.Contains("texture2d<float, access::read_write> result [[texture(1)]]", StringComparison.Ordinal));
    }

    private static string ManyTextures(int count, bool pixelShader)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using SharpCompute;");
        builder.AppendLine("namespace Tests;");
        builder.AppendLine("[GeneratedComputeShaderDescriptor]");
        builder.AppendLine($"public readonly partial struct TestShader : {(pixelShader ? "IComputeShader<Float4>" : "IComputeShader")}");
        builder.AppendLine("{");

        for (int i = 0; i < count; i++)
        {
            builder.AppendLine($"    public readonly ReadOnlyTexture2D<float> t{i};");
        }

        builder.AppendLine("    public readonly ReadWriteBuffer<Float4> output;");
        builder.AppendLine(pixelShader
            ? $"    public Float4 Execute() => t0[0, 0] + t{count - 1}[0, 0] + output[0];"
            : $"    public void Execute() => output[0] = t0[0, 0] + t{count - 1}[0, 0];");
        builder.AppendLine("}");
        return builder.ToString();
    }

    [Theory]
    [InlineData(31, false, "[[texture(30)]]")]
    [InlineData(30, true, "[[texture(30)]]")]
    public void TextureLimit_IsThirtyOne_CountedSeparatelyFromBuffers(int textures, bool pixelShader, string last)
    {
        // 31 textures and a buffer: more than 31 arguments in total, but each table has room.
        GeneratorRun run = GeneratorHarness.Run(ManyTextures(textures, pixelShader));
        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains(last, descriptor.MslSource);
        Assert.DoesNotContain("[[texture(31)]]", descriptor.MslSource);
        Assert.Contains("output [[buffer(0)]]", descriptor.MslSource);
        Assert.Contains("writer.SetTexture(30, shader.t" + (textures - 1), run.Source("TestShader.SharpCompute.g.cs"));
    }

    [Theory]
    [InlineData(32, false, "binds 32 textures, but Metal supports at most 31")]
    [InlineData(31, true, "binds 32 textures, but Metal supports at most 31 (including the output texture of a pixel shader)")]
    public void MSG0029_TooManyTextures(int textures, bool pixelShader, string message)
    {
        GeneratorRun run = GeneratorHarness.Run(ManyTextures(textures, pixelShader));
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0029", "TestShader");

        Assert.Equal("Shader struct 'TestShader' " + message, diagnostic.GetMessage());
        AssertStub(run);
    }

    [MetalFact]
    public void DispatchingDefault_NamesTheShaderAndTheTexture()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            namespace MyApp;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Copy(ReadOnlyTexture2D<Rgba32> source, ReadWriteTexture2D<Rgba32> destination) : IComputeShader
            {
                public void Execute() => destination[ThreadIds.XY] = source[ThreadIds.XY];
            }
            """);

        var exception = Assert.IsType<ArgumentNullException>(GpuDispatch.ForDefault(run.AssertClean(), "MyApp.Copy"));

        Assert.Equal("Copy.source", exception.ParamName);
        Assert.StartsWith("The texture 'Copy.source' is null. Create the shader with its constructor (not 'default') and pass a texture.", exception.Message);
    }

    [Fact]
    public void ReadmeExample_Translates()
    {
        // The ping-pong and AsReadOnly example of the README's "Textures" section.
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Diffuse(ReadWriteTexture2D<float> source, ReadWriteTexture2D<float> target) : IComputeShader
            {
                public void Execute()
                {
                    int x = ThreadIds.X;
                    int y = ThreadIds.Y;
                    float sum = source[Gpu.Max(x - 1, 0), y].X + source[Gpu.Min(x + 1, source.Width - 1), y].X
                              + source[x, Gpu.Max(y - 1, 0)].X + source[x, Gpu.Min(y + 1, source.Height - 1)].X;

                    target[x, y] = new Float4(sum * 0.25f);
                }
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct ShowHeat(ReadOnlyTexture2D<float> heat) : IComputeShader<Float4>
            {
                public Float4 Execute()
                {
                    float value = heat.Sample(Sampler.LinearClamp, ThreadIds.Normalized.XY).X;
                    return new Float4(value, 0f, 1f - value, 1f);
                }
            }
            """);

        ShaderDescriptor diffuse = run.Verify("Diffuse");
        ShaderDescriptor showHeat = run.Verify("ShowHeat");

        Assert.Contains("texture2d<float, access::read_write> source [[texture(0)]],\n    texture2d<float, access::read_write> target [[texture(1)]],", diffuse.MslSource);
        Assert.EndsWith("target.write(float4(sum * 0.25f), uint2(x, y));", diffuse.KernelBody);
        Assert.Contains("texture2d<float, access::write> __output [[texture(0)]],\n    texture2d<float, access::sample> heat [[texture(1)]],", showHeat.MslSource);
        Assert.StartsWith("float value = heat.sample(__ms_sampler_linear_clamp, float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1)), metal::level(0)).x;", showHeat.KernelBody);
    }

    // ---- reads, writes, sizes ---------------------------------------------------------------------

    [Theory]
    [InlineData("output[0] = source[1, 2];", "output[0] = source.read(uint2(1, 2));")]
    [InlineData("output[0] = source[ThreadIds.X, ThreadIds.Y + 1];", "output[0] = source.read(uint2(int(__tid.x), int(__tid.y) + 1));")]
    [InlineData("output[0] = source[y: 2, x: 1];", "output[0] = source.read(uint2(1, 2));")]
    [InlineData("output[0] = source[ThreadIds.XY];", "output[0] = source.read(uint2(int2(__tid.xy)));")]
    [InlineData("Int2 p = new Int2(3, 4); output[0] = source[p] + target[p];", "int2 p = int2(3, 4);\noutput[0] = source.read(uint2(p)) + target.read(uint2(p));")]
    [InlineData("output[0] = target[1, 2];", "output[0] = target.read(uint2(1, 2));")]
    [InlineData("float red = source[0, 0].X + target[0, 0].W; output[0] = new Float4(red);", "float red = source.read(uint2(0, 0)).x + target.read(uint2(0, 0)).w;\noutput[0] = float4(red);")]
    [InlineData("target[1, 2] = new Float4(1, 0, 0, 1);", "target.write(float4(1.0f, 0.0f, 0.0f, 1.0f), uint2(1, 2));")]
    [InlineData("target[ThreadIds.XY] = source[ThreadIds.XY] * 2;", "target.write(source.read(uint2(int2(__tid.xy))) * 2.0f, uint2(int2(__tid.xy)));")]
    [InlineData("target[y: 2, x: 1] = output[0];", "target.write(output[0], uint2(1, 2));")]
    [InlineData("Float4 c = target[0, 0]; c.X += 1; target[0, 0] = c;", "float4 c = target.read(uint2(0, 0));\nc.x += 1.0f;\ntarget.write(c, uint2(0, 0));")]
    [InlineData("for (int i = 0; i < 4; target[i, 0] = output[i], i++) { }", "for (int i = 0; i < 4; target.write(output[i], uint2(i, 0)), i++)\n{\n}")]
    [InlineData("int w = source.Width; int h = source.Height; output[w + h] = default;", "int w = int(source.get_width());\nint h = int(source.get_height());\noutput[w + h] = float4(0);")]
    [InlineData("output[target.Width * target.Height - 1] = default;", "output[int(target.get_width()) * int(target.get_height()) - 1] = float4(0);")]
    [InlineData("if (ThreadIds.X < source.Width) { output[0] = default; }", "if (int(__tid.x) < int(source.get_width()))\n{\n    output[0] = float4(0);\n}")]
    public void ReadsWritesAndSizes(string body, string expected)
    {
        ShaderDescriptor descriptor = RunBody(body).Verify(Shader);

        Assert.Equal(expected, descriptor.KernelBody);
    }

    [Fact]
    public void TextureLocals_AreDeclaredByValue()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            ReadOnlyTexture2D<Rgba32> image = source;
            ReadWriteTexture2D<Float4> canvas = target;
            canvas[0, 0] = image[0, 0] + image.Sample(Sampler.PointClamp, new Float2(0.5f));
            """).Verify(Shader);

        Assert.Equal(
            "texture2d<float, access::sample> image = source;\n" +
            "texture2d<float, access::read_write> canvas = target;\n" +
            "canvas.write(image.read(uint2(0, 0)) + image.sample(__ms_sampler_point_clamp, float2(0.5f), metal::level(0)), uint2(0, 0));",
            descriptor.KernelBody);
    }

    [Fact]
    public void LocalNamedLikeACapturedTexture_IsRenamed()
    {
        // The texture is a kernel parameter called `source`; a local function's parameter may reuse the name.
        ShaderDescriptor descriptor = RunBody(
            """
            output[0] = Twice(2) * source[0, 0];

            static float Twice(float source) => source * 2;
            """).Verify(Shader);

        Assert.Contains("static float Execute_Twice(float __local_source)\n{\n    return __local_source * 2.0f;\n}", descriptor.MslSource);
    }

    [Fact]
    public void NamesThatMslUsesForTextures_AreEscapedOrQualified()
    {
        // `level` would hide metal::level in an unqualified `level(0)`, and `texture2d` / `access` the
        // type of a texture parameter or local; the first is qualified, the other two are escaped.
        ShaderDescriptor descriptor = RunBody(
            """
            float level = 0.25f;
            float access = 2;
            float texture2d = 3;
            ReadOnlyTexture2D<Rgba32> sampler = source;
            output[0] = sampler.Sample(Sampler.LinearMirror, new Float2(level)) * access * texture2d;
            """).Verify(Shader);

        Assert.Equal(
            "float level = 0.25f;\n" +
            "float __ms_access = 2.0f;\n" +
            "float __ms_texture2d = 3.0f;\n" +
            "texture2d<float, access::sample> sampler = source;\n" +
            "output[0] = sampler.sample(__ms_sampler_linear_mirror, float2(level), metal::level(0)) * __ms_access * __ms_texture2d;",
            descriptor.KernelBody);
    }

    // ---- Sample and the sampler globals -----------------------------------------------------------

    [Theory]
    [InlineData("PointClamp", "constexpr sampler __ms_sampler_point_clamp(coord::normalized, filter::nearest, address::clamp_to_edge);")]
    [InlineData("PointRepeat", "constexpr sampler __ms_sampler_point_repeat(coord::normalized, filter::nearest, address::repeat);")]
    [InlineData("PointMirror", "constexpr sampler __ms_sampler_point_mirror(coord::normalized, filter::nearest, address::mirrored_repeat);")]
    [InlineData("LinearClamp", "constexpr sampler __ms_sampler_linear_clamp(coord::normalized, filter::linear, address::clamp_to_edge);")]
    [InlineData("LinearRepeat", "constexpr sampler __ms_sampler_linear_repeat(coord::normalized, filter::linear, address::repeat);")]
    [InlineData("LinearMirror", "constexpr sampler __ms_sampler_linear_mirror(coord::normalized, filter::linear, address::mirrored_repeat);")]
    public void EachSampler_BecomesAProgramScopeConstexprSampler(string property, string declaration)
    {
        ShaderDescriptor descriptor = RunBody($"output[0] = source.Sample(Sampler.{property}, new Float2(0.25f, 0.75f));").Verify(Shader);

        string name = declaration.Split(' ')[2].Split('(')[0];

        Assert.Equal($"output[0] = source.sample({name}, float2(0.25f, 0.75f), metal::level(0));", descriptor.KernelBody);
        Assert.Contains("using namespace metal;\n\n" + declaration + "\n\n", descriptor.MslSource);
        Assert.Single(descriptor.MslSource.Split('\n'), static line => line.StartsWith("constexpr sampler", StringComparison.Ordinal));
    }

    [Fact]
    public void Samplers_AreEmittedOnce_InAFixedOrder_HoweverOftenAndWhereverTheyAreUsed()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Float2 uv = new Float2(0.5f);
            Float4 a = source.Sample(Sampler.LinearRepeat, uv) + source.Sample(Sampler.LinearRepeat, uv * 2);
            Float4 b = source.Sample(sampler: Sampler.PointMirror, uv: uv) + source.Sample(uv: uv, sampler: Sampler.PointMirror);
            output[0] = a + b + Tap(uv);
            """,
            """
            private Float4 Tap(Float2 uv) => source.Sample(Sampler.LinearRepeat, uv) + source.Sample(Sampler.PointClamp, uv);
            """).Verify(Shader);

        // Used: LinearRepeat (three times, once in a helper), PointMirror (twice), PointClamp (in the helper only).
        Assert.Equal(
            [
                "constexpr sampler __ms_sampler_point_clamp(coord::normalized, filter::nearest, address::clamp_to_edge);",
                "constexpr sampler __ms_sampler_point_mirror(coord::normalized, filter::nearest, address::mirrored_repeat);",
                "constexpr sampler __ms_sampler_linear_repeat(coord::normalized, filter::linear, address::repeat);",
            ],
            descriptor.MslSource.Split('\n').Where(static line => line.StartsWith("constexpr sampler", StringComparison.Ordinal)));

        Assert.Contains("float4 b = source.sample(__ms_sampler_point_mirror, uv, metal::level(0)) + source.sample(__ms_sampler_point_mirror, uv, metal::level(0));", descriptor.KernelBody);
    }

    [Fact]
    public void ShaderWithoutSample_DeclaresNoSampler()
    {
        ShaderDescriptor descriptor = RunBody("output[0] = source[0, 0];").Verify(Shader);

        Assert.DoesNotContain("sampler", descriptor.MslSource);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    [Fact]
    public void Helpers_ReceiveCapturedTexturesAsHiddenParameters()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            output[ThreadIds.X] = Average(ThreadIds.X) + Outer(1);
            """,
            """
            private Float4 Average(int x) => (source[x, 0] + source[x + 1, 0]) * 0.5f;

            private Float4 Outer(int x)
            {
                Store(x, Average(x));
                return target[x, 0];
            }

            private void Store(int x, Float4 value) => target[x, 0] = value;
            """).Verify(Shader);

        // Each helper gets the textures it (or what it calls) uses, under the kernel's parameter names.
        Assert.Contains("static float4 Average(int x, texture2d<float, access::sample> source)\n{\n    return (source.read(uint2(x, 0)) + source.read(uint2(x + 1, 0))) * 0.5f;\n}", descriptor.MslSource);
        Assert.Contains("static void Store(int x, float4 value, texture2d<float, access::read_write> target)\n{\n    target.write(value, uint2(x, 0));\n}", descriptor.MslSource);
        Assert.Contains("static float4 Outer(int x, texture2d<float, access::sample> source, texture2d<float, access::read_write> target)\n{\n    ::Store(x, ::Average(x, source), target);\n    return target.read(uint2(x, 0));\n}", descriptor.MslSource);
        Assert.Equal("output[int(__tid.x)] = ::Average(int(__tid.x), source) + ::Outer(1, source, target);", descriptor.KernelBody);
    }

    [Fact]
    public void Helpers_TakeTexturesAsParameters_ByValue()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Float2 uv = new Float2(0.5f);
            output[0] = Bilinear(source, uv) + Texel(in source, 1, 2) + Size(target);
            Fill(target, output[0]);

            static Float4 Bilinear(ReadOnlyTexture2D<Rgba32> image, Float2 at) => image.Sample(Sampler.LinearClamp, at);

            static void Fill(ReadWriteTexture2D<Float4> canvas, Float4 color) => canvas[0, 0] = color + canvas[1, 1];
            """,
            """
            private static Float4 Texel(in ReadOnlyTexture2D<Rgba32> image, int x, int y) => image[x, y];

            private static float Size(ReadWriteTexture2D<Float4> canvas) => canvas.Width * canvas.Height;
            """).Verify(Shader);

        Assert.Contains("static float4 Execute_Bilinear(texture2d<float, access::sample> image, float2 at)\n{\n    return image.sample(__ms_sampler_linear_clamp, at, metal::level(0));\n}", descriptor.MslSource);
        Assert.Contains("static float4 Texel(texture2d<float, access::sample> image, int x, int y)\n{\n    return image.read(uint2(x, y));\n}", descriptor.MslSource);
        Assert.Contains("static float Size(texture2d<float, access::read_write> canvas)\n{\n    return float(int(canvas.get_width()) * int(canvas.get_height()));\n}", descriptor.MslSource);
        Assert.Contains("static void Execute_Fill(texture2d<float, access::read_write> canvas, float4 color)\n{\n    canvas.write(color + canvas.read(uint2(1, 1)), uint2(0, 0));\n    return;\n}", descriptor.MslSource);
        Assert.Equal(
            "float2 uv = float2(0.5f);\n" +
            "output[0] = ::Execute_Bilinear(source, uv) + ::Texel(source, 1, 2) + ::Size(target);\n" +
            "::Execute_Fill(target, output[0]);",
            descriptor.KernelBody);
    }

    // ---- SCG0027: a texture inside a struct -------------------------------------------------------

    [Fact]
    public void MSG0027_TextureNestedInStruct()
    {
        GeneratorRun run = RunShader("""
                public readonly Bundle bundle;

                public void Execute() { }
            """, trailer: "\npublic struct Bundle { public float Scale; public ReadOnlyTexture2D<Rgba32> Image; }");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0027", "Bundle");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Struct 'Bundle' has a texture field 'Image'; textures must be captured directly by the shader struct", diagnostic.GetMessage());
        AssertStub(run);
    }

    // ---- SCG0028: the element type ----------------------------------------------------------------

    [Theory]
    [InlineData("ReadOnlyTexture2D<int>", "'int' is not a texture element type; use Rgba32, Rgba32Srgb, R8, Rg16, Half, Rgba16Float, float or Float4")]
    [InlineData("ReadOnlyTexture2D<Float2>", "'Float2' is not a texture element type; use Rgba32, Rgba32Srgb, R8, Rg16, Half, Rgba16Float, float or Float4")]
    [InlineData("ReadWriteTexture2D<Texel>", "'Texel' is not a texture element type; use Rgba32, Rgba32Srgb, R8, Rg16, Half, Rgba16Float, float or Float4")]
    [InlineData("ReadWriteTexture2D<Rg16>", "the GPU cannot read and write 'Rg16' pixels in one shader; use ReadOnlyTexture2D<Rg16>")]
    [InlineData("ReadWriteTexture2D<Rgba32Srgb>", "the GPU cannot read and write 'Rgba32Srgb' pixels in one shader; use ReadOnlyTexture2D<Rgba32Srgb>")]
    public void MSG0028_UnsupportedTextureElementType(string type, string reason)
    {
        const string Texel = "\npublic struct Texel { public float A; public float B; }";

        // As a captured field...
        GeneratorRun asField = RunShader($$"""
                public readonly {{type}} image;

                public void Execute() { }
            """, trailer: Texel);

        Diagnostic diagnostic = asField.AssertOnlyDiagnostic("SCG0028", type);
        Assert.Equal($"Texture type '{type}' is not supported: {reason}", diagnostic.GetMessage());
        AssertStub(asField);

        // ...and as a helper parameter.
        GeneratorRun asParameter = RunShader($$"""
                public readonly ReadWriteBuffer<float> buffer;

                public void Execute() => buffer[0] = Width(null!);

                private static float Width({{type}} image) => image.Width;
            """, trailer: Texel);

        Assert.Contains(asParameter.Diagnostics, d => d.Id == "SCG0028" && asParameter.TextAt(d) == type);
        Assert.All(asParameter.Diagnostics, static d => Assert.NotEqual("SCG0007", d.Id));
        AssertStub(asParameter);
    }

    // ---- SCG0030: the sampler ---------------------------------------------------------------------

    [Fact]
    public void MSG0030_SamplerStoredInALocal()
    {
        GeneratorRun run = RunBody(
            """
            Sampler linear = Sampler.LinearClamp;
            output[0] = source.Sample(linear, new Float2(0.5f));
            """);

        // One diagnostic, at the value that is not passed straight to Sample; the later use of the local is not reported again.
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0030", "Sampler.LinearClamp");

        Assert.Equal(
            "'Sampler.LinearClamp' is used outside a Sample call; pass one of the static properties of Sampler (such as Sampler.LinearClamp) directly as the first argument of Sample, because the sampler is compiled into the shader",
            diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void MSG0030_SamplerChosenAtRunTime()
    {
        GeneratorRun run = RunBody("output[0] = source.Sample(ThreadIds.X > 4 ? Sampler.LinearClamp : Sampler.PointClamp, new Float2(0.5f));");
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0030", "ThreadIds.X > 4 ? Sampler.LinearClamp : Sampler.PointClamp");

        Assert.StartsWith("'ThreadIds.X > 4 ? Sampler.LinearClamp : Sampler.PointClamp' is not a static property of Sampler; pass one of", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void MSG0030_SamplerAsAHelperParameter()
    {
        GeneratorRun run = RunBody(
            "output[0] = Tap(Sampler.LinearClamp);",
            "private Float4 Tap(Sampler sampler) => source.Sample(sampler, new Float2(0.5f));");

        Diagnostic diagnostic = run.AssertDiagnostic("SCG0030", "Sampler");

        Assert.StartsWith("A Sampler cannot be stored or passed around in shader code; pass one of", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void SamplerAsCapturedState_IsAnUnsupportedCapture()
    {
        GeneratorRun run = RunBody("output[0] = source[0, 0];", "public readonly Sampler sampler;");
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0006", "Sampler");

        Assert.Equal("Field 'sampler' of type 'Sampler' cannot be captured by a compute shader: a Sampler cannot be stored or passed around in shader code", diagnostic.GetMessage());
        AssertStub(run);
    }

    // ---- SCG0031: compound assignment of a pixel --------------------------------------------------

    [Theory]
    [InlineData("target[1, 2] += output[0];", "target[1, 2] += output[0]")]
    [InlineData("target[ThreadIds.XY] *= 0.5f;", "target[ThreadIds.XY] *= 0.5f")]
    [InlineData("target[0, 0] -= source[0, 0];", "target[0, 0] -= source[0, 0]")]
    [InlineData("Float4 sum = target[0, 0] += output[0]; output[1] = sum;", "target[0, 0] += output[0]")]
    public void MSG0031_TextureElementModifiedInPlace(string body, string text)
    {
        GeneratorRun run = RunBody(body);
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0031", text);

        Assert.Equal(
            $"'{text}' reads and writes a texture element in one expression, which Metal's texture read and write functions cannot express; read the pixel into a local, change the local, and assign it back",
            diagnostic.GetMessage());
        AssertStub(run);
    }

    [Theory]
    [InlineData("Float4 written = target[0, 0] = output[0]; output[1] = written;", "target[0, 0] = output[0]")]
    [InlineData("output[1] = target[0, 0] = output[0];", "target[0, 0] = output[0]")]
    public void TextureWriteUsedAsAValue_IsRejected(string body, string text)
    {
        GeneratorRun run = RunBody(body);
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0012", text);

        Assert.Equal("Unsupported construct in a compute shader: a texture write used as a value (assign the pixel in a statement of its own)", diagnostic.GetMessage());
        AssertStub(run);
    }

    // ---- SCG0032: textures and helper signatures --------------------------------------------------

    [Fact]
    public void MSG0032_HelperReturnsATexture()
    {
        GeneratorRun run = RunBody(
            "output[0] = Pick(ThreadIds.X)[0, 0];",
            "private ReadOnlyTexture2D<Rgba32> Pick(int index) => source;");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0032", "ReadOnlyTexture2D<Rgba32>");

        Assert.Equal("Helper method 'Pick' returns a texture; a texture can only be passed to a helper method by value", diagnostic.GetMessage());
        AssertStub(run);
    }

    [Theory]
    [InlineData("ref", "Swap(ref image);", "ref ReadOnlyTexture2D<Rgba32> image")]
    [InlineData("out", "Swap(out image);", "out ReadOnlyTexture2D<Rgba32> image")]
    public void MSG0032_TexturePassedByReference(string modifier, string call, string text)
    {
        GeneratorRun run = RunBody(
            $"ReadOnlyTexture2D<Rgba32> image = source; {call} output[0] = image[0, 0];",
            $"private void Swap({modifier} ReadOnlyTexture2D<Rgba32> image) {{ image = source; }}");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0032", text);

        Assert.Equal($"Helper method 'Swap' takes the texture parameter 'image' by '{modifier}'; a texture can only be passed to a helper method by value", diagnostic.GetMessage());
        AssertStub(run);
    }

    // ---- SCG0033: sampling a read-write texture ---------------------------------------------------

    [Fact]
    public void MSG0033_AsReadOnlyInsideAShader()
    {
        // ReadWriteTexture2D has no Sample method, so C# already rejects `target.Sample(...)`; the way
        // around it that compiles is the one that cannot work on the GPU.
        GeneratorRun run = RunBody("output[0] = target.AsReadOnly().Sample(Sampler.LinearClamp, new Float2(0.5f));");
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0033", "target.AsReadOnly()");

        Assert.Equal(
            "'ReadWriteTexture2D<Float4>.AsReadOnly()' cannot be used in a compute shader: Metal cannot sample a texture that the shader can also write. " +
            "Capture the result of AsReadOnly() in a ReadOnlyTexture2D<T> field of a shader that runs in a later dispatch, and sample that.",
            diagnostic.GetMessage());
        AssertStub(run);
    }

    [Fact]
    public void SampleOnAReadWriteTexture_DoesNotCompileInCSharp()
    {
        GeneratorRun run = RunBody("output[0] = target.Sample(Sampler.LinearClamp, new Float2(0.5f));");

        // The C# compiler reports the missing method; the generator adds nothing on top.
        Assert.Empty(run.Diagnostics);
        Assert.Contains(run.Input.GetDiagnostics(), static d => d.Id == "CS1061" && d.GetMessage().Contains("Sample", StringComparison.Ordinal));
    }

    // ---- other members ----------------------------------------------------------------------------

    [Theory]
    [InlineData("source.CopyFrom(default);", "SCG0016", "source.CopyFrom(default)", "Method 'Texture2D<Rgba32>.CopyFrom(ReadOnlySpan<Rgba32>)'")]
    [InlineData("target.Dispose();", "SCG0016", "target.Dispose()", "Method 'Texture2D<Float4>.Dispose()'")]
    [InlineData("GraphicsDevice device = source.Device;", "SCG0017", "source.Device", "Member 'Texture2D<Rgba32>.Device'")]
    public void CpuOnlyMembers_AreRejected(string body, string id, string text, string member)
    {
        GeneratorRun run = RunBody(body);

        Assert.Contains(
            run.Diagnostics,
            d => d.Id == id &&
                run.TextAt(d) == text &&
                d.GetMessage() == member + " " + (id == "SCG0016" ? "cannot be called from" : "cannot be used in") + " a compute shader: only the indexers, 'Width', 'Height' and 'Sample' of a texture can be used in a shader");
        AssertStub(run);
    }

    [Fact]
    public void CapturedTexture_CannotBeReassigned()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public partial struct Mutating : IComputeShader
            {
                public ReadOnlyTexture2D<Rgba32> a;
                public ReadOnlyTexture2D<Rgba32> b;

                public void Execute() => a = b;
            }
            """);

        // SCG0004 (not readonly) is expected too; the write itself is SCG0018.
        Assert.Contains(run.Diagnostics, static d => d.Id == "SCG0018" && d.GetMessage().StartsWith("Cannot modify 'a'", StringComparison.Ordinal));
    }

    [Fact]
    public void TextureInAStaticInitializer_IsNotConstant()
    {
        GeneratorRun run = RunBody(
            "output[0] = Corner;",
            "private static readonly Float4 Corner = default(TestShader).source.Sample(Sampler.PointClamp, new Float2(0));");

        Assert.Contains(run.Diagnostics, static d => d.Id == "SCG0024");
        AssertStub(run);
    }
}
