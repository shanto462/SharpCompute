using System;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The wave matrices (<c>WaveMatrix8x8</c>, <c>WaveMatrixHalf8x8</c>): the MSL of every operation for
/// both element types and both address spaces, the pointer a load and a store take, the header and the
/// prelude helpers, the descriptor's <c>UsesSimdGroups</c>, and the diagnostics SCG0048 to SCG0051.
/// Every shader that is accepted is also compiled with Metal.
/// </summary>
public class WaveMatrixTests
{
    private const string Shader = "Tests.TestShader";

    /// <summary>A shader with float and half buffers, a few constants, and the given <c>Execute</c> body.</summary>
    private static GeneratorRun RunBody(string body, string extraMembers = "", string attributes = "[ThreadGroupSize(32)]") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader($$"""
                public readonly ReadOnlyBuffer<float> a;
                public readonly ReadWriteBuffer<float> c;
                public readonly ReadOnlyBuffer<Half> ha;
                public readonly ReadWriteBuffer<Half> hc;
                public readonly int n;
                public readonly float value;
                {{extraMembers}}

                public void Execute()
                {
                    {{body}}
                }
            """, attributes));

    private static string[] Body(string body, string extraMembers = "") =>
        RunBody(body, extraMembers).Verify(Shader).KernelBody.Split('\n');

    private static bool UsesSimdGroups(GeneratorRun run)
    {
        Type type = run.LoadType(Shader);
        return (bool)typeof(WaveMatrixTests).GetMethod(nameof(ReadUsesSimdGroups), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, null)!;
    }

    private static bool ReadUsesSimdGroups<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.UsesSimdGroups;

    // ---- types and values -------------------------------------------------------------------------

    [Fact]
    public void TheTypes_AreMetalsSimdgroupMatrices_AndTheShaderIncludesTheirHeader()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            WaveMatrix8x8 m = WaveMatrix8x8.Filled(value);
            WaveMatrixHalf8x8 h = WaveMatrixHalf8x8.Filled((Half)value);
            m.Store(c, 0, 8);
            h.Store(hc, 0, 8);
            """).Verify(Shader);

        Assert.Equal(
            [
                "metal::simdgroup_float8x8 m = metal::make_filled_simdgroup_matrix<float, 8, 8>(__c.value);",
                "metal::simdgroup_half8x8 h = metal::make_filled_simdgroup_matrix<half, 8, 8>(__ms_to_half(__c.value));",
                "metal::simdgroup_store(m, c + 0, ulong(8), ulong2(0), false);",
                "metal::simdgroup_store(h, hc + 0, ulong(8), ulong2(0), false);",
            ],
            descriptor.KernelBody.Split('\n'));

        // Right after the standard library, before anything that names the types.
        Assert.StartsWith("#include <metal_stdlib>\n#include <metal_simdgroup_matrix>\n\nusing namespace metal;", descriptor.MslSource);
    }

    [Fact]
    public void AShaderWithoutAMatrix_DoesNotIncludeTheHeader()
    {
        ShaderDescriptor descriptor = RunBody("c[0] = a[0];").Verify(Shader);

        Assert.DoesNotContain("metal_simdgroup_matrix", descriptor.MslSource);
        Assert.False(UsesSimdGroups(RunBody("c[0] = a[0];")));
    }

    [Fact]
    public void FilledDiagonalDefaultAndNew_MapToTheirMetalForms()
    {
        Assert.Equal(
            [
                "metal::simdgroup_float8x8 filled = metal::make_filled_simdgroup_matrix<float, 8, 8>(2.5f);",
                "metal::simdgroup_float8x8 diagonal = metal::simdgroup_float8x8(__c.value);",
                "metal::simdgroup_float8x8 zero = metal::make_filled_simdgroup_matrix<float, 8, 8>(0.0f);",
                "metal::simdgroup_float8x8 created = metal::make_filled_simdgroup_matrix<float, 8, 8>(0.0f);",
                "metal::simdgroup_float8x8 later;",
                "later = metal::make_filled_simdgroup_matrix<float, 8, 8>(0.0f);",
                "metal::simdgroup_float8x8 inner = __ms_wave_matrix_multiply<float>(created, later);",
                "metal::simdgroup_store(__ms_wave_matrix_multiply_accumulate<float>(filled, diagonal, __ms_wave_matrix_multiply<float>(zero, inner)), c + 0, ulong(8), ulong2(0), false);",
            ],
            Body(
                """
                WaveMatrix8x8 filled = WaveMatrix8x8.Filled(2.5f);
                WaveMatrix8x8 diagonal = WaveMatrix8x8.Diagonal(value);
                WaveMatrix8x8 zero = default;
                WaveMatrix8x8 created = new WaveMatrix8x8();
                WaveMatrix8x8 later;
                later = default(WaveMatrix8x8);
                WaveMatrix8x8 inner = WaveMatrix8x8.Multiply(created, later);
                WaveMatrix8x8.MultiplyAccumulate(filled, diagonal, WaveMatrix8x8.Multiply(zero, inner)).Store(c, 0, 8);
                """));
    }

    [Fact]
    public void HalfFilledDiagonalAndDefault_MapToTheirMetalForms()
    {
        Assert.Equal(
            [
                "metal::simdgroup_half8x8 filled = metal::make_filled_simdgroup_matrix<half, 8, 8>(half(2.5f));",
                "metal::simdgroup_half8x8 diagonal = metal::simdgroup_half8x8(__ms_to_half(__c.value));",
                "metal::simdgroup_half8x8 zero = metal::make_filled_simdgroup_matrix<half, 8, 8>(0.0h);",
                "metal::simdgroup_store(__ms_wave_matrix_multiply_accumulate<half>(filled, diagonal, zero), hc + 0, ulong(8), ulong2(0), false);",
            ],
            Body(
                """
                WaveMatrixHalf8x8 filled = WaveMatrixHalf8x8.Filled((Half)2.5f);
                WaveMatrixHalf8x8 diagonal = WaveMatrixHalf8x8.Diagonal((Half)value);
                WaveMatrixHalf8x8 zero = default;
                WaveMatrixHalf8x8.MultiplyAccumulate(filled, diagonal, zero).Store(hc, 0, 8);
                """));
    }

    // ---- load and store: address spaces -----------------------------------------------------------

    [Fact]
    public void LoadAndStore_OnBuffers_UseDevicePointers()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            WaveMatrix8x8 fromReadOnly = WaveMatrix8x8.Load(a, n, 16);
            WaveMatrix8x8 fromReadWrite = WaveMatrix8x8.Load(c, n, 16, true);
            WaveMatrixHalf8x8 halves = WaveMatrixHalf8x8.Load(ha, 8, n, transpose: n > 4);
            fromReadOnly.Store(c, 64, 8);
            fromReadWrite.Store(c, stride: n, offset: 128, transpose: true);
            halves.Store(hc, 0, 8);
            """).Verify(Shader);

        Assert.Equal(
            [
                "metal::simdgroup_float8x8 fromReadOnly = __ms_wave_matrix_load(a + __c.n, ulong(16), false);",
                "metal::simdgroup_float8x8 fromReadWrite = __ms_wave_matrix_load(c + __c.n, ulong(16), true);",
                "metal::simdgroup_half8x8 halves = __ms_wave_matrix_load(ha + 8, ulong(__c.n), __c.n > 4);",
                "metal::simdgroup_store(fromReadOnly, c + 64, ulong(8), ulong2(0), false);",
                "metal::simdgroup_store(fromReadWrite, c + 128, ulong(__c.n), ulong2(0), true);",
                "metal::simdgroup_store(halves, hc + 0, ulong(8), ulong2(0), false);",
            ],
            descriptor.KernelBody.Split('\n'));

        // The pointers the kernel adds to, and the helper overload they select.
        Assert.Contains("const device float* a [[buffer(0)]]", descriptor.MslSource);
        Assert.Contains("device float* c [[buffer(1)]]", descriptor.MslSource);
        Assert.Contains("const device half* ha [[buffer(2)]]", descriptor.MslSource);
        Assert.Contains("device half* hc [[buffer(3)]]", descriptor.MslSource);
        Assert.Contains("__ms_wave_matrix_load(const device T* source, ulong stride, bool transpose)", descriptor.MslSource);
    }

    [Fact]
    public void LoadAndStore_OnGroupSharedArrays_UseThreadgroupPointers()
    {
        GeneratorRun run = RunBody(
            """
            tile[GroupIds.Index] = a[GroupIds.Index];
            halves[GroupIds.Index] = ha[GroupIds.Index];
            Gpu.GroupMemoryBarrierWithGroupSync();
            WaveMatrix8x8 m = WaveMatrix8x8.Load(tile, SimdGroupIds.Index * 64, 8);
            WaveMatrixHalf8x8 h = WaveMatrixHalf8x8.Load(halves, 0, 8, true);
            m.Store(tile, 64, 8, true);
            h.Store(halves, 64, 8);
            """,
            """
            [GroupShared] private static readonly float[] tile = new float[128];
            [GroupShared] private static readonly Half[] halves = new Half[128];
            """,
            "[ThreadGroupSize(64)]");

        ShaderDescriptor descriptor = run.Verify(Shader);
        string[] body = descriptor.KernelBody.Split('\n');

        Assert.Equal("threadgroup float tile[128];", body[0]);
        Assert.Equal("threadgroup half halves[128];", body[1]);

        Assert.Equal(
            [
                "metal::simdgroup_float8x8 m = __ms_wave_matrix_load(tile + int(uint(int(__sgid)) * 64u), ulong(8), false);",
                "metal::simdgroup_half8x8 h = __ms_wave_matrix_load(halves + 0, ulong(8), true);",
                "metal::simdgroup_store(m, tile + 64, ulong(8), ulong2(0), true);",
                "metal::simdgroup_store(h, halves + 64, ulong(8), ulong2(0), false);",
            ],
            body[5..]);

        Assert.Contains("__ms_wave_matrix_load(const threadgroup T* source, ulong stride, bool transpose)", descriptor.MslSource);
        Assert.True(descriptor.UsesThreadgroupMemory);
        Assert.True(UsesSimdGroups(run));
    }

    [Fact]
    public void InAHelper_ABufferAndAGroupSharedArray_ArriveAsPointersOfTheirAddressSpace()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            tile[GroupIds.Index] = a[GroupIds.Index];
            Gpu.GroupMemoryBarrierWithGroupSync();
            WaveMatrix8x8 sum = Product(SimdGroupIds.Index * 8);
            Keep(sum, 0);
            """,
            """
            [GroupShared] private static readonly float[] tile = new float[64];

            private WaveMatrix8x8 Product(int offset)
            {
                WaveMatrix8x8 left = WaveMatrix8x8.Load(a, offset, n);
                WaveMatrix8x8 right = WaveMatrix8x8.Load(tile, 0, 8);
                return WaveMatrix8x8.Multiply(left, right);
            }

            private void Keep(WaveMatrix8x8 matrix, int offset)
            {
                matrix.Store(c, offset, n);
                matrix.Store(tile, 0, 8);
            }
            """).Verify(Shader);

        Assert.Contains("static metal::simdgroup_float8x8 Product(int offset, const device float* a, threadgroup float* tile, constant __Constants& __c)", descriptor.MslSource);
        Assert.Contains("static void Keep(metal::simdgroup_float8x8 matrix, int offset, device float* c, threadgroup float* tile, constant __Constants& __c)", descriptor.MslSource);

        Assert.Equal(
            [
                "metal::simdgroup_float8x8 left = __ms_wave_matrix_load(a + offset, ulong(__c.n), false);",
                "metal::simdgroup_float8x8 right = __ms_wave_matrix_load(tile + 0, ulong(8), false);",
                "return __ms_wave_matrix_multiply<float>(left, right);",
            ],
            descriptor.FunctionBody("static metal::simdgroup_float8x8 Product").Split('\n'));

        Assert.Equal(
            [
                "metal::simdgroup_store(matrix, c + offset, ulong(__c.n), ulong2(0), false);",
                "metal::simdgroup_store(matrix, tile + 0, ulong(8), ulong2(0), false);",
            ],
            descriptor.FunctionBody("static void Keep").Split('\n'));
    }

    [Fact]
    public void AMatrix_PassesByValueAndByReference_AndThroughTheConditionalOperator()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            WaveMatrix8x8 sum = default;
            WaveMatrix8x8 one = WaveMatrix8x8.Diagonal(1f);
            Accumulate(ref sum, one, n > 2 ? one : sum);
            Make(out WaveMatrix8x8 made);
            sum.Store(c, 0, 8);
            made.Store(c, 64, 8);
            """,
            """
            private static void Accumulate(ref WaveMatrix8x8 sum, WaveMatrix8x8 left, in WaveMatrix8x8 right)
            {
                sum = WaveMatrix8x8.MultiplyAccumulate(left, right, sum);
            }

            private static void Make(out WaveMatrix8x8 matrix)
            {
                matrix = WaveMatrix8x8.Filled(3f);
            }
            """).Verify(Shader);

        Assert.Contains("static void Accumulate(thread metal::simdgroup_float8x8& sum, metal::simdgroup_float8x8 left, metal::simdgroup_float8x8 right)", descriptor.MslSource);
        Assert.Contains("static void Make(thread metal::simdgroup_float8x8& matrix)", descriptor.MslSource);
        Assert.Contains("Accumulate(sum, one, __c.n > 2 ? one : sum);", descriptor.KernelBody);
    }

    // ---- the offset: one pointer addition per term ------------------------------------------------

    [Theory]
    [InlineData("row * n + k", "a + int(uint(row) * uint(__c.n)) + k")]
    [InlineData("(row * n) + k", "a + (int(uint(row) * uint(__c.n))) + k")]
    [InlineData("(row + 8) * n + k + 8", "a + int((uint(row) + 8u) * uint(__c.n)) + k + 8")]
    [InlineData("k", "a + k")]
    [InlineData("0", "a + 0")]
    [InlineData("row * n", "a + int(uint(row) * uint(__c.n))")]
    [InlineData("row * n - k", "a + int(uint(row) * uint(__c.n)) - k")]
    [InlineData("row * n - (k + 8)", "a + int(uint(row) * uint(__c.n)) - k - 8")]
    [InlineData("row * n - (k - 8)", "a + int(uint(row) * uint(__c.n)) - k + 8")]
    [InlineData("(row * n + k) + (8 + row)", "a + int(uint(row) * uint(__c.n)) + k + 8 + row")]
    [InlineData("-k + row", "a + int(-uint(k)) + row")]
    [InlineData("row << 3", "a + (row << 3)")]
    [InlineData("row > 2 ? k : 8", "a + (row > 2 ? k : 8)")]
    [InlineData("(int)(urow + uk)", "a + int(urow + uk)")]
    [InlineData("(int)urow + (int)uk", "a + int(urow) + int(uk)")]
    [InlineData("Twice(row) + k", "a + ::Twice(row) + k")]
    public void TheOffset_IsAddedToThePointerTermByTerm(string offset, string pointer)
    {
        string[] body = Body(
            $$"""
            int row = SimdGroupIds.Index * 8;
            int k = SimdGroupIds.Count;
            uint urow = (uint)row;
            uint uk = (uint)k;
            WaveMatrix8x8 m = WaveMatrix8x8.Load(a, {{offset}}, n);
            m.Store(c, {{offset}}, n);
            """,
            "private static int Twice(int x) => x * 2;");

        Assert.Equal($"metal::simdgroup_float8x8 m = __ms_wave_matrix_load({pointer}, ulong(__c.n), false);", body[4]);
        Assert.Equal($"metal::simdgroup_store(m, {pointer.Replace("a +", "c +")}, ulong(__c.n), ulong2(0), false);", body[5]);
    }

    // ---- products ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("WaveMatrix8x8", "float")]
    [InlineData("WaveMatrixHalf8x8", "half")]
    public void Products_OfEveryMixOfHalfAndFloat_NameTheResultsElementType(string result, string element)
    {
        string[] names = ["f", "h"];
        var statements = new System.Text.StringBuilder();
        var expected = new System.Collections.Generic.List<string>();

        foreach (string left in names)
        {
            foreach (string right in names)
            {
                statements.AppendLine($"r = {result}.Multiply({left}, {right});");
                expected.Add($"r = __ms_wave_matrix_multiply<{element}>({left}, {right});");

                foreach (string addend in names)
                {
                    statements.AppendLine($"r = {result}.MultiplyAccumulate({left}, {right}, {addend});");
                    expected.Add($"r = __ms_wave_matrix_multiply_accumulate<{element}>({left}, {right}, {addend});");
                }
            }
        }

        ShaderDescriptor descriptor = RunBody(
            $$"""
            WaveMatrix8x8 f = WaveMatrix8x8.Load(a, 0, 8);
            WaveMatrixHalf8x8 h = WaveMatrixHalf8x8.Load(ha, 0, 8);
            {{result}} r = default;
            {{statements}}
            r.Store({{(element == "half" ? "hc" : "c")}}, 0, 8);
            """).Verify(Shader);

        Assert.Equal(12, expected.Count);
        Assert.Equal(expected, descriptor.KernelBody.Split('\n')[3..15]);

        // The helpers the calls go to: the result's element type is named, the operands' are deduced.
        Assert.Contains("template <typename R, typename T, typename U> inline metal::simdgroup_matrix<R, 8, 8> __ms_wave_matrix_multiply(", descriptor.MslSource);
        Assert.Contains("template <typename R, typename T, typename U, typename V> inline metal::simdgroup_matrix<R, 8, 8> __ms_wave_matrix_multiply_accumulate(", descriptor.MslSource);
        Assert.Contains("metal::simdgroup_multiply_accumulate(sum, a, b, c); return sum;", descriptor.MslSource);
    }

    [Fact]
    public void EveryOperation_MakesTheShaderReportUsesSimdGroups()
    {
        string[] bodies =
        [
            "WaveMatrix8x8 m = WaveMatrix8x8.Filled(1f);",
            "WaveMatrix8x8 m = WaveMatrix8x8.Diagonal(1f);",
            "WaveMatrix8x8 m = WaveMatrix8x8.Load(a, 0, 8);",
            "WaveMatrix8x8 m = default; m.Store(c, 0, 8);",
            "WaveMatrix8x8 m = default; m = WaveMatrix8x8.Multiply(m, m);",
            "WaveMatrix8x8 m = default; m = WaveMatrix8x8.MultiplyAccumulate(m, m, m);",
            "WaveMatrixHalf8x8 m = WaveMatrixHalf8x8.Load(ha, 0, 8);",
        ];

        foreach (string body in bodies)
        {
            GeneratorRun run = RunBody(body);

            run.Verify(Shader);
            Assert.True(UsesSimdGroups(run), body);
        }
    }

    [Fact]
    public void ThePublicSurface_IsWhatTheGeneratorTranslates()
    {
        // Every public method of the two types is one of the six operations: a member added to them
        // without a translation would be reported as SCG0016 only when a shader uses it.
        string[] known = ["Filled", "Diagonal", "Load", "Store", "Multiply", "MultiplyAccumulate"];

        foreach (Type type in new[] { typeof(WaveMatrix8x8), typeof(WaveMatrixHalf8x8) })
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.Equal(known.Order(), methods.Select(static m => m.Name).Distinct().Order());

            // Four ways to multiply and eight to accumulate: every mix of the two types.
            Assert.Equal(4, methods.Count(static m => m.Name == "Multiply"));
            Assert.Equal(8, methods.Count(static m => m.Name == "MultiplyAccumulate"));
            Assert.All(methods.Where(static m => m.Name is "Multiply" or "MultiplyAccumulate" or "Filled" or "Diagonal" or "Load"), m => Assert.Equal(type, m.ReturnType));

            // Each ends in the call site, which is how the CPU tells two calls apart.
            Assert.All(methods, static m => Assert.Equal(["callerFile", "callerLine"], m.GetParameters()[^2..].Select(static p => p.Name)));
        }
    }

    // ---- SCG0035: call sites the CPU cannot tell apart ----------------------------------------------

    [Fact]
    public void TwoCallsOfOneOperationOnOneLine_AreReported()
    {
        GeneratorRun run = RunBody(
            """
            WaveMatrix8x8 x = WaveMatrix8x8.Load(a, 0, 8), y = WaveMatrix8x8.Load(a, 64, 8);
            WaveMatrix8x8.Multiply(x, y).Store(c, 0, 8);
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0035", "WaveMatrix8x8.Load(a, 64, 8)");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("WaveMatrix8x8.Load", diagnostic.GetMessage());
    }

    [Fact]
    public void TheSameOperationOfTheTwoTypesOnOneLine_IsFine()
    {
        // The CPU knows them apart by their element type.
        RunBody(
            """
            WaveMatrix8x8 x = WaveMatrix8x8.Load(a, 0, 8); WaveMatrixHalf8x8 y = WaveMatrixHalf8x8.Load(ha, 0, 8);
            WaveMatrix8x8.Multiply(x, y).Store(c, 0, 8);
            """).Verify(Shader);
    }

    // ---- SCG0048: a matrix has no layout in memory ------------------------------------------------

    [Theory]
    [InlineData("WaveMatrix8x8")]
    [InlineData("WaveMatrixHalf8x8")]
    public void AMatrixAsCapturedState_IsReported(string type)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            $$"""
                public readonly {{type}} matrix;
                public readonly ReadWriteBuffer<float> c;

                public void Execute() { c[0] = 1; }
            """));

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0048", type);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"A '{type}' cannot be captured shader state (the field 'matrix')", diagnostic.GetMessage());
        Assert.Contains("Store", diagnostic.GetMessage());
    }

    [Fact]
    public void AMatrixAsAPrimaryConstructorParameter_IsReported()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace Tests;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader(ReadWriteBuffer<float> c, WaveMatrix8x8 start) : IComputeShader
            {
                public void Execute() { start.Store(c, 0, 8); }
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0048", "WaveMatrix8x8");

        Assert.Contains("captured shader state (the primary constructor parameter 'start')", diagnostic.GetMessage());
    }

    [Fact]
    public void AMatrixAsAFieldOfAStruct_IsReported()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace Tests;

            public struct Pair
            {
                public WaveMatrix8x8 Left;
                public float Scale;
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader : IComputeShader
            {
                public readonly ReadWriteBuffer<float> c;

                public void Execute()
                {
                    Pair pair;
                    c[0] = 1;
                }
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0048", "Pair");

        Assert.Contains("A 'WaveMatrix8x8' cannot be the field 'Left' of the struct 'Pair'", diagnostic.GetMessage());
    }

    [Fact]
    public void AMatrixAsTheElementOfAnInlineArray_IsReported()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace Tests;

            [System.Runtime.CompilerServices.InlineArray(4)]
            public struct Accumulators
            {
                private WaveMatrix8x8 _first;
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader : IComputeShader
            {
                public readonly ReadWriteBuffer<float> c;

                public void Execute()
                {
                    Accumulators sums = default;
                    sums[0].Store(c, 0, 8);
                }
            }
            """);

        Assert.Contains(run.Diagnostics, static d => d.Id == "SCG0048" && d.GetMessage().Contains("the element of the inline array 'Accumulators'", StringComparison.Ordinal));
        Assert.All(run.Diagnostics, static d => Assert.Equal("SCG0048", d.Id));
    }

    [Theory]
    // `new T[n]` would be `T acc[n] = {}` in MSL, which leaves the matrices uninitialized.
    [InlineData("WaveMatrix8x8[] acc = new WaveMatrix8x8[4];\nacc[1].Store(c, 0, 8);", "WaveMatrix8x8")]
    [InlineData("WaveMatrixHalf8x8[] acc = new WaveMatrixHalf8x8[4];\nacc[1].Store(hc, 0, 8);", "WaveMatrixHalf8x8")]
    [InlineData("WaveMatrix8x8 m = WaveMatrix8x8.Filled(1f);\nWaveMatrix8x8[] acc = new WaveMatrix8x8[] { m, m };\nacc[1].Store(c, 0, 8);", "WaveMatrix8x8")]
    [InlineData("WaveMatrix8x8 m = WaveMatrix8x8.Filled(1f);\nSpan<WaveMatrix8x8> acc = [m, m];\nacc[1].Store(c, 0, 8);", "WaveMatrix8x8")]
    [InlineData("WaveMatrix8x8 m = WaveMatrix8x8.Filled(1f);\nReadOnlySpan<WaveMatrix8x8> acc = [m, m];\nacc[1].Store(c, 0, 8);", "WaveMatrix8x8")]
    public void AMatrixAsTheElementOfALocalArray_IsReported(string body, string type)
    {
        GeneratorRun run = RunBody(body);

        Assert.Contains(run.Diagnostics, d => d.Id == "SCG0048" && d.GetMessage().Contains($"A '{type}' cannot be the element of the local array 'acc'", StringComparison.Ordinal));
        Assert.All(run.Diagnostics, static d => Assert.Equal("SCG0048", d.Id));
    }

    [Theory]
    [InlineData("WaveMatrix8x8")]
    [InlineData("WaveMatrixHalf8x8")]
    public void AMatrixAsTheElementOfAGroupSharedArray_IsReported(string type)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            $$"""
                public readonly ReadWriteBuffer<float> c;

                [GroupShared] private static readonly {{type}}[] matrices = new {{type}}[4];

                public void Execute() { c[0] = 1; }
            """, "[ThreadGroupSize(32)]"));

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0048", type + "[]");

        Assert.Contains($"A '{type}' cannot be the element of the [GroupShared] array 'matrices'", diagnostic.GetMessage());
    }

    [Fact]
    public void AMatrixAsTheElementOfABuffer_IsReported_NextToTheCompilersError()
    {
        // The buffer types take unmanaged elements, so C# rejects this as well (CS8377); the generator
        // says what to do instead.
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            """
                public readonly ReadWriteBuffer<WaveMatrix8x8> matrices;

                public void Execute() { }
            """));

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0048", "ReadWriteBuffer<WaveMatrix8x8>");

        Assert.Contains("A 'WaveMatrix8x8' cannot be the element of a buffer", diagnostic.GetMessage());
        Assert.Contains(run.Input.GetDiagnostics(), static d => d.Id == "CS8377");
    }

    // ---- SCG0049: the memory of a load or store ----------------------------------------------------

    [Theory]
    [InlineData("WaveMatrix8x8.Load(ints, 0, 8)", "ints", "its elements are 'int', and a WaveMatrix8x8 is loaded from 'float' elements only")]
    [InlineData("WaveMatrix8x8.Load(ha, 0, 8)", "ha", "its elements are 'Half', and a WaveMatrix8x8 is loaded from 'float' elements only (use WaveMatrixHalf8x8 for Half elements")]
    [InlineData("WaveMatrixHalf8x8.Load(a, 0, 8)", "a", "its elements are 'float', and a WaveMatrixHalf8x8 is loaded from 'Half' elements only (use WaveMatrix8x8 for float elements")]
    [InlineData("WaveMatrix8x8.Load(source: vectors, offset: 0, stride: 8)", "vectors", "its elements are 'Vec2'")]
    [InlineData("WaveMatrix8x8.Load(intTile, 0, 8)", "intTile", "its elements are 'int'")]
    [InlineData("WaveMatrix8x8.Filled(1f).Store(hc, 0, 8)", "hc", "its elements are 'Half', and a WaveMatrix8x8 is stored to 'float' elements only")]
    [InlineData("WaveMatrix8x8.Filled(1f).Store(a, 0, 8)", "a", "it is a ReadOnlyBuffer<float>, which a shader cannot write; store to a ReadWriteBuffer<float> or a [GroupShared] array")]
    [InlineData("WaveMatrixHalf8x8.Filled((Half)1f).Store(stride: 8, destination: ha, offset: 0)", "ha", "it is a ReadOnlyBuffer<Half>, which a shader cannot write")]
    public void MemoryOfAnotherElementType_IsExplained_NextToTheCompilersError(string call, string memory, string reason)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            $$"""
                public readonly ReadOnlyBuffer<float> a;
                public readonly ReadWriteBuffer<float> c;
                public readonly ReadOnlyBuffer<Half> ha;
                public readonly ReadWriteBuffer<Half> hc;
                public readonly ReadOnlyBuffer<int> ints;
                public readonly ReadOnlyBuffer<Vec2> vectors;

                [GroupShared] private static readonly int[] intTile = new int[64];

                public void Execute()
                {
                    {{call}};
                }
            """, "[ThreadGroupSize(32)]"));

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0049", memory);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(reason, diagnostic.GetMessage());
        Assert.Contains($"cannot use '{memory}'", diagnostic.GetMessage());

        // C# rejected the call itself: no overload takes that memory.
        Assert.Contains(run.Input.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void AnArrayThatIsNotGroupShared_IsReported()
    {
        GeneratorRun run = RunBody(
            """
            WaveMatrix8x8 m = WaveMatrix8x8.Load(table, 0, 8);
            m.Store(c, 0, 8);
            """,
            "private static readonly float[] table = new float[64];");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0049", "table");

        Assert.Contains("'WaveMatrix8x8.Load' cannot use 'table': it is not a [GroupShared] array of the shader", diagnostic.GetMessage());
    }

    [Fact]
    public void AStoreToAnArrayThatIsNotGroupShared_IsReported()
    {
        GeneratorRun run = RunBody(
            "WaveMatrixHalf8x8.Filled((Half)1f).Store(table, 0, 8);",
            "private static readonly Half[] table = new Half[64];");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0049", "table");

        Assert.Contains("'WaveMatrixHalf8x8.Store' cannot use 'table'", diagnostic.GetMessage());
        Assert.Contains("stored to a captured buffer or a [GroupShared] array", diagnostic.GetMessage());
    }

    [Fact]
    public void AnInvalidCallThatIsNotAboutTheMemory_GetsNoExplanation()
    {
        // A missing argument: the compiler's error says all there is to say.
        GeneratorRun run = RunBody("WaveMatrix8x8 m = WaveMatrix8x8.Load(a, 0);");

        Assert.Empty(run.Diagnostics);
        Assert.Contains(run.Input.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }

    // ---- SCG0050: static fields -------------------------------------------------------------------

    [Theory]
    [InlineData("WaveMatrix8x8", "WaveMatrix8x8.Diagonal(1f)")]
    [InlineData("WaveMatrix8x8", "default")]
    [InlineData("WaveMatrixHalf8x8", "WaveMatrixHalf8x8.Filled((Half)2f)")]
    public void AMatrixInAStaticReadonlyField_IsReported(string type, string initializer)
    {
        GeneratorRun run = RunBody(
            "Identity.Store(c, 0, 8);".Replace("c,", type == "WaveMatrix8x8" ? "c," : "hc,"),
            $"private static readonly {type} Identity = {initializer};");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0050", "Identity");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"Static field 'TestShader.Identity' is a '{type}'", diagnostic.GetMessage());
        Assert.Contains("Create it in Execute", diagnostic.GetMessage());
    }

    [Fact]
    public void AStaticReadonlyMatrixThatNoShaderReads_IsNotReported()
    {
        // Like every static field: only what the kernel uses is lowered.
        RunBody(
            "c[0] = a[0];",
            "private static readonly WaveMatrix8x8 Identity = default;").AssertClean();
    }

    // ---- SCG0051: a threadgroup that is not a multiple of 32 threads ------------------------------

    [Theory]
    [InlineData("[ThreadGroupSize(48)]", "48, 1, 1", "48", "16")]
    [InlineData("[ThreadGroupSize(8, 5)]", "8, 5, 1", "40", "8")]
    [InlineData("[ThreadGroupSize(4, 4, 1)]", "4, 4, 1", "16", "16")]
    public void AThreadgroupThatIsNotAMultipleOf32_IsReported(string attribute, string size, string threads, string lanes)
    {
        GeneratorRun run = RunBody(
            """
            WaveMatrix8x8 m = WaveMatrix8x8.Load(a, 0, 8);
            m.Store(c, 0, 8);
            """,
            attributes: attribute);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0051", attribute[1..^1]);

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains($"[ThreadGroupSize({size})] has {threads} threads", diagnostic.GetMessage());
        Assert.Contains($"has only {lanes} lanes", diagnostic.GetMessage());

        // A warning: the kernel is still generated, and compiles.
        run.Descriptor(Shader).AssertMslCompiles();
    }

    [Theory]
    [InlineData("[ThreadGroupSize(32)]")]
    [InlineData("[ThreadGroupSize(128)]")]
    [InlineData("[ThreadGroupSize(16, 16)]")]
    [InlineData("[ThreadGroupSize(8, 8, 4)]")]
    [InlineData("")] // the runtime picks 256, 16 x 16 or 8 x 8 x 4
    public void AThreadgroupOfWholeSimdGroups_IsFine(string attribute)
    {
        RunBody(
            """
            WaveMatrix8x8 m = WaveMatrix8x8.Load(a, 0, 8);
            m.Store(c, 0, 8);
            """,
            attributes: attribute).Verify(Shader);
    }

    [Fact]
    public void AShaderWithoutAMatrix_MayHaveAnyThreadgroupSize()
    {
        RunBody("c[0] = Gpu.WaveActiveSum(a[0]);", attributes: "[ThreadGroupSize(48)]").Verify(Shader);
    }
}
