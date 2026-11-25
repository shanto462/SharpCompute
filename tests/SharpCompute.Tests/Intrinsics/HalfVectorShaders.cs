using System;
using SharpCompute;

namespace SharpCompute.Tests.Intrinsics;

// Shaders for the half-vector and bool-vector tests. Each runs unchanged on the GPU (For) and on the
// CPU (ForCpu), so the tests compare the two and check both against plain C#.

/// <summary>
/// Half arithmetic on <see cref="Half4"/>: every operator, with a vector and with a scalar, and the
/// three shapes of <c>a * b + c</c> the GPU may contract into one fused multiply-add.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HalfArithmetic(ReadOnlyBuffer<Half4> a, ReadOnlyBuffer<Half4> b, ReadOnlyBuffer<Half4> c, ReadWriteBuffer<Half4> output) : IComputeShader
{
    public const int Stride = 12;

    /// <summary>The first output that contains a multiply followed by an add or subtract.</summary>
    public const int FirstContractable = 8;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;
        Half4 x = a[i], y = b[i], z = c[i];

        output[o] = x + y;
        output[o + 1] = x - y;
        output[o + 2] = x * y;
        output[o + 3] = x / y;
        output[o + 4] = -x;
        output[o + 5] = x % y;
        output[o + 6] = x + y.X;
        output[o + 7] = x.X * y;
        output[o + 8] = x * y + z;
        output[o + 9] = z - x * y;
        output[o + 10] = x * y + y * z;
        output[o + 11] = x + y + z;
    }
}

/// <summary><see cref="HalfArithmetic"/> compiled with contraction off: every output is then one rounded operation after another, as in C#.</summary>
[GeneratedComputeShaderDescriptor]
[ShaderOptions(FpContract = false)]
public readonly partial struct HalfArithmeticUncontracted(ReadOnlyBuffer<Half4> a, ReadOnlyBuffer<Half4> b, ReadOnlyBuffer<Half4> c, ReadWriteBuffer<Half4> output) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * HalfArithmetic.Stride;
        Half4 x = a[i], y = b[i], z = c[i];

        output[o] = x + y;
        output[o + 1] = x - y;
        output[o + 2] = x * y;
        output[o + 3] = x / y;
        output[o + 4] = -x;
        output[o + 5] = x % y;
        output[o + 6] = x + y.X;
        output[o + 7] = x.X * y;
        output[o + 8] = x * y + z;
        output[o + 9] = z - x * y;
        output[o + 10] = x * y + y * z;
        output[o + 11] = x + y + z;
    }
}

/// <summary>
/// Every componentwise <c>Gpu</c> function of half vectors. Outputs 0 to <see cref="FirstTranscendental"/> - 1
/// are the ones the CPU reproduces bit for bit; the rest are the transcendental functions.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HalfFunctions(ReadOnlyBuffer<Half4> a, ReadOnlyBuffer<Half4> b, ReadOnlyBuffer<Half4> c, ReadWriteBuffer<Half4> output) : IComputeShader
{
    public const int FirstTranscendental = 25;

    public const int Stride = 43;

    /// <summary>The outputs whose zero results may differ in sign between the CPU and the GPU: min, max and clamp of zeros of both signs.</summary>
    public static readonly int[] EitherZero = [1, 2, 3, 19, 20, 24];

    public static readonly string[] Names =
    [
        "Abs", "Min", "Max", "Clamp", "Saturate", "Sign", "Floor", "Ceil", "Round", "Trunc",
        "Fract", "Fmod", "Sqrt", "Rsqrt", "Fma", "Lerp", "Step", "Smoothstep", "CopySign", "Min(v, s)",
        "Clamp(v, s, s)", "Lerp(v, v, s)", "Step(s, v)", "Smoothstep(s, s, v)", "Max(v, s)",
        "Pow", "Pow(v, s)", "Exp", "Exp2", "Exp10", "Log", "Log2", "Log10", "Sin", "Cos",
        "Tan", "Asin", "Acos", "Atan", "Atan2", "Sinh", "Cosh", "Tanh",
    ];

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;
        Half4 x = a[i], y = b[i], z = c[i];

        output[o] = Gpu.Abs(x);
        output[o + 1] = Gpu.Min(x, y);
        output[o + 2] = Gpu.Max(x, y);
        output[o + 3] = Gpu.Clamp(x, y, z);
        output[o + 4] = Gpu.Saturate(x);
        output[o + 5] = Gpu.Sign(x);
        output[o + 6] = Gpu.Floor(x);
        output[o + 7] = Gpu.Ceil(x);
        output[o + 8] = Gpu.Round(x);
        output[o + 9] = Gpu.Trunc(x);
        output[o + 10] = Gpu.Fract(x);
        output[o + 11] = Gpu.Fmod(x, y);
        output[o + 12] = Gpu.Sqrt(x);
        output[o + 13] = Gpu.Rsqrt(x);
        output[o + 14] = Gpu.Fma(x, y, z);
        output[o + 15] = Gpu.Lerp(x, y, z);
        output[o + 16] = Gpu.Step(x, y);
        output[o + 17] = Gpu.Smoothstep(x, y, z);
        output[o + 18] = Gpu.CopySign(x, y);
        output[o + 19] = Gpu.Min(x, y.X);
        output[o + 20] = Gpu.Clamp(x, y.X, z.X);
        output[o + 21] = Gpu.Lerp(x, y, z.X);
        output[o + 22] = Gpu.Step(x.X, y);
        output[o + 23] = Gpu.Smoothstep(x.X, y.X, z);
        output[o + 24] = Gpu.Max(x, y.X);

        output[o + 25] = Gpu.Pow(x, y);
        output[o + 26] = Gpu.Pow(x, y.X);
        output[o + 27] = Gpu.Exp(x);
        output[o + 28] = Gpu.Exp2(x);
        output[o + 29] = Gpu.Exp10(x);
        output[o + 30] = Gpu.Log(x);
        output[o + 31] = Gpu.Log2(x);
        output[o + 32] = Gpu.Log10(x);
        output[o + 33] = Gpu.Sin(x);
        output[o + 34] = Gpu.Cos(x);
        output[o + 35] = Gpu.Tan(x);
        output[o + 36] = Gpu.Asin(x);
        output[o + 37] = Gpu.Acos(x);
        output[o + 38] = Gpu.Atan(x);
        output[o + 39] = Gpu.Atan2(x, y);
        output[o + 40] = Gpu.Sinh(x);
        output[o + 41] = Gpu.Cosh(x);
        output[o + 42] = Gpu.Tanh(x);
    }
}

/// <summary>The geometric functions of half vectors, at every width.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HalfGeometry(ReadOnlyBuffer<Half4> a, ReadOnlyBuffer<Half4> b, ReadWriteBuffer<Half4> output) : IComputeShader
{
    public const int Stride = 9;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;
        Half4 x = a[i], y = b[i];

        output[o] = new Half4(Gpu.Dot(x.XY, y.XY), Gpu.Dot(x.XYZ, y.XYZ), Gpu.Dot(x, y), Gpu.Length(x.XY));
        output[o + 1] = new Half4(Gpu.Length(x.XYZ), Gpu.Length(x), Gpu.Distance(x.XY, y.XY), Gpu.Distance(x.XYZ, y.XYZ));
        output[o + 2] = new Half4(Gpu.Distance(x, y), Gpu.Dot(y, y), Gpu.Length(y.ZW), Gpu.Distance(y.XW, x.ZY));
        output[o + 3] = new Half4(Gpu.Normalize(x.XY), Gpu.Normalize(y.ZW));
        output[o + 4] = new Half4(Gpu.Normalize(x.XYZ), Gpu.Length(y.YZW));
        output[o + 5] = Gpu.Normalize(x);
        output[o + 6] = new Half4(Gpu.Reflect(x.XY, y.XY), Gpu.Reflect(y.ZW, x.ZW));
        output[o + 7] = new Half4(Gpu.Reflect(x.XYZ, y.XYZ), Gpu.Dot(x.YZW, y.YZW));
        output[o + 8] = new Half4(Gpu.Cross(x.XYZ, y.XYZ), Gpu.Reflect(x, y).W);
    }
}

/// <summary>Every conversion between half vectors and the other vectors.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HalfConversions(
    ReadOnlyBuffer<Float4> floats, ReadOnlyBuffer<Int4> ints, ReadOnlyBuffer<Half4> halves,
    ReadWriteBuffer<Half4> toHalf, ReadWriteBuffer<Float4> toFloat, ReadWriteBuffer<Int4> toInt, ReadWriteBuffer<UInt4> toUInt) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        toHalf[i * 3] = (Half4)floats[i];
        toHalf[(i * 3) + 1] = (Half4)ints[i];
        toHalf[(i * 3) + 2] = (Half4)(UInt4)ints[i];
        toFloat[i] = (Float4)halves[i];
        toInt[i] = (Int4)halves[i];
        toUInt[i] = (UInt4)halves[i];
    }
}

/// <summary>A struct that mixes half vectors with a float: the half vectors sit at even offsets, the float at a multiple of 4.</summary>
public struct HalfTexel
{
    public Half3 Color;   // 0..6
    public float Weight;  // 8
    public Half2 Uv;      // 12..16
    public Half Bias;     // 16
    public Half4 Tint;    // 18..26, and the struct is 28 bytes
}

/// <summary>
/// Half vectors everywhere a value crosses the CPU/GPU boundary: buffers of every width (a
/// <see cref="Half3"/> buffer has a 6-byte stride), captured constants next to a float, fields of a
/// struct element, a <c>[GroupShared]</c> array and a program-scope constant. Every statement is a
/// single operation, so there is nothing to contract and the GPU and the CPU agree bit for bit.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(16)]
public readonly partial struct HalfBoundary(
    ReadOnlyBuffer<Half2> twos, ReadOnlyBuffer<Half3> threes, ReadWriteBuffer<Half4> fours, ReadWriteBuffer<HalfTexel> texels, ReadWriteBuffer<Half3> sums,
    Half2 scale, float gain, Half3 offset, Half exposure, Half4 tint) : IComputeShader
{
    public static readonly Half3 Weights = (Half3)new Float3(0.5f, 0.25f, 2f);

    [GroupShared]
    private static readonly Half3[] tile = new Half3[16];

    public void Execute()
    {
        int i = ThreadIds.X;
        Half3 three = threes[i];
        Half2 two = twos[i] * scale;

        tile[GroupIds.X] = three + offset;
        Gpu.GroupMemoryBarrierWithGroupSync();
        Half3 neighbour = tile[GroupIds.X ^ 1];

        fours[i] = new Half4(two, three.Z, exposure) + tint;

        HalfTexel texel = texels[i];
        texel.Color = neighbour * Weights;
        texel.Weight = (float)three.X;
        texel.Uv = two.YX;
        texel.Bias = (Half)gain;
        texel.Tint = fours[i].WZYX;
        texels[i] = texel;

        sums[i] = texels[i].Color + threes[i].ZYX;
        sums[i].Y = neighbour.X;
    }
}

/// <summary>Reads an <see cref="Rgba16Float"/> texture as half vectors, scales them and writes them back.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HalfTextureGain(ReadWriteTexture2D<Rgba16Float> image, Half4 gain) : IComputeShader
{
    public void Execute()
    {
        Half4 pixel = (Half4)image[ThreadIds.XY];
        image[ThreadIds.XY] = (Float4)(pixel * gain);
    }
}

/// <summary>
/// Comparisons, equality, classification, the logical operators, <c>Any</c>/<c>All</c> and
/// <c>Select</c>, for every kind of vector. A bool vector cannot be stored, so each one is written as
/// a mask: bit i is component i.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct VectorMasks(
    ReadOnlyBuffer<Float4> floatA, ReadOnlyBuffer<Float4> floatB, ReadOnlyBuffer<Half4> halfA, ReadOnlyBuffer<Half4> halfB, ReadOnlyBuffer<Int4> intA, ReadOnlyBuffer<Int4> intB,
    ReadWriteBuffer<uint> masks, ReadWriteBuffer<UInt4> selectedBits, ReadWriteBuffer<Half4> selectedHalves) : IComputeShader
{
    public const int Stride = 52;

    public const int SelectStride = 5;

    private static uint Bits(Bool4 mask)
    {
        UInt4 bits = Gpu.Select(mask, new UInt4(1u, 2u, 4u, 8u), new UInt4(0u));
        return bits.X | bits.Y | bits.Z | bits.W;
    }

    private static uint Bits(Bool3 mask) => Bits(new Bool4(mask, false));

    private static uint Bits(Bool2 mask) => Bits(new Bool4(mask, false, false));

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;
        Float4 fa = floatA[i], fb = floatB[i];
        Half4 ha = halfA[i], hb = halfB[i];
        Int4 ia = intA[i], ib = intB[i];
        UInt4 ua = (UInt4)ia, ub = (UInt4)ib;

        // Float: vector with vector, vector with scalar, scalar with vector, and straight from the buffers.
        masks[o] = Bits(fa < fb);
        masks[o + 1] = Bits(fa <= fb);
        masks[o + 2] = Bits(fa > fb);
        masks[o + 3] = Bits(fa >= fb);
        masks[o + 4] = Bits(Gpu.Equal(fa, fb));
        masks[o + 5] = Bits(Gpu.NotEqual(fa, fb));
        masks[o + 6] = Bits(fa < fb.X);
        masks[o + 7] = Bits(fa.X >= fb);
        masks[o + 8] = Bits(Gpu.Equal(fa, fb.X));
        masks[o + 9] = Bits(floatA[i] <= floatB[i]);
        masks[o + 10] = Bits(Gpu.NotEqual(floatA[i], floatB[i]));

        // Half.
        masks[o + 11] = Bits(ha < hb);
        masks[o + 12] = Bits(ha <= hb);
        masks[o + 13] = Bits(ha > hb);
        masks[o + 14] = Bits(ha >= hb);
        masks[o + 15] = Bits(Gpu.Equal(ha, hb));
        masks[o + 16] = Bits(Gpu.NotEqual(ha, hb));
        masks[o + 17] = Bits(ha > hb.X);
        masks[o + 18] = Bits(ha.X <= hb);
        masks[o + 19] = Bits(Gpu.NotEqual(ha, hb.X));
        masks[o + 20] = Bits(halfA[i] >= halfB[i]);
        masks[o + 21] = Bits(Gpu.Equal(halfA[i], halfB[i]));

        // Int.
        masks[o + 22] = Bits(ia < ib);
        masks[o + 23] = Bits(ia <= ib);
        masks[o + 24] = Bits(ia > ib);
        masks[o + 25] = Bits(ia >= ib);
        masks[o + 26] = Bits(Gpu.Equal(ia, ib));
        masks[o + 27] = Bits(Gpu.NotEqual(ia, ib));
        masks[o + 28] = Bits(ia < ib.X);
        masks[o + 29] = Bits(ia.X >= ib);
        masks[o + 30] = Bits(intA[i] > intB[i]);

        // UInt: the same bits compare differently.
        masks[o + 31] = Bits(ua < ub);
        masks[o + 32] = Bits(ua <= ub);
        masks[o + 33] = Bits(ua > ub);
        masks[o + 34] = Bits(ua >= ub);
        masks[o + 35] = Bits(Gpu.Equal(ua, ub));
        masks[o + 36] = Bits(Gpu.NotEqual(ua, ub.X));

        // Classification.
        masks[o + 37] = Bits(Gpu.IsNaN(fa)) | (Bits(Gpu.IsInfinity(fa)) << 4) | (Bits(Gpu.IsFinite(fa)) << 8);
        masks[o + 38] = Bits(Gpu.IsNaN(ha)) | (Bits(Gpu.IsInfinity(ha)) << 4) | (Bits(Gpu.IsFinite(ha)) << 8);
        masks[o + 39] = Bits(Gpu.IsNaN(fa.XY)) | (Bits(Gpu.IsInfinity(ha.YZW)) << 4) | (Bits(Gpu.IsFinite(halfA[i])) << 8);

        // The logical operators, on two masks of a different origin.
        Bool4 m = ia < ib;
        Bool4 n = Gpu.NotEqual(ha, hb);
        masks[o + 40] = Bits(m & n);
        masks[o + 41] = Bits(m | n);
        masks[o + 42] = Bits(m ^ n);
        masks[o + 43] = Bits(!m);
        masks[o + 44] = Bits(m.XY & !n.ZW) | (Bits(m.XYZ ^ n.YZW) << 4);

        // Any and All at every width.
        masks[o + 45] = (Gpu.Any(m) ? 1u : 0u) | (Gpu.All(m) ? 2u : 0u) | (Gpu.Any(m.XYZ) ? 4u : 0u) | (Gpu.All(m.XYZ) ? 8u : 0u) | (Gpu.Any(m.ZW) ? 16u : 0u) | (Gpu.All(m.ZW) ? 32u : 0u);
        masks[o + 46] = Gpu.All(fa < fb | fa >= fb) ? 1u : 0u;

        // Select of bool vectors, and at the narrower widths.
        masks[o + 47] = Bits(Gpu.Select(m, n, !n));
        masks[o + 48] = Bits(Gpu.Select(m.XYZ, n.XYZ, n.YZW)) | (Bits(Gpu.Select(m.ZW, n.XY, m.XY)) << 4);
        Int3 three = Gpu.Select(n.XYZ, ia.XYZ, ib.XYZ);
        Int2 two = Gpu.Select(m.YZ, ia.ZW, ib.XY);
        masks[o + 49] = (uint)(three.X ^ three.Y ^ three.Z);
        masks[o + 50] = (uint)(two.X ^ two.Y);
        Float2 pair = Gpu.Select(n.XW, fa.XY, fb.ZW);
        masks[o + 51] = Gpu.AsUInt(pair.X) ^ Gpu.AsUInt(pair.Y);

        // Select copies bits: floats (as their bits), ints, uints, and halves in a buffer of their own.
        int s = i * SelectStride;
        selectedBits[s] = Gpu.AsUInt(Gpu.Select(m, fa, fb));
        selectedBits[s + 1] = (UInt4)Gpu.Select(n, ia, ib);
        selectedBits[s + 2] = Gpu.Select(m ^ n, ua, ub);
        selectedBits[s + 3] = Gpu.AsUInt(Gpu.Select(fa > fb, floatA[i], floatB[i]));
        selectedBits[s + 4] = Gpu.AsUInt(Gpu.Select(Gpu.IsNaN(fa), new Float4(0f), fa));
        selectedHalves[i * 2] = Gpu.Select(m, ha, hb);
        selectedHalves[(i * 2) + 1] = Gpu.Select(ha < hb, halfA[i], halfB[i]);
    }
}
