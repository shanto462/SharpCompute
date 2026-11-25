using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Threadgroup memory (<c>[GroupShared]</c> arrays), the barriers of <c>Gpu</c> and the group built-ins
/// (<c>GroupSize</c>, <c>GroupIds.Index</c>, <c>ThreadGroupCount</c>): the MSL they become, what the
/// descriptor reports about them, and the diagnostics for declarations that cannot be threadgroup memory.
/// </summary>
public class GroupSharedTests
{
    private const string Shader = "Tests.TestShader";

    private static GeneratorRun Run(string members, string attributes = "", string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(members, attributes) + trailer);

    private static string GeneratedCode(GeneratorRun run) => Assert.Single(run.Sources).SourceText.ToString();

    // ---- declarations -----------------------------------------------------------------------------

    [Fact]
    public void Array_BecomesAThreadgroupDeclarationInTheKernel_WithIndexingAndAConstantLength()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared]
                private static readonly float[] tile = new float[256];

                public void Execute()
                {
                    int local = GroupIds.X;
                    tile[local] = buffer[ThreadIds.X];
                    Gpu.GroupMemoryBarrierWithGroupSync();
                    tile[local] += tile[(local + 1) % tile.Length];
                    tile[0]++;
                    buffer[ThreadIds.X] = tile[local] * tile.Length;
                }
            """,
            "[ThreadGroupSize(256)]").Verify(Shader);

        Assert.Equal(
            """
            threadgroup float tile[256];
            int local = int(__gtid.x);
            tile[local] = buffer[int(__tid.x)];
            metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
            tile[local] += tile[(local + 1) % 256];
            tile[0]++;
            buffer[int(__tid.x)] = tile[local] * float(256);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        Assert.True(descriptor.UsesThreadgroupMemory);
        Assert.Equal(1024, descriptor.ThreadgroupMemoryLength);
    }

    [Theory]
    [InlineData("new float[N]", 64)]
    [InlineData("new float[N * 2]", 128)]
    [InlineData("new float[Side * Side + 1]", 17)]
    [InlineData("new float[sizeof(int) * 3]", 12)]
    [InlineData("new float[(int)Size.Big]", 100)]
    [InlineData("new float[8u]", 8)]
    [InlineData("new float[1]", 1)]
    public void Length_CanBeAnyCompileTimeConstant(string initializer, int length)
    {
        ShaderDescriptor descriptor = Run(
            $$"""
                private const int N = 64;
                private const int Side = 4;
                private enum Size { Big = 100 }
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared]
                private static readonly float[] tile = {{initializer}};

                public void Execute()
                {
                    tile[0] = 1;
                    buffer[0] = tile.Length;
                }
            """).Verify(Shader);

        Assert.Contains($"threadgroup float tile[{length}];", descriptor.MslSource);
        Assert.Contains($"buffer[0] = float({length});", descriptor.MslSource);
        Assert.Equal(length * 4, descriptor.ThreadgroupMemoryLength);
    }

    [Fact]
    public void Elements_CanBeScalarsPackedVectorsAndUserStructs()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            """
            using SharpCompute;
            using TestVectors;

            namespace Tests;

            public struct Particle
            {
                public Vec3 Position;
                public int Weight;
            }

            [ThreadGroupSize(8)]
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader : IComputeShader
            {
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly int[] counts = new int[8];
                [GroupShared] private static readonly uint[] bits = new uint[8];
                [GroupShared] private static readonly Vec3[] vectors = new Vec3[8];
                [GroupShared] private static readonly Vec2[] pairs = new Vec2[8];
                [GroupShared] private static readonly Particle[] particles = new Particle[8];

                public void Execute()
                {
                    int local = GroupIds.X;

                    counts[local] = local;
                    bits[local] = (uint)local << 1;
                    vectors[local] = new Vec3(1, 2, 3) * local;
                    vectors[local].Y = 5;
                    pairs[local] = vectors[local].XY;

                    Particle particle;
                    particle.Position = vectors[local];
                    particle.Weight = counts[local];
                    particles[local] = particle;
                    particles[local].Weight += 2;
                    particles[local].Position.X -= 1;
                    Gpu.GroupMemoryBarrierWithGroupSync();

                    Particle other = particles[7 - local];
                    Vec3 sum = other.Position + vectors[7 - local];
                    buffer[ThreadIds.X] = sum.X + pairs[7 - local].Y + other.Weight + counts[0] + bits[1];
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        // Elements are declared with their boundary types, as in a buffer, so the sizes are the C# ones.
        Assert.Contains(
            """
                threadgroup int counts[8];
                threadgroup uint bits[8];
                threadgroup packed_float3 vectors[8];
                threadgroup packed_float2 pairs[8];
                threadgroup Particle particles[8];
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        Assert.Contains("static_assert(sizeof(packed_float3) == 12,", descriptor.MslSource);
        Assert.Contains("static_assert(sizeof(Particle) == 16,", descriptor.MslSource);
        Assert.Contains("vectors[local].y = 5.0f;", descriptor.MslSource);
        Assert.Contains("particles[local].Weight += 2;", descriptor.MslSource);
        Assert.Contains("particles[local].Position.x -= 1.0f;", descriptor.MslSource);
        Assert.Contains("Particle other = particles[7 - local];", descriptor.MslSource);
        Assert.Equal((8 * 4) + (8 * 4) + (8 * 12) + (8 * 8) + (8 * 16), descriptor.ThreadgroupMemoryLength);
    }

    [Fact]
    public void OnlyTheArraysTheKernelUses_AreDeclaredAndCounted()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly float[] unused = new float[8000];
                [GroupShared] private static readonly float[] second = new float[16];
                [GroupShared] private static readonly float[] first = new float[8];

                public void Execute()
                {
                    first[0] = 1;
                    second[0] = first[0];
                    buffer[0] = second[0];
                }
            """).Verify(Shader);

        // In declaration order, whatever order they are used in.
        Assert.StartsWith("threadgroup float second[16];\nthreadgroup float first[8];\nfirst[0] = 1.0f;", descriptor.KernelBody);
        Assert.DoesNotContain("unused", descriptor.MslSource);
        Assert.Equal(96, descriptor.ThreadgroupMemoryLength);
    }

    [Fact]
    public void WithoutGroupSharedMemoryOrBarriers_TheDescriptorKeepsTheInterfaceDefaults()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly float[] unused = new float[8];

                public void Execute() { buffer[ThreadIds.X] = GroupSize.Count + GroupIds.Index + ThreadGroupCount.X; }
            """);
        ShaderDescriptor descriptor = run.Verify(Shader);

        // The built-ins alone do not make the threads cooperate.
        Assert.False(descriptor.UsesThreadgroupMemory);
        Assert.Equal(0, descriptor.ThreadgroupMemoryLength);
        Assert.DoesNotContain("UsesThreadgroupMemory", GeneratedCode(run));
        Assert.DoesNotContain("ThreadgroupMemoryLength", GeneratedCode(run));
        Assert.DoesNotContain("threadgroup ", descriptor.MslSource);
    }

    [Fact]
    public void DescriptorMembers_AreEmittedForGroupSharedShaders()
    {
        GeneratorRun run = Run(
            """
                [GroupShared] private static readonly float[] tile = new float[32];

                public void Execute() { tile[GroupIds.X] = 1; }
            """);
        run.Verify(Shader);

        string code = GeneratedCode(run);
        Assert.Contains("static bool global::SharpCompute.IComputeShaderDescriptor<global::Tests.TestShader>.UsesThreadgroupMemory => true;", code);
        Assert.Contains("static int global::SharpCompute.IComputeShaderDescriptor<global::Tests.TestShader>.ThreadgroupMemoryLength => 128;", code);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    [Fact]
    public void Helpers_ReceiveTheArraysTheyUse_AsHiddenThreadgroupPointers_AndForwardThem()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;
                public readonly float factor;

                [GroupShared] private static readonly float[] tile = new float[64];
                [GroupShared] private static readonly Vec3[] vectors = new Vec3[64];
                [GroupShared] private static readonly int[] flags = new int[4];

                public void Execute()
                {
                    Store(GroupIds.X, 2);
                    Gpu.GroupMemoryBarrierWithGroupSync();
                    buffer[ThreadIds.X] = Total(3) + flags[0];
                }

                private static void Store(int index, float value)
                {
                    tile[index] = value;
                    vectors[index] = new Vec3(value, 0, 0);
                }

                private float Total(int count)
                {
                    float sum = Sum(count) * factor;

                    return sum + buffer[0] + Local(1);

                    static float Local(int i) => vectors[i].X + tile.Length;
                }

                private static float Sum(int count)
                {
                    float sum = 0;

                    for (int i = 0; i < count; i++)
                    {
                        sum += tile[i];
                    }

                    return sum + GroupIds.Index;
                }
            """,
            "[ThreadGroupSize(64)]").Verify(Shader);

        // After the buffers, before the constants and the built-ins; pointers, because an array decays to one.
        Assert.Contains("static void Store(int index, float value, threadgroup float* tile, threadgroup packed_float3* vectors);", descriptor.MslSource);
        Assert.Contains("static float Sum(int count, threadgroup float* tile, uint __gindex);", descriptor.MslSource);
        Assert.Contains("static float Total_Local(int i, threadgroup packed_float3* vectors);", descriptor.MslSource);
        Assert.Contains(
            "static float Total(int count, device float* buffer, threadgroup float* tile, threadgroup packed_float3* vectors, constant __Constants& __c, uint __gindex);",
            descriptor.MslSource);

        // The kernel passes its arrays, a helper its pointers: the same names either way.
        Assert.Contains("float sum = ::Sum(count, tile, __gindex) * __c.factor;", descriptor.MslSource);
        Assert.Contains("return sum + buffer[0] + ::Total_Local(1, vectors);", descriptor.MslSource);
        Assert.Contains("return vectors[i].x + float(64);", descriptor.MslSource);
        Assert.Equal(
            """
            threadgroup float tile[64];
            threadgroup packed_float3 vectors[64];
            threadgroup int flags[4];
            ::Store(int(__gtid.x), 2.0f, tile, vectors);
            metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
            buffer[int(__tid.x)] = ::Total(3, buffer, tile, vectors, __c, __gindex) + float(flags[0]);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void AnArrayOnlyAHelperUses_IsStillDeclaredByTheKernel()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly float[] tile = new float[8];

                public void Execute() { buffer[0] = Read(); }

                private static float Read() { tile[1] = 2; return tile[1]; }
            """).Verify(Shader);

        Assert.Equal("threadgroup float tile[8];\nbuffer[0] = ::Read(tile);", descriptor.KernelBody);
        Assert.True(descriptor.UsesThreadgroupMemory);
    }

    [Fact]
    public void ABarrierOnlyAHelperCalls_StillMarksTheShader()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                public void Execute() { buffer[ThreadIds.X] = 1; Sync(); buffer[ThreadIds.X] += buffer[0]; }

                private static void Sync() => Gpu.DeviceMemoryBarrierWithGroupSync();
            """).Verify(Shader);

        Assert.Contains("static void Sync()\n{\n    metal::threadgroup_barrier(metal::mem_flags::mem_device);\n}", descriptor.MslSource);
        Assert.True(descriptor.UsesThreadgroupMemory);
        Assert.Equal(0, descriptor.ThreadgroupMemoryLength);
    }

    // ---- names ------------------------------------------------------------------------------------

    [Fact]
    public void Names_AreEscaped_AndLocalsThatWouldHideAnArrayAreRenamed()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly float[] kernel = new float[4];
                [GroupShared] private static readonly float[] tile = new float[4];

                public void Execute()
                {
                    float tile = 2;
                    TestShader.tile[0] = tile;
                    kernel[0] = Helper(3);
                    buffer[0] = TestShader.tile[0] + kernel[0];
                }

                private static float Helper(float tile) => tile + TestShader.tile[1];
            """).Verify(Shader);

        Assert.Contains("threadgroup float __ms_kernel[4];", descriptor.MslSource);
        Assert.Contains("float __local_tile = 2.0f;", descriptor.MslSource);
        Assert.Contains("tile[0] = __local_tile;", descriptor.MslSource);
        Assert.Contains("static float Helper(float __local_tile, threadgroup float* tile)", descriptor.MslSource);
        Assert.Contains("return __local_tile + tile[1];", descriptor.MslSource);
    }

    [Fact]
    public void AnArrayNamedLikeItsElementType_DoesNotHideTheType()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            """
            using SharpCompute;

            namespace Tests;

            public struct Cell { public int Value; }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader : IComputeShader
            {
                public readonly ReadWriteBuffer<int> buffer;

                [GroupShared] private static readonly Cell[] Cell = new Cell[4];

                public void Execute()
                {
                    Cell cell;
                    cell.Value = 3;
                    Cell[0] = cell;
                    buffer[0] = Cell[0].Value;
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains("threadgroup Cell __local_Cell[4];", descriptor.MslSource);
        Assert.Contains("__local_Cell[0] = cell;", descriptor.MslSource);
    }

    // ---- barriers ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("GroupMemoryBarrierWithGroupSync", "metal::mem_flags::mem_threadgroup")]
    [InlineData("DeviceMemoryBarrierWithGroupSync", "metal::mem_flags::mem_device")]
    [InlineData("AllMemoryBarrierWithGroupSync", "metal::mem_flags::mem_device | metal::mem_flags::mem_threadgroup")]
    public void Barriers_MapToThreadgroupBarrierWithTheirFlags(string method, string flags)
    {
        ShaderDescriptor descriptor = Run(
            $$"""
                public readonly ReadWriteBuffer<float> buffer;

                public void Execute()
                {
                    buffer[ThreadIds.X] = 1;
                    Gpu.{{method}}();
                    buffer[ThreadIds.X] += buffer[0];
                }
            """).Verify(Shader);

        Assert.Contains($"\n    metal::threadgroup_barrier({flags});\n", descriptor.MslSource);
        Assert.True(descriptor.UsesThreadgroupMemory);
    }

    [Theory]
    [InlineData("DeviceMemoryBarrierWithGroupSync", "metal::mem_flags::mem_device | metal::mem_flags::mem_texture")]
    [InlineData("AllMemoryBarrierWithGroupSync", "metal::mem_flags::mem_device | metal::mem_flags::mem_texture | metal::mem_flags::mem_threadgroup")]
    [InlineData("GroupMemoryBarrierWithGroupSync", "metal::mem_flags::mem_threadgroup")]
    public void DeviceBarriers_AlsoOrderTextureWrites_WhenTheShaderHasAReadWriteTexture(string method, string flags)
    {
        ShaderDescriptor descriptor = Run(
            $$"""
                public readonly ReadWriteTexture2D<float> image;

                public void Execute()
                {
                    image[ThreadIds.XY] = new Float4(1, 0, 0, 1);
                    Gpu.{{method}}();
                    Float4 first = image[0, 0];
                    image[ThreadIds.XY] = first;
                }
            """).Verify(Shader);

        Assert.Contains($"metal::threadgroup_barrier({flags});", descriptor.MslSource);
    }

    [Fact]
    public void DeviceBarriers_DoNotMentionTextures_WhenTheShaderOnlyReadsThem()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadOnlyTexture2D<float> image;
                public readonly ReadWriteBuffer<float> buffer;

                public void Execute()
                {
                    buffer[ThreadIds.X] = image[ThreadIds.XY].X;
                    Gpu.DeviceMemoryBarrierWithGroupSync();
                }
            """).Verify(Shader);

        Assert.Contains("metal::threadgroup_barrier(metal::mem_flags::mem_device);", descriptor.MslSource);
    }

    // ---- built-ins --------------------------------------------------------------------------------

    [Fact]
    public void GroupBuiltins_MapToTheMetalBuiltins()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<int> buffer;

                public void Execute()
                {
                    Int3 size = GroupSize.XYZ;
                    Int2 size2 = GroupSize.XY;
                    Int3 groups = ThreadGroupCount.XYZ;
                    Int2 groups2 = ThreadGroupCount.XY;
                    buffer[0] = GroupSize.X + GroupSize.Y + GroupSize.Z + size.X + size2.Y;
                    buffer[1] = GroupSize.Count;
                    buffer[2] = GroupIds.Index;
                    buffer[3] = ThreadGroupCount.X + ThreadGroupCount.Y + ThreadGroupCount.Z + groups.X + groups2.Y;
                }
            """).Verify(Shader);

        Assert.Contains("uint3 __gsize [[threads_per_threadgroup]]", descriptor.MslSource);
        Assert.Contains("uint3 __groups_in [[threadgroups_per_grid]]", descriptor.MslSource);
        Assert.Contains("uint __gindex [[thread_index_in_threadgroup]])", descriptor.MslSource);
        Assert.Equal(
            """
            int3 size = int3(__gsize.xyz);
            int2 size2 = int2(__gsize.xy);
            int3 groups = int3(__groups.xyz);
            int2 groups2 = int2(__groups.xy);
            buffer[0] = int(__gsize.x) + int(__gsize.y) + int(__gsize.z) + size.x + size2.y;
            buffer[1] = int(__gsize.x * __gsize.y * __gsize.z);
            buffer[2] = int(__gindex);
            buffer[3] = int(__groups.x) + int(__groups.y) + int(__groups.z) + groups.x + groups2.y;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void InAWindowedDispatch_TheGroupCountIsRebased_AndTheGroupSizeAndIndexAreNot()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<int> buffer;

                public void Execute() { buffer[GroupIds.Index] = GroupSize.Count + ThreadGroupCount.X; }
            """).Verify(Shader);

        // A window starts on a threadgroup boundary and runs to the end of the grid, so the whole
        // grid's count is the part's plus the groups before it; sizes and indices within a group are
        // the same in the part as in the whole.
        Assert.Equal("const uint3 __groups = __windowed ? __groups_in + uint3(__window.groupOffset) : __groups_in;", descriptor.KernelPrologue);
        Assert.Contains("uint3 __gsize [[threads_per_threadgroup]],", descriptor.MslSource);
        Assert.Contains("uint __gindex [[thread_index_in_threadgroup]])", descriptor.MslSource);
        Assert.DoesNotContain("__gsize_in", descriptor.MslSource);
        Assert.DoesNotContain("__gindex_in", descriptor.MslSource);
    }

    [Fact]
    public void GroupBuiltins_ReachHelpersAsHiddenParameters()
    {
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<int> buffer;

                public void Execute() { buffer[ThreadIds.X] = Flat() + Groups(); }

                private static int Flat() => GroupIds.Index + GroupSize.Count;

                private static int Groups() => ThreadGroupCount.XY.X;
            """).Verify(Shader);

        Assert.Contains("static int Flat(uint3 __gsize, uint __gindex)", descriptor.MslSource);
        Assert.Contains("static int Groups(uint3 __groups)", descriptor.MslSource);
        Assert.Contains("buffer[int(__tid.x)] = ::Flat(__gsize, __gindex) + ::Groups(__groups);", descriptor.MslSource);
    }

    // ---- pixel shaders ----------------------------------------------------------------------------

    [Fact]
    public void PixelShaders_CanUseGroupSharedMemoryAndBarriers()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            """
            using SharpCompute;

            namespace Tests;

            [ThreadGroupSize(8, 8)]
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader : IComputeShader<Float4>
            {
                [GroupShared] private static readonly float[] tile = new float[64];

                public Float4 Execute()
                {
                    tile[GroupIds.Index] = ThreadIds.X;
                    Gpu.GroupMemoryBarrierWithGroupSync();
                    return new Float4(tile[0], tile[63 - GroupIds.Index], 0, 1);
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains("    threadgroup float tile[64];\n    tile[int(__gindex)] = float(int(__tid.x));", descriptor.MslSource);
        Assert.True(descriptor.UsesThreadgroupMemory);
        Assert.Equal(256, descriptor.ThreadgroupMemoryLength);
    }

    // ---- diagnostics: the declaration -------------------------------------------------------------

    [Theory]
    [InlineData("[GroupShared] private readonly float[] tile = new float[4];", "must be 'static readonly'")]
    [InlineData("[GroupShared] private static float[] tile = new float[4];", "must be 'static readonly'")]
    [InlineData("[GroupShared] private static readonly float tile = 4;", "is not a one-dimensional array")]
    [InlineData("[GroupShared] private static readonly float[,] tile = new float[4, 4];", "is not a one-dimensional array")]
    [InlineData("[GroupShared] private static readonly ReadWriteBuffer<float> tile = null!;", "is not a one-dimensional array")]
    public void FieldsThatCannotBeThreadgroupMemory_AreReported_Once(string declaration, string reason)
    {
        GeneratorRun run = Run(
            $$"""
                public readonly ReadWriteBuffer<float> buffer;

                {{declaration}}

                public void Execute() { buffer[0] = 1; }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0040", "tile");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'tile'", diagnostic.GetMessage());
        Assert.Contains(reason, diagnostic.GetMessage());
    }

    [Fact]
    public void UsesOfAMisdeclaredArray_AreNotReportedAgain()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static float[] tile = new float[4];

                public void Execute()
                {
                    tile[0] = 1;
                    buffer[0] = tile[0] + tile.Length;
                }
            """);

        run.AssertOnlyDiagnostic("SCG0040", "tile");
    }

    [Theory]
    [InlineData("new float[Count]", "Count", "'Count' is not a compile-time constant")]
    [InlineData("new float[Environment.ProcessorCount]", "Environment.ProcessorCount", "is not a compile-time constant")]
    [InlineData("new float[0]", "0", "the length 0 is out of range")]
    [InlineData("new float[] { 1, 2, 3 }", "{ 1, 2, 3 }", "threadgroup memory starts out undefined")]
    [InlineData("new float[3] { 1, 2, 3 }", "{ 1, 2, 3 }", "threadgroup memory starts out undefined")]
    [InlineData("Make()", "Make()", "must be an array creation of the form 'new T[length]'")]
    [InlineData("null!", "null", "must be an array creation of the form 'new T[length]'")]
    public void LengthsThatAreNotConstant_AreReported(string initializer, string location, string reason)
    {
        GeneratorRun run = Run(
            $$"""
                private static readonly int Count = 4;
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly float[] tile = {{initializer}};

                public void Execute() { buffer[0] = 1; }

                private static float[] Make() => new float[4];
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0041", location);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'tile'", diagnostic.GetMessage());
        Assert.Contains(reason, diagnostic.GetMessage());
    }

    [Fact]
    public void AMissingInitializer_IsReported()
    {
        GeneratorRun run = Run(
            """
                [GroupShared] private static readonly float[] tile;

                public void Execute() { }
            """);

        Assert.Contains("it has no initializer", run.AssertOnlyDiagnostic("SCG0041", "tile").GetMessage());
    }

    [Theory]
    [InlineData("bool")]
    [InlineData("double")]
    [InlineData("string")]
    [InlineData("float[]")]
    [InlineData("ReadWriteBuffer<float>")]
    public void ElementTypesThatCannotCrossTheBoundary_AreReported(string type)
    {
        GeneratorRun run = Run(
            $$"""
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly {{type}}[] tile = new {{type.Replace("[]", "[4]")}}{{(type.EndsWith("[]") ? "[]" : "[4]")}};

                public void Execute() { buffer[0] = 1; }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0042", type + "[]");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'tile'", diagnostic.GetMessage());
    }

    [Fact]
    public void AUserStructThatCannotBeTranslated_IsReportedAsSuch()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            """
            using SharpCompute;

            namespace Tests;

            public struct Bad { public string Name; }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader : IComputeShader
            {
                [GroupShared] private static readonly Bad[] tile = new Bad[4];

                public void Execute() { }
            }
            """);

        // The struct's own diagnostic, at the declaration that uses it.
        Diagnostic diagnostic = Assert.Single(run.Diagnostics);
        Assert.NotEqual("SCG0042", diagnostic.Id);
        Assert.Equal("Bad[]", run.TextAt(diagnostic));
        Assert.Contains("Bad", diagnostic.GetMessage());
    }

    [Fact]
    public void MoreThan32KilobytesInTotal_IsReported_WithTheBreakdown()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly float[] big = new float[8000];
                [GroupShared] private static readonly Vec3[] vectors = new Vec3[65];
                [GroupShared] private static readonly int[] unused = new int[100000];

                public void Execute()
                {
                    big[0] = 1;
                    vectors[0] = new Vec3(1, 2, 3);
                    buffer[0] = big[0] + vectors[0].X;
                }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0043", "TestShader");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "Shader struct 'TestShader' uses 32780 bytes of [GroupShared] memory (big: 32000 + vectors: 780), but a threadgroup has at most 32768 bytes on Apple GPUs",
            diagnostic.GetMessage());
    }

    [Fact]
    public void Exactly32Kilobytes_IsAccepted()
    {
        ShaderDescriptor descriptor = Run(
            """
                [GroupShared] private static readonly float[] big = new float[8192];

                public void Execute() { big[GroupIds.Index] = 1; }
            """).Verify(Shader);

        Assert.Equal(32768, descriptor.ThreadgroupMemoryLength);
    }

    // ---- diagnostics: uses ------------------------------------------------------------------------

    [Fact]
    public void TheArrayItself_CannotBeUsedAsAValue()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly float[] tile = new float[4];

                public void Execute() { float[] copy = tile; }
            """);

        Diagnostic diagnostic = run.Diagnostics.Single(static d => d.Id == "SCG0017");

        Assert.Equal("tile", run.TextAt(diagnostic));
        Assert.Contains("only the elements ('name[i]') and 'Length' of a [GroupShared] array can be used in a shader", diagnostic.GetMessage());
    }

    [Fact]
    public void AnElement_CannotBePassedByReference()
    {
        // Threadgroup memory is its own address space; a helper's `ref` parameter is thread-local.
        // (Atomics on group-shared memory will need exactly this to be accepted for their intrinsics.)
        GeneratorRun run = Run(
            """
                [GroupShared] private static readonly float[] tile = new float[4];

                public void Execute() { Bump(ref tile[0]); }

                private static void Bump(ref float value) => value += 1;
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0012", "ref tile[0]");

        Assert.Contains("by-reference argument", diagnostic.GetMessage());
    }

    [Fact]
    public void AnElement_CannotBeTheLocationOfAnAtomicYet()
    {
        GeneratorRun run = Run(
            """
                [GroupShared] private static readonly int[] counts = new int[4];

                public void Execute() { Gpu.AtomicAdd(ref counts[GroupIds.X & 3], 1); }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0034", "counts[GroupIds.X & 3]");

        Assert.Contains("it is an element of a [GroupShared] array, and atomics on threadgroup memory are not supported yet", diagnostic.GetMessage());
    }

    [Fact]
    public void AtomicsOnBuffers_WorkNextToGroupSharedMemoryAndBarriers()
    {
        // A per-group count in shared memory, added to a global counter with an atomic.
        ShaderDescriptor descriptor = Run(
            """
                public readonly ReadWriteBuffer<int> total;

                [GroupShared] private static readonly int[] tile = new int[64];

                public void Execute()
                {
                    tile[GroupIds.X] = ThreadIds.X & 1;
                    Gpu.GroupMemoryBarrierWithGroupSync();

                    if (GroupIds.X == 0)
                    {
                        int sum = 0;

                        for (int i = 0; i < GroupSize.Count; i++)
                        {
                            sum += tile[i];
                        }

                        Gpu.AtomicAdd(ref total[0], sum);
                    }
                }
            """,
            "[ThreadGroupSize(64)]").Verify(Shader);

        Assert.True(descriptor.UsesThreadgroupMemory);
        Assert.True(descriptor.UsesAtomics);
        Assert.Contains("threadgroup int tile[64];", descriptor.MslSource);
        Assert.Contains("atomic_fetch_add_explicit", descriptor.MslSource);
    }

    [Fact]
    public void AnArrayOfAnotherType_IsRejected()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            """
            using SharpCompute;

            namespace Tests;

            public static class Shared
            {
                [GroupShared] public static readonly float[] Tile = new float[4];
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader : IComputeShader
            {
                public void Execute()
                {
                    Shared.Tile[0] = 1;
                    Shared.Tile[1] = Shared.Tile.Length;
                }
            }
            """);

        // Once, at the first use.
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0040", "Shared.Tile");

        Assert.Contains("declared in 'Shared'", diagnostic.GetMessage());
        Assert.Contains("'TestShader'", diagnostic.GetMessage());
    }

    [Fact]
    public void OtherArrays_AreStillRejected()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                private static readonly float[] table = new float[4];

                public void Execute() { buffer[0] = table[0]; }
            """);

        Assert.NotEmpty(run.Diagnostics);
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id is "SCG0040" or "SCG0041" or "SCG0042" or "SCG0043");
    }

    [Fact]
    public void AnElementReadInAStaticInitializer_IsNotAConstant()
    {
        GeneratorRun run = Run(
            """
                public readonly ReadWriteBuffer<float> buffer;

                [GroupShared] private static readonly float[] tile = new float[4];

                private static readonly float First = tile[0];

                public void Execute() { buffer[0] = First; }
            """);

        Diagnostic diagnostic = Assert.Single(run.Diagnostics);
        Assert.Contains("threadgroup memory only exists while the shader runs", diagnostic.GetMessage());
    }

    [Fact]
    public void IndexExpressionsWithSideEffects_AreCheckedForEvaluationOrder()
    {
        GeneratorRun run = Run(
            """
                [GroupShared] private static readonly int[] tile = new int[8];

                public void Execute()
                {
                    int i = 0;
                    tile[i++] = i;
                }
            """);

        run.AssertOnlyDiagnostic("SCG0021", "tile[i++] = i");
    }

    [Fact]
    public void InvalidCode_StaysQuiet()
    {
        GeneratorRun run = Run(
            """
                [GroupShared] private static readonly Missing[] tile = new Missing[4];
                [GroupShared] private static readonly float[] other = new float[Undefined];

                public void Execute() { tile[0] = other[0]; }
            """);

        // The C# compiler reports the real problems.
        Assert.Empty(run.Diagnostics);
    }
}
