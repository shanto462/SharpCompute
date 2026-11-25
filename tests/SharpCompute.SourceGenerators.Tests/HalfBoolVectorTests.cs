using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The MSL for SharpCompute's own <c>Half2..4</c> and <c>Bool2..4</c>: every position a value can be
/// in, the componentwise comparisons, <c>Gpu.Any</c>/<c>All</c>/<c>Select</c>, and the layout the
/// generator asserts for half vectors at the CPU/GPU boundary. Every MSL result is compiled with Metal.
/// </summary>
public class HalfBoolVectorTests
{
    private const string Shader = "Tests.TestShader";

    private static string Kernel(string body, string extraMembers = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(body, extraMembers)).Verify(Shader).KernelBody;

    private static void AssertKernel(string expected, string body, string extraMembers = "") =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), Kernel(body, extraMembers));

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    // ---- half vectors: expressions ---------------------------------------------------------------

    [Fact]
    public void HalfVectors_ConstructorsSwizzlesAndOperatorsAreNative()
    {
        AssertKernel(
            """
            half3 a = half3(float3(buffer[0], 1.0f, 2.0f));
            half3 b = a * a + half(0.5f);
            b -= a / half(2);
            b = metal::fmod(b, a);
            b = metal::fmod(b, half(3));
            b = -b * half(-2) + half(200) * a;
            half4 c = half4(b, half(1.0f));
            half4 d = half4(c.xy, half2(half(0.25f)));
            half2 s = d.zx;
            d.xy = s;
            half w = d.w;
            d.y = w;
            buffer[0] = float(d.x) + float(c.w);
            """,
            """
            Half3 a = (Half3)new Float3(buffer[0], 1f, 2f);
            Half3 b = a * a + (Half)0.5f;
            b -= a / (Half)2;
            b = b % a;
            b %= (Half)3;
            b = -b * -2 + 200 * a;
            Half4 c = new Half4(b, (Half)1f);
            Half4 d = new Half4(c.XY, new Half2((Half)0.25f));
            Half2 s = d.ZX;
            d.XY = s;
            Half w = d.W;
            d.Y = w;
            buffer[0] = (float)d.X + (float)c.W;
            """);
    }

    [Fact]
    public void HalfVectors_ConvertToAndFromEveryOtherVector()
    {
        AssertKernel(
            """
            float3 f = float3(buffer[0]);
            half3 h = half3(f);
            int3 i = int3(h);
            uint3 u = uint3(h);
            h = half3(i) + half3(u);
            f = float3(h);
            half2 zero = half2(0);
            half4 other = half4(0);
            buffer[0] = f.x + float(zero.x) + float(other.w);
            """,
            """
            Float3 f = new Float3(buffer[0]);
            Half3 h = (Half3)f;
            Int3 i = (Int3)h;
            UInt3 u = (UInt3)h;
            h = (Half3)i + (Half3)u;
            f = (Float3)h;
            Half2 zero = default;
            Half4 other = new Half4();
            buffer[0] = f.X + (float)zero.X + (float)other.W;
            """);
    }

    [Fact]
    public void HalfVectors_IntrinsicsMapToTheSameMetalFunctionsAsFloat()
    {
        AssertKernel(
            """
            half3 a = half3(float3(buffer[0], 1.0f, 2.0f));
            half3 b = metal::sqrt(metal::abs(a));
            b = metal::mix(a, b, half(0.5f));
            b = metal::clamp(b, half(0.0f), half(1.0f));
            b = metal::fma(a, b, a);
            b = metal::smoothstep(half(0.0f), half(1.0f), b) + metal::step(half(0.5f), a);
            b = metal::pow(b, half(2)) + metal::max(a, b);
            half d = metal::dot(a, b) + metal::length(a) + metal::distance(a, b);
            half3 n = metal::normalize(metal::cross(a, b)) + metal::reflect(a, b);
            buffer[0] = float(d) + float(n.x);
            """,
            """
            Half3 a = (Half3)new Float3(buffer[0], 1f, 2f);
            Half3 b = Gpu.Sqrt(Gpu.Abs(a));
            b = Gpu.Lerp(a, b, (Half)0.5f);
            b = Gpu.Clamp(b, (Half)0f, (Half)1f);
            b = Gpu.Fma(a, b, a);
            b = Gpu.Smoothstep((Half)0f, (Half)1f, b) + Gpu.Step((Half)0.5f, a);
            b = Gpu.Pow(b, (Half)2) + Gpu.Max(a, b);
            Half d = Gpu.Dot(a, b) + Gpu.Length(a) + Gpu.Distance(a, b);
            Half3 n = Gpu.Normalize(Gpu.Cross(a, b)) + Gpu.Reflect(a, b);
            buffer[0] = (float)d + (float)n.X;
            """);
    }

    /// <summary>
    /// Every public <c>Gpu</c> method that takes a half vector, called once: Metal accepts the MSL of
    /// each of them, so each maps to a function MSL defines for <c>half</c> operands.
    /// </summary>
    [Fact]
    public void HalfVectors_EveryGpuOverloadCompilesWithMetal()
    {
        Type[] halves = [typeof(Half2), typeof(Half3), typeof(Half4)];
        Type[] masks = [typeof(Bool2), typeof(Bool3), typeof(Bool4)];
        MethodInfo[] methods = typeof(Gpu).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetParameters().Any(p => halves.Contains(p.ParameterType)))
            .ToArray();

        // 36 componentwise functions (7 with a scalar form), 5 geometric ones and 3 classifications at
        // three widths, cross, equality with a vector and with a scalar, and select.
        Assert.Equal(((36 + 7 + 5 + 3) * 3) + 1 + (4 * 3) + 3, methods.Length);

        var body = new StringBuilder();
        body.AppendLine("Half sc = (Half)buffer[0];");
        body.AppendLine("Half2 h2 = new Half2(sc); Half3 h3 = new Half3(sc); Half4 h4 = new Half4(sc);");
        body.AppendLine("Bool2 b2 = h2 < sc; Bool3 b3 = h3 < sc; Bool4 b4 = h4 < sc;");
        body.AppendLine("float acc = 0;");

        foreach (MethodInfo m in methods)
        {
            string arguments = string.Join(", ", m.GetParameters().Select(p =>
                p.ParameterType == typeof(Half) ? "sc"
                : halves.Contains(p.ParameterType) ? "h" + p.ParameterType.Name[^1]
                : masks.Contains(p.ParameterType) ? "b" + p.ParameterType.Name[^1]
                : throw new InvalidOperationException($"{m}: unexpected parameter {p.ParameterType}")));
            string call = $"Gpu.{m.Name}({arguments})";

            body.AppendLine(
                m.ReturnType == typeof(Half) ? $"acc += (float){call};"
                : halves.Contains(m.ReturnType) ? $"acc += (float){call}.X;"
                : masks.Contains(m.ReturnType) ? $"acc += {call}.X ? 1f : 0f;"
                : throw new InvalidOperationException($"{m}: unexpected return type {m.ReturnType}"));
        }

        body.AppendLine("buffer[0] = acc;");

        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(body.ToString())).Verify(Shader);

        Assert.Contains("metal::exp10(h4)", descriptor.KernelBody);
        Assert.Contains("metal::copysign(h2, h2)", descriptor.KernelBody);
        Assert.Contains("metal::isfinite(h3)", descriptor.KernelBody);
        Assert.Contains("metal::select(h4, h4, b4)", descriptor.KernelBody);
        Assert.DoesNotContain("__ms_", descriptor.MslSource);
    }

    /// <summary>The example of the README's "Half precision" section, as it is printed there.</summary>
    [Fact]
    public void HalfVectors_TheReadmeExampleTranslates()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(
            """
            using System;
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Tone(ReadWriteBuffer<Half4> pixels, Half3 gain) : IComputeShader
            {
                private static readonly Half3 Luma = (Half3)new Float3(0.299f, 0.587f, 0.114f);

                public void Execute()
                {
                    Half4 pixel = pixels[ThreadIds.X];
                    Half3 color = pixel.XYZ * gain;
                    Half level = Gpu.Dot(color, Luma);

                    pixels[ThreadIds.X] = new Half4(Gpu.Select(color > (Half)1f, new Half3(level), color), pixel.W);
                }
            }
            """).Verify("Tone");

        Assert.Equal(
            """
            half4 pixel = pixels[int(__tid.x)];
            half3 color = pixel.xyz * __c.gain;
            half level = metal::dot(color, ::Luma);
            pixels[int(__tid.x)] = half4(metal::select(color, half3(level), color > half(1.0f)), pixel.w);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void HalfConstants_AreWrittenAsCasts()
    {
        // C# has no half literal. A float goes through half(...), which rounds as (Half) does, and the
        // few integer constants that convert implicitly (those that fit only one of byte and sbyte)
        // are written out as integers instead of going through 'byte', which has no MSL type.
        AssertKernel(
            """
            half a = half(0.1f);
            half b = half(3);
            half c = a * half(-1);
            half2 v = half2(a, b) * half(255) + half(-128);
            buffer[0] = float(v.x) + float(c);
            """,
            """
            Half a = (Half)0.1f;
            Half b = (Half)3;
            Half c = a * -1;
            Half2 v = new Half2(a, b) * 255 + -128;
            buffer[0] = (float)v.X + (float)c;
            """);
    }

    // ---- half vectors: every position --------------------------------------------------------------

    private const string HalfEverywhere = """
        using System;
        using SharpCompute;

        namespace Shading;

        public struct Texel
        {
            public Half3 Color;
            public float Weight;
            public Half2 Uv;
            public Half Bias;
            public Half4 Tint;
        }

        public struct Halves
        {
            public Half3 A;
            public Half4 B;
        }

        [GeneratedComputeShaderDescriptor]
        [ThreadGroupSize(16)]
        public readonly partial struct Tone : IComputeShader
        {
            public readonly ReadWriteBuffer<Half3> colors;
            public readonly ReadOnlyBuffer<Half4> weights;
            public readonly ReadWriteBuffer<Texel> texels;
            public readonly ReadWriteBuffer<Halves> halves;
            public readonly Half2 scale;
            public readonly float gain;
            public readonly Half3 lift;
            public readonly Half exposure;
            public readonly Half4 tint;

            private static readonly Half3 Luma = (Half3)new Float3(0.299f, 0.587f, 0.114f);
            private static readonly Half Knee = (Half)0.75f;

            [GroupShared]
            private static readonly Half4[] tile = new Half4[16];

            private static Half3 Curve(Half3 color, Half knee) => Gpu.Min(color, knee) + Gpu.Max(color - knee, (Half)0f) * (Half)0.5f;

            private Half Luminance(Half3 color) => Gpu.Dot(color, Luma) * exposure;

            public void Execute()
            {
                int i = ThreadIds.X;
                Half3 color = colors[i] * scale.X + lift;
                Half4 weight = weights[i];
                tile[GroupIds.X] = new Half4(color, Luminance(color)) * tint;
                Gpu.GroupMemoryBarrierWithGroupSync();
                Half4 shared = tile[(GroupIds.X + 1) & 15];
                color = Curve(color * weight.XYZ + shared.XYZ, Knee) * (Half)gain;
                colors[i] = color;
                colors[i].X = shared.W;
                Texel texel = texels[i];
                texel.Color = color;
                texel.Uv += scale;
                texel.Bias = color.Z;
                texel.Tint *= weight;
                texels[i] = texel;
                halves[i].A = texels[i].Color + halves[i].B.XYZ;
            }
        }
        """;

    [Fact]
    public void HalfVectors_InEveryPosition_UsePackedTypesAtTheBoundary()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(HalfEverywhere).Verify("Shading.Tone");

        Assert.Equal(
            Normalize("""
            #include <metal_stdlib>

            using namespace metal;

            constant half3 Luma = half3(float3(0.299f, 0.587f, 0.114f));
            constant half Knee = half(0.75f);

            static_assert(sizeof(packed_half3) == 6, "SharpCompute: size of 'packed_half3' does not match its C# layout");
            static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");

            struct Texel
            {
                packed_half3 Color;
                float Weight;
                packed_half2 Uv;
                half Bias;
                packed_half4 Tint;
            };
            static_assert(sizeof(Texel) == 28, "SharpCompute: size of 'Texel' does not match its C# layout");
            static_assert(__builtin_offsetof(Texel, Color) == 0, "SharpCompute: offset of 'Texel.Color' does not match its C# layout");
            static_assert(__builtin_offsetof(Texel, Weight) == 8, "SharpCompute: offset of 'Texel.Weight' does not match its C# layout");
            static_assert(__builtin_offsetof(Texel, Uv) == 12, "SharpCompute: offset of 'Texel.Uv' does not match its C# layout");
            static_assert(__builtin_offsetof(Texel, Bias) == 16, "SharpCompute: offset of 'Texel.Bias' does not match its C# layout");
            static_assert(__builtin_offsetof(Texel, Tint) == 18, "SharpCompute: offset of 'Texel.Tint' does not match its C# layout");

            struct Halves
            {
                packed_half3 A;
                packed_half4 B;
            };
            static_assert(sizeof(Halves) == 14, "SharpCompute: size of 'Halves' does not match its C# layout");
            static_assert(__builtin_offsetof(Halves, A) == 0, "SharpCompute: offset of 'Halves.A' does not match its C# layout");
            static_assert(__builtin_offsetof(Halves, B) == 6, "SharpCompute: offset of 'Halves.B' does not match its C# layout");

            struct __Constants
            {
                packed_half2 scale;
                float gain;
                packed_half3 lift;
                half exposure;
                packed_half4 tint;
            };
            static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, scale) == 0, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, gain) == 4, "SharpCompute: offset of '__Constants.gain' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, lift) == 8, "SharpCompute: offset of '__Constants.lift' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, exposure) == 14, "SharpCompute: offset of '__Constants.exposure' does not match its C# layout");
            static_assert(__builtin_offsetof(__Constants, tint) == 16, "SharpCompute: offset of '__Constants.tint' does not match its C# layout");
            """),
            Normalize(descriptor.MslSource[..descriptor.MslSource.IndexOf("\nstruct __GridWindow", StringComparison.Ordinal)]));

        Assert.Equal(
            Normalize("""
            half3 Curve(half3 color, half knee)
            {
                return metal::min(color, knee) + metal::max(color - knee, half(0.0f)) * half(0.5f);
            }
            """),
            Normalize(descriptor.MslSource[descriptor.MslSource.IndexOf("half3 Curve(half3 color, half knee)\n{", StringComparison.Ordinal)..].Split("\n\n")[0]));
        Assert.Contains("half Luminance(half3 color, constant __Constants& __c)", descriptor.MslSource);
        Assert.Contains("return metal::dot(color, ::Luma) * __c.exposure;", descriptor.MslSource);
        Assert.Contains("device packed_half3* colors [[buffer(0)]]", descriptor.MslSource);
        Assert.Contains("const device packed_half4* weights [[buffer(1)]]", descriptor.MslSource);
        Assert.Contains("threadgroup packed_half4 tile[16];", descriptor.MslSource);

        Assert.Equal(
            """
            threadgroup packed_half4 tile[16];
            int i = int(__tid.x);
            half3 color = colors[i] * __c.scale.x + __c.lift;
            half4 weight = weights[i];
            tile[int(__gtid.x)] = half4(color, ::Luminance(color, __c)) * __c.tint;
            metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
            half4 shared = tile[(int(__gtid.x) + 1) & 15];
            color = ::Curve(color * weight.xyz + shared.xyz, ::Knee) * half(__c.gain);
            colors[i] = color;
            colors[i].x = shared.w;
            Texel texel = texels[i];
            texel.Color = color;
            texel.Uv += __c.scale;
            texel.Bias = color.z;
            texel.Tint *= weight;
            texels[i] = texel;
            halves[i].A = texels[i].Color + halves[i].B.xyz;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        Assert.Equal(128, descriptor.ThreadgroupMemoryLength); // 16 elements of 8 bytes
    }

    /// <summary>
    /// The sizes and offsets the generator asserts are the ones the CLR uses: a half vector is aligned
    /// to 2 bytes, so it can start at any even offset, and a struct of halves alone has no tail padding.
    /// </summary>
    [Fact]
    public void HalfVectors_LayoutAssertsMatchTheClr()
    {
        GeneratorRun run = GeneratorHarness.Run(HalfEverywhere);
        ShaderDescriptor descriptor = run.Verify("Shading.Tone");

        descriptor.AssertLayoutMatchesClr("Texel", run.LoadType("Shading.Texel"));
        descriptor.AssertLayoutMatchesClr("Halves", run.LoadType("Shading.Halves"));
        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }

    [MetalFact]
    public void HalfVectors_DriftGuardFailsCompilationWhenTheLayoutDisagrees()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(HalfEverywhere).Verify("Shading.Tone");

        // An unpacked half3 is 8 bytes with 8-byte alignment. As the first member that moves nothing,
        // but the struct is padded to a multiple of 8; as a later member it moves, and so does the rest.
        MetalCompiler.Result member = MetalCompiler.Compile(descriptor.MslSource.Replace("packed_half3 Color;", "half3 Color;"), descriptor.EntryPoint);
        Assert.False(member.Success);
        Assert.Contains("SharpCompute: size of 'Texel' does not match its C# layout", member.Messages);

        MetalCompiler.Result later = MetalCompiler.Compile(descriptor.MslSource.Replace("packed_half4 B;", "half4 B;"), descriptor.EntryPoint);
        Assert.False(later.Success);
        Assert.Contains("SharpCompute: offset of 'Halves.B' does not match its C# layout", later.Messages);

        // The same for a buffer element: the stride of the C# array is 6 bytes.
        MetalCompiler.Result element = MetalCompiler.Compile(descriptor.MslSource.Replace("sizeof(packed_half3) == 6", "sizeof(half3) == 6"), descriptor.EntryPoint);
        Assert.False(element.Success);
        Assert.Contains("SharpCompute: size of 'packed_half3' does not match its C# layout", element.Messages);

        // And in the constants block, where the half vector after the float moves to a multiple of 8.
        MetalCompiler.Result constants = MetalCompiler.Compile(descriptor.MslSource.Replace("packed_half3 lift;", "half3 lift;"), descriptor.EntryPoint);
        Assert.False(constants.Success);
        Assert.Contains("SharpCompute: offset of '__Constants.exposure' does not match its C# layout", constants.Messages);
    }

    [Fact]
    public void HalfVectors_ConvertTexturePixels()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(
            """
            using System;
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Grade(ReadWriteTexture2D<Rgba16Float> image, Half4 gain) : IComputeShader
            {
                public void Execute()
                {
                    Half4 pixel = (Half4)image[ThreadIds.XY];
                    image[ThreadIds.XY] = (Float4)(pixel * gain);
                }
            }
            """).Verify("Grade");

        Assert.Contains("half4 pixel = half4(image.read(uint2(int2(__tid.xy))));", descriptor.KernelBody);
        Assert.Contains("image.write(float4(pixel * __c.gain), uint2(int2(__tid.xy)));", descriptor.KernelBody);
    }

    [Fact]
    public void HalfVectors_AreNotFoldedInProgramScopeInitializers()
    {
        // Constructors, casts and operators need no folding. A call does, and the folder only knows
        // 32-bit arithmetic: the call is reported instead of being evaluated with the wrong rounding.
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader("""
                public readonly ReadWriteBuffer<float> buffer;

                private static readonly Half3 Unit = Gpu.Normalize((Half3)new Float3(1f, 2f, 2f));

                public void Execute() => buffer[0] = (float)Unit.X;
            """));

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0024", "Gpu.Normalize((Half3)new Float3(1f, 2f, 2f))");
        Assert.Contains("the conversion '(Half3)new Float3(1f, 2f, 2f)' cannot be evaluated at compile time", diagnostic.GetMessage());
        Assert.Contains("cannot be evaluated at compile time", diagnostic.GetMessage());
    }

    // ---- bool vectors ------------------------------------------------------------------------------

    [Fact]
    public void Comparisons_OfEveryNumericVector_AreNativeAndReturnBoolVectors()
    {
        AssertKernel(
            """
            float3 f = float3(buffer[0], 1.0f, 2.0f);
            half3 h = half3(f);
            int3 i = int3(f);
            uint3 u = uint3(f);
            bool3 m = f < f * 2.0f;
            m = f <= 1.0f;
            m = 0.5f > f;
            m = f >= __c.threshold;
            m = h < h + half(1);
            m = half(0.5f) <= h;
            m = i > i - 1;
            m = i >= 2;
            m = u < 3u;
            m = 1u <= u;
            bool2 n = f.xy > f.yx;
            bool4 o = float4(f, 1.0f) < 2.0f;
            buffer[0] = (m.x && n.y) || o.w ? 1.0f : 0.0f;
            """,
            """
            Float3 f = new Float3(buffer[0], 1f, 2f);
            Half3 h = (Half3)f;
            Int3 i = (Int3)f;
            UInt3 u = (UInt3)f;
            Bool3 m = f < f * 2f;
            m = f <= 1f;
            m = 0.5f > f;
            m = f >= threshold;
            m = h < h + (Half)1;
            m = (Half)0.5f <= h;
            m = i > i - 1;
            m = i >= 2;
            m = u < 3u;
            m = 1u <= u;
            Bool2 n = f.XY > f.YX;
            Bool4 o = new Float4(f, 1f) < 2f;
            buffer[0] = m.X && n.Y || o.W ? 1f : 0f;
            """,
            "public readonly Float3 threshold;");
    }

    [Fact]
    public void EqualAndNotEqual_BecomeTheMslOperators()
    {
        // [MslBinaryOperator]: parenthesized, with vector operands converted to their native type, so
        // that a packed operand (a buffer element, a captured field) compares componentwise too.
        AssertKernel(
            """
            float3 f = float3(buffer[0], 1.0f, 2.0f);
            int2 i = int2(f.xy);
            bool3 e = (float3(f) == float3(__c.threshold));
            bool3 d = (float3(f) != 1.0f);
            bool2 k = (int2(i) == int2(vectors[0]));
            bool2 l = (int2(i) != 3);
            buffer[0] = metal::all(e) || metal::any(d) || k.x || l.y ? 1.0f : 0.0f;
            """,
            """
            Float3 f = new Float3(buffer[0], 1f, 2f);
            Int2 i = (Int2)f.XY;
            Bool3 e = Gpu.Equal(f, threshold);
            Bool3 d = Gpu.NotEqual(f, 1f);
            Bool2 k = Gpu.Equal(i, vectors[0]);
            Bool2 l = Gpu.NotEqual(i, 3);
            buffer[0] = Gpu.All(e) || Gpu.Any(d) || k.X || l.Y ? 1f : 0f;
            """,
            "public readonly Float3 threshold; public readonly ReadOnlyBuffer<Int2> vectors;");
    }

    [Fact]
    public void BoolVectors_LogicalOperatorsConstructorsAndSwizzles()
    {
        AssertKernel(
            """
            float3 f = float3(buffer[0], 1.0f, 2.0f);
            bool3 a = f > 1.0f;
            bool3 b = bool3(true, false, a.z);
            bool3 c = bool3(a.xy, false);
            bool3 d = bool3(false);
            bool3 z = bool3(false);
            bool2 y = bool2(false);
            d = (a & b) | c;
            d = (a | b) & !c;
            d = a ^ b ^ !d;
            d &= a;
            d |= b;
            d ^= c;
            bool2 s = d.zx;
            d.xy = s;
            d.z = s.x || y.y;
            if (metal::all(d | z))
            {
                buffer[0] = 1.0f;
            }
            buffer[1] = metal::any(!d) ? 2.0f : 3.0f;
            """,
            """
            Float3 f = new Float3(buffer[0], 1f, 2f);
            Bool3 a = f > 1f;
            Bool3 b = new Bool3(true, false, a.Z);
            Bool3 c = new Bool3(a.XY, false);
            Bool3 d = new Bool3(false);
            Bool3 z = default;
            Bool2 y = new Bool2();
            d = a & b | c;
            d = (a | b) & !c;
            d = a ^ b ^ !d;
            d &= a;
            d |= b;
            d ^= c;
            Bool2 s = d.ZX;
            d.XY = s;
            d.Z = s.X || y.Y;
            if (Gpu.All(d | z))
            {
                buffer[0] = 1f;
            }
            buffer[1] = Gpu.Any(!d) ? 2f : 3f;
            """);
    }

    [Fact]
    public void Select_ReversesItsArgumentsForMetal()
    {
        // HLSL's order in C#: (mask, whenTrue, whenFalse). MSL's select is (whenFalse, whenTrue, mask).
        AssertKernel(
            """
            float3 color = float3(buffer[0], buffer[1], buffer[2]);
            float3 lit = metal::select(color * 0.5f, color * 2.0f, color > 0.5f);
            bool3 bright = lit > 1.0f;
            int3 i = metal::select(int3(0), int3(1), bright);
            uint3 u = metal::select(uint3(i), uint3(7u), bright);
            half3 h = metal::select(half3(color), half3(lit), bright & (color < 0.9f));
            bool3 either = metal::select(!bright, bright, color > lit);
            lit = metal::select(lit, color, either);
            buffer[0] = lit.x + float(i.y) + float(u.z) + float(h.x);
            """,
            """
            Float3 color = new Float3(buffer[0], buffer[1], buffer[2]);
            Float3 lit = Gpu.Select(color > 0.5f, color * 2f, color * 0.5f);
            Bool3 bright = lit > 1f;
            Int3 i = Gpu.Select(bright, new Int3(1), new Int3(0));
            UInt3 u = Gpu.Select(bright, new UInt3(7u), (UInt3)i);
            Half3 h = Gpu.Select(bright & color < 0.9f, (Half3)lit, (Half3)color);
            Bool3 either = Gpu.Select(color > lit, bright, !bright);
            lit = Gpu.Select(whenFalse: lit, mask: either, whenTrue: color);
            buffer[0] = lit.X + i.Y + u.Z + (float)h.X;
            """);
    }

    [Fact]
    public void Classification_OfVectorsReturnsBoolVectors()
    {
        AssertKernel(
            """
            float4 f = float4(buffer[0], buffer[1], 1.0f, 2.0f);
            half4 h = half4(f);
            bool4 bad = metal::isnan(f) | metal::isinf(f) | !metal::isfinite(h);
            bool2 nan = (metal::isnan(h.xy) & metal::isinf(h.zw)) | metal::isfinite(f.xy);
            f = metal::select(f, float4(0.0f), bad);
            buffer[0] = f.x + (nan.x ? 1.0f : 0.0f);
            """,
            """
            Float4 f = new Float4(buffer[0], buffer[1], 1f, 2f);
            Half4 h = (Half4)f;
            Bool4 bad = Gpu.IsNaN(f) | Gpu.IsInfinity(f) | !Gpu.IsFinite(h);
            Bool2 nan = Gpu.IsNaN(h.XY) & Gpu.IsInfinity(h.ZW) | Gpu.IsFinite(f.XY);
            f = Gpu.Select(bad, new Float4(0f), f);
            buffer[0] = f.X + (nan.X ? 1f : 0f);
            """);
    }

    [Fact]
    public void BoolVectors_AsParametersReturnValuesAndProgramScopeConstants()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader("""
                public readonly ReadWriteBuffer<Float3> colors;
                public readonly Float3 threshold;

                private static readonly Bool3 Channels = new Bool3(true, false, true);
                private static readonly Bool3 Warm = new Float3(1f, 0.5f, 0.25f) > 0.4f;
                private static readonly Bool2 Both = Gpu.Equal(new Int2(1, 2), new Int2(1, 3));

                private static Bool3 Outside(Float3 value, Float3 low, Float3 high) => value < low | value > high;

                private Bool3 Mask(Bool3 mask, out Bool3 inverse)
                {
                    inverse = !mask;
                    return mask & Channels & (colors[0] >= threshold);
                }

                public void Execute()
                {
                    Float3 color = colors[ThreadIds.X];
                    Bool3 outside = Outside(color, new Float3(0f), threshold);
                    Bool3 kept = Mask(outside | Warm, out Bool3 inverse);
                    colors[ThreadIds.X] = Gpu.Select(kept ^ inverse, color, threshold) * (Both.X ? 1f : 2f);
                }
            """)).Verify(Shader);

        Assert.Contains("constant bool3 Channels = bool3(true, false, true);", descriptor.MslSource);
        Assert.Contains("constant bool3 Warm = float3(1.0f, 0.5f, 0.25f) > 0.4f;", descriptor.MslSource);
        Assert.Contains("constant bool2 Both = (int2(int2(1, 2)) == int2(int2(1, 3)));", descriptor.MslSource);
        Assert.Contains("static bool3 Outside(float3 value, float3 low, float3 high)\n{", descriptor.MslSource);
        Assert.Contains("return (value < low) | (value > high);", descriptor.MslSource);
        Assert.Contains("static bool3 Mask(bool3 mask, thread bool3& inverse, device packed_float3* colors, constant __Constants& __c)\n{", descriptor.MslSource);
        Assert.Contains("inverse = !mask;", descriptor.MslSource);
        Assert.Contains("return mask & ::Channels & (colors[0] >= __c.threshold);", descriptor.MslSource);

        Assert.Equal(
            """
            float3 color = colors[int(__tid.x)];
            bool3 outside = ::Outside(color, float3(0.0f), __c.threshold);
            bool3 inverse;
            bool3 kept = ::Mask(outside | ::Warm, inverse, colors, __c);
            colors[int(__tid.x)] = metal::select(__c.threshold, color, kept ^ inverse) * (::Both.x ? 1.0f : 2.0f);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    /// <summary>Comparisons read packed storage directly: buffer elements, struct fields and captured vectors.</summary>
    [Fact]
    public void Comparisons_OfPackedStorage_CompileWithMetal()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader("""
                public readonly ReadWriteBuffer<Float3> floats;
                public readonly ReadWriteBuffer<Half4> halves;
                public readonly ReadWriteBuffer<Int2> ints;
                public readonly ReadWriteBuffer<UInt3> uints;
                public readonly ReadWriteBuffer<Pair> pairs;
                public readonly Float3 limit;
                public readonly Half4 halfLimit;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    Bool3 a = floats[i] < floats[i + 1] | floats[i] >= limit | limit > 1f;
                    Bool4 b = halves[i] <= halves[i + 1] & halves[i] > halfLimit;
                    Bool2 c = ints[i] > ints[i + 1] ^ ints[i] <= 3;
                    Bool3 d = uints[i] < uints[i + 1] | 2u >= uints[i];
                    Bool3 e = pairs[i].Left < pairs[i].Right & Gpu.Equal(pairs[i].Left, floats[i]);
                    floats[i] = Gpu.Select(a | d | e, floats[i], limit);
                    halves[i] = Gpu.Select(b, halves[i], halfLimit);
                    ints[i] = Gpu.Select(c, ints[i], ints[i + 1]);
                    pairs[i].Left = Gpu.Select(Gpu.NotEqual(pairs[i].Right, 0f), pairs[i].Right, pairs[i].Left);
                }
            """) + "public struct Pair { public Float3 Left; public Float3 Right; }").Verify(Shader);

        Assert.Equal(
            """
            int i = int(__tid.x);
            bool3 a = (floats[i] < floats[i + 1]) | (floats[i] >= __c.limit) | (__c.limit > 1.0f);
            bool4 b = (halves[i] <= halves[i + 1]) & (halves[i] > __c.halfLimit);
            bool2 c = (ints[i] > ints[i + 1]) ^ (ints[i] <= 3);
            bool3 d = (uints[i] < uints[i + 1]) | (2u >= uints[i]);
            bool3 e = (pairs[i].Left < pairs[i].Right) & (float3(pairs[i].Left) == float3(floats[i]));
            floats[i] = metal::select(__c.limit, floats[i], a | d | e);
            halves[i] = metal::select(__c.halfLimit, halves[i], b);
            ints[i] = metal::select(ints[i + 1], ints[i], c);
            pairs[i].Left = metal::select(pairs[i].Left, pairs[i].Right, (float3(pairs[i].Right) != 0.0f));
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    // ---- bool vectors: diagnostics -----------------------------------------------------------------

    private const string BoolReason = "'bool' has no stable layout across the CPU/GPU boundary; use an 'int' or 'uint' instead";

    /// <summary>A bool vector has no packed form: like <c>bool</c>, it cannot be captured or stored in a buffer.</summary>
    [Theory]
    [InlineData("Bool2", "Field 'state' of type 'Bool2'")]
    [InlineData("Bool3", "Field 'state' of type 'Bool3'")]
    [InlineData("Bool4", "Field 'state' of type 'Bool4'")]
    [InlineData("ReadWriteBuffer<Bool3>", "its element type 'Bool3' is not supported")]
    [InlineData("ReadOnlyBuffer<Bool2>", "its element type 'Bool2' is not supported")]
    public void BoolVectors_CannotCrossTheBoundary_AsCapturedState(string type, string message)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader($$"""
                public readonly ReadWriteBuffer<float> buffer;
                public readonly {{type}} state;

                public void Execute() => buffer[0] = 1;
            """));

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0006", type);

        Assert.Contains(message, diagnostic.GetMessage());
        Assert.Contains(BoolReason, diagnostic.GetMessage());
    }

    [Fact]
    public void BoolVectors_CannotCrossTheBoundary_AsStructFields()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader("""
                public readonly ReadWriteBuffer<Flagged> buffer;

                public void Execute() => buffer[0].Value = 1;
            """) + "public struct Flagged { public float Value; public Bool3 Flags; }");

        // Reported where the struct is captured (and again where its element is used).
        Diagnostic diagnostic = run.Diagnostics.First(static d => d.Id == "SCG0009");

        Assert.Equal("ReadWriteBuffer<Flagged>", run.TextAt(diagnostic));
        Assert.Contains("its field 'Flags' has the unsupported type 'Bool3'", diagnostic.GetMessage());
        Assert.Contains(BoolReason, diagnostic.GetMessage());
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "SCG0006");
    }

    [Fact]
    public void BoolVectors_CannotCrossTheBoundary_AsGroupSharedElements()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared]
                private static readonly Bool4[] flags = new Bool4[64];

                public void Execute() => buffer[0] = flags.Length;
            """,
            "[ThreadGroupSize(64)]"));

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0042", "Bool4[]");

        Assert.Contains("'Bool4'", diagnostic.GetMessage());
        Assert.Contains(BoolReason, diagnostic.GetMessage());
    }

    [Fact]
    public void BoolVectors_AreNotFoldedInProgramScopeInitializers()
    {
        // Comparisons and Gpu.Equal are operators in MSL and need no folding (see the test above); a
        // function call would, and MSL allows none in a program-scope initializer.
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader("""
                public readonly ReadWriteBuffer<float> buffer;

                private static readonly Float3 Picked = Gpu.Select(new Float3(1f, 2f, 3f) > 2f, new Float3(1f), new Float3(0f));

                public void Execute() => buffer[0] = Picked.X;
            """));

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0024", "Gpu.Select(new Float3(1f, 2f, 3f) > 2f, new Float3(1f), new Float3(0f))");
        Assert.Contains("'bool3'", diagnostic.GetMessage());
        Assert.Contains("cannot be evaluated at compile time", diagnostic.GetMessage());
    }

    /// <summary>
    /// A comparison of vectors that C# resolved to one <c>bool</c> (a user-defined <c>==</c>) is still
    /// rejected: MSL would compute a bool vector there. SharpCompute's own vectors have no such operator.
    /// </summary>
    [Fact]
    public void VectorEquality_ThatReturnsBool_IsStillRejected()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Body("""
                Pair a = new Pair(1f, 2f);
                buffer[0] = a == a ? 1f : 0f;
                """) + """
                [MslType("float2", Packed = "packed_float2")]
                public struct Pair
                {
                    public float X;
                    public float Y;
                    public Pair(float x, float y) { X = x; Y = y; }
                    public static bool operator ==(Pair a, Pair b) => a.X == b.X && a.Y == b.Y;
                    public static bool operator !=(Pair a, Pair b) => !(a == b);
                    public override bool Equals(object? other) => other is Pair p && this == p;
                    public override int GetHashCode() => 0;
                }
                """);

        Diagnostic diagnostic = Assert.Single(run.Diagnostics);
        Assert.Contains("a '==' comparison of vectors that returns 'bool'", diagnostic.GetMessage());
    }
}
