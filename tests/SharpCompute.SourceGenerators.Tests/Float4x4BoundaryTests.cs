using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// <c>Float4x4</c> is the one matrix that may cross the CPU/GPU boundary: as a constant, a buffer
/// element and a struct field. Its boundary type is the prelude's <c>__ms_packed_float4x4</c>, and
/// every place it is stored carries a <c>static_assert</c> on the C# layout.
/// </summary>
public class Float4x4BoundaryTests
{
    private const string Shader = "Tests.TestShader";

    private const string Structs = """

        public struct Body { public float Mass; public Float4x4 Transform; public int Id; }
        public struct Camera { public Float3 Eye; public Float4x4 Projection; }
        """;

    private const string Members = """
            public readonly ReadWriteBuffer<float> f;
            public readonly ReadWriteBuffer<Float4x4> matrices;
            public readonly ReadWriteBuffer<Body> bodies;
            public readonly float scale;
            public readonly Float4x4 view;
            public readonly Camera camera;

            public void Execute()
            {
                int t = ThreadIds.X;
                Float4x4 m = matrices[t];
                Float4x4 product = Gpu.Mul(m, view);
                Float4 a = Gpu.Mul(new Float4(1, 2, 3, 4), product);
                Float4 b = Gpu.Mul(Gpu.Transpose(camera.Projection), a);
                matrices[t] = product * scale;
                bodies[t].Transform = Gpu.Transpose(bodies[t].Transform) + m;
                f[t] = Gpu.Determinant(product) + b.X + bodies[t].Mass;
            }
        """;

    private static GeneratorRun Run() => GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(Members) + Structs);

    [Fact]
    public void BufferElementsConstantsAndStructFields_UseThePackedPreludeType()
    {
        ShaderDescriptor descriptor = Run().Verify(Shader);
        string msl = descriptor.MslSource;

        Assert.Contains("device __ms_packed_float4x4* matrices [[buffer(1)]]", msl);
        Assert.Contains("    __ms_packed_float4x4 Transform;", msl);
        Assert.Contains("    __ms_packed_float4x4 Projection;", msl);
        Assert.Contains("    __ms_packed_float4x4 view;", msl);

        // Locals and expressions stay the native type: every load unpacks and every store packs.
        Assert.Equal(
            """
            int t = int(__tid.x);
            float4x4 m = __ms_unpack(matrices[t]);
            float4x4 product = (__ms_unpack(__c.view) * m);
            float4 a = (product * float4(float4(1.0f, 2.0f, 3.0f, 4.0f)));
            float4 b = (float4(a) * metal::transpose(__ms_unpack(__c.camera.Projection)));
            matrices[t] = __ms_pack(product * __c.scale);
            bodies[t].Transform = __ms_pack(metal::transpose(__ms_unpack(bodies[t].Transform)) + m);
            f[t] = metal::determinant(product) + b.x + bodies[t].Mass;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void ThePrelude_DeclaresTheTypeBeforeItsFirstUse_WithItsOwnLayoutGuard()
    {
        string msl = Run().Verify(Shader).MslSource;

        // Four packed columns: the size of a float4x4 with the alignment of a float.
        int declaration = msl.IndexOf("struct __ms_packed_float4x4 { packed_float4 columns[4]; };", StringComparison.Ordinal);
        Assert.True(declaration > 0, msl);
        Assert.True(declaration < msl.IndexOf("struct Body", StringComparison.Ordinal));
        Assert.Contains("static_assert(sizeof(__ms_packed_float4x4) == 64 && alignof(__ms_packed_float4x4) == 4,", msl);
        Assert.Contains("inline float4x4 __ms_unpack(__ms_packed_float4x4 m)", msl);
        Assert.Contains("inline __ms_packed_float4x4 __ms_pack(float4x4 m)", msl);
    }

    [Fact]
    public void DriftGuards_AssertTheClrLayout_AtUnalignedOffsets()
    {
        GeneratorRun run = Run();
        ShaderDescriptor descriptor = run.Verify(Shader);
        string msl = descriptor.MslSource;

        // A matrix after one float: offset 4, which MSL's own float4x4 (16-byte aligned) could not have.
        Assert.Contains("static_assert(sizeof(__ms_packed_float4x4) == 64,", msl);
        Assert.Contains("static_assert(sizeof(Body) == 72,", msl);
        Assert.Contains("static_assert(__builtin_offsetof(Body, Transform) == 4,", msl);
        Assert.Contains("static_assert(__builtin_offsetof(Body, Id) == 68,", msl);
        Assert.Contains("static_assert(__builtin_offsetof(Camera, Projection) == 12,", msl);
        Assert.Contains("static_assert(__builtin_offsetof(__Constants, view) == 4,", msl);
        Assert.Contains("static_assert(__builtin_offsetof(__Constants, camera) == 68,", msl);

        descriptor.AssertLayoutMatchesClr("Body", run.LoadType("Tests.Body"));
        descriptor.AssertLayoutMatchesClr("Camera", run.LoadType("Tests.Camera"));
        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
        Assert.Equal(64, Unsafe.SizeOf<Float4x4>());
    }

    [MetalFact]
    public void DriftGuard_FailsCompilationWhenTheMatrixIsNotPacked()
    {
        ShaderDescriptor descriptor = Run().Verify(Shader);

        // MSL's own float4x4 is 16-byte aligned: every struct that holds one moves and grows.
        string drifted = descriptor.MslSource.Replace("    __ms_packed_float4x4 Transform;", "    float4x4 Transform;");
        MetalCompiler.Result result = MetalCompiler.Compile(drifted, descriptor.EntryPoint);

        Assert.False(result.Success);
        Assert.Contains("SharpCompute: size of 'Body' does not match its C# layout", result.Messages);
        Assert.Contains("SharpCompute: offset of 'Body.Transform' does not match its C# layout", result.Messages);
    }

    [Fact]
    public void CompoundAssignment_UnpacksAppliesAndPacks_StructCopiesStayPlain()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            """
                public readonly ReadWriteBuffer<Float4x4> matrices;
                public readonly ReadWriteBuffer<Body> bodies;
                public readonly Float4x4 view;

                public void Execute()
                {
                    int t = ThreadIds.X;
                    matrices[t] *= 2f;
                    matrices[t] += view;
                    Body body = bodies[t];
                    body.Transform -= matrices[t];
                    body.Transform = t > 0 ? body.Transform : view;
                    Float4x4 local = body.Transform;
                    local *= 0.5f;
                    Scale(ref local, body.Mass);
                    body.Transform = Twice(body.Transform);
                    bodies[t] = body;
                }

                private static void Scale(ref Float4x4 m, float by) => m = m * by;

                private static Float4x4 Twice(Float4x4 m) => m * 2f;
            """) + Structs);

        Assert.Equal(
            """
            int t = int(__tid.x);
            matrices[t] = __ms_pack(__ms_unpack(matrices[t]) * 2.0f);
            matrices[t] = __ms_pack(__ms_unpack(matrices[t]) + __ms_unpack(__c.view));
            Body body = bodies[t];
            body.Transform = __ms_pack(__ms_unpack(body.Transform) - __ms_unpack(matrices[t]));
            body.Transform = __ms_pack(t > 0 ? __ms_unpack(body.Transform) : __ms_unpack(__c.view));
            float4x4 local = __ms_unpack(body.Transform);
            local *= 0.5f;
            ::Scale(local, body.Mass);
            body.Transform = __ms_pack(::Twice(__ms_unpack(body.Transform)));
            bodies[t] = body;
            """.ReplaceLineEndings("\n"),
            run.Verify(Shader).KernelBody);
    }

    [Theory]
    [InlineData("matrices[t++] *= 2f;", "'*=' on a matrix in a buffer or struct field whose expression has side effects")]
    [InlineData("Body body = bodies[t]; Scale(ref body.Transform); bodies[t] = body;", "a matrix in a struct field is stored packed; copy it to a local, pass that, and assign it back")]
    public void WhatPackedStorageCannotDo_IsDiagnosed(string body, string message)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            $$"""
                public readonly ReadWriteBuffer<Float4x4> matrices;
                public readonly ReadWriteBuffer<Body> bodies;

                public void Execute()
                {
                    int t = ThreadIds.X;
                    {{body}}
                }

                private static void Scale(ref Float4x4 m) => m = m * 2f;
            """) + Structs);

        Assert.Contains(run.Diagnostics, d => d.Id == "SCG0012" && d.GetMessage().Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void AShaderWithoutABoundaryMatrix_GetsNoPrelude()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            """
            Float4x4 m = new Float4x4(new Float4(factor), new Float4(1f), new Float4(2f), new Float4(3f));
            buffer[0] = Gpu.Determinant(Gpu.Mul(m, m));
            """));

        Assert.DoesNotContain("__ms_", run.Verify(Shader).MslSource);
    }

    [Fact]
    public void StaticReadonlyMatrices_BecomeProgramScopeConstants()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            """
                private static readonly Float4x4 Flip = new Float4x4(1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1) * 2;

                public readonly ReadWriteBuffer<float> buffer;
                public readonly float factor;

                public TestShader(ReadWriteBuffer<float> buffer, float factor)
                {
                    this.buffer = buffer;
                    this.factor = factor;
                }

                public void Execute() => buffer[0] = Gpu.Mul(new Float4(1, 2, 3, 4), Flip).Y * factor;
            """));
        ShaderDescriptor descriptor = run.Verify(Shader);

        // A program-scope constant is native storage: no packing, and so no prelude.
        Assert.Contains(
            "constant float4x4 Flip = float4x4(1.0f, 0.0f, 0.0f, 0.0f, 0.0f, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f) * 2.0f;",
            descriptor.MslSource);
        Assert.Equal("buffer[0] = (::Flip * float4(float4(1.0f, 2.0f, 3.0f, 4.0f))).y * __c.factor;", descriptor.KernelBody);
        Assert.DoesNotContain("__ms_", descriptor.MslSource);

        if (MetalCompiler.IsAvailable)
        {
            Assert.Equal(-12f, GpuDispatch.For(run, Shader, threads: 1, outputLength: 1, 3f)[0]);
        }
    }

    [Fact]
    public void TheSmallerMatrices_StillCannotCrossTheBoundary()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Shader("public readonly ReadWriteBuffer<Float3x3> transforms;\n\n    public void Execute() { }"));

        Diagnostic diagnostic = Assert.Single(run.Diagnostics.Where(static d => d.Id == "SCG0006"));
        Assert.Contains("its [MslType(\"float3x3\")] declares no Packed type", diagnostic.GetMessage());
    }

    [MetalFact]
    public void AMatrixConstant_ReachesTheGpuWithTheRowConvention()
    {
        // (1, 2, 3, 4) times a matrix whose rows are scaled unit vectors plus a translation row.
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            """
                public readonly ReadWriteBuffer<float> output;
                public readonly float bias;
                public readonly Float4x4 world;

                public TestShader(ReadWriteBuffer<float> output, float bias, Float4x4 world)
                {
                    this.output = output;
                    this.bias = bias;
                    this.world = world;
                }

                public void Execute()
                {
                    Float4 rowVector = Gpu.Mul(new Float4(1, 2, 3, 1), world);
                    Float4 columnVector = Gpu.Mul(world, new Float4(1, 2, 3, 1));
                    output[0] = rowVector.X + bias;
                    output[1] = rowVector.Y;
                    output[2] = rowVector.Z;
                    output[3] = rowVector.W;
                    output[4] = columnVector.X;
                    output[5] = columnVector.W;
                    output[6] = Gpu.Determinant(world);
                }
            """));
        run.Verify(Shader);

        var world = new Float4x4(
            2, 0, 0, 0,
            0, 3, 0, 0,
            0, 0, 4, 0,
            10, 20, 30, 1);
        float[] result = GpuDispatch.For(run, Shader, threads: 1, outputLength: 7, 0.5f, world);

        // Row vector: v * M picks up the translation in the last row. Column vector: M * v does not.
        Assert.Equal([12.5f, 26f, 42f, 1f, 2f, 10f + 40f + 90f + 1f, 24f], result);
    }
}
