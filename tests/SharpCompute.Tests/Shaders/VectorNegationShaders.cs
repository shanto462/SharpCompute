using SharpCompute;

namespace SharpCompute.Tests.Shaders;

// Shaders for the vector negation tests (VectorNegationTests). Most have the shape that the Metal
// compiler miscompiles when an integer vector is negated with its own operator: the negation has a
// lane that is never read, and the same vector is subtracted in an add/multiply/subtract chain. The
// chain then loses that lane on the GPU. Each thread takes one pair (a, b) and writes a fixed number
// of slots, which the GPU and ForCpu must agree on.

/// <summary>A vector of three <c>long</c>s, which SharpCompute does not have: locals only.</summary>
[MslType("long3")]
public struct Long3
{
    public long X;
    public long Y;
    public long Z;

    public Long3(long value) => (X, Y, Z) = (value, value, value);

    public Long3(long x, long y, long z) => (X, Y, Z) = (x, y, z);

    public static Long3 operator -(Long3 v) => unchecked(new Long3(-v.X, -v.Y, -v.Z));

    public static Long3 operator +(Long3 a, Long3 b) => unchecked(new Long3(a.X + b.X, a.Y + b.Y, a.Z + b.Z));

    public static Long3 operator -(Long3 a, Long3 b) => unchecked(new Long3(a.X - b.X, a.Y - b.Y, a.Z - b.Z));

    public static Long3 operator *(Long3 a, long b) => unchecked(new Long3(a.X * b, a.Y * b, a.Z * b));
}

/// <summary>A vector of three <c>short</c>s, as for <see cref="Long3"/>.</summary>
[MslType("short3")]
public struct Short3
{
    public short X;
    public short Y;
    public short Z;

    public Short3(short value) => (X, Y, Z) = (value, value, value);

    public Short3(short x, short y, short z) => (X, Y, Z) = (x, y, z);

    public static Short3 operator -(Short3 v) => unchecked(new Short3((short)-v.X, (short)-v.Y, (short)-v.Z));

    public static Short3 operator +(Short3 a, Short3 b) => unchecked(new Short3((short)(a.X + b.X), (short)(a.Y + b.Y), (short)(a.Z + b.Z)));

    public static Short3 operator -(Short3 a, Short3 b) => unchecked(new Short3((short)(a.X - b.X), (short)(a.Y - b.Y), (short)(a.Z - b.Z)));

    public static Short3 operator *(Short3 a, short b) => unchecked(new Short3((short)(a.X * b), (short)(a.Y * b), (short)(a.Z * b)));
}

/// <summary>A negated <c>Int3</c> whose Y is unused, and a chain that subtracts the same vector. Nothing overflows.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NegationInt3(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Slots = 4;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;

        Int3 v = new Int3(a, b, a ^ b);
        Int3 n = -v;
        output[o] = n.X + n.Z;

        Int3 t = v;
        t += new Int3(1000);
        t *= 3;
        t -= v;
        output[o + 1] = t.X;
        output[o + 2] = t.Y;
        output[o + 3] = t.Z;
    }
}

/// <summary>As <see cref="NegationInt3"/> for an <c>Int4</c> of which two lanes of the negation are unused.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NegationInt4(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Slots = 5;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;

        Int4 v = new Int4(a, b, a ^ b, a + b);
        Int4 n = -v;
        output[o] = n.X + n.W;

        Int4 t = v;
        t += new Int4(1000);
        t *= 3;
        t -= v;
        output[o + 1] = t.X;
        output[o + 2] = t.Y;
        output[o + 3] = t.Z;
        output[o + 4] = t.W;
    }
}

/// <summary>As <see cref="NegationInt3"/> for a vector of <c>long</c>s.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NegationLong3(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 4;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;

        Long3 v = new Long3(a, b, a ^ b);
        Long3 n = -v;
        output[o] = n.X + n.Z;

        Long3 t = v;
        t += new Long3(1000);
        t *= 3;
        t -= v;
        output[o + 1] = t.X;
        output[o + 2] = t.Y;
        output[o + 3] = t.Z;
    }
}

/// <summary>As <see cref="NegationInt3"/> for a vector of <c>short</c>s.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NegationShort3(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Slots = 4;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;

        Short3 v = new Short3((short)a, (short)b, (short)(a ^ b));
        Short3 n = -v;
        output[o] = n.X + n.Z;

        Short3 t = v;
        t += new Short3(1000);
        t *= 3;
        t -= v;
        output[o + 1] = t.X;
        output[o + 2] = t.Y;
        output[o + 3] = t.Z;
    }
}

/// <summary>
/// The same shape where the arithmetic wraps: the pairs are edge values, <c>int.MinValue</c> among
/// them, whose negation is itself, and the chains overflow. The second block negates inside a chain
/// of wrapped arithmetic; the last slots are every lane of the negations.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NegationWrapped(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Slots = 20;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;

        Int3 v = new Int3(a, b, a ^ b);
        Int3 n = -v;
        output[o] = n.X + n.Z;

        Int3 t = v;
        t += new Int3(int.MaxValue);
        t *= 3;
        t -= v;
        output[o + 1] = t.X;
        output[o + 2] = t.Y;
        output[o + 3] = t.Z;

        // The negation is an operand of a wrapped product, and the chain subtracts that product.
        Int3 w = new Int3(b, a + b, a - b);
        Int3 x = new Int3(a + 1, b - 1, a * b);
        Int3 m = -w * x;
        output[o + 4] = m.X + m.Z;

        Int3 s = w;
        s += new Int3(int.MinValue);
        s *= 5;
        s -= w * x;
        output[o + 5] = s.X;
        output[o + 6] = s.Y;
        output[o + 7] = s.Z;

        // Every lane, of every form of the negation.
        Int3 y = new Int3(b, a, a + b);
        Int3 direct = -y;
        Int3 zeroMinus = new Int3(0) - y;
        Int3 timesMinusOne = y * -1;
        Int3 chained = x + -y;
        output[o + 8] = direct.X;
        output[o + 9] = direct.Y;
        output[o + 10] = direct.Z;
        output[o + 11] = zeroMinus.X;
        output[o + 12] = zeroMinus.Y;
        output[o + 13] = zeroMinus.Z;
        output[o + 14] = timesMinusOne.X;
        output[o + 15] = timesMinusOne.Y;
        output[o + 16] = timesMinusOne.Z;
        output[o + 17] = chained.X;
        output[o + 18] = chained.Y;
        output[o + 19] = chained.Z;
    }
}

/// <summary>
/// The operators that negate by a constant, each on a vector of its own and in the shape of
/// <see cref="NegationInt3"/> (written out per form: the shape has to be in one function): <c>0 - v</c>, <c>v * -1</c>, <c>-1 * v</c>, <c>v / -1</c> and
/// <c>v *= -1</c> on an <c>Int3</c>, and <c>0 - u</c> and <c>u * uint.MaxValue</c> on a <c>UInt3</c>.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NegationByConstant(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Forms = 8;
    public const int Slots = Forms * 4;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;

        Int3 v0 = new Int3(a, b, a ^ b);
        Int3 n0 = new Int3(0) - v0;
        output[o] = n0.X + n0.Z;

        Int3 t0 = v0;
        t0 += new Int3(1000);
        t0 *= 3;
        t0 -= v0;
        output[o + 1] = t0.X;
        output[o + 2] = t0.Y;
        output[o + 3] = t0.Z;

        Int3 v1 = new Int3(a + 1, b, a ^ b);
        Int3 n1 = 0 - v1;
        output[o + 4] = n1.X + n1.Z;

        Int3 t1 = v1;
        t1 += new Int3(1000);
        t1 *= 3;
        t1 -= v1;
        output[o + 5] = t1.X;
        output[o + 6] = t1.Y;
        output[o + 7] = t1.Z;

        Int3 v2 = new Int3(a + 2, b, a ^ b);
        Int3 n2 = v2 * -1;
        output[o + 8] = n2.X + n2.Z;

        Int3 t2 = v2;
        t2 += new Int3(1000);
        t2 *= 3;
        t2 -= v2;
        output[o + 9] = t2.X;
        output[o + 10] = t2.Y;
        output[o + 11] = t2.Z;

        Int3 v3 = new Int3(a + 3, b, a ^ b);
        Int3 n3 = new Int3(-1) * v3;
        output[o + 12] = n3.X + n3.Z;

        Int3 t3 = v3;
        t3 += new Int3(1000);
        t3 *= 3;
        t3 -= v3;
        output[o + 13] = t3.X;
        output[o + 14] = t3.Y;
        output[o + 15] = t3.Z;

        Int3 v4 = new Int3(a + 4, b, a ^ b);
        Int3 n4 = v4 / -1;
        output[o + 16] = n4.X + n4.Z;

        Int3 t4 = v4;
        t4 += new Int3(1000);
        t4 *= 3;
        t4 -= v4;
        output[o + 17] = t4.X;
        output[o + 18] = t4.Y;
        output[o + 19] = t4.Z;

        Int3 v5 = new Int3(a + 5, b, a ^ b);
        Int3 n5 = v5;
        n5 *= -1;
        output[o + 20] = n5.X + n5.Z;

        Int3 t5 = v5;
        t5 += new Int3(1000);
        t5 *= 3;
        t5 -= v5;
        output[o + 21] = t5.X;
        output[o + 22] = t5.Y;
        output[o + 23] = t5.Z;

        UInt3 v6 = new UInt3((uint)a + 6, (uint)b, (uint)(a ^ b));
        UInt3 n6 = new UInt3(0) - v6;
        output[o + 24] = (int)(n6.X + n6.Z);

        UInt3 t6 = v6;
        t6 += new UInt3(1000);
        t6 *= 3;
        t6 -= v6;
        output[o + 25] = (int)t6.X;
        output[o + 26] = (int)t6.Y;
        output[o + 27] = (int)t6.Z;

        UInt3 v7 = new UInt3((uint)a + 7, (uint)b, (uint)(a ^ b));
        UInt3 n7 = v7 * uint.MaxValue;
        output[o + 28] = (int)(n7.X + n7.Z);

        UInt3 t7 = v7;
        t7 += new UInt3(1000);
        t7 *= 3;
        t7 -= v7;
        output[o + 29] = (int)t7.X;
        output[o + 30] = (int)t7.Y;
        output[o + 31] = (int)t7.Z;
    }
}

/// <summary>
/// Constants that negate only some lanes, where the lanes that are never read are the other ones:
/// a difference from a constant with zero lanes and a product with a constant with lanes of minus
/// one, each on a vector of its own and followed by a chain that subtracts the same vector.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NegationOfSomeLanes(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Slots = 36;

    private static readonly Int3 Flip = new Int3(-1, 1, -1);

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;
        Int3 c = new Int3(1000, 2000, 3000);

        // 0: (0, 5, 0) - w, of which Y is never read.
        Int3 w0 = new Int3(a, b, a ^ b);
        Int3 n0 = new Int3(0, 5, 0) - w0;
        output[o] = n0.X + n0.Z;
        Int3 t0 = ((w0 + c) * 3) - w0;
        output[o + 1] = t0.X;
        output[o + 2] = t0.Y;
        output[o + 3] = t0.Z;

        // 1: w * (-1, 7, -1).
        Int3 w1 = new Int3(a + 1, b - 1, (a ^ b) + 1);
        Int3 n1 = w1 * new Int3(-1, 7, -1);
        output[o + 4] = n1.X + n1.Z;
        Int3 t1 = ((w1 + c) * 3) - w1;
        output[o + 5] = t1.X;
        output[o + 6] = t1.Y;
        output[o + 7] = t1.Z;

        // 2: flipping two axes of an Int4, of which only those two are read.
        Int4 w2 = new Int4(a + 2, b - 2, a + b, a - b);
        Int4 n2 = new Int4(1, -1, -1, 1) * w2;
        output[o + 8] = n2.Y + n2.Z;
        Int4 t2 = ((w2 + new Int4(10, 20, 30, 40)) * 3) - w2;
        output[o + 9] = t2.X;
        output[o + 10] = t2.Y;
        output[o + 11] = t2.Z;
        output[o + 12] = t2.W;

        // 3: a constant field, and the product assigned in place.
        Int3 w3 = new Int3(a + 3, b - 3, (a ^ b) + 3);
        Int3 n3 = w3;
        n3 *= Flip;
        output[o + 13] = n3.X + n3.Z;
        Int3 t3 = ((w3 + c) * 3) - w3;
        output[o + 14] = t3.X;
        output[o + 15] = t3.Y;
        output[o + 16] = t3.Z;

        // 4 and 5: unsigned.
        UInt3 u4 = new UInt3((uint)a + 4u, (uint)b + 4u, (uint)(a ^ b) + 4u);
        UInt3 n4 = new UInt3(0u, 5u, 0u) - u4;
        output[o + 17] = (int)(n4.X + n4.Z);
        UInt3 t4 = ((u4 + new UInt3(1000u, 2000u, 3000u)) * 3u) - u4;
        output[o + 18] = (int)t4.X;
        output[o + 19] = (int)t4.Y;
        output[o + 20] = (int)t4.Z;

        UInt3 u5 = new UInt3((uint)a + 5u, (uint)b + 5u, (uint)(a ^ b) + 5u);
        UInt3 n5 = u5 * new UInt3(uint.MaxValue, 7u, uint.MaxValue);
        output[o + 21] = (int)(n5.X + n5.Z);
        UInt3 t5 = ((u5 + new UInt3(1000u, 2000u, 3000u)) * 3u) - u5;
        output[o + 22] = (int)t5.X;
        output[o + 23] = (int)t5.Y;
        output[o + 24] = (int)t5.Z;

        // 6: every lane of both forms, for the values themselves.
        Int3 w6 = new Int3(a + 6, b - 6, (a ^ b) + 6);
        Int3 difference = new Int3(0, 5, 0) - w6;
        Int3 product = w6 * new Int3(1, -1, 7);
        Int3 chained = c + (new Int3(7, 0, 0) - w6) + (w6 * new Int3(-1, -1, 3));
        output[o + 25] = difference.X;
        output[o + 26] = difference.Y;
        output[o + 27] = difference.Z;
        output[o + 28] = product.X;
        output[o + 29] = product.Y;
        output[o + 30] = product.Z;
        output[o + 31] = chained.X;
        output[o + 32] = chained.Y;
        output[o + 33] = chained.Z;

        // 7: the scalar that a difference broadcasts.
        Int3 n7 = new Int3(0, 5, 0) - a;
        output[o + 34] = n7.X + n7.Z;
        output[o + 35] = n7.Y;
    }
}

/// <summary>A struct with integer vectors, for <see cref="NegationInStorage"/>.</summary>
public struct NegationCell
{
    public Int3 V;
    public Int2 P;
    public UInt4 U;
}

/// <summary>
/// Negations of vectors that are read from storage, in the same shape: an <c>Int2</c> and an
/// <c>Int4</c>, a packed element of a buffer, a field of a struct in a buffer, a group-shared
/// element, a negation in a helper and inside a chain, and <c>*= -1</c> in place on a struct field
/// and on a buffer element.
/// </summary>
[ThreadGroupSize(64, 1, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct NegationInStorage(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<Int3> vectors, ReadWriteBuffer<NegationCell> cells, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Slots = 51;

    [GroupShared]
    private static readonly Int3[] Tile = new Int3[64];

    private static Int3 Negate(Int3 value) => -value;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;
        Int3 v = new Int3(a, b, a ^ b);
        Int3 c = new Int3(1000, 2000, 3000);

        // 0: Int2 and Int4.
        Int2 p = new Int2(a + 6, b + 6);
        Int2 np = -p;
        output[o] = np.X;
        Int2 tp = ((p + new Int2(100, 200)) * 3) - p;
        output[o + 1] = tp.X;
        output[o + 2] = tp.Y;
        Int4 q = new Int4(a + 7, b + 7, a + b, a - b);
        Int4 nq = -q;
        output[o + 3] = nq.X + nq.W;
        Int4 tq = ((q + new Int4(10, 20, 30, 40)) * 3) - q;
        output[o + 4] = tq.X;
        output[o + 5] = tq.Y;
        output[o + 6] = tq.Z;
        output[o + 7] = tq.W;

        // 1: a packed element of a buffer.
        vectors[i] = v + 9;
        Int3 n1 = -vectors[i];
        output[o + 8] = n1.X + n1.Z;
        Int3 t1 = ((vectors[i] + c) * 3) - vectors[i];
        output[o + 9] = t1.X;
        output[o + 10] = t1.Y;
        output[o + 11] = t1.Z;

        // 2: fields of a struct in a buffer.
        cells[i].V = v + 10;
        cells[i].P = new Int2(a + 10, b + 10);
        Int3 n2 = -cells[i].V;
        Int2 n2p = -cells[i].P;
        output[o + 12] = n2.Y + n2p.Y;
        Int3 t2 = ((cells[i].V + c) * 3) - cells[i].V;
        Int2 t2p = ((cells[i].P + new Int2(5, 6)) * 3) - cells[i].P;
        output[o + 13] = t2.X;
        output[o + 14] = t2.Y;
        output[o + 15] = t2.Z;
        output[o + 16] = t2p.X;
        output[o + 17] = t2p.Y;

        // 3: a group-shared element (each thread uses its own).
        int g = GroupIds.X;
        Tile[g] = v + 11;
        Int3 n3 = -Tile[g];
        output[o + 18] = n3.X;
        Int3 t3 = ((Tile[g] + c) * 3) - Tile[g];
        output[o + 19] = t3.X;
        output[o + 20] = t3.Y;
        output[o + 21] = t3.Z;

        // 4: in a helper.
        Int3 w4 = new Int3(a + 12, b - 12, (a ^ b) + 12);
        Int3 n4 = Negate(w4);
        output[o + 22] = n4.X + n4.Z;
        Int3 t4 = ((w4 + c) * 3) - w4;
        output[o + 23] = t4.X;
        output[o + 24] = t4.Y;
        output[o + 25] = t4.Z;

        // 5: inside a chain.
        Int3 w5 = new Int3(a + 13, b - 13, (a ^ b) + 13);
        Int3 n5 = c + (-w5) + c;
        output[o + 26] = n5.Z;
        Int3 t5 = ((w5 + c) * 3) - w5;
        output[o + 27] = t5.X;
        output[o + 28] = t5.Y;
        output[o + 29] = t5.Z;

        // 6: *= -1 in place on a field of a struct local.
        NegationCell cell = cells[i];
        cell.V *= -1;
        output[o + 30] = cell.V.X;
        Int3 t6 = ((cells[i].V + c) * 9) - cells[i].V;
        output[o + 31] = t6.X;
        output[o + 32] = t6.Y;
        output[o + 33] = t6.Z;

        // 7: *= -1 in place on a buffer element, and on one whose index is a local.
        vectors[i] *= -1;
        output[o + 34] = vectors[i].Z;
        Int3 back = -vectors[i];
        Int3 t7 = ((back + c) * 11) - back;
        output[o + 35] = t7.X;
        output[o + 36] = t7.Y;
        output[o + 37] = t7.Z;
        int slot = ThreadIds.X + (a - a);
        vectors[slot] *= new Int3(1, -1, 1);
        output[o + 38] = vectors[slot].Y;
        output[o + 39] = vectors[slot].X + vectors[slot].Z;

        // 8: every lane read, and an unsigned vector stored in a struct.
        Int3 w8 = new Int3(a + 14, b - 14, (a ^ b) + 14);
        Int3 n8 = -w8;
        output[o + 40] = n8.X;
        output[o + 41] = n8.Y;
        output[o + 42] = n8.Z;
        UInt4 uq = new UInt4((uint)a, (uint)b, 5u, 7u);
        cells[i].U = new UInt4(0u) - uq;
        output[o + 43] = (int)cells[i].U.X;
        output[o + 44] = (int)cells[i].U.Y;
        output[o + 45] = (int)cells[i].U.Z;
        output[o + 46] = (int)cells[i].U.W;

        // 9: floats keep their operator.
        Float3 f = new Float3(a, b, a + b);
        Float3 nf = -f;
        output[o + 47] = (int)(nf.X + nf.Z);
        Float3 tf = ((f + new Float3(1f, 2f, 3f)) * 3f) - f;
        output[o + 48] = (int)tf.X;
        output[o + 49] = (int)tf.Y;
        output[o + 50] = (int)tf.Z;
    }
}
