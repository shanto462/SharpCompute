using System.Linq;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Integer division, remainder and bit counts are emitted through <c>MslPrelude.Integers</c>, which
/// works around miscompiles in Apple's GPU compiler: a divisor that cannot be zero
/// (<c>__ms_nonzero</c>), a constant dividend near 2^32 (<c>__ms_div_near_max</c>), and the counts
/// <c>__ms_popcount</c> and <c>__ms_clz</c>. A shader carries the overloads it calls and no other.
/// Every result is compiled with Metal; the GPU results are in the differential tests.
/// </summary>
public class IntegerMiscompileTests
{
    private const string Shader = "Tests.TestShader";

    private static ShaderDescriptor Translate(string body) =>
        GeneratorHarness.Run($$"""
            using System;
            using System.Numerics;
            using SharpCompute;

            namespace Tests;

            [GeneratedComputeShaderDescriptor]
            [ThreadGroupSize(64, 1, 1)]
            public readonly partial struct TestShader(ReadWriteBuffer<int> ints, ReadWriteBuffer<uint> uints, ReadWriteBuffer<long> longs, ReadWriteBuffer<UInt3> vectors, ReadWriteBuffer<short> shorts, int width) : IComputeShader
            {
                private const uint Max = uint.MaxValue;
                private static readonly uint ReadOnlyMax = 0xFFFFFFF0u;

                private static void Bump(ref uint x) => x++;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    int a = ints[0], b = ints[1];
                    uint u = uints[0];
                    long l = longs[0];
                    UInt3 v = vectors[0];
                    short s = shorts[0];
                    {{body}}
                }
            }
            """).Verify(Shader);

    private static string Statement(string body) => Translate(body).KernelBody.Split('\n').Last();

    [Theory]
    [InlineData("ints[2] = a / b;", "ints[2] = a / __ms_nonzero(b);")]
    [InlineData("ints[2] = a % b;", "ints[2] = a % __ms_nonzero(b);")]
    [InlineData("ints[2] = i % width;", "ints[2] = i % __ms_nonzero(__c.width);")]
    [InlineData("uints[2] = u / (u + 1);", "uints[2] = u / __ms_nonzero(u + 1u);")]
    [InlineData("longs[2] = l % (long)a;", "longs[2] = l % __ms_nonzero(long(a));")]
    [InlineData("vectors[1] = v / (v + 1);", "vectors[1] = v / __ms_nonzero(__ms_add(v, uint3(1u)));")]
    [InlineData("ints[2] = b == 0 ? 0 : a / b;", "ints[2] = b == 0 ? 0 : a / __ms_nonzero(b);")]
    [InlineData("shorts[1] = (short)(s / s);", "shorts[1] = short(int(s) / __ms_nonzero(int(s)));")]
    public void ADivisionByAVariable_DividesByANonzeroDivisor(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("uints[2] = 1000000007u % u;", "uints[2] = 1000000007u % __ms_nonzero_c(u);")]
    [InlineData("ints[2] = -1431655766 / b;", "ints[2] = -1431655766 / __ms_nonzero_c(b);")]
    [InlineData("uint c = 123456789u; uints[2] = c / u;", "uints[2] = c / __ms_nonzero_c(u);")]
    [InlineData("vectors[1] = new UInt3(1, 2, 3) % v;", "vectors[1] = uint3(1u, 2u, 3u) % __ms_nonzero_c(v);")]
    [InlineData("uints[2] = (u + 1u) % u;", "uints[2] = (u + 1u) % __ms_nonzero(u);")]
    // A local written through a member is no constant.
    [InlineData("UInt2 w = new UInt2(7u, 9u); w.X = u; vectors[1] = new UInt3(w / v.XY, 0);", "vectors[1] = uint3(w / __ms_nonzero(v.xy), 0u);")]
    [InlineData("UInt2 w = new UInt2(7u, 9u); w.Y++; vectors[1] = new UInt3(w / v.XY, 0);", "vectors[1] = uint3(w / __ms_nonzero(v.xy), 0u);")]
    public void AConstantDividend_DividesByTheFoldableNonzeroDivisor(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("ints[2] = a / 3;", "ints[2] = a / 3;")]
    [InlineData("ints[2] = a % -7;", "ints[2] = a % -7;")]
    [InlineData("uints[2] = u / 10u;", "uints[2] = u / 10u;")]
    [InlineData("vectors[1] = v / new UInt3(1, 2, 3);", "vectors[1] = v / uint3(1u, 2u, 3u);")]
    public void ADivisionByANonzeroConstant_KeepsTheOperator(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("uints[2] = 4294967295u / u;", "uints[2] = __ms_div_near_max(4294967295u, u);")]
    [InlineData("uints[2] = 0xFFFFFF80u % u;", "uints[2] = __ms_rem_near_max(4294967168u, u);")]
    [InlineData("uints[2] = Max / u;", "uints[2] = __ms_div_near_max(::Max, u);")]
    [InlineData("uints[2] = ReadOnlyMax % u;", "uints[2] = __ms_rem_near_max(::ReadOnlyMax, u);")]
    [InlineData("longs[2] = 4294967295L % l;", "longs[2] = __ms_rem_near_max(4294967295L, l);")]
    [InlineData("longs[2] = -4294967295L / l;", "longs[2] = __ms_div_near_max(-4294967295L, l);")]
    [InlineData("longs[2] = 4294967295u / a;", "longs[2] = __ms_div_near_max(4294967295L, long(a));")]
    [InlineData("vectors[1] = new UInt3(uint.MaxValue, 1, 2) / v;", "vectors[1] = __ms_div_near_max(uint3(4294967295u, 1u, 2u), v);")]
    [InlineData("vectors[1] = uint.MaxValue % v;", "vectors[1] = __ms_rem_near_max(uint3(4294967295u), v);")]
    // Below 2^32 - 128 the constant is divided as any dividend.
    [InlineData("uints[2] = 0xFFFFFF7Fu / u;", "uints[2] = 4294967167u / __ms_nonzero_c(u);")]
    [InlineData("longs[2] = 4294967296L / l;", "longs[2] = 4294967296L / __ms_nonzero_c(l);")]
    public void AConstantDividendNear2To32_IsDividedThroughTheHelper(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("uint m = uint.MaxValue; uints[2] = m / u;", "uints[2] = __ms_div_near_max(m, u);")]
    [InlineData("long m = 4294967295; longs[2] = m % l;", "longs[2] = __ms_rem_near_max(m, l);")]
    [InlineData("uint m = uint.MaxValue; uint k = m; uints[2] = k / u;", "uints[2] = __ms_div_near_max(k, u);")]
    [InlineData("UInt3 m = new UInt3(uint.MaxValue, 1, 2); vectors[1] = m / v;", "vectors[1] = __ms_div_near_max(m, v);")]
    // A local that is written after its declaration is divided as any dividend.
    [InlineData("uint m = uint.MaxValue; m = u; uints[2] = m / u;", "uints[2] = m / __ms_nonzero(u);")]
    [InlineData("uint m = uint.MaxValue; m -= 1; uints[2] = m / u;", "uints[2] = m / __ms_nonzero(u);")]
    [InlineData("uint m = uint.MaxValue; m++; uints[2] = m / u;", "uints[2] = m / __ms_nonzero(u);")]
    [InlineData("uint m = uint.MaxValue; Bump(ref m); uints[2] = m / u;", "uints[2] = m / __ms_nonzero(u);")]
    [InlineData("uint m = u; uints[2] = m / u;", "uints[2] = m / __ms_nonzero(u);")]
    public void AConstantLocalNear2To32_IsDividedThroughTheHelperUnlessItIsWritten(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("ints[2] /= b;", "ints[2] /= __ms_nonzero(b);")]
    [InlineData("uints[i] %= u;", "uints[i] %= __ms_nonzero(u);")]
    [InlineData("shorts[1] /= s;", "shorts[1] /= __ms_nonzero(int(s));")]
    [InlineData("ints[2] %= 8;", "ints[2] %= 8;")]
    public void ACompoundDivision_DividesByANonzeroDivisor(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Theory]
    [InlineData("ints[2] = Gpu.CountBits(a);", "ints[2] = __ms_popcount(a);")]
    [InlineData("shorts[1] = Gpu.CountBits(s);", "shorts[1] = __ms_popcount(s);")]
    [InlineData("longs[2] = Gpu.CountBits(l);", "longs[2] = __ms_popcount(l);")]
    [InlineData("vectors[1] = Gpu.CountBits(v);", "vectors[1] = __ms_popcount(v);")]
    [InlineData("ints[2] = Gpu.LeadingZeroCount(a);", "ints[2] = __ms_clz(a);")]
    [InlineData("longs[2] = Gpu.LeadingZeroCount(l);", "longs[2] = __ms_clz(l);")]
    [InlineData("vectors[1] = Gpu.LeadingZeroCount(v);", "vectors[1] = __ms_clz(v);")]
    [InlineData("ints[2] = BitOperations.PopCount(u);", "ints[2] = int(__ms_popcount(u));")]
    [InlineData("ints[2] = BitOperations.LeadingZeroCount((ulong)l);", "ints[2] = int(__ms_clz(ulong(l)));")]
    // The 8- and 16-bit clz and every ctz and bit reversal are not affected.
    [InlineData("shorts[1] = Gpu.LeadingZeroCount(s);", "shorts[1] = metal::clz(s);")]
    [InlineData("ints[2] = Gpu.TrailingZeroCount(a);", "ints[2] = metal::ctz(a);")]
    [InlineData("ints[2] = Gpu.ReverseBits(a);", "ints[2] = metal::reverse_bits(a);")]
    public void BitCounts_GoThroughTheHelpers(string body, string expected) =>
        Assert.Equal(expected, Statement(body));

    [Fact]
    public void AShader_CarriesTheOverloadsItCalls_AndTheOnesThoseCall()
    {
        string msl = Translate("shorts[1] = (short)(shorts[2] % s); longs[2] = 4294967295L / l; ints[2] = Gpu.CountBits(a);").MslSource;
        string[] helpers = msl.Split('\n').Where(static line => line.StartsWith("inline ", System.StringComparison.Ordinal)).ToArray();

        Assert.Equal(
            [
                "inline int __ms_nonzero(int x) { return __builtin_constant_p(x) ? x : x | int(metal::reverse_bits(x) == int(0)); }",
                "inline ulong __ms_nonzero_c(ulong x) { ulong u = ulong(x); return ulong(u | (((u - 1UL) & ~u) >> 63)); }",
                "inline ulong __ms_div_near_max(ulong a, ulong b) { return b > a ? 0UL : (a - b) / __ms_nonzero_c(b) + 1UL; }",
                "inline long __ms_div_near_max(long a, long b) { ulong q = __ms_div_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long((a < 0L) != (b < 0L) ? 0UL - q : q); }",
                "inline int __ms_popcount(int x) { return metal::popcount(metal::reverse_bits(x)); }",
            ],
            helpers);
        Assert.DoesNotContain("SharpCompute prelude (MslPrelude.Source)", msl);
    }

    [Fact]
    public void AShaderWithoutIntegerDivisionOrBitCounts_CarriesNoHelper()
    {
        string msl = Translate("ints[2] = a * b + a / 4; uints[2] = Gpu.TrailingZeroCount(u);").MslSource;

        Assert.DoesNotContain("MslPrelude.Integers", msl);
        Assert.DoesNotContain("inline ", msl);
    }

    [Fact]
    public void TheIntegersBlock_HasOneOverloadALine_ForEveryTypeTheGeneratorCalls()
    {
        string[] lines = MslPrelude.Integers.Split('\n');

        foreach (string type in new[] { "char", "uchar", "short", "ushort", "int", "uint", "long", "ulong" })
        {
            foreach (string width in new[] { "", "2", "3", "4" })
            {
                Assert.Single(lines, line => line.StartsWith($"inline {type}{width} __ms_nonzero({type}{width} x)", System.StringComparison.Ordinal));
            }

            Assert.Single(lines, line => line.StartsWith($"inline {type} __ms_popcount({type} x)", System.StringComparison.Ordinal));
        }

        foreach (string type in new[] { "int", "uint", "long", "ulong", "int2", "int3", "int4", "uint2", "uint3", "uint4" })
        {
            Assert.Single(lines, line => line.StartsWith($"inline {type} __ms_clz({type} x)", System.StringComparison.Ordinal));
        }

        foreach (string type in new[] { "uint", "ulong", "long", "uint2", "uint3", "uint4" })
        {
            Assert.Single(lines, line => line.StartsWith($"inline {type} __ms_div_near_max({type} a, {type} b)", System.StringComparison.Ordinal));
            Assert.Single(lines, line => line.StartsWith($"inline {type} __ms_rem_near_max({type} a, {type} b)", System.StringComparison.Ordinal));
        }
    }
}
