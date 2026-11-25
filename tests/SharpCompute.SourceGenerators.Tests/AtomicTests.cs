using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// <c>Gpu.Atomic*</c> (<c>[MslAtomic]</c>) on buffer elements: the MSL each operation maps to, and
/// SCG0034 for every location that is not atomic storage. Atomics on threadgroup memory are in
/// <see cref="ThreadgroupAtomicTests"/>.
/// </summary>
public class AtomicTests
{
    private const string Shader = "Tests.TestShader";

    private const string Buffers = """
            public readonly ReadWriteBuffer<int> counts;
            public readonly ReadWriteBuffer<uint> flags;
            public readonly ReadWriteBuffer<float> sums;
            public readonly ReadOnlyBuffer<int> frozen;
            public readonly ReadWriteBuffer<Cell> cells;
            public readonly ReadWriteBuffer<Vec2> points;
            public readonly int seed;
        """;

    private const string Cell = "\npublic struct Cell { public int Count; public float Weight; }";

    private static GeneratorRun Run(string body, string extraMembers = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader($$"""
            {{Buffers}}
            {{extraMembers}}

                public void Execute()
                {
                    {{body}}
                }
            """) + Cell);

    private static void AssertKernel(string expected, string body) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), Run(body).Verify(Shader).KernelBody);

    [Fact]
    public void EveryIntOperation_MapsToItsExplicitMetalFunctionOnADeviceAtomic()
    {
        AssertKernel(
            """
            int i = int(__tid.x);
            int a = metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[i]), 1, metal::memory_order_relaxed);
            int b = metal::atomic_fetch_sub_explicit((device metal::atomic_int*)&(counts[i]), a, metal::memory_order_relaxed);
            int c = metal::atomic_fetch_min_explicit((device metal::atomic_int*)&(counts[int(uint(i) + 1u)]), b, metal::memory_order_relaxed);
            int d = metal::atomic_fetch_max_explicit((device metal::atomic_int*)&(counts[int(uint(i) + 1u)]), c, metal::memory_order_relaxed);
            int e = metal::atomic_fetch_and_explicit((device metal::atomic_int*)&(counts[2]), d, metal::memory_order_relaxed);
            int f = metal::atomic_fetch_or_explicit((device metal::atomic_int*)&(counts[2]), e, metal::memory_order_relaxed);
            int g = metal::atomic_fetch_xor_explicit((device metal::atomic_int*)&(counts[2]), f, metal::memory_order_relaxed);
            int h = metal::atomic_exchange_explicit((device metal::atomic_int*)&(counts[3]), g, metal::memory_order_relaxed);
            counts[4] = h;
            """,
            """
            int i = ThreadIds.X;
            int a = Gpu.AtomicAdd(ref counts[i], 1);
            int b = Gpu.AtomicSub(ref counts[i], a);
            int c = Gpu.AtomicMin(ref counts[i + 1], b);
            int d = Gpu.AtomicMax(ref counts[i + 1], c);
            int e = Gpu.AtomicAnd(ref counts[2], d);
            int f = Gpu.AtomicOr(ref counts[2], e);
            int g = Gpu.AtomicXor(ref counts[2], f);
            int h = Gpu.AtomicExchange(ref counts[3], g);
            counts[4] = h;
            """);
    }

    [Fact]
    public void EveryUIntOperation_UsesAtomicUInt()
    {
        AssertKernel(
            """
            uint v = uint(int(__tid.x));
            uint a = metal::atomic_fetch_add_explicit((device metal::atomic_uint*)&(flags[0]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_sub_explicit((device metal::atomic_uint*)&(flags[0]), 1u, metal::memory_order_relaxed);
            a += metal::atomic_fetch_min_explicit((device metal::atomic_uint*)&(flags[1]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_max_explicit((device metal::atomic_uint*)&(flags[1]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_and_explicit((device metal::atomic_uint*)&(flags[2]), ~v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_or_explicit((device metal::atomic_uint*)&(flags[2]), 1u << int(v & 31u), metal::memory_order_relaxed);
            a += metal::atomic_fetch_xor_explicit((device metal::atomic_uint*)&(flags[2]), v, metal::memory_order_relaxed);
            a += metal::atomic_exchange_explicit((device metal::atomic_uint*)&(flags[3]), a, metal::memory_order_relaxed);
            flags[4] = a;
            """,
            """
            uint v = (uint)ThreadIds.X;
            uint a = Gpu.AtomicAdd(ref flags[0], v);
            a += Gpu.AtomicSub(ref flags[0], 1);
            a += Gpu.AtomicMin(ref flags[1], v);
            a += Gpu.AtomicMax(ref flags[1], v);
            a += Gpu.AtomicAnd(ref flags[2], ~v);
            a += Gpu.AtomicOr(ref flags[2], 1u << (int)(v & 31));
            a += Gpu.AtomicXor(ref flags[2], v);
            a += Gpu.AtomicExchange(ref flags[3], a);
            flags[4] = a;
            """);
    }

    [Fact]
    public void FloatAdd_UsesTheAtomicFloatTemplate()
    {
        AssertKernel(
            """
            float before = metal::atomic_fetch_add_explicit((device metal::atomic<float>*)&(sums[0]), 0.5f, metal::memory_order_relaxed);
            sums[1] = before;
            """,
            """
            float before = Gpu.AtomicAdd(ref sums[0], 0.5f);
            sums[1] = before;
            """);
    }

    [Fact]
    public void CompareExchange_CallsThePreludeHelper_WithoutAMemoryOrder_AndPullsInThePrelude()
    {
        ShaderDescriptor descriptor = Run(
            """
            int seen = Gpu.AtomicCompareExchange(ref counts[0], 0, ThreadIds.X + 1);
            uint other = Gpu.AtomicCompareExchange(ref flags[0], comparand: 7u, value: 9u);
            counts[1] = seen + (int)other;
            """).Verify(Shader);

        Assert.Equal(
            """
            int seen = __ms_atomic_compare_exchange((device metal::atomic_int*)&(counts[0]), 0, int(__tid.x + 1u));
            uint other = __ms_atomic_compare_exchange((device metal::atomic_uint*)&(flags[0]), 7u, 9u);
            counts[1] = int(uint(seen) + other);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        // The strong CAS loops over MSL's weak one, with the only memory order MSL has.
        Assert.Contains("inline int __ms_atomic_compare_exchange(device metal::atomic_int* location, int comparand, int value)", descriptor.MslSource);
        Assert.Contains("while (!metal::atomic_compare_exchange_weak_explicit(location, &expected, value, metal::memory_order_relaxed, metal::memory_order_relaxed) && expected == comparand) { }", descriptor.MslSource);
    }

    [Fact]
    public void OtherAtomics_DoNotNeedThePrelude()
    {
        ShaderDescriptor descriptor = Run("Gpu.AtomicAdd(ref counts[0], 1);").Verify(Shader);

        Assert.Equal("metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[0]), 1, metal::memory_order_relaxed);", descriptor.KernelBody);
        Assert.DoesNotContain("__ms_", descriptor.MslSource);
    }

    [Fact]
    public void TheDescriptor_SaysWhetherTheShaderUsesAtomics()
    {
        // The runtime reads UsesDeviceAtomics to keep a CPU+GPU split from dividing the shader. UsesAtomics
        // is a static virtual with a default of false, so the member is only emitted when it is true;
        // UsesDeviceAtomics defaults to UsesAtomics, which is right for atomics on buffers, so it is
        // not emitted here at all.
        const string Member = "static bool global::SharpCompute.IComputeShaderDescriptor<global::Tests.TestShader>.UsesAtomics => true;";

        GeneratorRun direct = Run("Gpu.AtomicAdd(ref counts[0], 1);");
        Assert.True(direct.Verify(Shader).UsesAtomics);
        Assert.True(direct.Verify(Shader).UsesDeviceAtomics);
        Assert.Contains(Member, direct.Source("TestShader.SharpCompute.g.cs"));
        Assert.DoesNotContain("UsesDeviceAtomics", direct.Source("TestShader.SharpCompute.g.cs"));

        // An atomic in a helper, two calls down, counts as well.
        GeneratorRun viaHelpers = Run(
            "counts[1] = Outer(ThreadIds.X);",
            """
            private int Outer(int value) => Inner(value) + 1;

            private int Inner(int value) => (int)Gpu.AtomicCompareExchange(ref flags[0], 0u, (uint)value);
            """);
        Assert.True(viaHelpers.Verify(Shader).UsesAtomics);
        Assert.True(viaHelpers.Verify(Shader).UsesDeviceAtomics);
        Assert.Contains(Member, viaHelpers.Source("TestShader.SharpCompute.g.cs"));

        // No atomic: the default applies, also when a helper with an atomic exists but is never called.
        GeneratorRun without = Run(
            "counts[ThreadIds.X] += 1;",
            "private void Unused() => Gpu.AtomicAdd(ref counts[0], 1);");
        Assert.False(without.Verify(Shader).UsesAtomics);
        Assert.False(without.Verify(Shader).UsesDeviceAtomics);
        Assert.DoesNotContain("UsesAtomics", without.Source("TestShader.SharpCompute.g.cs"));
        Assert.DoesNotContain("UsesDeviceAtomics", without.Source("TestShader.SharpCompute.g.cs"));
    }

    [Fact]
    public void Atomics_InAShaderThatCapturesTextures_KeepTheirBuffersAndTheFlag()
    {
        // Textures take their own binding indices and are passed to helpers next to the buffers; an
        // atomic still addresses its buffer element, directly and inside a helper that also reads a
        // texture. A pixel read is a native float4, so the matrix operand conversion is a no-op on it.
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TextureHistogram(
                ReadOnlyTexture2D<Rgba32> source,
                ReadWriteTexture2D<Float4> target,
                ReadWriteBuffer<int> counts,
                ReadWriteBuffer<uint> seen,
                Float4x4 tint) : IComputeShader
            {
                private int Count(Int2 xy)
                {
                    uint packed = Gpu.PackUnorm4x8(source[xy]);
                    Gpu.AtomicOr(ref seen[0], packed);
                    return Gpu.AtomicAdd(ref counts[(int)(packed & 255u)], 1);
                }

                public void Execute()
                {
                    Int2 xy = ThreadIds.XY;
                    int before = Count(xy);
                    Gpu.AtomicMax(ref counts[256], before);
                    Float2 uv = (new Float2(xy.X, xy.Y) + 0.5f) / new Float2(source.Width, source.Height);
                    target[xy] = Gpu.Mul(tint, source[xy]) + Gpu.Mul(source.Sample(Sampler.LinearClamp, uv), tint) + Gpu.Mul(tint, target[xy]);
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("TextureHistogram");

        Assert.True(descriptor.UsesAtomics);
        Assert.Contains(
            "static bool global::SharpCompute.IComputeShaderDescriptor<global::TextureHistogram>.UsesAtomics => true;",
            run.Source("TextureHistogram.SharpCompute.g.cs"));
        Assert.Equal(
            """
            int2 xy = int2(__tid.xy);
            int before = ::Count(xy, source, seen, counts);
            metal::atomic_fetch_max_explicit((device metal::atomic_int*)&(counts[256]), before, metal::memory_order_relaxed);
            float2 uv = (float2(float(xy.x), float(xy.y)) + 0.5f) / float2(float(int(source.get_width())), float(int(source.get_height())));
            target.write((float4(source.read(uint2(xy))) * __ms_unpack(__c.tint)) + (__ms_unpack(__c.tint) * float4(source.sample(__ms_sampler_linear_clamp, uv, metal::level(0)))) + (float4(target.read(uint2(xy))) * __ms_unpack(__c.tint)), uint2(xy));
            target.fence();
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        // The helper gets the texture and both buffers; the atomics address the buffers it was given.
        Assert.Contains(
            """
            static int Count(int2 xy, texture2d<float, access::sample> source, device uint* seen, device int* counts)
            {
                uint packed = metal::pack_float_to_unorm4x8(source.read(uint2(xy)));
                metal::atomic_fetch_or_explicit((device metal::atomic_uint*)&(seen[0]), packed, metal::memory_order_relaxed);
                return metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[int(packed & 255u)]), 1, metal::memory_order_relaxed);
            }
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        // Textures and buffers are numbered separately, so the atomics' buffers start at 0.
        Assert.Contains("texture2d<float, access::read_write> target [[texture(1)]],\n    device int* counts [[buffer(0)]],\n    device uint* seen [[buffer(1)]],", descriptor.MslSource);
    }

    [Fact]
    public void ATextureElement_IsNotAnAtomicLocation()
    {
        // A pixel is read and written through Metal's texture functions and has no address. C# already
        // refuses it (the indexer returns a Float4 by value, CS0206); the generator must not emit an atomic for it.
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Bad(ReadWriteTexture2D<float> target) : IComputeShader
            {
                public void Execute()
                {
                    Gpu.AtomicAdd(ref target[ThreadIds.XY], 1f);
                }
            }
            """);

        Assert.Contains(run.Input.GetDiagnostics(), static d => d.Id == "CS0206");
        Assert.DoesNotContain(run.Sources, static source => source.SourceText.ToString().Contains("atomic_fetch", System.StringComparison.Ordinal));
    }

    [Fact]
    public void NamedArguments_AreEmittedInParameterOrder_AndTheIndexMayBeAnyExpression()
    {
        AssertKernel(
            """
            int bin = int(__tid.x) & 255;
            metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[(int(uint(bin) * 2u + uint(__c.seed))) % 16]), int(uint(bin) - 1u), metal::memory_order_relaxed);
            """,
            """
            int bin = ThreadIds.X & 255;
            Gpu.AtomicAdd(value: bin - 1, location: ref counts[(bin * 2 + seed) % 16]);
            """);
    }

    [Fact]
    public void Atomics_WorkInsideHelpers_OnTheCapturedBufferAndOnABufferParameter()
    {
        ShaderDescriptor descriptor = Run(
            """
            int slot = Reserve();
            Bump(counts, slot);
            """,
            """
            private int Reserve() => Gpu.AtomicAdd(ref counts[0], 1);

            private static void Bump(ReadWriteBuffer<int> target, int index) => Gpu.AtomicMax(ref target[index], index);
            """).Verify(Shader);

        Assert.Contains("return metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[0]), 1, metal::memory_order_relaxed);", descriptor.MslSource);
        Assert.Contains("metal::atomic_fetch_max_explicit((device metal::atomic_int*)&(target[index]), index, metal::memory_order_relaxed);", descriptor.MslSource);
    }

    // ---- SCG0034 --------------------------------------------------------------------------------

    [Theory]
    [InlineData("int local = 0; Gpu.AtomicAdd(ref local, 1); counts[0] = local;", "local", "it is a local, which is private to one GPU thread and never shared")]
    [InlineData("Cell cell = cells[0]; Gpu.AtomicAdd(ref cell.Count, 1); cells[0] = cell;", "cell.Count", "it is a local")]
    [InlineData("Gpu.AtomicAdd(ref cells[ThreadIds.X].Count, 1);", "cells[ThreadIds.X].Count", "it is a field of a struct element, and only whole elements can be atomic")]
    [InlineData("Gpu.AtomicAdd(ref cells[0].Weight, 1f);", "cells[0].Weight", "it is a field of a struct element")]
    public void SCG0034_TheLocationMustBeABufferElement(string body, string expectedText, string expectedReason)
    {
        GeneratorRun run = Run(body);
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0034", expectedText);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"'{expectedText}' cannot be the target of the atomic operation 'AtomicAdd': {expectedReason}", diagnostic.GetMessage());
        Assert.Contains("or an element of a [GroupShared] array of int, uint or float, by reference, as in 'ref buffer[i]'", diagnostic.GetMessage());
        Assert.Contains("This shader has SharpCompute errors", Assert.Single(run.Sources).SourceText.ToString());
    }

    [Fact]
    public void SCG0034_HelperParametersAreLocalsToo()
    {
        GeneratorRun run = Run(
            "int value = 0; Add(ref value); counts[0] = value;",
            "private static void Add(ref int target) => Gpu.AtomicAdd(ref target, 1);");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0034", "target");
        Assert.Contains("it is a local", diagnostic.GetMessage());
    }

    [Fact]
    public void SCG0034_AReadOnlyBufferElement_IsExplained_OnTopOfTheCompilerError()
    {
        // C# already rejects `ref` to a `ref readonly` element (CS8329); SCG0034 says what to use instead.
        GeneratorRun run = Run("Gpu.AtomicAdd(ref frozen[0], 1);");

        Assert.Contains(run.Input.GetDiagnostics(), static d => d.Id == "CS8329");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0034", "frozen[0]");
        Assert.Contains("it is an element of a ReadOnlyBuffer<T>, which a shader cannot write", diagnostic.GetMessage());
    }

    [Fact]
    public void LocationsThatAreNotValidCSharp_AreLeftToTheCompiler()
    {
        // A vector component is a property (CS0206) and captured state is readonly (CS0192):
        // the compiler's errors say it all, so the generator adds nothing.
        foreach (string body in new[] { "Gpu.AtomicAdd(ref points[0].X, 1f);", "Gpu.AtomicAdd(ref seed, 1);" })
        {
            GeneratorRun run = Run(body);

            Assert.Contains(run.Input.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
            Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "SCG0034");
        }
    }

    [Fact]
    public void UserDefinedAtomicMethods_AreDrivenByTheAttributeAlone()
    {
        // The generator knows no atomic by name: any [MslAtomic] method maps the same way, and one on
        // an element type without an MSL atomic is SCG0034.
        const string Custom = """

            public static class MyAtomics
            {
                [MslAtomic("metal::atomic_fetch_add_explicit")]
                public static int Increment(ref int location, int by) => throw null!;

                [MslAtomic("metal::atomic_exchange_explicit")]
                public static Vec2 Swap(ref Vec2 location, Vec2 value) => throw null!;
            }
            """;

        GeneratorRun valid = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader($$"""
            {{Buffers}}

                public void Execute() => MyAtomics.Increment(ref counts[1], 2);
            """) + Cell + Custom);

        Assert.Equal(
            "metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[1]), 2, metal::memory_order_relaxed);",
            valid.Verify(Shader).KernelBody);

        GeneratorRun invalid = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader($$"""
            {{Buffers}}

                public void Execute() => MyAtomics.Swap(ref points[1], new Vec2(1, 2));
            """) + Cell + Custom);

        Diagnostic diagnostic = invalid.AssertOnlyDiagnostic("SCG0034", "points[1]");
        Assert.Contains("MSL has no atomic type for 'Vec2' elements (only for int, uint and float)", diagnostic.GetMessage());
    }

    [Fact]
    public void AnAtomicCallInAStaticInitializer_IsNotAConstant()
    {
        GeneratorRun run = Run(
            "counts[0] = Start;",
            "private static readonly int[] Storage = new int[1];\n    private static readonly int Start = Gpu.AtomicAdd(ref Storage[0], 1);");

        Assert.Contains(run.Diagnostics, static d => d.Id == "SCG0024");
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "SCG0034");
    }
}
