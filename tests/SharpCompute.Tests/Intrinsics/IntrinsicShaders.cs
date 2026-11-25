using System;
using System.Numerics;
using SharpCompute;

namespace SharpCompute.Tests.Intrinsics;

// Shaders for the atomics, bit/packing intrinsic and Float4x4 tests. Each runs unchanged on the GPU
// (For) and on the CPU (ForCpu), so the tests compare the two and check both against plain C#.

/// <summary>
/// Every thread applies every atomic operation, int and uint, to the same few elements: the
/// heaviest contention there is. Slots: 0 add, 1 sub, 2 min, 3 max, 4 and, 5 or, 6 xor, 7 exchange,
/// 8 the sum of what the exchanges returned. <c>addOrder</c> counts how often each value returned by
/// the add was seen; if the add is atomic that is exactly once per value, so no two threads ever
/// write the same element of it.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AtomicEveryOperation(
    ReadWriteBuffer<int> ints, ReadWriteBuffer<uint> uints, ReadWriteBuffer<int> addOrder, ReadWriteBuffer<uint> uintAddOrder) : IComputeShader
{
    /// <summary>A bijection of the 32-bit integers (an odd multiplier and an xor-shift), so the values of a dispatch are all distinct.</summary>
    public static uint Mix(uint x)
    {
        x *= 2654435761u;
        x ^= x >> 15;
        return x;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        uint u = (uint)i;
        int mixed = (int)Mix(u);

        addOrder[Gpu.AtomicAdd(ref ints[0], 1)] += 1;
        uintAddOrder[(int)Gpu.AtomicAdd(ref uints[0], 1u)] += 1u;

        Gpu.AtomicSub(ref ints[1], 3);
        Gpu.AtomicSub(ref uints[1], 3u);

        Gpu.AtomicMin(ref ints[2], mixed);
        Gpu.AtomicMin(ref uints[2], (uint)mixed);
        Gpu.AtomicMax(ref ints[3], mixed);
        Gpu.AtomicMax(ref uints[3], (uint)mixed);

        // Every thread clears one of the low 12 bits and sets one of the low 20: 12 stay clear of the and, 20 are set by the or.
        Gpu.AtomicAnd(ref ints[4], ~(1 << (i % 12)));
        Gpu.AtomicAnd(ref uints[4], ~(1u << (i % 12)));
        Gpu.AtomicOr(ref ints[5], 1 << (i % 20));
        Gpu.AtomicOr(ref uints[5], 1u << (i % 20));

        Gpu.AtomicXor(ref ints[6], mixed);
        Gpu.AtomicXor(ref uints[6], (uint)mixed);

        // What an exchange returns was stored by exactly one other exchange (or is the initial value).
        Gpu.AtomicAdd(ref ints[8], Gpu.AtomicExchange(ref ints[7], i + 1));
        Gpu.AtomicAdd(ref uints[8], Gpu.AtomicExchange(ref uints[7], u + 1u));
    }
}

/// <summary>
/// Compare-and-swap: every thread tries to claim one element (exactly one may win), and increments a
/// counter with the classic retry loop, which only counts right if the swap is a strong one.
/// Slots: 0 the claim, 1 the number of winners, 2 the counter.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AtomicCompareExchangeLoop(ReadWriteBuffer<int> ints, ReadWriteBuffer<uint> uints) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        if (Gpu.AtomicCompareExchange(ref ints[0], 0, i + 1) == 0)
        {
            Gpu.AtomicAdd(ref ints[1], 1);
        }

        if (Gpu.AtomicCompareExchange(ref uints[0], 0u, (uint)i + 1u) == 0u)
        {
            Gpu.AtomicAdd(ref uints[1], 1u);
        }

        int seen = 0;

        for (int attempt = 0; attempt < 100000000; attempt++)
        {
            int original = Gpu.AtomicCompareExchange(ref ints[2], seen, seen + 1);

            if (original == seen)
            {
                break;
            }

            seen = original;
        }

        uint useen = 0u;

        for (int attempt = 0; attempt < 100000000; attempt++)
        {
            uint original = Gpu.AtomicCompareExchange(ref uints[2], useen, useen + 1u);

            if (original == useen)
            {
                break;
            }

            useen = original;
        }
    }
}

/// <summary>
/// The float add: all threads add 1 to <c>sums[0]</c> (the returned values are the integers
/// 0..n-1, each once) and a multiple of 0.25 to <c>sums[1]</c>. Both stay exact in a float, so the
/// order the threads ran in does not show.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AtomicFloatSum(ReadWriteBuffer<float> sums, ReadWriteBuffer<int> order) : IComputeShader
{
    public void Execute()
    {
        float before = Gpu.AtomicAdd(ref sums[0], 1f);
        order[(int)before] += 1;
        Gpu.AtomicAdd(ref sums[1], (ThreadIds.X & 3) * 0.25f);
    }
}

/// <summary>A histogram: many threads, <c>bins</c> counters.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AtomicHistogram(ReadOnlyBuffer<uint> values, ReadWriteBuffer<int> counts, int bins) : IComputeShader
{
    public void Execute() => Gpu.AtomicAdd(ref counts[(int)(values[ThreadIds.X] % (uint)bins)], 1);
}

/// <summary>Every integer bit intrinsic of one input word, 24 output words per thread.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BitIntrinsics(ReadOnlyBuffer<uint> input, ReadWriteBuffer<uint> output) : IComputeShader
{
    public const int Stride = 24;

    public void Execute()
    {
        int t = ThreadIds.X;
        uint u = input[t];
        int i = (int)u;
        int o = t * Stride;

        // Reinterpretation: through a float local and back, as scalars and as vectors.
        float f = Gpu.AsFloat(u);
        output[o + 0] = Gpu.AsUInt(f);
        output[o + 1] = (uint)Gpu.AsInt(Gpu.AsFloat(i));
        output[o + 2] = BitConverter.SingleToUInt32Bits(BitConverter.UInt32BitsToSingle(u)) ^ (uint)BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(i));
        UInt3 roundTrip = Gpu.AsUInt(Gpu.AsFloat(new UInt3(u, ~u, u ^ 0x55555555u)));
        Int2 signedRoundTrip = Gpu.AsInt(Gpu.AsFloat(new Int2(i, ~i)));
        output[o + 3] = roundTrip.X ^ (roundTrip.Y << 1) ^ (roundTrip.Z << 2) ^ (uint)signedRoundTrip.X ^ ((uint)signedRoundTrip.Y << 3);

        // Counts and reversal, uint and int.
        output[o + 4] = Gpu.CountBits(u);
        output[o + 5] = (uint)Gpu.CountBits(i);
        output[o + 6] = Gpu.LeadingZeroCount(u);
        output[o + 7] = (uint)Gpu.LeadingZeroCount(i);
        output[o + 8] = Gpu.TrailingZeroCount(u);
        output[o + 9] = (uint)Gpu.TrailingZeroCount(i);
        output[o + 10] = Gpu.ReverseBits(u);
        output[o + 11] = (uint)Gpu.ReverseBits(i);

        // The same on vectors.
        UInt4 v = new UInt4(u, ~u, u >> 7, u << 9);
        UInt4 counted = Gpu.CountBits(v) + (Gpu.LeadingZeroCount(v) << 8) + (Gpu.TrailingZeroCount(v) << 16);
        output[o + 12] = counted.X ^ (counted.Y << 1) ^ (counted.Z << 2) ^ (counted.W << 3);
        UInt4 reversed = Gpu.ReverseBits(v);
        output[o + 13] = reversed.X ^ reversed.Y ^ reversed.Z ^ reversed.W;
        Int3 s = new Int3(i, -i, i >> 3);
        Int3 signedCounted = Gpu.CountBits(s) + (Gpu.LeadingZeroCount(s) << 8) + (Gpu.TrailingZeroCount(s) << 16) + Gpu.ReverseBits(s);
        output[o + 14] = (uint)(signedCounted.X ^ (signedCounted.Y << 1) ^ (signedCounted.Z << 2));

        // System.Numerics.BitOperations, including rotate counts outside 0..31 and negative ones.
        output[o + 15] = (uint)BitOperations.PopCount(u);
        output[o + 16] = (uint)BitOperations.LeadingZeroCount(u);
        output[o + 17] = (uint)BitOperations.TrailingZeroCount(u) + ((uint)BitOperations.TrailingZeroCount(i) << 8);
        output[o + 18] = BitOperations.RotateLeft(u, t % 67);
        output[o + 19] = BitOperations.RotateRight(u, t % 67);
        output[o + 20] = BitOperations.RotateLeft(u, 13 - t);
        output[o + 21] = BitOperations.RotateRight(u, i);

        // Classification of the word as a float, and the sign-only operation on it.
        output[o + 22] = (Gpu.IsNaN(f) ? 1u : 0u) | (Gpu.IsInfinity(f) ? 2u : 0u) | (Gpu.IsFinite(f) ? 4u : 0u)
            | (float.IsNaN(f) ? 8u : 0u) | (float.IsInfinity(f) ? 16u : 0u) | (float.IsFinite(f) ? 32u : 0u);
        output[o + 23] = Gpu.AsUInt(Gpu.CopySign(f, -1f)) ^ (Gpu.AsUInt(Gpu.CopySign(2f, f)) >> 1) ^ (Gpu.AsUInt(MathF.CopySign(f, 1f)) >> 2);
    }
}

/// <summary>The float intrinsics of three inputs: bit patterns of the exact ones, values of the transcendental ones.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct FloatIntrinsics(
    ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadOnlyBuffer<float> c, ReadWriteBuffer<uint> exact, ReadWriteBuffer<float> approximate) : IComputeShader
{
    public const int ExactStride = 4;
    public const int ApproximateStride = 8;

    public void Execute()
    {
        int t = ThreadIds.X;
        float x = a[t], y = b[t], z = c[t];

        exact[(t * ExactStride) + 0] = Gpu.AsUInt(Gpu.Fma(x, y, z));
        exact[(t * ExactStride) + 1] = Gpu.AsUInt(MathF.FusedMultiplyAdd(z, y, x));
        Float3 fused = Gpu.Fma(new Float3(x, y, z), new Float3(y, z, x), new Float3(z, x, y));
        exact[(t * ExactStride) + 2] = Gpu.AsUInt(fused.X) ^ (Gpu.AsUInt(fused.Y) >> 1) ^ (Gpu.AsUInt(fused.Z) >> 2);
        Float2 signs = Gpu.CopySign(new Float2(x, y), new Float2(y, z));
        exact[(t * ExactStride) + 3] = Gpu.AsUInt(signs.X) ^ (Gpu.AsUInt(signs.Y) >> 1) ^ (Gpu.AsUInt(Gpu.CopySign(z, x)) >> 2);

        Float2 logs = Gpu.Log10(new Float2(y, z));
        Float2 powers = Gpu.Exp10(new Float2(z, x));
        approximate[(t * ApproximateStride) + 0] = Gpu.Log10(x);
        approximate[(t * ApproximateStride) + 1] = Gpu.Exp10(y);
        approximate[(t * ApproximateStride) + 2] = logs.X;
        approximate[(t * ApproximateStride) + 3] = logs.Y;
        approximate[(t * ApproximateStride) + 4] = powers.X;
        approximate[(t * ApproximateStride) + 5] = powers.Y;
        approximate[(t * ApproximateStride) + 6] = MathF.Log10(z);
        approximate[(t * ApproximateStride) + 7] = float.Exp10(x);
    }
}

/// <summary>Packs a color, and unpacks a word, per thread.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Unorm8Packing(
    ReadOnlyBuffer<Float4> colors, ReadWriteBuffer<uint> packed, ReadOnlyBuffer<uint> words, ReadWriteBuffer<Float4> unpacked) : IComputeShader
{
    public void Execute()
    {
        int t = ThreadIds.X;

        packed[t] = Gpu.PackUnorm4x8(colors[t]);
        unpacked[t] = Gpu.UnpackUnorm4x8(words[t]);
    }
}

/// <summary>A struct with a matrix at an offset no 16-byte aligned type could have.</summary>
public struct Posed
{
    public float Weight;
    public Float4x4 Transform;
    public int Id;
}

/// <summary>
/// Float4x4 in every boundary position: a constant, buffer elements (read and written) and a struct
/// field, with each product form, the componentwise operators, transpose and determinant.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatrixMath(
    ReadOnlyBuffer<Float4x4> left,
    ReadWriteBuffer<Float4x4> right,
    ReadOnlyBuffer<Float4> vectors,
    ReadWriteBuffer<Float4x4> products,
    ReadWriteBuffer<Float4> transformed,
    ReadWriteBuffer<float> determinants,
    ReadWriteBuffer<Posed> posed,
    float scale,
    Float4x4 view) : IComputeShader
{
    public void Execute()
    {
        int t = ThreadIds.X;
        Float4x4 a = left[t];
        Float4x4 b = right[t];
        Float4 v = vectors[t];

        products[(t * 4) + 0] = Gpu.Mul(a, b);
        products[(t * 4) + 1] = Gpu.Mul(Gpu.Mul(a, view), right[t]);
        products[(t * 4) + 2] = Gpu.Transpose(a);
        products[(t * 4) + 3] = ((a + b) - view) * scale / 4f;

        transformed[(t * 3) + 0] = Gpu.Mul(v, a);
        transformed[(t * 3) + 1] = Gpu.Mul(a, vectors[t]);
        transformed[(t * 3) + 2] = Gpu.Mul(Gpu.Mul(vectors[t], view), Gpu.Transpose(b));

        determinants[(t * 3) + 0] = Gpu.Determinant(a);
        determinants[(t * 3) + 1] = Gpu.Determinant(Gpu.Transpose(b));
        determinants[(t * 3) + 2] = Gpu.Determinant(view) * posed[t].Weight;

        // Read-modify-write of packed storage, in a buffer element and in a struct field.
        right[t] *= 0.5f;
        Posed p = posed[t];
        p.Transform = Gpu.Mul(p.Transform, a);
        p.Id += 1;
        posed[t] = p;
        posed[t].Transform += view;
    }
}

/// <summary>Transpose and determinant of the matrices that only exist as locals, built from buffer floats.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SmallMatrixMath(ReadOnlyBuffer<float> elements, ReadWriteBuffer<float> results) : IComputeShader
{
    public const int Stride = 16;

    public void Execute()
    {
        int t = ThreadIds.X;
        int e = t * 9;
        Float2x2 m2 = new Float2x2(elements[e], elements[e + 1], elements[e + 2], elements[e + 3]);
        Float3x3 m3 = new Float3x3(
            elements[e], elements[e + 1], elements[e + 2],
            elements[e + 3], elements[e + 4], elements[e + 5],
            elements[e + 6], elements[e + 7], elements[e + 8]);
        Float2 v2 = new Float2(elements[e + 4], elements[e + 5]);
        Float3 v3 = new Float3(elements[e + 6], elements[e + 7], elements[e + 8]);
        int o = t * Stride;

        results[o + 0] = Gpu.Determinant(m2);
        results[o + 1] = Gpu.Determinant(m3);
        results[o + 2] = Gpu.Determinant(Gpu.Transpose(m2));
        results[o + 3] = Gpu.Determinant(Gpu.Transpose(m3));

        // Mul(M, v) == Mul(v, transpose(M)), exactly: the same products in the same order.
        Float2 a2 = Gpu.Mul(m2, v2), b2 = Gpu.Mul(v2, Gpu.Transpose(m2));
        Float3 a3 = Gpu.Mul(m3, v3), b3 = Gpu.Mul(v3, Gpu.Transpose(m3));
        results[o + 4] = a2.X;
        results[o + 5] = a2.Y;
        results[o + 6] = b2.X;
        results[o + 7] = b2.Y;
        results[o + 8] = a3.X;
        results[o + 9] = a3.Y;
        results[o + 10] = a3.Z;
        results[o + 11] = b3.X;
        results[o + 12] = b3.Y;
        results[o + 13] = b3.Z;
        results[o + 14] = Gpu.Determinant(Gpu.Mul(m2, Gpu.Transpose(m2)));
        results[o + 15] = Gpu.Determinant(Gpu.Mul(m3, m3));
    }
}

/// <summary>
/// Matrix products whose vector operand has the packed type without being a direct read of a buffer:
/// a conditional between two elements, a helper's result, a swizzle, a struct field.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MulPackedOperands(ReadWriteBuffer<Float3> points, ReadWriteBuffer<Float3> results, Float3 offset) : IComputeShader
{
    private Float3 Pick(int index) => points[index];

    public void Execute()
    {
        int i = ThreadIds.X;
        Float3x3 m = new Float3x3(2f, 0f, 0f, 0f, 3f, 0f, 1f, 0f, 4f);
        Float2x2 r = new Float2x2(0f, 1f, -1f, 0f);

        results[(i * 4) + 0] = Gpu.Mul(m, i > 1 ? points[i] : points[0]);
        results[(i * 4) + 1] = Gpu.Mul(i > 1 ? points[i] : offset, m);
        results[(i * 4) + 2] = Gpu.Mul(m, Pick(i)) + Gpu.Mul(Pick(0), m);
        results[(i * 4) + 3] = new Float3(Gpu.Mul(r, points[i].XY) + Gpu.Mul(points[i].ZX, r), 0f);
    }
}
