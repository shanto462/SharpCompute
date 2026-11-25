using System;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpCompute;

namespace SharpCompute.Tests.Shaders;

// Shaders for the 8-, 16- and 64-bit integer tests (byte, sbyte, short, ushort, long, ulong). Each
// runs unchanged on the GPU (For) and on the CPU (ForCpu), so the tests compare the two element by
// element. Nothing here divides by zero or negates a MinValue by division: C# throws for those and
// the GPU does not (see ScalarTypeTests.DivisionByZero_DoesNotTrapOnTheGpu).

/// <summary>
/// Arithmetic on the small integers. C# promotes them to <c>int</c> first, so the outputs are ints;
/// the wraparound shows where a result is cast back to the small type, and in the compound
/// assignments and increments, which truncate on every step.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SmallIntegerOps(
    ReadOnlyBuffer<byte> bytes,
    ReadOnlyBuffer<sbyte> sbytes,
    ReadOnlyBuffer<short> shorts,
    ReadOnlyBuffer<ushort> ushorts,
    ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Stride = 56;

    private const byte LowNibble = 0x0F;

    private static readonly short Bias = -1234;

    public void Execute()
    {
        int i = ThreadIds.X;
        int j = ((i * 7) + 3) % bytes.Length;
        int o = i * Stride;
        byte b = bytes[i], b2 = bytes[j];
        sbyte sb = sbytes[i], sb2 = sbytes[j];
        short s = shorts[i], s2 = shorts[j];
        ushort us = ushorts[i], us2 = ushorts[j];
        int count = i % 40; // beyond the 32 bits of the promoted operand: C# masks the count with 31

        // Promotion: each of these is int arithmetic on the widened operands.
        output[o + 0] = b + b2;
        output[o + 1] = b - b2;
        output[o + 2] = b * b2;
        output[o + 3] = sb + sb2;
        output[o + 4] = sb * sb2;
        output[o + 5] = s * s2;
        output[o + 6] = us + us2;
        output[o + 7] = us - us2;
        output[o + 8] = b2 != 0 ? b / b2 : -1;
        output[o + 9] = b2 != 0 ? b % b2 : -1;
        output[o + 10] = sb2 != 0 ? sb / sb2 : -1;
        output[o + 11] = sb2 != 0 ? sb % sb2 : -1;
        output[o + 12] = s2 != 0 ? s / s2 : -1;
        output[o + 13] = us2 != 0 ? us % us2 : -1;
        output[o + 14] = -b;
        output[o + 15] = ~b;
        output[o + 16] = -sb;
        output[o + 17] = ~us;
        output[o + 18] = b << count;
        output[o + 19] = sb >> count;
        output[o + 20] = sb >>> count;
        output[o + 21] = us << count;
        output[o + 22] = s >> count;
        output[o + 23] = (b & b2) | (us ^ s);

        // Wraparound: the cast back truncates to the width of the small type.
        output[o + 24] = (byte)(b + b2);
        output[o + 25] = (sbyte)(sb + sb2);
        output[o + 26] = (short)(s + s2);
        output[o + 27] = (ushort)(us + us2);
        output[o + 28] = (byte)(b * b2);
        output[o + 29] = (short)(s * s2);
        output[o + 30] = (sbyte)b;
        output[o + 31] = (byte)sb;
        output[o + 32] = (ushort)s;
        output[o + 33] = (short)us;
        output[o + 34] = (byte)s;
        output[o + 35] = (sbyte)us;
        output[o + 36] = (int)((uint)us * us2);

        // Compound assignments convert back to the small type after every step.
        byte cb = b;
        cb += b2;
        cb <<= 1;
        cb ^= 0x5A;
        cb >>= 2;
        output[o + 37] = cb;

        sbyte csb = sb;
        csb -= sb2;
        csb *= 3;
        csb >>= 1;
        output[o + 38] = csb;

        short cs = s;
        cs += s2;
        cs *= s2;
        cs |= 0x1010;
        cs <<= 3;
        output[o + 39] = cs;

        ushort cus = us;
        cus -= us2;
        cus /= 3;
        cus %= 1000;
        cus &= 0x3FF;
        output[o + 40] = cus;

        // Increments wrap at the width of the type.
        byte ib = b;
        ib++;
        ++ib;
        sbyte isb = sb;
        isb--;
        short ks = s;
        ks++;
        ushort kus = us;
        --kus;
        output[o + 41] = ib;
        output[o + 42] = isb;
        output[o + 43] = ks;
        output[o + 44] = kus;

        output[o + 45] =
            (b < b2 ? 1 : 0) | (sb <= sb2 ? 2 : 0) | (s > s2 ? 4 : 0) | (us >= us2 ? 8 : 0) |
            (b == b2 ? 16 : 0) | (sb != sb2 ? 32 : 0) | (sb < b ? 64 : 0) | (s < us ? 128 : 0);

        // Typed literals, constants of the types themselves, and program-scope constants.
        byte picked = b > 100 ? b : (byte)200;
        output[o + 46] = picked;
        output[o + 47] = b & LowNibble;
        output[o + 48] = s + Bias;
        output[o + 49] = byte.MaxValue - b + sbyte.MinValue + short.MaxValue + ushort.MaxValue;
        output[o + 50] = Widen(sb, us);
        output[o + 51] = Halve(b);

        Split(s, out byte low, out sbyte high);
        output[o + 52] = low;
        output[o + 53] = high;

        // A switch over a small integer.
        int kind;

        switch ((byte)(b & 3))
        {
            case 0:
                kind = 10;
                break;
            case 1:
                kind = 20;
                break;
            default:
                kind = 30;
                break;
        }

        output[o + 54] = kind;
        output[o + 55] = default(short) + default(byte);
    }

    private static int Widen(sbyte x, ushort y) => (x * 3) + y;

    private static byte Halve(byte x) => (byte)(x >> 1);

    private static void Split(short value, out byte low, out sbyte high)
    {
        low = (byte)value;
        high = (sbyte)(value >> 8);
    }
}

/// <summary>
/// 64-bit arithmetic: wrapping add, subtract and multiply, division, shifts whose count runs below 0
/// and beyond 63 (C# masks it with 63), comparisons, the int and uint operands that promote to long,
/// and conversions to and from every other integer type. Unsigned overflow wraps by definition on
/// both sides; the signed sums and products here are only stored, where the GPU's undefined signed
/// overflow still comes out wrapped (nothing in the kernel lets the compiler reason about it).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WideIntegerOps(ReadOnlyBuffer<long> longs, ReadOnlyBuffer<ulong> ulongs, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Stride = 64;

    private const long Multiplier = 0x62A9D9ED799705F5L;

    private const ulong Mixer = 0xBF58476D1CE4E5B9UL;

    private static readonly long Lowest = long.MinValue;

    private static readonly ulong HighBit = 1UL << 63;

    public void Execute()
    {
        int i = ThreadIds.X;
        int j = ((i * 5) + 1) % longs.Length;
        int o = i * Stride;
        long a = longs[i], b = longs[j];
        ulong u = ulongs[i], v = ulongs[j];
        int count = (i % 140) - 5;

        output[o + 0] = a + b;
        output[o + 1] = a - b;
        output[o + 2] = a * b;
        output[o + 3] = b != 0 && !(a == long.MinValue && b == -1) ? a / b : 0;
        output[o + 4] = b != 0 && !(a == long.MinValue && b == -1) ? a % b : 0;
        output[o + 5] = (long)(u + v);
        output[o + 6] = (long)(u - v);
        output[o + 7] = (long)(u * v);
        output[o + 8] = v != 0 ? (long)(u / v) : 0;
        output[o + 9] = v != 0 ? (long)(u % v) : 0;

        output[o + 10] = a << count;
        output[o + 11] = a >> count;
        output[o + 12] = a >>> count;
        output[o + 13] = (long)(u << count);
        output[o + 14] = (long)(u >> count);

        output[o + 15] = a & b;
        output[o + 16] = a | b;
        output[o + 17] = a ^ b;
        output[o + 18] = ~a;
        output[o + 19] = -a;
        output[o + 20] = (long)((u & v) | (~u ^ v));

        output[o + 21] =
            (a < b ? 1 : 0) | (a <= b ? 2 : 0) | (a > b ? 4 : 0) | (a >= b ? 8 : 0) | (a == b ? 16 : 0) | (a != b ? 32 : 0) |
            (u < v ? 64 : 0) | (u >= v ? 128 : 0) | (u == v ? 256 : 0);

        // int and uint operands: C# promotes them to long where the two meet, and for -uint.
        int n = (int)a;
        uint m = (uint)u;
        output[o + 22] = n + a;
        output[o + 23] = m - n;
        output[o + 24] = -m;
        output[o + 25] = (long)n * n;
        output[o + 26] = (long)((ulong)m * m);
        output[o + 27] = n < m ? 1 : 0;

        // Conversions: truncation, sign extension and zero extension.
        output[o + 28] = (int)a;
        output[o + 29] = (uint)a;
        output[o + 30] = (short)a;
        output[o + 31] = (ushort)a;
        output[o + 32] = (byte)a;
        output[o + 33] = (sbyte)a;
        output[o + 34] = (long)u;
        output[o + 35] = (long)(int)u;
        output[o + 36] = (long)(uint)a;
        output[o + 37] = (long)(ulong)(sbyte)a;
        output[o + 38] = (long)(ulong)(short)a;

        long ca = a;
        ca += b;
        ca ^= ca >> 33;
        ca *= Multiplier;
        ca -= n;
        ca <<= 3;
        ca >>= 1;
        ca |= 1;
        ca &= ~0x10L;
        ca++;
        output[o + 39] = ca;

        ulong cu = u;
        cu += v;
        cu ^= cu >> 29;
        cu *= Mixer;
        cu >>= 2;
        cu <<= 1;
        cu %= 1000003UL;
        cu--;
        output[o + 40] = (long)cu;

        long picked = a > b ? a : Lowest;
        output[o + 41] = picked;
        output[o + 42] = (long)(ulong.MaxValue - u);
        output[o + 43] = long.MaxValue - (a & long.MaxValue);
        output[o + 44] = (long)(u | HighBit);
        output[o + 45] = Fold(a, u);

        Swap(ref a, ref b);
        output[o + 46] = a - 1;
        output[o + 47] = b + 1;

        long bucket;

        switch (u & 3UL)
        {
            case 0UL:
                bucket = 1L << 40;
                break;
            case 1UL:
                bucket = -1L;
                break;
            default:
                bucket = 0x7FFFFFFFFFFFFFFFL;
                break;
        }

        output[o + 48] = bucket;
        output[o + 49] = default(long) + (long)default(ulong);

        // An int product that overflows, widened afterwards. Signed overflow is undefined on the GPU
        // (the compiler widens first and keeps the exact product), so C#'s wrapped result is spelled
        // through uint, where the multiplication is exact modulo 2^32 on both sides. The signed
        // sums and products above are only stored, which is where they still come out wrapped.
        output[o + 50] = (long)(int)((uint)n * (uint)n);
        output[o + 51] = (long)((uint)n * m);
    }

    /// <summary>The finalizer of SplitMix64 on the sum of the two values, signed and unsigned mixed.</summary>
    private static long Fold(long x, ulong y)
    {
        ulong z = (ulong)x + y;
        z = (z ^ (z >> 30)) * Mixer;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return (long)(z ^ (z >> 31));
    }

    private static void Swap(ref long x, ref long y)
    {
        long t = x;
        x = y;
        y = t;
    }
}

/// <summary>
/// Conversions between floating point and every integer type, in both directions. In .NET a float
/// converts to an integer by truncating, NaN gives 0, and a value beyond the range of the target
/// saturates to its bounds; the GPU does the same.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct FloatIntegerConversions(
    ReadOnlyBuffer<float> floats,
    ReadOnlyBuffer<long> longs,
    ReadWriteBuffer<long> integers,
    ReadWriteBuffer<float> reals) : IComputeShader
{
    public const int IntegerStride = 16;

    public const int RealStride = 8;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * IntegerStride;
        float f = floats[i];
        Half h = (Half)f;

        integers[o + 0] = (long)f;
        integers[o + 1] = (long)(ulong)f;
        integers[o + 2] = (byte)f;
        integers[o + 3] = (sbyte)f;
        integers[o + 4] = (short)f;
        integers[o + 5] = (ushort)f;
        integers[o + 6] = (int)f;
        integers[o + 7] = (uint)f;
        integers[o + 8] = (long)h;
        integers[o + 9] = (long)(ulong)h;
        integers[o + 10] = (byte)h;
        integers[o + 11] = (sbyte)h;
        integers[o + 12] = (short)h;
        integers[o + 13] = (ushort)h;
        integers[o + 14] = (long)(f * 0.5f);
        integers[o + 15] = (long)MathF.Floor(f);

        int r = i * RealStride;
        long l = longs[i];

        reals[r + 0] = l;
        reals[r + 1] = (ulong)l;
        reals[r + 2] = (byte)l;
        reals[r + 3] = (sbyte)l;
        reals[r + 4] = (short)l;
        reals[r + 5] = (ushort)l;
        reals[r + 6] = (float)(Half)(sbyte)l;
        reals[r + 7] = l * 0.5f;
    }
}

/// <summary>
/// Every integer intrinsic for the six types: abs, min, max, clamp, the bit counts, the bit reversal
/// and the rotates (also for int and uint), plus the 64-bit members of <c>BitOperations</c> and
/// <c>Math</c>. The result of each has the type of its argument, so the counts are those of that width.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct IntegerIntrinsics(ReadOnlyBuffer<long> values, ReadOnlyBuffer<int> counts, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Stride = 88;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;
        long x = values[i];
        long y = values[((i * 3) + 1) % values.Length];
        long z = values[((i * 11) + 5) % values.Length];
        int n = counts[i];

        byte b = (byte)x, b2 = (byte)y, b3 = (byte)z;
        output[o + 0] = Gpu.Abs(b);
        output[o + 1] = Gpu.Min(b, b2);
        output[o + 2] = Gpu.Max(b, b2);
        output[o + 3] = Gpu.Clamp(b, Gpu.Min(b2, b3), Gpu.Max(b2, b3));
        output[o + 4] = Gpu.CountBits(b);
        output[o + 5] = Gpu.LeadingZeroCount(b);
        output[o + 6] = Gpu.TrailingZeroCount(b);
        output[o + 7] = Gpu.ReverseBits(b);
        output[o + 8] = Gpu.RotateLeft(b, n);
        output[o + 9] = Gpu.RotateRight(b, n);

        sbyte sb = (sbyte)x, sb2 = (sbyte)y, sb3 = (sbyte)z;
        output[o + 10] = Gpu.Abs(sb);
        output[o + 11] = Gpu.Min(sb, sb2);
        output[o + 12] = Gpu.Max(sb, sb2);
        output[o + 13] = Gpu.Clamp(sb, Gpu.Min(sb2, sb3), Gpu.Max(sb2, sb3));
        output[o + 14] = Gpu.CountBits(sb);
        output[o + 15] = Gpu.LeadingZeroCount(sb);
        output[o + 16] = Gpu.TrailingZeroCount(sb);
        output[o + 17] = Gpu.ReverseBits(sb);
        output[o + 18] = Gpu.RotateLeft(sb, n);
        output[o + 19] = Gpu.RotateRight(sb, n);

        short s = (short)x, s2 = (short)y, s3 = (short)z;
        output[o + 20] = Gpu.Abs(s);
        output[o + 21] = Gpu.Min(s, s2);
        output[o + 22] = Gpu.Max(s, s2);
        output[o + 23] = Gpu.Clamp(s, Gpu.Min(s2, s3), Gpu.Max(s2, s3));
        output[o + 24] = Gpu.CountBits(s);
        output[o + 25] = Gpu.LeadingZeroCount(s);
        output[o + 26] = Gpu.TrailingZeroCount(s);
        output[o + 27] = Gpu.ReverseBits(s);
        output[o + 28] = Gpu.RotateLeft(s, n);
        output[o + 29] = Gpu.RotateRight(s, n);

        ushort us = (ushort)x, us2 = (ushort)y, us3 = (ushort)z;
        output[o + 30] = Gpu.Abs(us);
        output[o + 31] = Gpu.Min(us, us2);
        output[o + 32] = Gpu.Max(us, us2);
        output[o + 33] = Gpu.Clamp(us, Gpu.Min(us2, us3), Gpu.Max(us2, us3));
        output[o + 34] = Gpu.CountBits(us);
        output[o + 35] = Gpu.LeadingZeroCount(us);
        output[o + 36] = Gpu.TrailingZeroCount(us);
        output[o + 37] = Gpu.ReverseBits(us);
        output[o + 38] = Gpu.RotateLeft(us, n);
        output[o + 39] = Gpu.RotateRight(us, n);

        output[o + 40] = Gpu.Abs(x);
        output[o + 41] = Gpu.Min(x, y);
        output[o + 42] = Gpu.Max(x, y);
        output[o + 43] = Gpu.Clamp(x, Gpu.Min(y, z), Gpu.Max(y, z));
        output[o + 44] = Gpu.CountBits(x);
        output[o + 45] = Gpu.LeadingZeroCount(x);
        output[o + 46] = Gpu.TrailingZeroCount(x);
        output[o + 47] = Gpu.ReverseBits(x);
        output[o + 48] = Gpu.RotateLeft(x, n);
        output[o + 49] = Gpu.RotateRight(x, n);

        ulong u = (ulong)x, u2 = (ulong)y, u3 = (ulong)z;
        output[o + 50] = (long)Gpu.Abs(u);
        output[o + 51] = (long)Gpu.Min(u, u2);
        output[o + 52] = (long)Gpu.Max(u, u2);
        output[o + 53] = (long)Gpu.Clamp(u, Gpu.Min(u2, u3), Gpu.Max(u2, u3));
        output[o + 54] = (long)Gpu.CountBits(u);
        output[o + 55] = (long)Gpu.LeadingZeroCount(u);
        output[o + 56] = (long)Gpu.TrailingZeroCount(u);
        output[o + 57] = (long)Gpu.ReverseBits(u);
        output[o + 58] = (long)Gpu.RotateLeft(u, n);
        output[o + 59] = (long)Gpu.RotateRight(u, n);

        // The rotates of int and uint, new alongside the others.
        output[o + 60] = Gpu.RotateLeft((int)x, n);
        output[o + 61] = Gpu.RotateRight((int)x, n);
        output[o + 62] = Gpu.RotateLeft((uint)x, n);
        output[o + 63] = Gpu.RotateRight((uint)x, n);

        // System.Numerics.BitOperations and System.Math on 64-bit values.
        output[o + 64] = BitOperations.PopCount(u);
        output[o + 65] = BitOperations.LeadingZeroCount(u);
        output[o + 66] = BitOperations.TrailingZeroCount(u);
        output[o + 67] = BitOperations.TrailingZeroCount(x);
        output[o + 68] = (long)BitOperations.RotateLeft(u, n);
        output[o + 69] = (long)BitOperations.RotateRight(u, n);
        output[o + 70] = Math.Min(x, y);
        output[o + 71] = Math.Max(x, y);
        output[o + 72] = (long)Math.Min(u, u2);
        output[o + 73] = (long)Math.Max(u, u2);
        output[o + 74] = Math.Clamp(x, Math.Min(y, z), Math.Max(y, z));
        output[o + 75] = Math.Min(b, b2);
        output[o + 76] = Math.Max(s, s2);
        output[o + 77] = Math.Min(sb, sb2);
        output[o + 78] = Math.Max(us, us2);

        // Typed literals pick the overload of the small type, not the int one.
        output[o + 79] = Gpu.Min(b, (byte)100);
        output[o + 80] = Gpu.Max(s, (short)-5);
        output[o + 81] = Gpu.Clamp(x, -1000L, 1000L);
        output[o + 82] = (long)Gpu.Min(u, 1UL << 40);
        output[o + 83] = Gpu.LeadingZeroCount((byte)(b | 1));
        output[o + 84] = Gpu.CountBits((ushort)(us ^ us2));
        output[o + 85] = Gpu.Abs((sbyte)-128);
        output[o + 86] = Gpu.Abs(long.MinValue);
        output[o + 87] = Gpu.ReverseBits((short)1);
    }
}

/// <summary>
/// A user struct with every new type, ordered so that the layout needs padding: 40 bytes, with the
/// 64-bit fields aligned to 8 on both sides.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MixedRecord
{
    public byte Tag;
    public long Key;
    public short Delta;
    public ulong Hash;
    public sbyte Sign;
    public ushort Count;
}

/// <summary>
/// Constants of every new type, ordered so that their natural alignments leave holes (a byte before a
/// long, a short before a ulong), next to an int and a float: the layout of the constants struct on
/// both sides. Each constant is written out, and a few are combined.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MixedConstants(
    byte tag, long key, short delta, ulong hash, sbyte sign, ushort count, int offset, long last, float scale, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Stride = 12;

    public void Execute()
    {
        int o = ThreadIds.X * Stride;

        output[o + 0] = tag;
        output[o + 1] = key;
        output[o + 2] = delta;
        output[o + 3] = (long)hash;
        output[o + 4] = sign;
        output[o + 5] = count;
        output[o + 6] = offset;
        output[o + 7] = last;
        output[o + 8] = (long)scale;
        output[o + 9] = key + (tag * delta) - sign + ThreadIds.X;
        output[o + 10] = (long)(hash ^ (ulong)last) >> (count & 63);
        output[o + 11] = output.Length;
    }
}

/// <summary>Small constants only: the constants struct keeps its sequential 4-byte packing.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SmallConstants(byte a, int b, sbyte c, short d, byte e, ushort f, float g, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Stride = 8;

    public void Execute()
    {
        int o = ThreadIds.X * Stride;

        output[o + 0] = a;
        output[o + 1] = b;
        output[o + 2] = c;
        output[o + 3] = d;
        output[o + 4] = e;
        output[o + 5] = f;
        output[o + 6] = (int)g;
        output[o + 7] = a + c + d + e + f;
    }
}

/// <summary>Reads and updates every field of a <see cref="MixedRecord"/> in a buffer, with another record as a constant.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct RecordUpdate(ReadWriteBuffer<MixedRecord> records, MixedRecord bias) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        MixedRecord record = records[i];

        record.Tag += bias.Tag;
        record.Key = (record.Key * 31) + bias.Key;
        record.Delta -= bias.Delta;
        record.Hash ^= bias.Hash >> (i & 63);
        record.Sign = (sbyte)(record.Sign + bias.Sign);
        record.Count++;

        records[i] = record;
        records[i].Key += record.Tag;
        records[i].Count += (ushort)i;
    }
}

/// <summary>
/// Threadgroup memory of every new type: each thread stores a value in its own element, and after a
/// barrier reads the element of the thread opposite it in the group.
/// </summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SharedSmallAndWide(ReadOnlyBuffer<long> values, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int GroupSize = 64;

    public const int Stride = 6;

    [GroupShared]
    private static readonly byte[] bytes = new byte[GroupSize];

    [GroupShared]
    private static readonly sbyte[] sbytes = new sbyte[GroupSize];

    [GroupShared]
    private static readonly short[] shorts = new short[GroupSize];

    [GroupShared]
    private static readonly ushort[] ushorts = new ushort[GroupSize];

    [GroupShared]
    private static readonly long[] longs = new long[GroupSize];

    [GroupShared]
    private static readonly ulong[] ulongs = new ulong[GroupSize];

    public void Execute()
    {
        int t = GroupIds.Index;
        long value = values[ThreadIds.X];

        bytes[t] = (byte)value;
        sbytes[t] = (sbyte)(value >> 8);
        shorts[t] = (short)(value >> 16);
        ushorts[t] = (ushort)(value >> 32);
        longs[t] = value;
        ulongs[t] = (ulong)~value;

        Gpu.GroupMemoryBarrierWithGroupSync();

        int other = GroupSize - 1 - t;
        int o = ThreadIds.X * Stride;

        output[o + 0] = bytes[other];
        output[o + 1] = sbytes[other];
        output[o + 2] = shorts[other];
        output[o + 3] = ushorts[other];
        output[o + 4] = longs[other];
        output[o + 5] = (long)ulongs[other];
    }
}

/// <summary>
/// The two 64-bit atomics there are: every thread offers its key to one minimum and one maximum.
/// <c>extremes[0]</c> must start at <see cref="ulong.MaxValue"/> and <c>extremes[1]</c> at 0.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AtomicExtremes(ReadOnlyBuffer<ulong> keys, ReadWriteBuffer<ulong> extremes) : IComputeShader
{
    public void Execute()
    {
        ulong key = keys[ThreadIds.X];

        Gpu.AtomicMin(ref extremes[0], key);
        Gpu.AtomicMax(ref extremes[1], key);
    }
}

/// <summary>Writes one value of every new type to a buffer of that type.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct StoreEveryType(
    ReadOnlyBuffer<long> values,
    ReadWriteBuffer<byte> bytes,
    ReadWriteBuffer<sbyte> sbytes,
    ReadWriteBuffer<short> shorts,
    ReadWriteBuffer<ushort> ushorts,
    ReadWriteBuffer<long> longs,
    ReadWriteBuffer<ulong> ulongs) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        long value = values[i];

        bytes[i] = (byte)value;
        sbytes[i] = (sbyte)(value >> 8);
        shorts[i] = (short)(value >> 16);
        ushorts[i] = (ushort)(value >> 32);
        longs[i] = value * 3;
        ulongs[i] = (ulong)value << 1;
    }
}

/// <summary>An 8-bit image processed in place: <c>image[i] = 255 - image[i]</c>, then a gain with saturation.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct InvertBytes(ReadWriteBuffer<byte> image, byte gain) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        byte inverted = (byte)(255 - image[i]);

        image[i] = (byte)Gpu.Min(inverted * gain / 16, 255);
    }
}

/// <summary>16-bit samples scaled in place, clamped to the range of a short.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ScaleSamples(ReadWriteBuffer<short> samples, short gain) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        samples[i] = (short)Gpu.Clamp(samples[i] * gain / 256, short.MinValue, short.MaxValue);
    }
}

/// <summary>Three 64-bit running totals, in an <c>[InlineArray]</c> a struct can hold.</summary>
[System.Runtime.CompilerServices.InlineArray(3)]
public struct Totals
{
    private long _element;
}

/// <summary>
/// Local arrays of the new types: a <c>stackalloc</c> window of bytes, an array of 64-bit sums, constant
/// tables of ushorts and ulongs, and an inline array of longs. Each thread folds eight bytes of the
/// input into three weighted sums and one hash.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct LocalArraysOfIntegers(ReadOnlyBuffer<byte> bytes, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Window = 8;

    public const int Stride = 5;

    public void Execute()
    {
        int i = ThreadIds.X;
        Span<byte> window = stackalloc byte[Window];
        long[] sums = new long[3];
        ReadOnlySpan<ushort> weights = [1, 4, 6, 4, 1, 60000, 257, 9];
        ReadOnlySpan<ulong> primes = [0x100000001B3, 0xD6E8FEB86659FD93];
        sbyte[] signs = [-1, 1];
        Totals totals = default;
        ulong hash = 0xCBF29CE484222325;

        for (int k = 0; k < window.Length; k++)
        {
            window[k] = bytes[i + k];
        }

        for (int k = 0; k < window.Length; k++)
        {
            sums[k % 3] += window[k] * weights[k] * signs[k & 1];
            totals[k % 3] += (long)window[k] << (k * 7);
            hash = (hash ^ window[k]) * primes[0];
        }

        int o = i * Stride;

        output[o + 0] = sums[0];
        output[o + 1] = sums[1] - sums[2];
        output[o + 2] = totals[0] ^ totals[1];
        output[o + 3] = totals[2];
        output[o + 4] = (long)((hash ^ (hash >> 32)) * primes[1]);
    }
}

/// <summary>Helpers of a static class that is not a shader: they take and return the new types, and two are generic.</summary>
public static class IntegerHelpers
{
    public const ulong Prime = 0x100000001B3;

    /// <summary>One FNV-1a step: xor a byte in, multiply by the prime.</summary>
    public static ulong Feed(ulong hash, byte value) => (hash ^ value) * Prime;

    public static byte High(ushort value) => (byte)(value >> 8);

    public static long Widen(sbyte small, short medium) => (long)small * medium;

    public static T Pick<T>(bool condition, T a, T b) => condition ? a : b;

    public static void Swap<T>(ref T a, ref T b)
    {
        T held = a;
        a = b;
        b = held;
    }

    public static T Load<T>(ReadOnlyBuffer<T> data, int index)
        where T : unmanaged => data[index];
}

/// <summary>A user struct with fields of the new types and members that work on them.</summary>
public struct Tally
{
    public ulong Total;
    public short Delta;
    public byte Count;

    public Tally(ulong total, short delta)
    {
        Total = total;
        Delta = delta;
        Count = 1;
    }

    public readonly long Signed => (long)Total + Delta;

    public readonly ushort Scaled(byte factor) => (ushort)(Count * factor);

    public void Add(byte value)
    {
        Total += value;
        Count++;
    }

    public static Tally operator +(Tally a, Tally b) => new Tally(a.Total + b.Total, (short)(a.Delta + b.Delta));

    public static explicit operator long(Tally tally) => tally.Signed;
}

/// <summary>
/// The new types through shared code: static helpers of another type, generic helpers specialized for
/// byte, short, long and ulong, and the constructor, methods, property, operator and conversion of a
/// struct whose fields have these types (on a local and on a buffer element).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SharedIntegerCode(
    ReadOnlyBuffer<long> values, ReadOnlyBuffer<byte> bytes, ReadWriteBuffer<Tally> tallies, ReadWriteBuffer<long> output, byte step) : IComputeShader
{
    public const int Stride = 12;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;
        long a = IntegerHelpers.Load(values, i), a2 = values[(i + 1) % values.Length];
        byte b = IntegerHelpers.Load(bytes, i), b2 = bytes[(i + 1) % bytes.Length];
        ushort word = (ushort)a;
        ulong hash = IntegerHelpers.Feed(IntegerHelpers.Feed((ulong)a, b), IntegerHelpers.High(word));

        output[o + 0] = (long)hash;
        output[o + 1] = IntegerHelpers.Widen((sbyte)b, (short)word);

        IntegerHelpers.Swap(ref a, ref a2);
        IntegerHelpers.Swap(ref b, ref b2);
        output[o + 2] = a - 1;
        output[o + 3] = b;
        output[o + 4] = IntegerHelpers.Pick(a > a2, a, a2);
        output[o + 5] = IntegerHelpers.Pick(b > b2, b, b2);
        output[o + 6] = IntegerHelpers.Pick(a > 0, (short)word, (short)-1);
        output[o + 7] = (long)IntegerHelpers.Pick(b > 100, hash, 1UL << 40);

        Tally tally = new Tally((ulong)a, -3);
        tally.Add(step);
        tally.Add(b);
        Tally sum = tally + tallies[i];
        tallies[i].Add(step);

        output[o + 8] = sum.Signed;
        output[o + 9] = sum.Scaled(step);
        output[o + 10] = (long)tallies[i];
        output[o + 11] = tally.Count + sum.Count;
    }
}

/// <summary>
/// Divides by a divisor that is zero for some threads, in every width. C# throws there, so this only
/// runs on the GPU: the test checks that the dispatch completes and that the other threads are right.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DivideByMaybeZero(ReadOnlyBuffer<long> divisors, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Stride = 6;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;
        long d = divisors[i];
        long x = 1000003L * (i + 1);

        output[o + 0] = x / d;
        output[o + 1] = x % d;
        output[o + 2] = (long)((ulong)x / (ulong)d);
        output[o + 3] = (byte)x / (byte)d;
        output[o + 4] = (short)x % (short)d;
        output[o + 5] = (ushort)x / (ushort)d;
    }
}
