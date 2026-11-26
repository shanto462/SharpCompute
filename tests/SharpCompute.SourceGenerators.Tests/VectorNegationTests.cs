using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The negation of an integer vector is emitted as the prelude's <c>__ms_neg(v)</c>, never as the
/// native <c>-v</c>: the Metal compiler miscompiles that operator when a lane of its result is unused
/// and the same vector is subtracted elsewhere (see <c>MslTranslator.VectorNegation</c>). So are the
/// operators that negate by a constant, <c>0 - v</c>, <c>v * -1</c> and <c>v / -1</c>; a product
/// with a constant that negates only some lanes goes through the lane-by-lane <c>__ms_mul</c>. And
/// because the compiler derives a negation from other arithmetic too (<c>~v + 1</c>,
/// <c>v - 2 * v</c>), every sum and difference of integer vectors is lane by lane as well,
/// <c>__ms_add</c> and <c>__ms_sub</c>, which leaves no vector chain for a negation to be reused
/// in. Floats, halves and scalars keep their operators. Every result is compiled with Metal.
/// </summary>
public class VectorNegationTests
{
    private const string Shader = "Tests.TestShader";

    private static ShaderDescriptor Translate(string body, string members = "") => Run(body, members).Verify(Shader);

    private static GeneratorRun Run(string body, string members = "", string extraBuffer = "ReadWriteBuffer<int> extra") =>
        GeneratorHarness.Run($$"""
            using System;
            using SharpCompute;

            namespace Tests;

            [MslType("long3")]
            public struct Long3(long x, long y, long z)
            {
                public long X = x, Y = y, Z = z;

                public static Long3 operator -(Long3 v) => new(-v.X, -v.Y, -v.Z);
                public static Long3 operator -(Long3 a, Long3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
                public static Long3 operator *(Long3 a, long b) => new(a.X * b, a.Y * b, a.Z * b);
            }

            [MslType("short4")]
            public struct Short4(short x, short y, short z, short w)
            {
                public short X = x, Y = y, Z = z, W = w;

                public static Short4 operator -(Short4 v) => new((short)-v.X, (short)-v.Y, (short)-v.Z, (short)-v.W);
            }

            [MslType("uchar2")]
            public struct Byte2(byte x, byte y)
            {
                public byte X = x, Y = y;

                public static Byte2 operator -(Byte2 v) => new((byte)-v.X, (byte)-v.Y);
            }

            public struct Cell
            {
                public Int3 V;
                public Int2 P;
                public UInt4 U;
            }

            [GeneratedComputeShaderDescriptor]
            [ThreadGroupSize(64, 1, 1)]
            public readonly partial struct TestShader(ReadWriteBuffer<int> ints, ReadWriteBuffer<Int3> vectors, ReadWriteBuffer<UInt3> unsigneds, ReadWriteBuffer<Float3> floats, ReadWriteBuffer<long> longs, {{extraBuffer}}) : IComputeShader
            {
                {{members}}

                public void Execute()
                {
                    int i = ThreadIds.X;
                    int a = ints[0], b = ints[1];
                    Int3 v = vectors[1], w = vectors[2];
                    UInt3 u = unsigneds[1];
                    Float3 f = floats[1];
                    {{body}}
                }

                private int Bump(int slot)
                {
                    ints[slot] += 1;
                    return 10;
                }
            }
            """);

    private static string Statement(string body, string members = "") =>
        Translate(body, members).KernelBody.Split('\n').Last();

    [Theory]
    [InlineData("vectors[0] = -v;", "vectors[0] = __ms_neg(v);")]
    [InlineData("vectors[0] = -vectors[1];", "vectors[0] = __ms_neg(vectors[1]);")]
    [InlineData("vectors[0] = -(-v);", "vectors[0] = __ms_neg(__ms_neg(v));")]
    [InlineData("vectors[0] = -(v + w);", "vectors[0] = __ms_neg(int3(__ms_add(uint3(v), uint3(w))));")]
    [InlineData("vectors[0] = new Int3(-v.XY, a);", "vectors[0] = int3(__ms_neg(v.xy), a);")]
    [InlineData("vectors[0] = Gpu.Select(v < 0, -v, v);", "vectors[0] = metal::select(v, __ms_neg(v), v < 0);")]
    [InlineData("vectors[0] = -new Int3(a, b, a ^ b) >> 1;", "vectors[0] = __ms_neg(int3(a, b, a ^ b)) >> 1;")]
    public void UnaryMinus_OnAnIntVector_IsThePreludeHelper(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("vectors[0] = w + -v;", "vectors[0] = int3(__ms_add(uint3(w), __ms_neg(uint3(v))));")]
    [InlineData("vectors[0] = -v * 3 - w;", "vectors[0] = int3(__ms_sub(__ms_neg(uint3(v)) * 3u, uint3(w)));")]
    [InlineData("vectors[0] = w - -(v * 2);", "vectors[0] = int3(__ms_sub(uint3(w), __ms_neg(uint3(v) * 2u)));")]
    [InlineData("vectors[0] = w * -vectors[1];", "vectors[0] = int3(uint3(w) * __ms_neg(uint3(vectors[1])));")]
    [InlineData("w += -v; vectors[0] = w;", "w = __ms_add(w, __ms_neg(v));")]
    [InlineData("vectors[Bump(3)] -= -v; vectors[0] = w;", "__ms_sub_from(vectors[::Bump(3, ints)], int3(__ms_neg(v)));")]
    public void UnaryMinus_InsideAWrappedChain_NegatesTheUnsignedVectorThroughTheHelper(string body, string expected) =>
        Assert.Contains(expected, Translate(body).KernelBody.Split('\n').Select(static line => line.Trim()));

    [Theory]
    [InlineData("new Int3(0) - v")]
    [InlineData("new Int3(0, 0, 0) - v")]
    [InlineData("new Int3() - v")]
    [InlineData("default(Int3) - v")]
    [InlineData("0 - v")]
    [InlineData("Zero - v")]
    [InlineData("v * -1")]
    [InlineData("-1 * v")]
    [InlineData("v * new Int3(-1)")]
    [InlineData("new Int3(-1, -1, -1) * v")]
    [InlineData("v * MinusOne")]
    [InlineData("v * ~Zero")]
    [InlineData("v / -1")]
    [InlineData("v / new Int3(-1)")]
    public void ANegationByAConstant_OnAnIntVector_IsThePreludeHelper(string expression)
    {
        string statement = Statement(
            $"vectors[0] = {expression};",
            "private static readonly Int3 Zero = new Int3(0); private const int MinusOne = -1;");

        Assert.Contains("__ms_neg(v)", statement);
        Assert.DoesNotContain(" - ", statement);
        Assert.DoesNotContain(" * ", statement);
        Assert.DoesNotContain(" / ", statement);
    }

    [Theory]
    [InlineData("unsigneds[0] = new UInt3(0) - u;", "unsigneds[0] = __ms_neg(u);")]
    [InlineData("unsigneds[0] = 0u - u;", "unsigneds[0] = __ms_neg(u);")]
    [InlineData("unsigneds[0] = default(UInt3) - unsigneds[1];", "unsigneds[0] = __ms_neg(unsigneds[1]);")]
    [InlineData("unsigneds[0] = u * uint.MaxValue;", "unsigneds[0] = __ms_neg(u);")]
    [InlineData("unsigneds[0] = new UInt3(0xFFFFFFFFu) * u;", "unsigneds[0] = __ms_neg(u);")]
    // An unsigned division by uint.MaxValue is no negation: it is 1 for uint.MaxValue and 0 otherwise.
    [InlineData("unsigneds[0] = u / uint.MaxValue;", "unsigneds[0] = u / 4294967295u;")]
    public void ANegationByAConstant_OnAUintVector_IsThePreludeHelper(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("vectors[0] = w + (0 - v);", "vectors[0] = int3(__ms_add(uint3(w), __ms_neg(uint3(v))));")]
    [InlineData("vectors[0] = w - v * -1;", "vectors[0] = int3(__ms_sub(uint3(w), __ms_neg(uint3(v))));")]
    [InlineData("vectors[0] = (w + v) * -1 + w;", "vectors[0] = int3(__ms_add(__ms_neg(__ms_add(uint3(w), uint3(v))), uint3(w)));")]
    public void ANegationByAConstant_InsideAWrappedChain_NegatesTheUnsignedVector(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("vectors[0] = new Int3(0, 5, 0) - v;", "vectors[0] = int3(__ms_sub(uint3(int3(0, 5, 0)), uint3(v)));")]
    [InlineData("vectors[0] = new Int3(7, 0, 9) - vectors[1];", "vectors[0] = int3(__ms_sub(uint3(int3(7, 0, 9)), uint3(vectors[1])));")]
    [InlineData("vectors[0] = Offsets - v;", "vectors[0] = int3(__ms_sub(uint3(::Offsets), uint3(v)));")]
    [InlineData("vectors[0] = w + (new Int3(0, 5, 0) - v) * 3;", "vectors[0] = int3(__ms_add(uint3(w), (__ms_sub(uint3(int3(0, 5, 0)), uint3(v))) * 3u));")]
    [InlineData("vectors[0] = new Int3(0, 5, 0) - v - w;", "vectors[0] = int3(__ms_sub(__ms_sub(uint3(int3(0, 5, 0)), uint3(v)), uint3(w)));")]
    [InlineData("vectors[0] = w - (new Int3(0, 5, 0) - v);", "vectors[0] = int3(__ms_sub(uint3(w), __ms_sub(uint3(int3(0, 5, 0)), uint3(v))));")]
    [InlineData("vectors[0] = new Int3(0, 5, 0) - a;", "vectors[0] = int3(__ms_sub(uint3(int3(0, 5, 0)), uint3(uint(a))));")]
    [InlineData("unsigneds[0] = new UInt3(0u, 5u, 0u) - u;", "unsigneds[0] = __ms_sub(uint3(0u, 5u, 0u), u);")]
    [InlineData("unsigneds[0] = (new UInt3(0u, 5u, 0u) - u) * 3u;", "unsigneds[0] = (__ms_sub(uint3(0u, 5u, 0u), u)) * 3u;")]
    [InlineData("unsigneds[0] = u - (new UInt3(0u, 5u, 0u) - u);", "unsigneds[0] = __ms_sub(u, __ms_sub(uint3(0u, 5u, 0u), u));")]
    public void ADifference_FromAConstantWithAZeroLane_IsSubtractedLaneByLane(string body, string expected) =>
        Assert.Equal(expected, Statement(body, "private static readonly Int3 Offsets = new Int3(4, 0, 0);"));

    [Theory]
    [InlineData("vectors[0] = v * new Int3(-1, 7, -1);", "vectors[0] = __ms_mul(v, int3(-1, 7, -1));")]
    [InlineData("vectors[0] = new Int3(1, -1, 1) * v;", "vectors[0] = __ms_mul(int3(1, -1, 1), v);")]
    [InlineData("vectors[0] = vectors[1] * Flip;", "vectors[0] = __ms_mul(vectors[1], ::Flip);")]
    [InlineData("vectors[0] = w + v * new Int3(1, -1, 1);", "vectors[0] = int3(__ms_add(uint3(w), __ms_mul(uint3(v), uint3(int3(1, -1, 1)))));")]
    [InlineData("vectors[0] = (v + w) * new Int3(1, 1, -1) - w;", "vectors[0] = int3(__ms_sub(__ms_mul(__ms_add(uint3(v), uint3(w)), uint3(int3(1, 1, -1))), uint3(w)));")]
    [InlineData("vectors[0] = a * new Int3(1, -1, 1);", "vectors[0] = __ms_mul(int3(a), int3(1, -1, 1));")]
    [InlineData("unsigneds[0] = u * new UInt3(uint.MaxValue, 7u, 1u);", "unsigneds[0] = __ms_mul(u, uint3(4294967295u, 7u, 1u));")]
    [InlineData("unsigneds[0] = new UInt3(1u, 0xFFFFFFFFu, 1u) * u + u;", "unsigneds[0] = __ms_add(__ms_mul(uint3(1u, 4294967295u, 1u), u), u);")]
    public void AProduct_WithAConstantWithALaneOfMinusOne_IsMultipliedLaneByLane(string body, string expected) =>
        Assert.Equal(expected, Statement(body, "private static readonly Int3 Flip = new Int3(1, -1, 1);"));

    [Theory]
    // The scalar is negated in every lane.
    [InlineData("vectors[0] = new Int3(0) - a;", "vectors[0] = __ms_neg(int3(a));")]
    [InlineData("vectors[0] = a * new Int3(-1);", "vectors[0] = __ms_neg(int3(a));")]
    [InlineData("vectors[0] = w + (new Int3(0) - a);", "vectors[0] = int3(__ms_add(uint3(w), __ms_neg(uint3(uint(a)))));")]
    [InlineData("unsigneds[0] = new UInt3(0) - (uint)a;", "unsigneds[0] = __ms_neg(uint3(uint(a)));")]
    public void ANegationByAConstant_OfAScalar_NegatesItInEveryLane(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("v *= -1; vectors[0] = v;", "v = __ms_neg(v);")]
    [InlineData("v *= new Int3(-1); vectors[0] = v;", "v = __ms_neg(v);")]
    [InlineData("v /= -1; vectors[0] = v;", "v = __ms_neg(v);")]
    [InlineData("vectors[i] *= -1;", "vectors[i] = __ms_neg(vectors[i]);")]
    [InlineData("u *= uint.MaxValue; unsigneds[0] = u;", "u = __ms_neg(u);")]
    [InlineData("v *= new Int3(1, -1, 1); vectors[0] = v;", "v = __ms_mul(v, int3(1, -1, 1));")]
    [InlineData("vectors[i] *= new Int3(-1, 7, -1);", "vectors[i] = __ms_mul(vectors[i], int3(-1, 7, -1));")]
    [InlineData("u *= new UInt3(1u, uint.MaxValue, 1u); unsigneds[0] = u;", "u = __ms_mul(u, uint3(1u, 4294967295u, 1u));")]
    // A multiplication that negates no lane is free to have side effects in its target.
    [InlineData("vectors[Bump(3)] *= 2;", "reinterpret_cast<device packed_uint3&>(vectors[::Bump(3, ints)]) *= 2u;")]
    // Not negations.
    [InlineData("v *= 1; vectors[0] = v;", "v = int3(uint3(v) * 1u);")]
    [InlineData("u /= uint.MaxValue; unsigneds[0] = u;", "u /= 4294967295u;")]
    [InlineData("v /= new Int3(-1, 7, -1); vectors[0] = v;", "v /= int3(-1, 7, -1);")]
    public void ACompoundNegation_AssignsTheHelperResult(string body, string expected) =>
        Assert.Contains(expected, Translate(body).KernelBody.Split('\n').Select(static line => line.Trim()));

    [Theory]
    [InlineData("vectors[Bump(3)] *= -1;", "*=")]
    [InlineData("vectors[Bump(3)] /= -1;", "/=")]
    [InlineData("vectors[Bump(3)] *= new Int3(-1);", "*=")]
    [InlineData("vectors[Bump(3)] *= new Int3(1, -1, 1);", "*=")]
    [InlineData("unsigneds[Bump(3)] *= uint.MaxValue;", "*=")]
    [InlineData("vectors[ints[2]++] *= -1;", "*=")]
    public void ACompoundNegation_OnATargetWithSideEffects_IsUnsupported(string body, string op)
    {
        // The helper's result is assigned back, which would evaluate the target twice, and the
        // native operator is the one the Metal compiler miscompiles.
        Diagnostic diagnostic = Run(body).AssertOnlyDiagnostic("SCG0012", body.TrimEnd(';'));

        Assert.Contains($"'{op}' by a constant that negates lanes of an integer vector, on a target whose expression has side effects", diagnostic.GetMessage());
        Assert.Contains("put the index in a local first", diagnostic.GetMessage());
    }

    [Fact]
    public void ACompoundNegation_WithItsIndexInALocal_IsTheHelper()
    {
        string[] lines = Translate("int slot = Bump(3); vectors[slot] *= -1;").KernelBody.Split('\n').Select(static line => line.Trim()).ToArray();

        Assert.Contains("int slot = ::Bump(3, ints);", lines);
        Assert.Contains("vectors[slot] = __ms_neg(vectors[slot]);", lines);
    }

    [Theory]
    [InlineData("vectors[0] = v * 1;", "vectors[0] = int3(uint3(v) * 1u);")]
    [InlineData("vectors[0] = v % -1;", "vectors[0] = v % -1;")]
    [InlineData("vectors[0] = -1 / v;", "vectors[0] = -1 / v;")]
    [InlineData("vectors[0] = v * a;", "vectors[0] = int3(uint3(v) * uint(a));")]
    // No lane of this constant is minus one.
    [InlineData("vectors[0] = v * new Int3(2, 7, 1);", "vectors[0] = int3(uint3(v) * uint3(int3(2, 7, 1)));")]
    // A division by a constant with a lane of minus one does not lose a lane.
    [InlineData("vectors[0] = v / new Int3(-1, 7, -1);", "vectors[0] = v / int3(-1, 7, -1);")]
    public void OtherOperators_OnIntVectors_AreUnchanged(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    // An int vector: lane by lane inside the unsigned chain, where the lanes wrap as in C#.
    [InlineData("vectors[0] = v + w;", "vectors[0] = int3(__ms_add(uint3(v), uint3(w)));")]
    [InlineData("vectors[0] = v - w;", "vectors[0] = int3(__ms_sub(uint3(v), uint3(w)));")]
    [InlineData("vectors[0] = v - new Int3(0);", "vectors[0] = int3(__ms_sub(uint3(v), uint3(int3(0))));")]
    [InlineData("vectors[0] = new Int3(1, 5, 2) - v;", "vectors[0] = int3(__ms_sub(uint3(int3(1, 5, 2)), uint3(v)));")]
    [InlineData("vectors[0] = v - new Int3(0, 5, 0);", "vectors[0] = int3(__ms_sub(uint3(v), uint3(int3(0, 5, 0))));")]
    [InlineData("vectors[0] = v + new Int3(0, -1, 0);", "vectors[0] = int3(__ms_add(uint3(v), uint3(int3(0, -1, 0))));")]
    [InlineData("vectors[0] = vectors[1] + vectors[2];", "vectors[0] = int3(__ms_add(uint3(vectors[1]), uint3(vectors[2])));")]
    // A scalar operand is converted to the vector, as MSL would broadcast it.
    [InlineData("vectors[0] = 1 - v;", "vectors[0] = int3(__ms_sub(uint3(1u), uint3(v)));")]
    [InlineData("vectors[0] = v + a;", "vectors[0] = int3(__ms_add(uint3(v), uint3(uint(a))));")]
    // The shapes that only amount to a negation: none is left a vector sum or difference.
    [InlineData("vectors[0] = ~v + 1;", "vectors[0] = int3(__ms_add(uint3(~v), uint3(1u)));")]
    [InlineData("vectors[0] = v - 2 * v;", "vectors[0] = int3(__ms_sub(uint3(v), 2u * uint3(v)));")]
    [InlineData("vectors[0] = v - (v + v);", "vectors[0] = int3(__ms_sub(uint3(v), __ms_add(uint3(v), uint3(v))));")]
    [InlineData("vectors[0] = v - (v << 1);", "vectors[0] = int3(__ms_sub(uint3(v), uint3(v << 1)));")]
    [InlineData("vectors[0] = v + (-v);", "vectors[0] = int3(__ms_add(uint3(v), __ms_neg(uint3(v))));")]
    [InlineData("vectors[0] = (0 - v) - v;", "vectors[0] = int3(__ms_sub(__ms_neg(uint3(v)), uint3(v)));")]
    [InlineData("vectors[0] = ~(v - 1);", "vectors[0] = ~(int3(__ms_sub(uint3(v), uint3(1u))));")]
    // Inside a product, which keeps its operator.
    [InlineData("vectors[0] = (v + w) * 3 - w;", "vectors[0] = int3(__ms_sub((__ms_add(uint3(v), uint3(w))) * 3u, uint3(w)));")]
    // A uint vector, which has no chain to be in.
    [InlineData("unsigneds[0] = u + u;", "unsigneds[0] = __ms_add(u, u);")]
    [InlineData("unsigneds[0] = u - unsigneds[2];", "unsigneds[0] = __ms_sub(u, unsigneds[2]);")]
    [InlineData("unsigneds[0] = ~u + 1u;", "unsigneds[0] = __ms_add(~u, uint3(1u));")]
    [InlineData("unsigneds[0] = u - 2u * u;", "unsigneds[0] = __ms_sub(u, 2u * u);")]
    [InlineData("unsigneds[0] = u * 3u + 7u - u;", "unsigneds[0] = __ms_sub(__ms_add(u * 3u, uint3(7u)), u);")]
    public void SumsAndDifferences_OfIntegerVectors_AreLaneByLane(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    // Nothing has side effects: the helper's result is assigned.
    [InlineData("w += v; vectors[0] = w;", "w = __ms_add(w, v);")]
    [InlineData("w -= v * 2; vectors[0] = w;", "w = __ms_sub(w, int3(uint3(v) * 2u));")]
    [InlineData("w -= 1; vectors[0] = w;", "w = __ms_sub(w, int3(1));")]
    [InlineData("vectors[i] += v;", "vectors[i] = __ms_add(vectors[i], v);")]
    [InlineData("vectors[i] -= vectors[i + 1];", "vectors[i] = __ms_sub(vectors[i], vectors[int(uint(i) + 1u)]);")]
    [InlineData("u += u; unsigneds[0] = u;", "u = __ms_add(u, u);")]
    [InlineData("unsigneds[i] -= 3u;", "unsigneds[i] = __ms_sub(unsigneds[i], uint3(3u));")]
    // Components of a vector are a vector too.
    [InlineData("w.XY += v.XY; vectors[0] = w;", "w.xy = __ms_add(w.xy, v.xy);")]
    // Side effects in the target or in the value: the target is named once, through a reference.
    [InlineData("vectors[Bump(3)] += v;", "__ms_add_to(vectors[::Bump(3, ints)], v);")]
    [InlineData("vectors[Bump(3)] -= new Int3(a);", "__ms_sub_from(vectors[::Bump(3, ints)], int3(a));")]
    [InlineData("vectors[Bump(3)] -= a;", "__ms_sub_from(vectors[::Bump(3, ints)], int3(a));")]
    [InlineData("vectors[Bump(3)] += vectors[1];", "__ms_add_to(vectors[::Bump(3, ints)], int3(vectors[1]));")]
    [InlineData("unsigneds[Bump(3)] += u * 2u;", "__ms_add_to(unsigneds[::Bump(3, ints)], uint3(u * 2u));")]
    [InlineData("w += new Int3(Bump(3)); vectors[0] = w;", "__ms_add_to(w, int3(::Bump(3, ints)));")]
    // The value of the assignment is the helper's result.
    [InlineData("vectors[0] = vectors[Bump(3)] += v;", "vectors[0] = __ms_add_to(vectors[::Bump(3, ints)], v);")]
    public void CompoundSumsAndDifferences_OfIntegerVectors_AreLaneByLane(string body, string expected) =>
        Assert.Contains(expected, Translate(body).KernelBody.Split('\n').Select(static line => line.Trim()));

    [Fact]
    public void ACompoundSum_OnComponentsWhoseExpressionHasSideEffects_IsUnsupported()
    {
        // Components bind no reference, the assignment would name the target twice, and the native
        // operator is a vector sum.
        Diagnostic diagnostic = Run("cells[Bump(3)].V.XY += v.XY;", "", "ReadWriteBuffer<Cell> cells").AssertOnlyDiagnostic("SCG0012", "cells[Bump(3)].V.XY += v.XY");

        Assert.Contains("'+=' on components of an integer vector whose expression has side effects", diagnostic.GetMessage());
        Assert.Contains("put the index in a local first", diagnostic.GetMessage());
    }

    [Fact]
    public void SumsOfUserVectorsOfAnyIntegerType_AreLaneByLane()
    {
        string[] lines = Translate(
            """
            Long3 wide = new Long3(a, b, a ^ b);
            Long3 difference = wide - new Long3(1, 2, 3);
            difference -= wide;
            longs[0] = difference.X;
            """).KernelBody.Split('\n').Select(static line => line.Trim()).ToArray();

        Assert.Contains("long3 difference = __ms_sub(wide, long3(1L, 2L, 3L));", lines);
        Assert.Contains("difference = __ms_sub(difference, wide);", lines);
    }

    [Fact]
    public void TheSumHelpers_AreABlockOfTheirOwn_WhichOnlyAShaderThatUsesThemCarries()
    {
        ShaderDescriptor sum = Translate("vectors[0] = v + w;");
        ShaderDescriptor negation = Translate("vectors[0] = -v;");
        ShaderDescriptor both = Translate("vectors[0] = -v + w;");

        Assert.Contains(MslPrelude.VectorArithmetic, sum.MslSource);
        Assert.DoesNotContain("__ms_neg", sum.MslSource);
        Assert.DoesNotContain("__ms_add", negation.MslSource);
        Assert.Contains(MslPrelude.Source, negation.MslSource);

        // The prelude first, then the block.
        Assert.True(both.MslSource.IndexOf(MslPrelude.Source, System.StringComparison.Ordinal) < both.MslSource.IndexOf(MslPrelude.VectorArithmetic, System.StringComparison.Ordinal));

        foreach (string scalar in new[] { "char", "uchar", "short", "ushort", "int", "uint", "long", "ulong" })
        {
            for (int width = 2; width <= 4; width++)
            {
                Assert.Contains($"inline {scalar}{width} __ms_add({scalar}{width} a, {scalar}{width} b) {{ return {scalar}{width}(", MslPrelude.VectorArithmetic);
                Assert.Contains($"inline {scalar}{width} __ms_sub({scalar}{width} a, {scalar}{width} b) {{ return {scalar}{width}(", MslPrelude.VectorArithmetic);
            }
        }

        // Each lane wraps: a signed lane is computed in the unsigned type of its width.
        Assert.Contains("inline int3 __ms_add(int3 a, int3 b) { return int3(int(uint(a.x) + uint(b.x)), int(uint(a.y) + uint(b.y)), int(uint(a.z) + uint(b.z))); }", MslPrelude.VectorArithmetic);
        Assert.Contains("inline uint2 __ms_sub(uint2 a, uint2 b) { return uint2(a.x - b.x, a.y - b.y); }", MslPrelude.VectorArithmetic);
        Assert.Contains("inline short2 __ms_sub(short2 a, short2 b) { return short2(short(ushort(a.x) - ushort(b.x)), short(ushort(a.y) - ushort(b.y))); }", MslPrelude.VectorArithmetic);

        // No helper adds or subtracts vectors, and the by-reference forms exist for each address space.
        Assert.DoesNotContain("a + b", MslPrelude.VectorArithmetic);
        Assert.DoesNotContain("a - b", MslPrelude.VectorArithmetic);

        foreach (string space in new[] { "thread", "device", "threadgroup" })
        {
            Assert.Contains($"template <typename T, typename V> inline V __ms_add_to({space} T& target, V value) {{ V result = __ms_add(V(target), value); target = result; return result; }}", MslPrelude.VectorArithmetic);
            Assert.Contains($"template <typename T, typename V> inline V __ms_sub_from({space} T& target, V value) {{ V result = __ms_sub(V(target), value); target = result; return result; }}", MslPrelude.VectorArithmetic);
        }
    }

    [Fact]
    public void TheByReferenceHelpers_CompileForEveryKindOfTarget()
    {
        // A local, a buffer element (packed), a struct field in a buffer, a [GroupShared] element and
        // a struct local's field, each with side effects in the target or the value.
        Run(
            """
            Int3 local = v;
            local += new Int3(Bump(3));
            vectors[Bump(3)] += local;
            cells[Bump(3)].V -= local;
            cells[Bump(3)].P += local.XY;
            cells[Bump(3)].U -= new UInt4(u, 1u);
            Tile[Bump(3) & 63] += local;
            Cell cell = cells[i];
            cell.V -= new Int3(Bump(3));
            cells[i] = cell;
            """,
            "[GroupShared] private static readonly Int3[] Tile = new Int3[64];",
            "ReadWriteBuffer<Cell> cells").Verify(Shader);
    }

    [Fact]
    public void FloatAndHalfVectors_AndScalars_KeepTheirOperators_AndNeedNoPrelude()
    {
        ShaderDescriptor descriptor = Translate(
            """
            Half3 h = new Half3((Half)1.5f);
            h = -h;
            f = -f + (new Float3(0) - f) + (f * -1) + (f / -1);
            a = -a + (0 - b) + (a * -1) + (b / -1);
            uint n = 0u - (uint)a;
            floats[0] = f + (float)h.X + a + n;
            """);

        string[] lines = descriptor.KernelBody.Split('\n').Select(static line => line.Trim()).ToArray();

        Assert.Contains("h = -h;", lines);
        Assert.Contains("f = -f + (float3(0.0f) - f) + (f * -1.0f) + (f / -1.0f);", lines);
        Assert.Contains("a = int(-uint(a) + (0u - uint(b)) + (uint(a) * uint(-1)) + uint(b / -1));", lines);
        Assert.Contains("uint n = 0u - uint(a);", lines);
        Assert.DoesNotContain("__ms_", descriptor.MslSource);
    }

    [Fact]
    public void TheHelpers_ComeFromThePrelude_OneOverloadPerIntegerVectorType()
    {
        ShaderDescriptor descriptor = Translate("vectors[0] = -v;");

        Assert.Contains("inline int3 __ms_neg(int3 x) { return int3(int(-uint(x.x)), int(-uint(x.y)), int(-uint(x.z))); }", descriptor.MslSource);
        Assert.Contains("inline uint3 __ms_neg(uint3 x) { return uint3(-x.x, -x.y, -x.z); }", descriptor.MslSource);

        foreach (string scalar in new[] { "char", "uchar", "short", "ushort", "int", "uint", "long", "ulong" })
        {
            for (int width = 2; width <= 4; width++)
            {
                Assert.Contains($"inline {scalar}{width} __ms_neg({scalar}{width} x) {{ return {scalar}{width}(", MslPrelude.Source);
            }
        }

        // No lane of any helper is a native vector negation.
        Assert.Equal(24, MslPrelude.Source.Split('\n').Count(static line => line.Contains("__ms_neg(")));
        Assert.DoesNotContain("-x;", MslPrelude.Source);
        Assert.DoesNotContain("-x)", MslPrelude.Source);

        // The lane-by-lane product, for the int and uint vectors: no vector multiplication either.
        Assert.Contains("inline int3 __ms_mul(int3 a, int3 b) { return int3(int(uint(a.x) * uint(b.x)), int(uint(a.y) * uint(b.y)), int(uint(a.z) * uint(b.z))); }", MslPrelude.Source);
        Assert.Contains("inline uint3 __ms_mul(uint3 a, uint3 b) { return uint3(a.x * b.x, a.y * b.y, a.z * b.z); }", MslPrelude.Source);
        Assert.Equal(6, MslPrelude.Source.Split('\n').Count(static line => line.Contains("__ms_mul(")));
        Assert.DoesNotContain("a * b", MslPrelude.Source);
    }

    [Fact]
    public void UnaryMinus_OnAUserVectorOfAnyIntegerType_IsThePreludeHelper()
    {
        string[] lines = Translate(
            """
            Long3 wide = new Long3(a, b, a ^ b);
            Long3 negatedWide = -wide;
            Short4 small = new Short4((short)a, (short)b, (short)i, (short)1);
            Short4 negatedSmall = -small;
            Byte2 bytes = new Byte2((byte)a, (byte)b);
            Byte2 negatedBytes = -bytes;
            // The folder evaluates int and uint constants only: the product keeps its operator, and the
            // difference is one like any other.
            Long3 scaled = wide * -1L;
            Long3 zeroMinus = default(Long3) - wide;
            longs[0] = negatedWide.X + negatedSmall.W + negatedBytes.Y + scaled.Y + zeroMinus.Z;
            """).KernelBody.Split('\n').Select(static line => line.Trim()).ToArray();

        Assert.Contains("long3 negatedWide = __ms_neg(wide);", lines);
        Assert.Contains("short4 negatedSmall = __ms_neg(small);", lines);
        Assert.Contains("uchar2 negatedBytes = __ms_neg(bytes);", lines);
        Assert.Contains("long3 scaled = wide * -1L;", lines);
        Assert.Contains("long3 zeroMinus = __ms_sub(long3(0), wide);", lines);
    }

    [Fact]
    public void AProgramScopeInitializer_KeepsTheNativeOperator_BecauseItMayNotCallAFunction()
    {
        ShaderDescriptor descriptor = Translate(
            "vectors[0] = Flipped; vectors[1] = Mirrored; vectors[2] = Scaled; vectors[3] = Shifted; vectors[4] = Turned; vectors[5] = Summed;",
            """
            private static readonly Int3 Offsets = new Int3(1, -2, int.MinValue);
            private static readonly Int3 Flipped = -Offsets;
            private static readonly Int3 Mirrored = new Int3(0) - Offsets;
            private static readonly Int3 Scaled = Offsets * -1;
            private static readonly Int3 Shifted = new Int3(0, 5, 0) - Offsets;
            private static readonly Int3 Turned = Offsets * new Int3(1, -1, 1);
            private static readonly Int3 Summed = Offsets + new Int3(1, 2, 3);
            """);

        Assert.Contains("constant int3 Flipped = int3(-uint3(::Offsets));", descriptor.MslSource);
        Assert.Contains("constant int3 Mirrored = int3(uint3(int3(0)) - uint3(::Offsets));", descriptor.MslSource);
        Assert.Contains("constant int3 Scaled = int3(uint3(::Offsets) * uint(-1));", descriptor.MslSource);
        Assert.Contains("constant int3 Shifted = int3(uint3(int3(0, 5, 0)) - uint3(::Offsets));", descriptor.MslSource);
        Assert.Contains("constant int3 Turned = int3(uint3(::Offsets) * uint3(int3(1, -1, 1)));", descriptor.MslSource);
        Assert.Contains("constant int3 Summed = int3(uint3(::Offsets) + uint3(int3(1, 2, 3)));", descriptor.MslSource);
        Assert.DoesNotContain("__ms_", descriptor.MslSource);
    }

    [Fact]
    public void TheConstantOperand_IsStillTranslated_SoWhatItMustNotContainIsReported()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            namespace Tests;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader(ReadWriteBuffer<Int3> vectors) : IComputeShader
            {
                private static readonly Int3 Zero = new Int3(0);

                static TestShader()
                {
                    Zero = new Int3(7);
                }

                public void Execute()
                {
                    vectors[0] = Zero - vectors[1];
                }
            }
            """);

        // The static constructor reassigns the field, so the GPU would not read what C# does.
        run.AssertOnlyDiagnostic("SCG0026", "Zero");
    }
}
