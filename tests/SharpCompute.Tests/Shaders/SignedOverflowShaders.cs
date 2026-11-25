using SharpCompute;

namespace SharpCompute.Tests.Shaders;

// Shaders for the signed overflow tests (SignedOverflowTests). Each thread takes one pair (a, b) of
// edge values and writes a fixed number of slots; every slot is an expression whose result depends
// on C#'s wrap-around, so the GPU, ForCpu and plain C# must agree bit for bit. Slots 1, 2, 3 and 5 of IntOverflow are the idioms that the
// Metal compiler optimized on the assumption that signed overflow does not happen.

/// <summary>Signed <c>int</c> arithmetic that overflows for some of the pairs.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct IntOverflow(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 20;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;

        output[o] = (long)(a * b);
        output[o + 1] = b > 1 ? (a * b) / b : 0;
        output[o + 2] = a + 1 > a ? 1 : 0;
        output[o + 3] = b > 0 && (a + b) < a ? 1 : 0;
        output[o + 4] = a - 1 < a ? 1 : 0;
        output[o + 5] = a * 2 / 2;
        output[o + 6] = a + b;
        output[o + 7] = a - b;
        output[o + 8] = -a;
        output[o + 9] = (long)(a + b) + (long)(a - b);
        output[o + 10] = a * -3;
        output[o + 11] = (a * b * a) - b;
        output[o + 12] = (a * 1103515245) + 12345;

        int x = a;
        x += b;
        x *= b;
        x -= a;
        output[o + 13] = x;

        int y = a;
        y++;
        output[o + 14] = y;
        y = a;
        --y;
        output[o + 15] = y;

        // A postfix whose old value is used, and a prefix used as a value.
        int z = a;
        int old = z++;
        output[o + 16] = ((long)old << 32) | (uint)z;
        output[o + 17] = (++z) + b;
        output[o + 18] = -(a * b);
        output[o + 19] = b > 0 && a > int.MaxValue - b ? 1 : 0; // the overflow check that does not overflow
    }
}

/// <summary>Signed <c>long</c> arithmetic that overflows for some of the pairs.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct LongOverflow(ReadOnlyBuffer<long> pairs, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 16;

    public void Execute()
    {
        int i = ThreadIds.X;
        long a = pairs[2 * i];
        long b = pairs[(2 * i) + 1];
        int o = i * Slots;

        output[o] = a * b;
        output[o + 1] = b > 1 ? (a * b) / b : 0;
        output[o + 2] = a + 1 > a ? 1 : 0;
        output[o + 3] = b > 0 && (a + b) < a ? 1 : 0;
        output[o + 4] = a - 1 < a ? 1 : 0;
        output[o + 5] = a * 2 / 2;
        output[o + 6] = a + b;
        output[o + 7] = a - b;
        output[o + 8] = -a;
        output[o + 9] = (a * 6364136223846793005L) + 1442695040888963407L;
        output[o + 10] = (int)a * (long)(int)b;

        long x = a;
        x += b;
        x *= b;
        x -= a;
        output[o + 11] = x;

        long y = a;
        y++;
        output[o + 12] = y;
        y = a;
        y--;
        output[o + 13] = y;

        long z = a;
        long before = z--;
        output[o + 14] = before + (z * 3);
        output[o + 15] = (a + b) * (a - b);
    }
}

/// <summary>
/// <c>short</c> arithmetic: C# computes it in <c>int</c>, so it overflows only where an <c>int</c>
/// does (<c>a * a * a * a</c>), and wraps at 16 bits where a result is cast back.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ShortOverflow(ReadOnlyBuffer<short> pairs, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 12;

    public void Execute()
    {
        int i = ThreadIds.X;
        short a = pairs[2 * i];
        short b = pairs[(2 * i) + 1];
        int o = i * Slots;

        output[o] = (short)(a * b);
        output[o + 1] = b > 1 ? (short)(a * b) / b : 0;
        output[o + 2] = (short)(a + 1) > a ? 1 : 0;
        output[o + 3] = b > 0 && (short)(a + b) < a ? 1 : 0;
        output[o + 4] = (short)(a * 2) / 2;
        output[o + 5] = a * a * a * a;
        output[o + 6] = -a;
        output[o + 7] = a * 65537 * b;

        short x = a;
        x += b;
        x *= b;
        x -= a;
        output[o + 8] = x;

        short y = a;
        y++;
        output[o + 9] = y;
        y = a;
        y--;
        output[o + 10] = y;
        y = a;
        y += 32767;
        output[o + 11] = y;
    }
}

/// <summary><c>sbyte</c> arithmetic, as for <see cref="ShortOverflow"/>, wrapping at 8 bits.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SbyteOverflow(ReadOnlyBuffer<sbyte> pairs, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 12;

    public void Execute()
    {
        int i = ThreadIds.X;
        sbyte a = pairs[2 * i];
        sbyte b = pairs[(2 * i) + 1];
        int o = i * Slots;

        output[o] = (sbyte)(a * b);
        output[o + 1] = b > 1 ? (sbyte)(a * b) / b : 0;
        output[o + 2] = (sbyte)(a + 1) > a ? 1 : 0;
        output[o + 3] = b > 0 && (sbyte)(a + b) < a ? 1 : 0;
        output[o + 4] = (sbyte)(a * 2) / 2;
        output[o + 5] = a * a * a * a * a;
        output[o + 6] = -a;
        output[o + 7] = a * 16843009 * b;

        sbyte x = a;
        x += b;
        x *= b;
        x -= a;
        output[o + 8] = x;

        sbyte y = a;
        y++;
        output[o + 9] = y;
        y = a;
        y--;
        output[o + 10] = y;
        y = a;
        y *= -127;
        output[o + 11] = y;
    }
}

/// <summary>
/// Component-wise <c>Int2</c> arithmetic. Each component of <c>a</c> and <c>b</c> is a pair of the
/// edge values; <c>d</c> holds the divisors (each <c>b</c> component, or 2 where that is not above 1).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Int2Overflow(ReadOnlyBuffer<Int2> a, ReadOnlyBuffer<Int2> b, ReadOnlyBuffer<Int2> d, ReadWriteBuffer<Int2> output) : IComputeShader
{
    public const int Slots = 10;

    public void Execute()
    {
        int i = ThreadIds.X;
        Int2 x = a[i], y = b[i], q = d[i];
        int o = i * Slots;

        output[o] = x * y;
        output[o + 1] = (x * q) / q;
        output[o + 2] = x * 2 / 2;
        output[o + 3] = x + y;
        output[o + 4] = x - y;
        output[o + 5] = -x;
        output[o + 6] = (x * 1103515245) + 12345;

        Int2 c = x;
        c += y;
        c *= y;
        c -= x;
        output[o + 7] = c;
        output[o + 8] = 3 - (x * y);
        output[o + 9] = -(x + new Int2(1));
    }
}

/// <summary>Component-wise <c>Int3</c> arithmetic, as for <see cref="Int2Overflow"/>; the buffers hold packed vectors.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Int3Overflow(ReadOnlyBuffer<Int3> a, ReadOnlyBuffer<Int3> b, ReadOnlyBuffer<Int3> d, ReadWriteBuffer<Int3> output) : IComputeShader
{
    public const int Slots = 10;

    public void Execute()
    {
        int i = ThreadIds.X;
        Int3 x = a[i], y = b[i], q = d[i];
        int o = i * Slots;

        output[o] = x * y;
        output[o + 1] = (x * q) / q;
        output[o + 2] = x * 2 / 2;
        output[o + 3] = x + y;
        output[o + 4] = x - y;
        output[o + 5] = -x;
        output[o + 6] = (x * 1103515245) + 12345;

        // In place, on the buffer element: a packed vector.
        output[o + 7] = x;
        output[o + 7] += y;
        output[o + 7] *= y;
        output[o + 7] -= x;
        output[o + 8] = 3 - (x * y);
        output[o + 9] = -(x + new Int3(1));
    }
}

/// <summary>Component-wise <c>Int4</c> arithmetic, as for <see cref="Int2Overflow"/>.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Int4Overflow(ReadOnlyBuffer<Int4> a, ReadOnlyBuffer<Int4> b, ReadOnlyBuffer<Int4> d, ReadWriteBuffer<Int4> output) : IComputeShader
{
    public const int Slots = 10;

    public void Execute()
    {
        int i = ThreadIds.X;
        Int4 x = a[i], y = b[i], q = d[i];
        int o = i * Slots;

        output[o] = x * y;
        output[o + 1] = (x * q) / q;
        output[o + 2] = x * 2 / 2;
        output[o + 3] = x + y;
        output[o + 4] = x - y;
        output[o + 5] = -x;
        output[o + 6] = (x * 1103515245) + 12345;

        Int4 c = x;
        c += y;
        c *= y;
        c -= x;
        output[o + 7] = c;
        output[o + 8] = 3 - (x * y);
        output[o + 9] = -(x + new Int4(1));
    }
}

/// <summary>
/// Compound assignments and increments whose target is evaluated once: an element whose index is a
/// helper call (device memory), a group-shared element (threadgroup memory), a <c>ref</c> parameter
/// (thread memory), a packed <c>Int3</c> element and components of one. The generator keeps the
/// native operators there, on the target viewed as unsigned.
/// </summary>
[GeneratedComputeShaderDescriptor]
[ThreadGroupSize(64, 1, 1)]
public readonly partial struct OverflowInPlace(ReadOnlyBuffer<int> values, ReadWriteBuffer<int> ints, ReadWriteBuffer<long> longs, ReadWriteBuffer<Int3> vectors) : IComputeShader
{
    public const int Slots = 8;

    [GroupShared]
    private static readonly int[] Tile = new int[64];

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = values[2 * i];
        int b = values[(2 * i) + 1];

        ints[Slot(i, 0)] = a;
        ints[Slot(i, 0)] += b;
        ints[Slot(i, 1)] = a;
        ints[Slot(i, 1)] *= b;
        ints[Slot(i, 2)] = a;
        ints[Slot(i, 2)] -= b;
        ints[Slot(i, 3)] = a;
        ints[Slot(i, 3)]++;
        ints[Slot(i, 4)] = a;
        --ints[Slot(i, 4)];
        ints[Slot(i, 5)] = a;
        ints[Slot(i, 5)] += Twice(b);

        int t = GroupIds.X;
        Tile[t] = a;
        Tile[Next(t)] += b;
        Tile[Next(t)] *= 3;
        ints[Slot(i, 6)] = Tile[t];

        int local = a;
        Bump(ref local, b);
        ints[Slot(i, 7)] = local;

        longs[Slot(i, 0)] = (long)a << 32;
        longs[Slot(i, 0)] += long.MaxValue;
        longs[Slot(i, 1)] = a;
        longs[Slot(i, 1)] *= long.MinValue + b;
        long counter = (long)b << 33;
        long previous = counter++;
        longs[Slot(i, 2)] = previous + counter;

        vectors[Slot(i, 0)] = new Int3(a, b, a);
        vectors[Slot(i, 0)] += new Int3(b);
        vectors[Slot(i, 0)] *= 3;
        vectors[Slot(i, 1)] = new Int3(a);
        vectors[Slot(i, 1)].Y += b;
        vectors[Slot(i, 1)].XZ *= b;
    }

    private static int Slot(int thread, int slot) => (thread * Slots) + slot;

    private static int Next(int t) => t;

    private static int Twice(int value) => value * 2;

    private static void Bump(ref int value, int by)
    {
        value += by;
        value *= by;
        value++;
    }
}

/// <summary>Program-scope constants whose initializers overflow: emitted as MSL, and folded where they call an intrinsic.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct OverflowingConstants(ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 12;

    private static readonly int Big = int.MaxValue;

    private static readonly int Wrapped = Big + 1;

    private static readonly int Product = Big * 3;

    private static readonly int Negated = -Wrapped;

    private static readonly long LongWrapped = long.MaxValue + (long)Big;

    private static readonly long LongProduct = (long)Big * Big * 5;

    private static readonly Int2 Vector = new Int2(Big, Wrapped) * 2;

    // Folded at compile time: each calls an intrinsic, which a program-scope initializer must not.
    private static readonly int FoldedSum = Gpu.Abs(Big + 2);

    private static readonly int FoldedProduct = Gpu.Max(Big * 2, Wrapped * 3);

    private static readonly Int2 FoldedVector = Gpu.Min(new Int2(Big, Wrapped) + 5, new Int2(Wrapped) * 7);

    public void Execute()
    {
        output[0] = Wrapped;
        output[1] = Product;
        output[2] = Negated;
        output[3] = LongWrapped;
        output[4] = LongProduct;
        output[5] = Wrapped - Big;
        output[6] = Vector.X;
        output[7] = Vector.Y;
        output[8] = FoldedSum;
        output[9] = FoldedProduct;
        output[10] = FoldedVector.X;
        output[11] = FoldedVector.Y;
    }
}
