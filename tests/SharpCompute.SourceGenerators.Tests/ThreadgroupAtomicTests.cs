using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// <c>Gpu.Atomic*</c> on elements of <c>[GroupShared]</c> arrays: the same MSL functions as for a buffer
/// element, on a pointer in the <c>threadgroup</c> address space, in the kernel and in helpers; what
/// the descriptor says about a shader whose atomics never leave a threadgroup; the float add, which
/// MSL lacks there and the prelude supplies; and SCG0034 for the group-shared locations that still
/// cannot be atomic.
/// </summary>
public class ThreadgroupAtomicTests
{
    private const string Shader = "Tests.TestShader";

    private const string Members = """
            public readonly ReadWriteBuffer<int> output;
            public readonly ReadWriteBuffer<uint> totals;
            public readonly int seed;

            [GroupShared] private static readonly int[] tile = new int[64];
            [GroupShared] private static readonly uint[] bits = new uint[8];
            [GroupShared] private static readonly float[] sums = new float[4];
            [GroupShared] private static readonly Cell[] cells = new Cell[4];
            [GroupShared] private static readonly Vec2[] points = new Vec2[4];
        """;

    private const string Cell = "\npublic struct Cell { public int Count; public float Weight; }";

    private static GeneratorRun Run(string body, string extraMembers = "", string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            $$"""
            {{Members}}
            {{extraMembers}}

                public void Execute()
                {
                    {{body}}
                }
            """,
            "[ThreadGroupSize(64)]") + Cell + trailer);

    /// <summary>The kernel body after the declarations of the arrays it uses (only those are declared).</summary>
    private static string Statements(ShaderDescriptor descriptor)
    {
        string[] lines = descriptor.KernelBody.Split('\n');
        string[] declarations = lines.TakeWhile(static line => line.StartsWith("threadgroup ", System.StringComparison.Ordinal)).ToArray();

        Assert.NotEmpty(declarations);
        Assert.All(declarations, static line => Assert.Matches(@"^threadgroup (int tile\[64\]|uint bits\[8\]|float sums\[4\]|Cell cells\[4\]|packed_float2 points\[4\]);$", line));

        return string.Join("\n", lines.Skip(declarations.Length));
    }

    private static void AssertKernel(string expected, string body) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), Statements(Run(body).Verify(Shader)));

    // ---- the MSL ----------------------------------------------------------------------------------

    [Fact]
    public void EveryIntOperation_MapsToTheSameMetalFunction_OnAThreadgroupAtomic()
    {
        AssertKernel(
            """
            int i = int(__gtid.x);
            int a = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(tile[i]), 1, metal::memory_order_relaxed);
            int b = metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_int*)&(tile[i]), a, metal::memory_order_relaxed);
            int c = metal::atomic_fetch_min_explicit((threadgroup metal::atomic_int*)&(tile[int(uint(i) + 1u)]), b, metal::memory_order_relaxed);
            int d = metal::atomic_fetch_max_explicit((threadgroup metal::atomic_int*)&(tile[int(uint(i) + 1u)]), c, metal::memory_order_relaxed);
            int e = metal::atomic_fetch_and_explicit((threadgroup metal::atomic_int*)&(tile[2]), d, metal::memory_order_relaxed);
            int f = metal::atomic_fetch_or_explicit((threadgroup metal::atomic_int*)&(tile[2]), e, metal::memory_order_relaxed);
            int g = metal::atomic_fetch_xor_explicit((threadgroup metal::atomic_int*)&(tile[2]), f, metal::memory_order_relaxed);
            int h = metal::atomic_exchange_explicit((threadgroup metal::atomic_int*)&(tile[3]), g, metal::memory_order_relaxed);
            output[int(__tid.x)] = h;
            """,
            """
            int i = GroupIds.X;
            int a = Gpu.AtomicAdd(ref tile[i], 1);
            int b = Gpu.AtomicSub(ref tile[i], a);
            int c = Gpu.AtomicMin(ref tile[i + 1], b);
            int d = Gpu.AtomicMax(ref tile[i + 1], c);
            int e = Gpu.AtomicAnd(ref tile[2], d);
            int f = Gpu.AtomicOr(ref tile[2], e);
            int g = Gpu.AtomicXor(ref tile[2], f);
            int h = Gpu.AtomicExchange(ref tile[3], g);
            output[ThreadIds.X] = h;
            """);
    }

    [Fact]
    public void EveryUIntOperation_UsesAtomicUInt()
    {
        AssertKernel(
            """
            uint v = uint(int(__gtid.x));
            uint a = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_uint*)&(bits[0]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_uint*)&(bits[0]), 1u, metal::memory_order_relaxed);
            a += metal::atomic_fetch_min_explicit((threadgroup metal::atomic_uint*)&(bits[1]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_max_explicit((threadgroup metal::atomic_uint*)&(bits[1]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_and_explicit((threadgroup metal::atomic_uint*)&(bits[2]), ~v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_or_explicit((threadgroup metal::atomic_uint*)&(bits[2]), 1u << int(v & 31u), metal::memory_order_relaxed);
            a += metal::atomic_fetch_xor_explicit((threadgroup metal::atomic_uint*)&(bits[2]), v, metal::memory_order_relaxed);
            a += metal::atomic_exchange_explicit((threadgroup metal::atomic_uint*)&(bits[3]), a, metal::memory_order_relaxed);
            totals[int(__tid.x)] = a;
            """,
            """
            uint v = (uint)GroupIds.X;
            uint a = Gpu.AtomicAdd(ref bits[0], v);
            a += Gpu.AtomicSub(ref bits[0], 1);
            a += Gpu.AtomicMin(ref bits[1], v);
            a += Gpu.AtomicMax(ref bits[1], v);
            a += Gpu.AtomicAnd(ref bits[2], ~v);
            a += Gpu.AtomicOr(ref bits[2], 1u << (int)(v & 31));
            a += Gpu.AtomicXor(ref bits[2], v);
            a += Gpu.AtomicExchange(ref bits[3], a);
            totals[ThreadIds.X] = a;
            """);
    }

    [Fact]
    public void CompareExchange_CallsThePreludeHelper_WhichHasAnOverloadForThreadgroupMemory()
    {
        ShaderDescriptor descriptor = Run(
            """
            int seen = Gpu.AtomicCompareExchange(ref tile[0], 0, GroupIds.X + 1);
            uint other = Gpu.AtomicCompareExchange(ref bits[0], comparand: 7u, value: 9u);
            output[ThreadIds.X] = seen + (int)other;
            """).Verify(Shader);

        Assert.Equal(
            """
            int seen = __ms_atomic_compare_exchange((threadgroup metal::atomic_int*)&(tile[0]), 0, int(__gtid.x + 1u));
            uint other = __ms_atomic_compare_exchange((threadgroup metal::atomic_uint*)&(bits[0]), 7u, 9u);
            output[int(__tid.x)] = int(uint(seen) + other);
            """.ReplaceLineEndings("\n"),
            Statements(descriptor));

        // The same retry loop as for a buffer element, overloaded on the pointer's address space.
        Assert.Contains("inline int __ms_atomic_compare_exchange(threadgroup metal::atomic_int* location, int comparand, int value)", descriptor.MslSource);
        Assert.Contains("inline uint __ms_atomic_compare_exchange(threadgroup metal::atomic_uint* location, uint comparand, uint value)", descriptor.MslSource);
    }

    [Fact]
    public void OtherAtomics_DoNotNeedThePrelude()
    {
        ShaderDescriptor descriptor = Run("Gpu.AtomicAdd(ref tile[0], 1);").Verify(Shader);

        Assert.Equal("metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(tile[0]), 1, metal::memory_order_relaxed);", Statements(descriptor));
        Assert.DoesNotContain("__ms_", descriptor.MslSource);
    }

    [Fact]
    public void NamedArguments_AreEmittedInParameterOrder_AndTheIndexMayBeAnyExpression()
    {
        AssertKernel(
            """
            int bin = int(__tid.x) & 255;
            metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(tile[(int(uint(bin) * 2u + uint(__c.seed))) % 64]), int(uint(bin) - 1u), metal::memory_order_relaxed);
            """,
            """
            int bin = ThreadIds.X & 255;
            Gpu.AtomicAdd(value: bin - 1, location: ref tile[(bin * 2 + seed) % tile.Length]);
            """);
    }

    [Fact]
    public void ThreadgroupAndDeviceAtomics_SitSideBySide_EachInItsOwnAddressSpace()
    {
        // The privatised histogram: count within the group, then one buffer atomic per bin per group.
        AssertKernel(
            """
            int local = int(__gindex);
            if (local < 64)
            {
                tile[local] = 0;
            }
            metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
            metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(tile[int(__tid.x) & 63]), 1, metal::memory_order_relaxed);
            metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
            if (local < 64)
            {
                metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(output[local]), tile[local], metal::memory_order_relaxed);
            }
            """,
            """
            int local = GroupIds.Index;

            if (local < tile.Length)
            {
                tile[local] = 0;
            }

            Gpu.GroupMemoryBarrierWithGroupSync();
            Gpu.AtomicAdd(ref tile[ThreadIds.X & 63], 1);
            Gpu.GroupMemoryBarrierWithGroupSync();

            if (local < tile.Length)
            {
                Gpu.AtomicAdd(ref output[local], tile[local]);
            }
            """);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    [Fact]
    public void EveryOperation_WorksInsideAHelper_OnTheThreadgroupPointerItReceives()
    {
        ShaderDescriptor descriptor = Run(
            """
            int ints = Ints(GroupIds.X);
            output[ThreadIds.X] = ints + (int)UInts((uint)GroupIds.X) + Claim(GroupIds.X);
            """,
            """
            private static int Ints(int i)
            {
                int a = Gpu.AtomicAdd(ref tile[i], 1);
                a += Gpu.AtomicSub(ref tile[i], a);
                a += Gpu.AtomicMin(ref tile[i + 1], a);
                a += Gpu.AtomicMax(ref tile[i + 1], a);
                a += Gpu.AtomicAnd(ref tile[2], a);
                a += Gpu.AtomicOr(ref tile[2], a);
                a += Gpu.AtomicXor(ref tile[2], a);
                a += Gpu.AtomicExchange(ref tile[3], a);
                return a + Gpu.AtomicCompareExchange(ref tile[4], 0, a);
            }

            private uint UInts(uint v)
            {
                uint a = Gpu.AtomicAdd(ref bits[0], v);
                a += Gpu.AtomicSub(ref bits[0], 1);
                a += Gpu.AtomicMin(ref bits[1], v);
                a += Gpu.AtomicMax(ref bits[1], v);
                a += Gpu.AtomicAnd(ref bits[2], ~v);
                a += Gpu.AtomicOr(ref bits[2], v);
                a += Gpu.AtomicXor(ref bits[2], v);
                a += Gpu.AtomicExchange(ref bits[3], a + (uint)seed);
                return a + Gpu.AtomicCompareExchange(ref bits[4], 0u, a);
            }

            private static int Claim(int i)
            {
                return Local(i) + 1;

                static int Local(int slot) => Gpu.AtomicExchange(ref tile[slot], slot);
            }
            """).Verify(Shader);

        // The array reaches a helper as a `threadgroup T*`, and the atomic view of an element is a
        // pointer in that address space, exactly as in the kernel.
        Assert.Equal(
            """
            int a = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(tile[i]), 1, metal::memory_order_relaxed);
            reinterpret_cast<thread uint&>(a) += uint(metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_int*)&(tile[i]), a, metal::memory_order_relaxed));
            reinterpret_cast<thread uint&>(a) += uint(metal::atomic_fetch_min_explicit((threadgroup metal::atomic_int*)&(tile[int(uint(i) + 1u)]), a, metal::memory_order_relaxed));
            reinterpret_cast<thread uint&>(a) += uint(metal::atomic_fetch_max_explicit((threadgroup metal::atomic_int*)&(tile[int(uint(i) + 1u)]), a, metal::memory_order_relaxed));
            reinterpret_cast<thread uint&>(a) += uint(metal::atomic_fetch_and_explicit((threadgroup metal::atomic_int*)&(tile[2]), a, metal::memory_order_relaxed));
            reinterpret_cast<thread uint&>(a) += uint(metal::atomic_fetch_or_explicit((threadgroup metal::atomic_int*)&(tile[2]), a, metal::memory_order_relaxed));
            reinterpret_cast<thread uint&>(a) += uint(metal::atomic_fetch_xor_explicit((threadgroup metal::atomic_int*)&(tile[2]), a, metal::memory_order_relaxed));
            reinterpret_cast<thread uint&>(a) += uint(metal::atomic_exchange_explicit((threadgroup metal::atomic_int*)&(tile[3]), a, metal::memory_order_relaxed));
            return int(uint(a) + uint(__ms_atomic_compare_exchange((threadgroup metal::atomic_int*)&(tile[4]), 0, a)));
            """.ReplaceLineEndings("\n"),
            descriptor.FunctionBody("static int Ints(int i, threadgroup int* tile)"));

        Assert.Equal(
            """
            uint a = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_uint*)&(bits[0]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_uint*)&(bits[0]), 1u, metal::memory_order_relaxed);
            a += metal::atomic_fetch_min_explicit((threadgroup metal::atomic_uint*)&(bits[1]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_max_explicit((threadgroup metal::atomic_uint*)&(bits[1]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_and_explicit((threadgroup metal::atomic_uint*)&(bits[2]), ~v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_or_explicit((threadgroup metal::atomic_uint*)&(bits[2]), v, metal::memory_order_relaxed);
            a += metal::atomic_fetch_xor_explicit((threadgroup metal::atomic_uint*)&(bits[2]), v, metal::memory_order_relaxed);
            a += metal::atomic_exchange_explicit((threadgroup metal::atomic_uint*)&(bits[3]), a + uint(__c.seed), metal::memory_order_relaxed);
            return a + __ms_atomic_compare_exchange((threadgroup metal::atomic_uint*)&(bits[4]), 0u, a);
            """.ReplaceLineEndings("\n"),
            descriptor.FunctionBody("static uint UInts(uint v, threadgroup uint* bits, constant __Constants& __c)"));

        // A local function of a helper, two calls from the kernel.
        Assert.Contains("static int Claim_Local(int slot, threadgroup int* tile);", descriptor.MslSource);
        Assert.Contains("return metal::atomic_exchange_explicit((threadgroup metal::atomic_int*)&(tile[slot]), slot, metal::memory_order_relaxed);", descriptor.MslSource);

        // Two calls that both make atomics on `tile` are split: their order is unspecified in MSL (SCG0021).
        Assert.Equal(
            """
            int ints = ::Ints(int(__gtid.x), tile);
            output[int(__tid.x)] = int(uint(ints) + ::UInts(uint(int(__gtid.x)), bits, __c) + uint(::Claim(int(__gtid.x), tile)));
            """.ReplaceLineEndings("\n").TrimEnd('\n'),
            Statements(descriptor));
    }

    [Fact]
    public void AnArrayOnlyAnAtomicInAHelperUses_IsStillDeclaredAndPassed()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            """
                [GroupShared] private static readonly uint[] seen = new uint[2];

                public void Execute() => Mark(GroupIds.X);

                private static void Mark(int bit) => Gpu.AtomicOr(ref seen[bit >> 5], 1u << (bit & 31));
            """,
            "[ThreadGroupSize(64)]")).Verify(Shader);

        Assert.Equal("threadgroup uint seen[2];\n::Mark(int(__gtid.x), seen);", descriptor.KernelBody);
        Assert.Equal(
            "metal::atomic_fetch_or_explicit((threadgroup metal::atomic_uint*)&(seen[bit >> 5]), 1u << (bit & 31), metal::memory_order_relaxed);",
            descriptor.FunctionBody("static void Mark(int bit, threadgroup uint* seen)"));
        Assert.True(descriptor.UsesThreadgroupMemory);
        Assert.Equal(8, descriptor.ThreadgroupMemoryLength);
    }

    [Fact]
    public void UserDefinedAtomicMethods_AreDrivenByTheAttributeAlone()
    {
        const string Custom = """

            public static class MyAtomics
            {
                [MslAtomic("metal::atomic_fetch_add_explicit")]
                public static int Increment(ref int location, int by) => throw null!;

                [MslAtomic("metal::atomic_exchange_explicit")]
                public static TestVectors.Vec2 Swap(ref TestVectors.Vec2 location, TestVectors.Vec2 value) => throw null!;

                [MslAtomic("metal::atomic_exchange_explicit")]
                public static float Swap(ref float location, float value) => throw null!;

                [MslAtomic("metal::atomic_fetch_add_explicit", ThreadgroupFloat = "__ms_atomic_fetch_add_float")]
                public static float Accumulate(ref float location, float value) => throw null!;
            }
            """;

        Assert.Equal(
            "metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(tile[1]), 2, metal::memory_order_relaxed);",
            Statements(Run("MyAtomics.Increment(ref tile[1], 2);", trailer: Custom).Verify(Shader)));

        Diagnostic vector = Run("MyAtomics.Swap(ref points[1], new Vec2(1, 2));", trailer: Custom).AssertOnlyDiagnostic("SCG0034", "points[1]");
        Assert.Contains("MSL has no atomic type for 'Vec2' elements (only for int, uint and float)", vector.GetMessage());

        // The generator knows no atomic by name: a float element of threadgroup memory is accepted
        // exactly when the method's attribute names a function that works on the bits.
        Assert.Equal(
            "__ms_atomic_fetch_add_float((threadgroup metal::atomic_uint*)&(sums[1]), 2.0f);",
            Statements(Run("MyAtomics.Accumulate(ref sums[1], 2f);", trailer: Custom).Verify(Shader)));

        Diagnostic single = Run("MyAtomics.Swap(ref sums[1], 2f);", trailer: Custom).AssertOnlyDiagnostic("SCG0034", "sums[1]");
        Assert.Contains(
            "'sums[1]' cannot be the target of the atomic operation 'Swap': it is a float element of a [GroupShared] array, where MSL 3.2 has no atomic float: " +
            "only an add is available there (Gpu.AtomicAdd, a compare-and-swap loop over the element's bits).",
            single.GetMessage());
        Assert.Contains("or an element of a [GroupShared] array of int, uint or float, by reference", single.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, single.Severity);
    }

    // ---- the float add ----------------------------------------------------------------------------

    [Fact]
    public void FloatAdd_IsACompareAndSwapLoopOverTheBits_BecauseMslHasNoThreadgroupAtomicFloat()
    {
        ShaderDescriptor descriptor = Run(
            """
            float before = Gpu.AtomicAdd(ref sums[GroupIds.X & 3], 0.5f);
            output[ThreadIds.X] = (int)(before + Total(seed));
            """,
            """
            private static float Total(float step)
            {
                float local = Local(step);
                return local + Gpu.AtomicAdd(value: step, location: ref sums[1]);

                static float Local(float by) => Gpu.AtomicAdd(ref sums[2], -by);
            }
            """).Verify(Shader);

        // The element is viewed as an atomic_uint, and no memory order is passed: the helper has the only one there is.
        Assert.Equal(
            """
            float before = __ms_atomic_fetch_add_float((threadgroup metal::atomic_uint*)&(sums[int(__gtid.x) & 3]), 0.5f);
            output[int(__tid.x)] = int(before + ::Total(float(__c.seed), sums));
            """.ReplaceLineEndings("\n"),
            Statements(descriptor));

        // In a helper and in its local function, on the `threadgroup float*` they receive.
        Assert.Equal(
            """
            float local = ::Total_Local(step, sums);
            return local + __ms_atomic_fetch_add_float((threadgroup metal::atomic_uint*)&(sums[1]), step);
            """.ReplaceLineEndings("\n").TrimEnd('\n'),
            descriptor.FunctionBody("static float Total(float step, threadgroup float* sums)"));
        Assert.Equal(
            "return __ms_atomic_fetch_add_float((threadgroup metal::atomic_uint*)&(sums[2]), -by);",
            descriptor.FunctionBody("static float Total_Local(float by, threadgroup float* sums)"));

        // The prelude's loop: swap in the bits of the sum until no other thread got in between.
        Assert.Contains(
            "inline float __ms_atomic_fetch_add_float(threadgroup metal::atomic_uint* location, float value) " +
            "{ uint expected = metal::atomic_load_explicit(location, metal::memory_order_relaxed); " +
            "while (!metal::atomic_compare_exchange_weak_explicit(location, &expected, as_type<uint>(as_type<float>(expected) + value), " +
            "metal::memory_order_relaxed, metal::memory_order_relaxed)) { } return as_type<float>(expected); }",
            descriptor.MslSource);

        // Threadgroup memory only: the shader can be split.
        Assert.True(descriptor.UsesAtomics);
        Assert.False(descriptor.UsesDeviceAtomics);
    }

    [Fact]
    public void FloatAdd_OnABufferElement_StaysMslsOwnAtomicFloat()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            """
                public readonly ReadWriteBuffer<float> totals;

                [GroupShared] private static readonly float[] sums = new float[4];

                public void Execute()
                {
                    Gpu.AtomicAdd(ref totals[0], Gpu.AtomicAdd(ref sums[0], 1f));
                }
            """,
            "[ThreadGroupSize(64)]")).Verify(Shader);

        Assert.Equal(
            """
            threadgroup float sums[4];
            metal::atomic_fetch_add_explicit((device metal::atomic<float>*)&(totals[0]), __ms_atomic_fetch_add_float((threadgroup metal::atomic_uint*)&(sums[0]), 1.0f), metal::memory_order_relaxed);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
        Assert.True(descriptor.UsesDeviceAtomics);
    }

    // ---- the descriptor ---------------------------------------------------------------------------

    private const string AtomicsMember = "static bool global::SharpCompute.IComputeShaderDescriptor<global::Tests.TestShader>.UsesAtomics => true;";
    private const string DeviceAtomicsMember = "static bool global::SharpCompute.IComputeShaderDescriptor<global::Tests.TestShader>.UsesDeviceAtomics => false;";

    [Fact]
    public void AShaderWhoseOnlyAtomicsAreOnThreadgroupMemory_UsesAtomics_ButNoDeviceAtomics()
    {
        // The runtime refuses to divide a shader between the CPU and the GPU when UsesDeviceAtomics is
        // true. It defaults to UsesAtomics, so the generator states false where the atomics all stay
        // within a threadgroup.
        GeneratorRun direct = Run("Gpu.AtomicAdd(ref tile[GroupIds.X & 3], 1);");
        ShaderDescriptor descriptor = direct.Verify(Shader);

        Assert.True(descriptor.UsesAtomics);
        Assert.False(descriptor.UsesDeviceAtomics);
        Assert.True(descriptor.UsesThreadgroupMemory);
        Assert.Contains(AtomicsMember, direct.Source("TestShader.SharpCompute.g.cs"));
        Assert.Contains(DeviceAtomicsMember, direct.Source("TestShader.SharpCompute.g.cs"));

        // The same when the atomic is in a helper, two calls down, and when it is the prelude's.
        GeneratorRun viaHelpers = Run(
            "output[ThreadIds.X] = Outer(GroupIds.X);",
            """
            private int Outer(int value) => Inner(value) + 1;

            private static int Inner(int value) => (int)Gpu.AtomicCompareExchange(ref bits[0], 0u, (uint)value);
            """);

        Assert.True(viaHelpers.Verify(Shader).UsesAtomics);
        Assert.False(viaHelpers.Verify(Shader).UsesDeviceAtomics);
        Assert.Contains(DeviceAtomicsMember, viaHelpers.Source("TestShader.SharpCompute.g.cs"));
    }

    [Theory]
    [InlineData("Gpu.AtomicAdd(ref tile[0], 1); Gpu.AtomicAdd(ref output[0], 1);", "")]
    [InlineData("Gpu.AtomicAdd(ref output[0], 1); Gpu.AtomicAdd(ref tile[0], 1);", "")]
    [InlineData("Gpu.AtomicAdd(ref tile[0], 1); Merge();", "private void Merge() => Gpu.AtomicOr(ref totals[0], 1u);")]
    [InlineData("Count(); Gpu.AtomicAdd(ref output[0], 1);", "private static void Count() => Gpu.AtomicAdd(ref tile[0], 1);")]
    [InlineData("tile[GroupIds.X] = 1; Gpu.AtomicAdd(ref output[0], 1);", "")]
    public void OneAtomicOnABufferElement_MakesItADeviceAtomicsShader_WhereverItIs(string body, string extraMembers)
    {
        GeneratorRun run = Run(body, extraMembers);
        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.True(descriptor.UsesAtomics);
        Assert.True(descriptor.UsesDeviceAtomics);
        Assert.Contains(AtomicsMember, run.Source("TestShader.SharpCompute.g.cs"));
        Assert.DoesNotContain("UsesDeviceAtomics", run.Source("TestShader.SharpCompute.g.cs"));
    }

    [Fact]
    public void AnAtomicThatIsNeverReached_DoesNotCount()
    {
        // Helpers are only translated when called: an uncalled one with a buffer atomic leaves a
        // threadgroup-only shader splittable, and one with a threadgroup atomic leaves a plain shader plain.
        GeneratorRun threadgroupOnly = Run(
            "Gpu.AtomicAdd(ref tile[0], 1);",
            "private void Unused() => Gpu.AtomicAdd(ref output[0], 1);");

        Assert.True(threadgroupOnly.Verify(Shader).UsesAtomics);
        Assert.False(threadgroupOnly.Verify(Shader).UsesDeviceAtomics);

        GeneratorRun none = Run(
            "tile[GroupIds.X] = 1;",
            "private static void Unused() => Gpu.AtomicAdd(ref tile[0], 1);");

        Assert.False(none.Verify(Shader).UsesAtomics);
        Assert.False(none.Verify(Shader).UsesDeviceAtomics);
        Assert.DoesNotContain("Atomics", none.Source("TestShader.SharpCompute.g.cs"));
    }

    // ---- SCG0034 ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Gpu.AtomicAdd(ref cells[GroupIds.X & 3].Count, 1);", "cells[GroupIds.X & 3].Count", "it is a field of a struct element, and only whole elements can be atomic; keep the value in its own [GroupShared] array of int or uint")]
    [InlineData("Gpu.AtomicAdd(ref cells[0].Weight, 1f);", "cells[0].Weight", "it is a field of a struct element")]
    [InlineData("int local = tile[0]; Gpu.AtomicAdd(ref local, 1); tile[0] = local;", "local", "it is a local, which is private to one GPU thread and never shared")]
    [InlineData("Cell cell = cells[0]; Gpu.AtomicAdd(ref cell.Count, 1); cells[0] = cell;", "cell.Count", "it is a local")]
    public void SCG0034_OnlyWholeElementsOfTheArrayItself_CanBeAtomic(string body, string expectedText, string expectedReason)
    {
        Diagnostic diagnostic = Run(body).AssertOnlyDiagnostic("SCG0034", expectedText);

        Assert.Contains($"'{expectedText}' cannot be the target of the atomic operation 'AtomicAdd': {expectedReason}", diagnostic.GetMessage());
    }

    [Fact]
    public void SCG0034_AnElementOfAnArrayThatIsNotGroupShared_IsNotAtomicStorage()
    {
        // A static array without [GroupShared] is a program-scope constant in MSL: read-only, and in no
        // address space that has atomics.
        GeneratorRun run = Run(
            "Gpu.AtomicAdd(ref Table[GroupIds.X & 3], 1);",
            "private static readonly int[] Table = { 1, 2, 3, 4 };");

        Diagnostic diagnostic = run.Diagnostics.Single(static d => d.Id == "SCG0034");

        Assert.Equal("Table[GroupIds.X & 3]", run.TextAt(diagnostic));
        Assert.Contains("it is not an element of a buffer or of a [GroupShared] array", diagnostic.GetMessage());
    }

    [Fact]
    public void AnElementOfAnotherTypesGroupSharedArray_IsReportedAsThatField_NotAsAnAtomicTarget()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            """
            using SharpCompute;

            namespace Tests;

            public static class Shared
            {
                [GroupShared] public static readonly int[] Tile = new int[4];
            }

            [ThreadGroupSize(64)]
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader : IComputeShader
            {
                public void Execute() => Gpu.AtomicAdd(ref Shared.Tile[0], 1);
            }
            """);

        // SCG0040 says what is wrong (the array belongs to another type); an SCG0034 would only add noise.
        run.AssertOnlyDiagnostic("SCG0040", "Shared.Tile");
    }

    [Fact]
    public void AnElementPassedByReferenceToAHelper_IsStillRejected_AlsoWhenTheHelperOnlyCallsAnAtomic()
    {
        // The helper's `ref int` is a reference into the thread address space; the atomic needs the
        // element itself, so it is written where the array is indexed.
        GeneratorRun run = Run(
            "Add(ref tile[0]);",
            "private static void Add(ref int target) => Gpu.AtomicAdd(ref target, 1);");

        Assert.Equal(["SCG0034", "SCG0012"], run.Diagnostics.Select(static d => d.Id));
        Assert.Contains("it is a local", run.Diagnostics[0].GetMessage());
        Assert.Equal("ref tile[0]", run.TextAt(run.Diagnostics[1]));
    }

    [Fact]
    public void LocationsThatAreNotValidCSharp_AreLeftToTheCompiler()
    {
        // A vector component is a property (CS0206), and an int overload does not take an element of a
        // Vec2[] (CS1503): the compiler's errors say it all.
        foreach (string body in new[] { "Gpu.AtomicAdd(ref points[0].X, 1f);", "Gpu.AtomicAdd(ref points[0], 1);" })
        {
            GeneratorRun run = Run(body);

            Assert.Contains(run.Input.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
            Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "SCG0034");
        }
    }
}
