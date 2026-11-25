using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The MSL of the bit, packing, classification and matrix intrinsics of <c>Gpu</c>, and of the
/// <c>BitConverter</c> / <c>BitOperations</c> / <c>MathF</c> members that map to the same functions.
/// Every kernel is compiled with Metal.
/// </summary>
public class BitAndMathIntrinsicTests
{
    private const string Shader = "Tests.TestShader";

    private const string Buffers = """
            public readonly ReadWriteBuffer<float> f;
            public readonly ReadWriteBuffer<int> i;
            public readonly ReadWriteBuffer<uint> u;
            public readonly ReadWriteBuffer<Float4> v;
        """;

    private static GeneratorRun Run(string body) =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader($$"""
            {{Buffers}}

                public void Execute()
                {
                    {{body}}
                }
            """));

    private static void AssertKernel(string expected, string body) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), Run(body).Verify(Shader).KernelBody);

    [Fact]
    public void BitReinterpretation_IsAsType_ForGpuAndBitConverter()
    {
        AssertKernel(
            """
            f[0] = as_type<float>(i[0]) + as_type<float>(u[0]) + as_type<float>(i[1]) + as_type<float>(u[1]);
            i[0] = as_type<int>(f[0]) + as_type<int>(f[1]);
            u[0] = as_type<uint>(f[0]) + as_type<uint>(f[1]);
            """,
            """
            f[0] = Gpu.AsFloat(i[0]) + Gpu.AsFloat(u[0]) + BitConverter.Int32BitsToSingle(i[1]) + BitConverter.UInt32BitsToSingle(u[1]);
            i[0] = Gpu.AsInt(f[0]) + BitConverter.SingleToInt32Bits(f[1]);
            u[0] = Gpu.AsUInt(f[0]) + BitConverter.SingleToUInt32Bits(f[1]);
            """);
    }

    [Fact]
    public void BitReinterpretation_OfVectors_NamesTheVectorType()
    {
        AssertKernel(
            """
            float3 bits = as_type<float3>(int3(1, 2, 3)) + as_type<float3>(uint3(1u, 2u, 3u));
            int2 back = as_type<int2>(bits.xy);
            uint4 raw = as_type<uint4>(v[0]);
            float2 a = as_type<float2>(back);
            float4 b = as_type<float4>(raw) + as_type<float4>(int4(back, back));
            int3 c = as_type<int3>(bits);
            int4 d = as_type<int4>(b);
            uint2 e = as_type<uint2>(a);
            uint3 g = as_type<uint3>(bits);
            v[1] = b + float4(float(c.x + d.x), float(e.x + g.x), a);
            """,
            """
            Float3 bits = Gpu.AsFloat(new Int3(1, 2, 3)) + Gpu.AsFloat(new UInt3(1u, 2u, 3u));
            Int2 back = Gpu.AsInt(bits.XY);
            UInt4 raw = Gpu.AsUInt(v[0]);
            Float2 a = Gpu.AsFloat(back);
            Float4 b = Gpu.AsFloat(raw) + Gpu.AsFloat(new Int4(back, back));
            Int3 c = Gpu.AsInt(bits);
            Int4 d = Gpu.AsInt(b);
            UInt2 e = Gpu.AsUInt(a);
            UInt3 g = Gpu.AsUInt(bits);
            v[1] = b + new Float4(c.X + d.X, e.X + g.X, a);
            """);
    }

    [Fact]
    public void BitCounts_MapToPopcountClzCtzReverseBits_ForScalarsAndVectors()
    {
        AssertKernel(
            """
            i[1] = metal::popcount(i[0]) + metal::clz(i[0]) + metal::ctz(i[0]) + metal::reverse_bits(i[0]);
            u[1] = metal::popcount(u[0]) + metal::clz(u[0]) + metal::ctz(u[0]) + metal::reverse_bits(u[0]);
            uint4 raw = uint4(u[0], 1u, 2u, 3u);
            uint4 counted = metal::popcount(raw) + metal::clz(raw) + metal::ctz(raw) + metal::reverse_bits(raw);
            int2 pair = int2(i[0], -1);
            int2 signedCounts = metal::popcount(pair) + metal::clz(pair) + metal::ctz(pair) + metal::reverse_bits(pair);
            uint3 three = metal::popcount(raw.xyz) + metal::reverse_bits(raw.zyx);
            u[2] = counted.x + uint(signedCounts.y) + three.z;
            """,
            """
            i[1] = Gpu.CountBits(i[0]) + Gpu.LeadingZeroCount(i[0]) + Gpu.TrailingZeroCount(i[0]) + Gpu.ReverseBits(i[0]);
            u[1] = Gpu.CountBits(u[0]) + Gpu.LeadingZeroCount(u[0]) + Gpu.TrailingZeroCount(u[0]) + Gpu.ReverseBits(u[0]);
            UInt4 raw = new UInt4(u[0], 1, 2, 3);
            UInt4 counted = Gpu.CountBits(raw) + Gpu.LeadingZeroCount(raw) + Gpu.TrailingZeroCount(raw) + Gpu.ReverseBits(raw);
            Int2 pair = new Int2(i[0], -1);
            Int2 signedCounts = Gpu.CountBits(pair) + Gpu.LeadingZeroCount(pair) + Gpu.TrailingZeroCount(pair) + Gpu.ReverseBits(pair);
            UInt3 three = Gpu.CountBits(raw.XYZ) + Gpu.ReverseBits(raw.ZYX);
            u[2] = counted.X + (uint)signedCounts.Y + three.Z;
            """);
    }

    [Fact]
    public void BitOperations_CountsAreCastToInt_RotatesMaskTheCount()
    {
        // BitOperations returns int where MSL returns the argument's type. C# takes a rotate count
        // modulo 32; a right rotate is a left rotate by the negated count, negated as unsigned so
        // that int.MinValue does not overflow.
        AssertKernel(
            """
            int t = int(__tid.x);
            i[2] = int(metal::popcount(u[t])) + int(metal::clz(u[t])) + int(metal::ctz(u[t])) + int(metal::ctz(i[t]));
            u[2] = metal::rotate(u[t], uint(t) & 31u) + metal::rotate(u[t], (0u - uint(t + 1)) & 31u);
            """,
            """
            int t = ThreadIds.X;
            i[2] = System.Numerics.BitOperations.PopCount(u[t]) + System.Numerics.BitOperations.LeadingZeroCount(u[t]) + System.Numerics.BitOperations.TrailingZeroCount(u[t]) + System.Numerics.BitOperations.TrailingZeroCount(i[t]);
            u[2] = System.Numerics.BitOperations.RotateLeft(u[t], t) + System.Numerics.BitOperations.RotateRight(u[t], t + 1);
            """);
    }

    [Theory]
    [InlineData("u[0] = (uint)System.Numerics.BitOperations.PopCount((ulong)u[0]);", "SCG0007", "64-bit integers are not supported")]
    [InlineData("f[0] = (float)BitConverter.Int64BitsToDouble(1);", "SCG0007", "'double' is not available")]
    [InlineData("i[0] = System.Numerics.BitOperations.Log2(u[0]);", "SCG0016", "it has no MSL equivalent")]
    [InlineData("u[0] = System.Numerics.BitOperations.RoundUpToPowerOf2(u[0]);", "SCG0016", "it has no MSL equivalent")]
    [InlineData("i[0] = BitConverter.IsLittleEndian ? 1 : 0;", "SCG0017", "IsLittleEndian")]
    public void BitHelpers_WithoutAnMslEquivalent_AreDiagnosed(string body, string id, string message)
    {
        GeneratorRun run = Run(body);

        Assert.Contains(run.Diagnostics, d => d.Id == id && d.GetMessage().Contains(message, System.StringComparison.Ordinal));
        Assert.All(run.Diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void FmaLog10Exp10CopySign_MapForGpuAndSystemMath()
    {
        AssertKernel(
            """
            float x = f[0];
            f[1] = metal::fma(x, 2.0f, 1.0f) + metal::fma(x, x, x) + metal::log10(x) + metal::log10(x) + metal::exp10(x) + metal::exp10(x) + metal::copysign(x, -1.0f) + metal::copysign(1.0f, x);
            float2 p = float2(x, 1.0f);
            float2 w = metal::fma(p, p, p) + metal::log10(p) + metal::exp10(p) + metal::copysign(p, p.yx);
            float4 q = metal::fma(v[0], v[1], v[2]) + metal::copysign(v[0], v[1]) + metal::log10(v[2]) + metal::exp10(v[3]);
            float3 r = metal::fma(q.xyz, q.xyz, q.xyz) + metal::log10(q.xyz) + metal::exp10(q.zyx) + metal::copysign(q.xyz, q.zyx);
            f[2] = w.x + q.x + r.x;
            """,
            """
            float x = f[0];
            f[1] = Gpu.Fma(x, 2f, 1f) + MathF.FusedMultiplyAdd(x, x, x) + Gpu.Log10(x) + MathF.Log10(x) + Gpu.Exp10(x) + float.Exp10(x) + Gpu.CopySign(x, -1f) + MathF.CopySign(1f, x);
            Float2 p = new Float2(x, 1f);
            Float2 w = Gpu.Fma(p, p, p) + Gpu.Log10(p) + Gpu.Exp10(p) + Gpu.CopySign(p, p.YX);
            Float4 q = Gpu.Fma(v[0], v[1], v[2]) + Gpu.CopySign(v[0], v[1]) + Gpu.Log10(v[2]) + Gpu.Exp10(v[3]);
            Float3 r = Gpu.Fma(q.XYZ, q.XYZ, q.XYZ) + Gpu.Log10(q.XYZ) + Gpu.Exp10(q.ZYX) + Gpu.CopySign(q.XYZ, q.ZYX);
            f[2] = w.X + q.X + r.X;
            """);
    }

    [Fact]
    public void StaticInitializers_FoldTheMathFunctions_AndExplainTheOnesTheyCannot()
    {
        GeneratorRun folded = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            "buffer[0] = Decades * factor;",
            "private static readonly float Decades = Gpu.Log10(1000f) + Gpu.Exp10(2f) + Gpu.Fma(2f, 3f, 4f) + Gpu.CopySign(5f, -1f) + MathF.Log10(100f) + float.Exp10(1f);"));

        Assert.Contains("constant float Decades = 3.0f + 100.0f + 10.0f + -5.0f + 2.0f + 10.0f;", folded.Verify(Shader).MslSource);

        // The bit functions are not evaluated at compile time: MSL allows no call in the initializer.
        GeneratorRun rejected = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            "buffer[0] = Bits;",
            "private static readonly int Bits = Gpu.CountBits(255);"));

        Diagnostic diagnostic = rejected.AssertOnlyDiagnostic("SCG0024", "Gpu.CountBits(255)");
        Assert.Contains("the MSL function 'metal::popcount' cannot be evaluated at compile time", diagnostic.GetMessage());
    }

    [Fact]
    public void Classification_ReturnsBool_ForGpuAndFloatStatics()
    {
        AssertKernel(
            """
            float x = f[0];
            bool nan = metal::isnan(x);
            bool inf = metal::isinf(x) || metal::isinf(f[1]);
            bool finite = metal::isfinite(x) && metal::isfinite(f[1]) && !metal::isnan(f[1]);
            if (metal::isnan(x) || !metal::isfinite(x))
            {
                x = 0.0f;
            }
            f[2] = x + (nan || inf || finite ? 1.0f : 0.0f);
            """,
            """
            float x = f[0];
            bool nan = Gpu.IsNaN(x);
            bool inf = Gpu.IsInfinity(x) || float.IsInfinity(f[1]);
            bool finite = Gpu.IsFinite(x) && float.IsFinite(f[1]) && !float.IsNaN(f[1]);
            if (Gpu.IsNaN(x) || !Gpu.IsFinite(x))
            {
                x = 0f;
            }
            f[2] = x + (nan || inf || finite ? 1f : 0f);
            """);
    }

    [Fact]
    public void Unorm8Packing_MapsToThePackFunctions()
    {
        AssertKernel(
            """
            u[3] = metal::pack_float_to_unorm4x8(v[0]) ^ metal::pack_float_to_unorm4x8(float4(f[0], 0.5f, 0.25f, 1.0f));
            v[0] = metal::unpack_unorm4x8_to_float(u[0]) + metal::unpack_unorm4x8_to_float(4278190335u);
            """,
            """
            u[3] = Gpu.PackUnorm4x8(v[0]) ^ Gpu.PackUnorm4x8(new Float4(f[0], 0.5f, 0.25f, 1f));
            v[0] = Gpu.UnpackUnorm4x8(u[0]) + Gpu.UnpackUnorm4x8(0xFF0000FF);
            """);
    }

    [Fact]
    public void TransposeAndDeterminant_MapForEveryMatrixSize()
    {
        AssertKernel(
            """
            float2x2 a = float2x2(1.0f, 2.0f, 3.0f, 4.0f);
            float3x3 b = float3x3(float3(1.0f, 0.0f, 0.0f), float3(0.0f, 2.0f, 0.0f), float3(0.0f, 0.0f, 3.0f));
            float4x4 c = float4x4(v[0], v[1], v[2], v[3]);
            float2x2 at = metal::transpose(a);
            float3x3 bt = metal::transpose(b);
            float4x4 ct = metal::transpose(c);
            f[0] = metal::determinant(at) + metal::determinant(bt) + metal::determinant(ct) + metal::determinant(metal::transpose(ct));
            """,
            """
            Float2x2 a = new Float2x2(1, 2, 3, 4);
            Float3x3 b = new Float3x3(new Float3(1, 0, 0), new Float3(0, 2, 0), new Float3(0, 0, 3));
            Float4x4 c = new Float4x4(v[0], v[1], v[2], v[3]);
            Float2x2 at = Gpu.Transpose(a);
            Float3x3 bt = Gpu.Transpose(b);
            Float4x4 ct = Gpu.Transpose(c);
            f[0] = Gpu.Determinant(at) + Gpu.Determinant(bt) + Gpu.Determinant(ct) + Gpu.Determinant(Gpu.Transpose(ct));
            """);
    }

    [Fact]
    public void Float4x4_ConstructorsOperatorsAndMul_FollowTheSmallerMatrices()
    {
        // Constructor arguments pass through unchanged (C# rows become MSL columns) and Mul swaps
        // its operands to compensate, exactly as for Float2x2 and Float3x3. Every vector operand
        // is converted to the native type first: MSL's matrix operators are templates, which do
        // not deduce through the implicit packed_float4 -> float4 conversion of a buffer element.
        AssertKernel(
            """
            float4x4 m = float4x4(1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f, 9.0f, 10.0f, 11.0f, 12.0f, 13.0f, 14.0f, 15.0f, 16.0f);
            float4x4 n = float4x4(v[0], v[1], v[2], v[3]);
            float4x4 sum = (m + n - m) * 2.0f / 4.0f;
            sum = 0.5f * sum;
            float4 rowTimes = (sum * float4(v[0]));
            float4 timesColumn = (float4(v[0]) * sum);
            float4x4 product = (n * m);
            float4x4 zero = float4x4(0);
            v[1] = rowTimes + timesColumn + (zero * float4((float4(v[2]) * product)));
            """,
            """
            Float4x4 m = new Float4x4(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
            Float4x4 n = new Float4x4(v[0], v[1], v[2], v[3]);
            Float4x4 sum = (m + n - m) * 2f / 4f;
            sum = 0.5f * sum;
            Float4 rowTimes = Gpu.Mul(v[0], sum);
            Float4 timesColumn = Gpu.Mul(sum, v[0]);
            Float4x4 product = Gpu.Mul(m, n);
            Float4x4 zero = default;
            v[1] = rowTimes + timesColumn + Gpu.Mul(Gpu.Mul(product, v[2]), zero);
            """);
    }
}
