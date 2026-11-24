using System;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Functions besides <c>Execute</c>: local functions (hoisted), instance helpers (captured state
/// forwarded), <c>ref</c>/<c>out</c> parameters, and <c>[MslBinaryOperator]</c> calls such as <c>Gpu.Mul</c>.
/// </summary>
public class HelperTests
{
    private const string Shader = "Tests.TestShader";

    private static GeneratorRun RunBody(string body, string extraMembers = "", string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(body, extraMembers) + trailer);

    // ---- local functions --------------------------------------------------------------------------

    [Fact]
    public void LocalFunctions_AreHoistedWithMangledNames_AndForwardDeclared()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            float twice = Twice(buffer[0]);   // called before its declaration
            buffer[1] = twice + Helper(2) + Square(3);

            static float Twice(float x) => x * 2;

            float Square(float x) { return x * x; }   // not static, but captures nothing
            """,
            """
            private static float Helper(float x)
            {
                static float Twice(float y)
                {
                    return Inner(y) * 2;

                    static float Inner(float z) => z + 1;
                }

                return Twice(x);
            }
            """).Verify(Shader);

        Assert.Equal("float twice = ::Execute_Twice(buffer[0]);\nbuffer[1] = twice + ::Helper(2.0f) + ::Execute_Square(3.0f);", descriptor.KernelBody);
        Assert.Contains(
            """
            static float Execute_Twice(float x);
            static float Helper_Twice_Inner(float z);
            static float Helper_Twice(float y);
            static float Helper(float x);
            static float Execute_Square(float x);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        Assert.Contains("static float Helper_Twice(float y)\n{\n    return ::Helper_Twice_Inner(y) * 2.0f;\n}", descriptor.MslSource);
    }

    [Fact]
    public void LocalFunctions_WithTheSameName_GetDistinctNames()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            buffer[0] = F(1) + A(2);

            static float F(float x) => x + 1;
            """,
            """
            private static float A(float x)
            {
                return F(x);

                static float F(float x) => x * 3;
            }

            private static float Execute_F(float x) => x;
            """).Verify(Shader);

        // The user's own Execute_F keeps its name only if it is allocated first; either way, all differ.
        Assert.Contains("static float Execute_F(float x)", descriptor.MslSource);
        Assert.Contains("static float A_F(float x)", descriptor.MslSource);
        Assert.DoesNotContain("Execute_F_1", descriptor.MslSource);
    }

    [Fact]
    public void MSG0025_LocalFunctionThatCapturesALocal()
    {
        GeneratorRun run = RunBody(
            """
            float offset = factor;
            buffer[0] = Shift(1) + Shift(2);

            float Shift(float x) => x + offset;
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0025", "Shift");

        Assert.Equal("Local function 'Shift' captures 'offset' from its enclosing method; pass it as a parameter instead (a local function becomes a separate MSL function)", diagnostic.GetMessage());
    }

    [Fact]
    public void MSG0025_LocalFunctionThatCapturesAParameter_EvenWhenUnused()
    {
        GeneratorRun run = RunBody(
            "buffer[0] = Helper(1);",
            """
            private static float Helper(float scale)
            {
                return 1;

                float Unused(float x) => x * scale;
            }
            """);

        Assert.Contains("captures 'scale'", run.AssertOnlyDiagnostic("SCG0025", "Unused").GetMessage());
    }

    [Fact]
    public void LocalFunctions_RecursionAndGenericsAreReported()
    {
        GeneratorRun recursive = RunBody(
            """
            buffer[0] = Fact(4);

            static int Fact(int n) => n <= 1 ? 1 : n * Fact(n - 1);
            """);

        Assert.Contains("(Fact -> Fact)", recursive.AssertOnlyDiagnostic("SCG0014", "Fact(n - 1)").GetMessage());

        GeneratorRun generic = RunBody(
            """
            buffer[0] = Id(4f);

            static T Id<T>(T value) => value;
            """);

        Assert.Contains("generic local functions are not supported", generic.AssertOnlyDiagnostic("SCG0016", "Id(4f)").GetMessage());
    }

    // ---- instance helpers -------------------------------------------------------------------------

    [Fact]
    public void InstanceHelpers_ReceiveTheCapturedStateTheyUse()
    {
        ShaderDescriptor descriptor = RunBody(
            "Store(ThreadIds.X, Scaled(1));",
            """
            private float Scaled(float value) => value * factor;

            private void Store(int i, float value)
            {
                if (i < buffer.Length)
                {
                    buffer[i] = Twice(value);
                }
            }

            private float Twice(float value) => Scaled(value) * 2 + ThreadIds.Y;
            """).Verify(Shader);

        Assert.Contains(
            """
            static float Scaled(float value, constant __Constants& __c);
            static float Twice(float value, constant __Constants& __c, uint3 __tid);
            static void Store(int i, float value, device float* buffer, constant __Constants& __c, uint3 __tid);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        Assert.Contains("return ::Scaled(value, __c) * 2.0f + float(int(__tid.y));", descriptor.MslSource);
        Assert.Equal("::Store(int(__tid.x), ::Scaled(1.0f, __c), buffer, __c, __tid);", descriptor.KernelBody);
    }

    [MetalFact]
    public void InstanceHelpers_ComputeTheRightValuesOnTheGpu()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Affine(ReadWriteBuffer<float> output, float scale, float offset) : IComputeShader
            {
                private float Apply(float x) => (x * scale) + Offset();

                private float Offset() => offset;

                public void Execute() => output[ThreadIds.X] = Apply(ThreadIds.X);
            }
            """);

        float[] result = GpuDispatch.For(run.AssertClean(), "Affine", 4, 4, 3f, 0.5f);

        Assert.Equal([0.5f, 3.5f, 6.5f, 9.5f], result);
    }

    // ---- ref / out --------------------------------------------------------------------------------

    [Fact]
    public void RefAndOutParameters_AreThreadReferences_AndInlineOutDeclarationsAreHoisted()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            int count = 0;
            float whole;
            float part = Split(buffer[0], out whole, ref count);
            float rest = Split(part, out float again, ref count);
            rest += again;
            if (Split(rest, out float third, ref count) > 0) { rest += third; }
            Pair pair = default;
            Split(rest, out pair.Left, ref count);
            buffer[1] = whole + rest + count + pair.Left;
            """,
            """
            private static float Split(float value, out float whole, ref int count)
            {
                whole = MathF.Floor(value);
                count++;
                return value - whole;
            }
            """,
            "public struct Pair { public float Left; public float Right; }").Verify(Shader);

        Assert.Contains("static float Split(float value, thread float& whole, thread int& count);", descriptor.MslSource);
        Assert.Equal(
            """
            int count = 0;
            float whole;
            float part = ::Split(buffer[0], whole, count);
            float again;
            float rest = ::Split(part, again, count);
            rest += again;
            float third;
            if (::Split(rest, third, count) > 0.0f)
            {
                rest += third;
            }
            Pair pair = Pair{};
            ::Split(rest, pair.Left, count);
            buffer[1] = whole + rest + float(count) + pair.Left;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [MetalFact]
    public void RefAndOutParameters_WriteBackOnTheGpu()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Counter(ReadWriteBuffer<float> output) : IComputeShader
            {
                private static void Bump(ref int count, out float last, float value)
                {
                    count += 2;
                    last = value * 10;
                }

                public void Execute()
                {
                    int count = 1;
                    Bump(ref count, out float last, 1.5f);
                    Bump(ref count, out last, last);
                    output[0] = count;
                    output[1] = last;
                }
            }
            """);

        float[] result = GpuDispatch.For(run.AssertClean(), "Counter", 1, 2);

        Assert.Equal([5f, 150f], result);
    }

    // ---- [MslBinaryOperator] ----------------------------------------------------------------------

    [Fact]
    public void MslBinaryOperator_IsGeneric_AndParenthesizesItsOperands()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            float a = buffer[0], b = buffer[1];
            buffer[2] = Ops.Minus(a + 1, b * 2) * Ops.Reverse(a - b, b) + Ops.Shift(a < b ? 1 : 2, 3);
            """,
            trailer: """

            public static class Ops
            {
                [MslBinaryOperator("-")]
                public static float Minus(float x, float y) => x - y;

                [MslBinaryOperator("-", SwapOperands = true)]
                public static float Reverse(float x, float y) => y - x;

                [MslBinaryOperator("<<")]
                public static int Shift(int x, int y) => x << y;
            }
            """).Verify(Shader);

        Assert.EndsWith(
            "buffer[2] = (a + 1.0f - b * 2.0f) * (b - (a - b)) + float(((a < b ? 1 : 2) << 3));",
            descriptor.KernelBody);
    }

    [Fact]
    public void GpuMul_SwapsItsOperands()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Float2 v = new Float2(buffer[0], buffer[1]);
            Float2x2 m = new Float2x2(1, 2, 3, 4);
            Float2 a = Gpu.Mul(v, m);
            Float2 b = Gpu.Mul(m, v);
            Float2x2 c = Gpu.Mul(m, m * 2);
            buffer[2] = a.X + b.Y + Gpu.Mul(c, a).X;
            """).Verify(Shader);

        Assert.Equal(
            """
            float2 v = float2(buffer[0], buffer[1]);
            float2x2 m = float2x2(1.0f, 2.0f, 3.0f, 4.0f);
            float2 a = (m * v);
            float2 b = (v * m);
            float2x2 c = (m * 2.0f * m);
            buffer[2] = a.x + b.y + (a * c).x;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [MetalFact]
    public void GpuMul_MatchesTheCpuOnTheGpu()
    {
        // The transpose rule end to end: rows in C#, columns in MSL, operands swapped by Gpu.Mul.
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Products(ReadWriteBuffer<float> output) : IComputeShader
            {
                public void Execute()
                {
                    Float3 v = new Float3(1.5f, -2f, 0.25f);
                    Float3x3 a = new Float3x3(1, 2, 3, 4, 5, 6, 7, 8, 10);
                    Float3x3 b = new Float3x3(new Float3(0.5f, 1, -1), new Float3(2, 0, 1), new Float3(-3, 1, 4));
                    Float3 vm = Gpu.Mul(v, a);
                    Float3 mv = Gpu.Mul(a, v);
                    Float3 ab = Gpu.Mul(Gpu.Mul(a, b), v);
                    Float2 w = Gpu.Mul(new Float2(3, 4), new Float2x2(1, 2, -1, 0.5f) - new Float2x2(0, 1, 1, 0) / 2);

                    output[0] = vm.X; output[1] = vm.Y; output[2] = vm.Z;
                    output[3] = mv.X; output[4] = mv.Y; output[5] = mv.Z;
                    output[6] = ab.X; output[7] = ab.Y; output[8] = ab.Z;
                    output[9] = w.X; output[10] = w.Y;
                }
            }
            """);

        float[] gpu = GpuDispatch.For(run.AssertClean(), "Products", 1, 11);

        Float3 v = new(1.5f, -2f, 0.25f);
        Float3x3 a = new(1, 2, 3, 4, 5, 6, 7, 8, 10);
        Float3x3 b = new(new Float3(0.5f, 1, -1), new Float3(2, 0, 1), new Float3(-3, 1, 4));
        Float3 vm = Gpu.Mul(v, a), mv = Gpu.Mul(a, v), ab = Gpu.Mul(Gpu.Mul(a, b), v);
        Float2 w = Gpu.Mul(new Float2(3, 4), new Float2x2(1, 2, -1, 0.5f) - (new Float2x2(0, 1, 1, 0) / 2));
        float[] cpu = [vm.X, vm.Y, vm.Z, mv.X, mv.Y, mv.Z, ab.X, ab.Y, ab.Z, w.X, w.Y];

        for (int i = 0; i < cpu.Length; i++)
        {
            Assert.True(MathF.Abs(gpu[i] - cpu[i]) <= 1e-5f * MathF.Max(1, MathF.Abs(cpu[i])), $"[{i}] GPU {gpu[i]} != CPU {cpu[i]}");
        }
    }

    // ---- matrices ---------------------------------------------------------------------------------

    [Fact]
    public void Matrices_AreFineAsLocalsParametersAndReturns()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Float2x2 m = default;
            m += Rotation(factor);
            m -= m / 4;
            m *= 2;
            buffer[0] = Gpu.Mul(new Float2(1, 0), 0.5f * m).X;
            """,
            "private static Float2x2 Rotation(float a) => new(MathF.Cos(a), MathF.Sin(a), -MathF.Sin(a), MathF.Cos(a));").Verify(Shader);

        Assert.Contains("static float2x2 Rotation(float a);", descriptor.MslSource);
        Assert.Equal(
            """
            float2x2 m = float2x2(0);
            m += ::Rotation(__c.factor);
            m -= m / 4.0f;
            m *= 2.0f;
            buffer[0] = (0.5f * m * float2(1.0f, 0.0f)).x;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Theory]
    [InlineData("public readonly Float2x2 transform;", "SCG0006", "Float2x2", "its [MslType(\"float2x2\")] declares no Packed type")]
    [InlineData("public readonly ReadWriteBuffer<Float3x3> transforms;", "SCG0006", "ReadWriteBuffer<Float3x3>", "its element type 'Float3x3' is not supported (its [MslType(\"float3x3\")] declares no Packed type")]
    [InlineData("public readonly Holder holder;", "SCG0009", "Holder", "its field 'Transform' has the unsupported type 'Float2x2'")]
    public void Matrices_AtTheCpuGpuBoundary_AreADiagnostic(string field, string id, string location, string message)
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Shader(field + "\n\n    public void Execute() { }") + "\npublic struct Holder { public float Scale; public Float2x2 Transform; }");

        Assert.Contains(message, run.AssertOnlyDiagnostic(id, location).GetMessage());
    }
}
