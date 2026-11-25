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
