using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// <c>Gpu.Atomic*</c> (<c>[MslAtomic]</c>): the MSL each operation maps to, and SCG0034 for every
/// location that is not an element of a <c>ReadWriteBuffer&lt;int/uint/float&gt;</c>.
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
            int c = metal::atomic_fetch_min_explicit((device metal::atomic_int*)&(counts[i + 1]), b, metal::memory_order_relaxed);
            int d = metal::atomic_fetch_max_explicit((device metal::atomic_int*)&(counts[i + 1]), c, metal::memory_order_relaxed);
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
            int seen = __ms_atomic_compare_exchange((device metal::atomic_int*)&(counts[0]), 0, int(__tid.x) + 1);
            uint other = __ms_atomic_compare_exchange((device metal::atomic_uint*)&(flags[0]), 7u, 9u);
            counts[1] = seen + int(other);
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
    public void NamedArguments_AreEmittedInParameterOrder_AndTheIndexMayBeAnyExpression()
    {
        AssertKernel(
            """
            int bin = int(__tid.x) & 255;
            metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[(bin * 2 + __c.seed) % 16]), bin - 1, metal::memory_order_relaxed);
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
        Assert.Contains("as in 'ref buffer[i]'", diagnostic.GetMessage());
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
