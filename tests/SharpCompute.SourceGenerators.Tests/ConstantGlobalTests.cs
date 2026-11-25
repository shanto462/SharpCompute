using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// <c>static readonly</c> and <c>const</c> fields lowered to MSL program-scope constants, the constant
/// folding of intrinsic calls in their initializers (SCG0024 otherwise), and the MSL prelude.
/// </summary>
public class ConstantGlobalTests
{
    private const string Shader = "Tests.TestShader";

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    private static GeneratorRun RunBody(string body, string extraMembers = "", string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(body, extraMembers) + trailer);

    [Fact]
    public void StaticReadonlyAndConstFields_BecomeProgramScopeConstants()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Float3 tint = Doubled + Base;
            int count = Count;
            buffer[0] = tint.X + Gpu.Mul(new Float2(1, 0), Rotation).Y + count + Count;   // a converted const folds to a literal
            {
                float Base = Scale;   // a local that shadows the field: `::` still names the global
                buffer[1] = Base + TestShader.Base.Z;
            }
            """,
            """
            private const float Scale = 0.5f;
            private const int Count = 3;
            private static readonly Float3 Base = new(1, 2, 3);
            private static readonly Float3 Doubled = (Base * 2) - new Float3(Scale);
            private static readonly Float2x2 Rotation = new Float2x2(0, 1, -1, 0) * Scale;
            private static readonly Float3 Unused = new(9);
            """).Verify(Shader);

        // Emitted in dependency order, only when used, right after `using namespace metal;`.
        Assert.Contains(
            Normalize("""
            using namespace metal;

            constant float3 Base = float3(1.0f, 2.0f, 3.0f);
            constant float Scale = 0.5f;
            constant float3 Doubled = (::Base * 2.0f) - float3(::Scale);
            constant int Count = 3;
            constant float2x2 Rotation = float2x2(0.0f, 1.0f, -1.0f, 0.0f) * ::Scale;

            struct __Constants
            """),
            descriptor.MslSource);
        Assert.DoesNotContain("Unused", descriptor.MslSource);
        Assert.Equal(
            Normalize("""
            float3 tint = ::Doubled + ::Base;
            int count = ::Count;
            buffer[0] = tint.x + (::Rotation * float2(float2(1.0f, 0.0f))).y + float(count) + 3.0f;
            {
                float Base = ::Scale;
                buffer[1] = Base + ::Base.z;
            }
            """),
            descriptor.KernelBody);
    }

    [Fact]
    public void FieldsOfAContainingType_AreConstantsToo()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run("""
            using SharpCompute;

            public static partial class Effects
            {
                private static readonly Float4 Tint = new(1, 0.5f, 0.25f, 1);

                [GeneratedComputeShaderDescriptor]
                public readonly partial struct Fill : IComputeShader<Float4>
                {
                    public Float4 Execute() => Tint;
                }
            }
            """).Verify("Effects+Fill");

        Assert.Contains("constant float4 Tint = float4(1.0f, 0.5f, 0.25f, 1.0f);", descriptor.MslSource);
        Assert.Equal("__output.write(::Tint, __tid.xy);\nreturn;", descriptor.KernelBody);
    }

    [Fact]
    public void IntrinsicCalls_AreFolded_SoNoCallReachesAnInitializer()
    {
        ShaderDescriptor descriptor = RunBody(
            "buffer[0] = Direction.X + Length + Angle + Mixed.Y + Product.Z + Remainder + Rooted;",
            """
            private static readonly Float3 Direction = Gpu.Normalize(new Float3(1, 2, 3));
            private static readonly float Length = Gpu.Length(Direction * 4) + Gpu.Dot(new Float2(1, 2), new Float2(3, 4));
            private static readonly float Angle = Gpu.Radians(180f);
            private static readonly Float3 Mixed = Gpu.Lerp(new Float3(0), Gpu.Cross(new Float3(1, 0, 0), new Float3(0, 1, 0)), 0.25f) * 2;
            private static readonly Float3 Product = Gpu.Mul(new Float3(1, 2, 3), new Float3x3(1, 0, 0, 0, 2, 0, 0, 0, 3) * 2);
            private static readonly float Remainder = 5.5f % 2f;
            private static readonly float Rooted = MathF.Sqrt(2) + Gpu.Smoothstep(0, 1, Gpu.Fract(1.25f));
            """).Verify(Shader);

        // Function calls are evaluated, operators and constructors stay as written.
        Assert.DoesNotContain("metal::", descriptor.MslSource.Split("struct __Constants")[0]);
        Assert.DoesNotContain("__ms_", descriptor.MslSource);
        Assert.Contains("constant float Remainder = 1.5f;", descriptor.MslSource);
        Assert.Contains("constant float3 Mixed = float3(0.0f, 0.0f, 0.25f) * 2.0f;", descriptor.MslSource);
        Assert.Contains("constant float3 Product = (float3x3(1.0f, 0.0f, 0.0f, 0.0f, 2.0f, 0.0f, 0.0f, 0.0f, 3.0f) * 2.0f * float3(float3(1.0f, 2.0f, 3.0f)));", descriptor.MslSource);

        // The folded values are the ones SharpCompute's CPU implementations compute.
        AssertFolded(descriptor, "Direction", Gpu.Normalize(new Float3(1, 2, 3)));
        AssertFolded(descriptor, "Angle", Gpu.Radians(180f));
        Assert.Contains($"constant float Length = {Literal(Gpu.Length(Gpu.Normalize(new Float3(1, 2, 3)) * 4))} + 11.0f;", descriptor.MslSource);
        Assert.Contains($"constant float Rooted = {Literal(MathF.Sqrt(2))} + {Literal(Gpu.Smoothstep(0, 1, Gpu.Fract(1.25f)))};", descriptor.MslSource);
    }

    [MetalFact]
    public void FoldedConstants_HaveTheCpuValuesOnTheGpu()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Constants(ReadWriteBuffer<float> output) : IComputeShader
            {
                private static readonly Float3 Direction = Gpu.Normalize(new Float3(1, 2, 3));
                private static readonly Float3 Reflected = Gpu.Reflect(Direction, new Float3(0, 1, 0)) * 3;
                private static readonly Float2 Product = Gpu.Mul(new Float2(1, 2), new Float2x2(1, 2, 3, 4));
                private static readonly float Mixed = Gpu.Lerp(1.5f, 4f, 0.3f) + Gpu.Clamp(Gpu.Abs(-7f), 0, 5);

                public void Execute()
                {
                    output[0] = Direction.X; output[1] = Direction.Y; output[2] = Direction.Z;
                    output[3] = Reflected.X; output[4] = Reflected.Y; output[5] = Reflected.Z;
                    output[6] = Product.X; output[7] = Product.Y;
                    output[8] = Mixed;
                }
            }
            """);

        float[] gpu = GpuDispatch.For(run.AssertClean(), "Constants", 1, 9);

        Float3 direction = Gpu.Normalize(new Float3(1, 2, 3));
        Float3 reflected = Gpu.Reflect(direction, new Float3(0, 1, 0)) * 3;
        Float2 product = Gpu.Mul(new Float2(1, 2), new Float2x2(1, 2, 3, 4));
        float mixed = Gpu.Lerp(1.5f, 4f, 0.3f) + Gpu.Clamp(Gpu.Abs(-7f), 0, 5);

        Assert.Equal([direction.X, direction.Y, direction.Z, reflected.X, reflected.Y, reflected.Z, product.X, product.Y, mixed], gpu);
    }

    // ---- SCG0024 ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("private static readonly float A = Helper(1); private static float Helper(float x) => x;", "Helper(1)", "it calls 'Helper', which is not an [MslIntrinsic] function; MSL does not allow function calls in program-scope initializers")]
    [InlineData("private static readonly float A = Noise.Of(1) * 2;", "Noise.Of(1)", "the MSL function 'metal::noise' cannot be evaluated at compile time; MSL does not allow function calls in program-scope initializers")]
    [InlineData("private static readonly float A = ThreadIds.X;", "ThreadIds.X", "'ThreadIds.X' is only known while the shader runs")]
    [InlineData("private static readonly float A = ThreadIds.Normalized.X;", "ThreadIds.Normalized.X", "'ThreadIds.Normalized.X' is only known while the shader runs")]
    [InlineData("private static readonly float A;", "A", "it has no initializer (static constructors are not translated)")]
    [InlineData("private static readonly float A = P.Left; private static readonly Pair P = default;", "P", "its type 'Pair' is not a scalar, vector, or matrix type")]
    [InlineData("private static readonly float A = B; private static readonly float B = A + 1;", "A", "its initializer depends on itself")]
    [InlineData("private static readonly float A = Gpu.Sqrt(B); private static readonly float B = A;", "Gpu.Sqrt(B)", "the initializer of 'B' depends on itself")]
    [InlineData("private static readonly int A = Gpu.Abs((int)Gpu.Exp(100f));", "Gpu.Abs((int)Gpu.Exp(100f))", "the out-of-range conversion '(int)Gpu.Exp(100f)' cannot be evaluated at compile time")]
    public void MSG0024_InitializersThatCannotBeEvaluated(string members, string location, string reason)
    {
        GeneratorRun run = RunBody(
            "buffer[0] = A;",
            members,
            """

            public struct Pair { public float Left; public float Right; }

            public static class Noise
            {
                [MslIntrinsic("metal::noise")]
                public static float Of(float x) => x;
            }
            """);

        Diagnostic diagnostic = run.Diagnostics.First(static d => d.Id == "SCG0024");

        Assert.Equal(location, run.TextAt(diagnostic));
        Assert.EndsWith(reason, diagnostic.GetMessage());
        Assert.StartsWith("Static field 'TestShader.", diagnostic.GetMessage());
        Assert.All(run.Diagnostics, static d => Assert.Equal("SCG0024", d.Id));
    }

    // ---- SCG0026 ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("static TestShader() { K = 5f; }", "K")]
    [InlineData("static TestShader() { K *= 5f; }", "K")]
    [InlineData("static TestShader() { K++; }", "K")]
    [InlineData("static TestShader() { (Other, TestShader.K) = (2f, 5f); }", "(Other, TestShader.K)")]
    [InlineData("static TestShader() { Set(out K); } private static void Set(out float value) => value = 5f;", "K")]
    public void MSG0026_StaticFieldReassignedInStaticConstructor(string constructor, string location)
    {
        GeneratorRun run = RunBody(
            "buffer[0] = K;",
            "private static readonly float K = 1f; private static readonly float Other = 1f; " + constructor);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0026", location);

        Assert.StartsWith("Static field 'TestShader.K' is assigned in a static constructor", diagnostic.GetMessage());
    }

    [Fact]
    public void MSG0026_IsNotReportedForFieldsTheStaticConstructorOnlyReadsOrDoesNotTouch()
    {
        ShaderDescriptor descriptor = RunBody(
            "buffer[0] = K;",
            "private static readonly float K = 1f; private static float Other; static TestShader() { Other = K * 2f; }").Verify(Shader);

        Assert.Contains("constant float K = 1.0f;", descriptor.MslSource);
    }

    // ---- prelude ----------------------------------------------------------------------------------

    [Fact]
    public void Prelude_IsEmittedAfterTheUsingDirective_OnlyWhenAPreludeHelperIsUsed()
    {
        ShaderDescriptor with = RunBody(
            "buffer[0] = Turn(buffer[1]);",
            "private static float Turn(float degrees) => Gpu.Radians(degrees) + Gpu.Degrees(new Float2(1, 2)).Y;").Verify(Shader);

        Assert.StartsWith(
            "#include <metal_stdlib>\n\nusing namespace metal;\n\n" + Normalize(MslPrelude.Source) + "\n\nstruct __Constants",
            with.MslSource);
        Assert.Contains("return __ms_radians(degrees) + __ms_degrees(float2(1.0f, 2.0f)).y;", with.MslSource);

        ShaderDescriptor without = RunBody("buffer[0] = Gpu.Sin(buffer[1]);").Verify(Shader);

        Assert.DoesNotContain("SharpCompute prelude", without.MslSource);
        Assert.DoesNotContain("__ms_radians", without.MslSource);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static string Literal(float value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return (text.Contains('.') || text.Contains('E') ? text : text + ".0") + "f";
    }

    private static void AssertFolded(ShaderDescriptor descriptor, string name, float expected) =>
        Assert.Contains($"constant float {name} = {Literal(expected)};", descriptor.MslSource);

    private static void AssertFolded(ShaderDescriptor descriptor, string name, Float3 expected)
    {
        Match match = Regex.Match(descriptor.MslSource, $@"constant float3 {name} = float3\(([^)]*)\);");

        Assert.True(match.Success, descriptor.MslSource);
        float[] actual = match.Groups[1].Value.Split(", ").Select(static c => float.Parse(c.TrimEnd('f'), CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal([expected.X, expected.Y, expected.Z], actual);
    }
}
