using System.Linq;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Signed <c>+</c>, <c>-</c>, <c>*</c> and negation are emitted through the unsigned type of the same
/// width, where overflow wraps as in C#; compound assignments and increments keep the native operator
/// on the target viewed as unsigned where something in them has side effects, so that the target is
/// evaluated once and in the same order as before. Every result is compiled with Metal.
/// </summary>
public class SignedArithmeticTests
{
    private const string Shader = "Tests.TestShader";

    private static ShaderDescriptor Translate(string body, string members = "") =>
        GeneratorHarness.Run($$"""
            using System;
            using SharpCompute;

            namespace Tests;

            public struct Cell
            {
                public int Count;
                public Int3 Sum;
            }

            [GeneratedComputeShaderDescriptor]
            [ThreadGroupSize(64, 1, 1)]
            public readonly partial struct TestShader(ReadWriteBuffer<int> ints, ReadWriteBuffer<long> longs, ReadWriteBuffer<Int3> vectors, ReadWriteBuffer<Cell> cells, ReadWriteBuffer<uint> uints) : IComputeShader
            {
                [GroupShared]
                private static readonly int[] Tile = new int[64];

                {{members}}

                public void Execute()
                {
                    int i = ThreadIds.X;
                    int a = ints[0], b = ints[1];
                    {{body}}
                }

                private int Bump(int slot)
                {
                    ints[slot] += 1;
                    return 10;
                }

                private static int Next(int slot) => slot;
            }
            """).Verify(Shader);

    private static string Statement(string body, string members = "") =>
        Translate(body, members).KernelBody.Split('\n').Last();

    [Theory]
    [InlineData("ints[2] = a * b;", "ints[2] = int(uint(a) * uint(b));")]
    [InlineData("ints[2] = a * b + 3;", "ints[2] = int(uint(a) * uint(b) + 3u);")]
    [InlineData("ints[2] = (a + 1) * (b - 1);", "ints[2] = int((uint(a) + 1u) * (uint(b) - 1u));")]
    [InlineData("ints[2] = a * -3;", "ints[2] = int(uint(a) * uint(-3));")]
    [InlineData("ints[2] = -a;", "ints[2] = int(-uint(a));")]
    [InlineData("ints[2] = -(-a);", "ints[2] = int(-(-uint(a)));")]
    [InlineData("ints[2] = a - -b;", "ints[2] = int(uint(a) - -uint(b));")]
    [InlineData("ints[2] = -5;", "ints[2] = -5;")]
    [InlineData("ints[2] = (a * b) / b;", "ints[2] = (int(uint(a) * uint(b))) / b;")]
    [InlineData("ints[2] = a + 1 > a ? 1 : 0;", "ints[2] = int(uint(a) + 1u) > a ? 1 : 0;")]
    [InlineData("longs[2] = (long)(a * b);", "longs[2] = long(int(uint(a) * uint(b)));")]
    [InlineData("longs[2] = (long)a * b;", "longs[2] = long(ulong(a) * ulong(b));")]
    [InlineData("longs[2] = longs[0] * 6364136223846793005L + 1;", "longs[2] = long(ulong(longs[0]) * 6364136223846793005UL + 1UL);")]
    [InlineData("longs[2] = longs[0] - (uint)a;", "longs[2] = long(ulong(longs[0]) - ulong(uint(a)));")]
    [InlineData("ints[2] = i + 1;", "ints[2] = int(uint(i) + 1u);")]
    [InlineData("ints[2] = ThreadIds.X + 1;", "ints[2] = int(__tid.x + 1u);")]
    [InlineData("ints[2] = (int)uints[0] + a;", "ints[2] = int(uints[0] + uint(a));")]
    [InlineData("vectors[0] = vectors[1] * 3 + new Int3(a);", "vectors[0] = int3(uint3(vectors[1]) * 3u + uint3(int3(a)));")]
    [InlineData("vectors[0] = -vectors[1];", "vectors[0] = int3(-uint3(vectors[1]));")]
    public void SignedArithmetic_IsComputedUnsigned_AndConvertedBack(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("uints[2] = uints[0] * uints[1] + 1;", "uints[2] = uints[0] * uints[1] + 1u;")]
    [InlineData("ints[2] = a / b;", "ints[2] = a / b;")]
    [InlineData("ints[2] = a % b;", "ints[2] = a % b;")]
    [InlineData("ints[2] = a << b;", "ints[2] = a << b;")]
    [InlineData("ints[2] = (a & b) | (a ^ ~b) >> 3;", "ints[2] = (a & b) | ((a ^ ~b) >> 3);")]
    public void UnsignedArithmetic_DivisionShiftsAndBitwiseOperators_AreUnchanged(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Fact]
    public void HalfAndBoolVectors_AndIntVectorComparisons_AreUnchanged()
    {
        string[] lines = Translate(
            """
            Half2 h = new Half2((Half)1.5f, (Half)2);
            h = (h * h) + h - h;
            h += h;
            Bool2 less = new Int2(a, b) < new Int2(b, a) + 1;
            ints[2] = Gpu.Any(less) ? (int)(float)h.X : 0;
            """).KernelBody.Split('\n');

        Assert.Contains("h = (h * h) + h - h;", lines);
        Assert.Contains("h += h;", lines);
        // The comparison stays a comparison; only the signed sum it compares is computed unsigned.
        Assert.Contains("bool2 less = int2(a, b) < int2(uint2(int2(b, a)) + 1u);", lines);
    }

    [Theory]
    [InlineData("int x = a; x += b;", "x = int(uint(x) + uint(b));")]
    [InlineData("int x = a; x *= 3;", "x = int(uint(x) * 3u);")]
    [InlineData("long x = a; x -= a;", "x = long(ulong(x) - ulong(a));")]
    [InlineData("int x = a; x++;", "x = int(uint(x) + 1u);")]
    [InlineData("long x = a; --x;", "x = long(ulong(x) - 1UL);")]
    [InlineData("ints[i] += b;", "ints[i] = int(uint(ints[i]) + uint(b));")]
    [InlineData("vectors[i] *= 3;", "vectors[i] = int3(uint3(vectors[i]) * 3u);")]
    [InlineData("for (int k = 0; k < b; k++) { }", "for (int k = 0; k < b; k = int(uint(k) + 1u))")]
    public void CompoundAssignmentsAndIncrements_WithoutSideEffects_AssignTheWrappedResult(string body, string expected) =>
        Assert.Contains(expected, Translate(body).KernelBody.Split('\n'));

    [Theory]
    [InlineData("short s = (short)a; s += 7; s++; ints[2] = s;", "s += 7;", "s++;")]
    [InlineData("sbyte s = (sbyte)a; s *= 3; s--; ints[2] = s;", "s *= 3;", "s--;")]
    [InlineData("byte s = (byte)a; s -= 1; ++s; ints[2] = s;", "s -= 1;", "++s;")]
    public void SmallIntegerTargets_StayNative_BecauseTheirIntArithmeticCannotOverflow(string body, string compound, string increment)
    {
        string[] lines = Translate(body).KernelBody.Split('\n');

        Assert.Contains(compound, lines);
        Assert.Contains(increment, lines);
    }

    [Fact]
    public void CompoundAssignments_WithSideEffects_KeepTheNativeOperatorAndItsOrder()
    {
        // The cases where the order of evaluation shows: each keeps the shape it had before, the
        // native operator with the target named once, so the parts are evaluated as they were.
        string kernel = Translate(
            """
            ints[0] += Bump(0);
            ints[ints[8]] = Bump(8);
            ints[24] += ints[24] = 5;
            ints[ints[32]] += Bump(32);
            ints[Next(40)] *= b;
            longs[Next(0)] -= a;
            vectors[Next(1)] += new Int3(b);
            cells[Next(2)].Count += Bump(3);
            cells[Next(2)].Sum *= 3;
            Tile[Next(i)] += b;
            int x = a;
            x += Bump(4);
            """).KernelBody;

        Assert.Contains("reinterpret_cast<device uint&>(ints[0]) += uint(::Bump(0, ints));", kernel);
        Assert.Contains("ints[ints[8]] = ::Bump(8, ints);", kernel);
        Assert.Contains("reinterpret_cast<device uint&>(ints[24]) += uint(ints[24] = 5);", kernel);
        Assert.Contains("reinterpret_cast<device uint&>(ints[ints[32]]) += uint(::Bump(32, ints));", kernel);
        Assert.Contains("reinterpret_cast<device uint&>(ints[::Next(40)]) *= uint(b);", kernel);
        Assert.Contains("reinterpret_cast<device ulong&>(longs[::Next(0)]) -= ulong(a);", kernel);
        Assert.Contains("reinterpret_cast<device packed_uint3&>(vectors[::Next(1)]) += uint3(int3(b));", kernel);
        Assert.Contains("reinterpret_cast<device uint&>(cells[::Next(2)].Count) += uint(::Bump(3, ints));", kernel);
        Assert.Contains("reinterpret_cast<device packed_uint3&>(cells[::Next(2)].Sum) *= 3u;", kernel);
        Assert.Contains("reinterpret_cast<threadgroup uint&>(Tile[::Next(i)]) += uint(b);", kernel);
        Assert.Contains("reinterpret_cast<thread uint&>(x) += uint(::Bump(4, ints));", kernel);
    }

    [Fact]
    public void Increments_WhoseValueIsUsedOrWhoseTargetHasSideEffects_KeepTheNativeOperator()
    {
        string kernel = Translate(
            """
            int n = a;
            ints[n++] = 1;
            ints[--n] = 2;
            ints[Next(3)]++;
            long m = longs[0];
            longs[1] = m-- + 1;
            """).KernelBody;

        Assert.Contains("ints[int(reinterpret_cast<thread uint&>(n)++)] = 1;", kernel);
        Assert.Contains("ints[int(--reinterpret_cast<thread uint&>(n))] = 2;", kernel);
        Assert.Contains("reinterpret_cast<device uint&>(ints[::Next(3)])++;", kernel);
        Assert.Contains("longs[1] = long(ulong(long(reinterpret_cast<thread ulong&>(m)--)) + 1UL);", kernel);
    }

    [Fact]
    public void AVectorComponent_ModifiedInPlace_WithSideEffects_ViewsTheWholeVectorAsUnsigned()
    {
        string kernel = Translate("vectors[Next(0)].X += 1; vectors[Next(1)].YZ *= 3; Int3 v = vectors[2]; v.Z += Bump(5); ints[2] = v.Y++;").KernelBody;

        Assert.Contains("reinterpret_cast<device packed_uint3&>(vectors[::Next(0)]).x += 1u;", kernel);
        Assert.Contains("reinterpret_cast<device packed_uint3&>(vectors[::Next(1)]).yz *= 3u;", kernel);
        Assert.Contains("reinterpret_cast<thread uint3&>(v).z += uint(::Bump(5, ints));", kernel);
        Assert.Contains("ints[2] = int(reinterpret_cast<thread uint3&>(v).y++);", kernel);
    }

    [Fact]
    public void AVectorComponent_ModifiedInPlace_WithoutSideEffects_AssignsTheWrappedResult_UnlessItsValueIsUsed()
    {
        string kernel = Translate("Int2 v = new Int2(a, b); v.X += b; v.Y++; ints[2] = v.X++;").KernelBody;

        Assert.Contains("v.x = int(uint(v.x) + uint(b));", kernel);
        Assert.Contains("v.y = int(uint(v.y) + 1u);", kernel);
        Assert.Contains("ints[2] = int(reinterpret_cast<thread uint2&>(v).x++);", kernel);
    }

    [Fact]
    public void ProgramScopeInitializers_WrapWhereTheyAreEmitted_AndWhereTheyAreFolded()
    {
        ShaderDescriptor descriptor = Translate(
            "ints[2] = Wrapped + Folded + Negated + (int)Long + Vector.X + (int)Unsigned;",
            """
            private static readonly int Big = int.MaxValue;
            private static readonly int Wrapped = Big + 1;
            private static readonly int Negated = -Wrapped;
            private static readonly long Long = (long)Big * Big * 5;
            private static readonly Int2 Vector = new Int2(Big, Wrapped) * 2;
            private static readonly int Folded = Gpu.Abs(Big) + Gpu.Max(Big + 2, Wrapped * 3);
            private static readonly uint Unsigned = Gpu.Max(4000000001u, 2u);
            """);

        Assert.Contains("constant int Wrapped = int(uint(::Big) + 1u);", descriptor.MslSource);
        Assert.Contains("constant int Negated = int(-uint(::Wrapped));", descriptor.MslSource);
        Assert.Contains("constant long Long = long(ulong(::Big) * ulong(::Big) * 5UL);", descriptor.MslSource);
        Assert.Contains("constant int2 Vector = int2(uint2(int2(::Big, ::Wrapped)) * 2u);", descriptor.MslSource);

        // Each call is folded with C#'s wrapping: |MaxValue| and max(MinValue + 1, MinValue); the sum is emitted.
        Assert.Contains("constant int Folded = int(uint(2147483647) + uint(-2147483647));", descriptor.MslSource);

        // The folder keeps every integer exact (it once rounded those above 2^24 through float).
        Assert.Contains("constant uint Unsigned = 4000000001u;", descriptor.MslSource);
    }
}
