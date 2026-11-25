using System;

namespace SharpCompute.Tests.Conformance.Semantics;

public struct InPlaceCell
{
    public int Count;
    public long Total;
    public Int3 V;
    public Int2 P;
}

/// <summary>
/// Compound assignments and increments on int, long, Int2 and Int3 targets in device, threadgroup and
/// thread memory, with a value or an index that has a side effect (<c>Side</c> counts its calls in
/// <c>counts</c>), a read before and after each store, and the value of the assignment used.
/// </summary>
[ThreadGroupSize(64, 1, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct InPlaceStorageShader(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<int> data, ReadWriteBuffer<long> wide, ReadWriteBuffer<Int3> vec, ReadWriteBuffer<InPlaceCell> cells, ReadWriteBuffer<int> counts, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 48;
    public const int Stride = 8;

    [GroupShared]
    private static readonly int[] Tile = new int[64];

    /// <summary>A value with a side effect, in a buffer of its own so that SCG0021 accepts it beside every target.</summary>
    private int Side(int scratch, int value)
    {
        counts[scratch] += 1;
        return value;
    }

    private static void AddTo(ref int target, int by)
    {
        target += by;
        target *= 3;
        target++;
    }

    private static int PostIncrement(ref int target) => target++;

    private static void AddLong(ref long target, long by)
    {
        target += by;
        target--;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;
        int e = i * Stride;
        int scratch = i;

        // Device int, a value with a side effect, reads on both sides of the store.
        data[e] = a;
        int first = data[e];
        data[e] += Side(scratch, b);
        int second = data[e];
        output[o + 0] = first;
        output[o + 1] = second;
        output[o + 2] = second - first == b ? 1 : 0;

        data[e + 1] = a;
        data[Side(scratch, e + 1)] *= b;
        output[o + 3] = data[e + 1];

        data[e + 2] = a;
        int used = data[Side(scratch, e + 2)] -= b;
        output[o + 4] = used;
        output[o + 5] = data[e + 2];

        data[e + 3] = a;
        int oldValue = data[e + 3]++;
        int newValue = ++data[e + 3];
        output[o + 6] = oldValue;
        output[o + 7] = newValue;
        output[o + 8] = data[e + 3];
        output[o + 9] = oldValue < newValue ? 1 : 0;

        data[e + 4] = a;
        data[e + 4] += Side(scratch, b);
        output[o + 10] = data[e + 4];
        int local = a;
        AddTo(ref local, b);
        output[o + 11] = local;
        output[o + 12] = PostIncrement(ref local);
        output[o + 13] = local;

        // A struct in a buffer.
        cells[i].Count = a;
        cells[i].Count += Side(scratch, b);
        cells[i].Count++;
        cells[i].Total = ((long)a << 32) + b;
        cells[i].Total *= Side(scratch, b);
        cells[i].Total += long.MaxValue;
        cells[i].V = new Int3(a, b, a);
        cells[i].V += new Int3(Side(scratch, b));
        cells[i].V.Y *= Side(scratch, b);
        cells[i].V.XZ -= new Int2(Side(scratch, 1), b);
        cells[i].P = new Int2(a, b);
        cells[i].P.X += Side(scratch, int.MaxValue);
        int px = cells[i].P.Y++;
        output[o + 14] = cells[i].Count;
        output[o + 15] = cells[i].Total;
        output[o + 16] = cells[i].V.X;
        output[o + 17] = cells[i].V.Y;
        output[o + 18] = cells[i].V.Z;
        output[o + 19] = cells[i].P.X;
        output[o + 20] = ((long)px << 32) ^ (uint)cells[i].P.Y;

        // A local struct.
        InPlaceCell c = cells[i];
        c.Count *= Side(scratch, b);
        c.V.X += Side(scratch, a);
        c.Total -= Side(scratch, b);
        c.P += new Int2(Side(scratch, b), 1);
        c.P.Y *= b;
        int cy = ++c.V.Y;
        output[o + 21] = c.Count;
        output[o + 22] = c.V.X;
        output[o + 23] = c.Total;
        output[o + 24] = c.P.X;
        output[o + 25] = c.P.Y;
        output[o + 26] = cy;

        // A packed Int3 in a buffer.
        vec[i] = new Int3(a, b, 1);
        Int3 seen = vec[i];
        vec[Side(scratch, i)] *= b;
        vec[i].Z += Side(scratch, a);
        int vz = vec[i].Z++;
        Int3 now = vec[i];
        output[o + 27] = vz;
        output[o + 28] = now.X;
        output[o + 29] = now.Y;
        output[o + 30] = now.Z;
        output[o + 31] = seen.X == now.X ? 1 : 0;

        // 64-bit elements.
        long la = ((long)a << 32) | (uint)b;
        wide[i] = la;
        long wideFirst = wide[i];
        wide[Side(scratch, i)] += long.MaxValue;
        long wideOld = wide[i]++;
        long wideNew = --wide[i];
        output[o + 32] = wideFirst;
        output[o + 33] = wideOld;
        output[o + 34] = wideNew;
        long wideLocal = la;
        AddLong(ref wideLocal, long.MinValue + b);
        output[o + 35] = wideLocal;

        // A local array.
        Span<int> window = stackalloc int[4];
        window[0] = a;
        window[1] = b;
        window[Side(scratch, 0)] += b;
        int w0 = window[0]++;
        window[2] = w0;
        int w1 = --window[1];
        window[3] = w1;
        AddTo(ref window[1], a);
        output[o + 36] = window[0];
        output[o + 37] = window[1];
        output[o + 38] = window[2];
        output[o + 39] = window[3];

        // Group-shared memory.
        int g = GroupIds.X;
        Tile[g] = a;
        int tileFirst = Tile[g];
        Tile[Side(scratch, g)] += b;
        int tileOld = Tile[g]++;
        Tile[Side(scratch, g)] *= 3;
        Tile[g] -= Side(scratch, b);
        output[o + 40] = tileFirst;
        output[o + 41] = tileOld;
        output[o + 42] = Tile[g];

        // A value used, on a local, with and without a side effect.
        int m = a;
        int viaCall = m += Side(scratch, b);
        int plain = m *= b;
        int post = m--;
        output[o + 43] = viaCall;
        output[o + 44] = plain;
        output[o + 45] = post;
        output[o + 46] = m;
        output[o + 47] = counts[scratch];
    }
}

public struct VectorCell
{
    public Int2 A;
    public Int3 B;
    public Int4 C;
    public int D;
}

/// <summary>
/// Compound assignments on int vectors whose value is a constructor or a call, by where the target
/// lives: locals, helper parameters, buffers of Int2, packed Int3 and Int4, a struct in a buffer and in
/// a local, group-shared memory and a local array, with reads around each store.
/// </summary>
[ThreadGroupSize(64, 1, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct VectorViewShader(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<Int2> twos, ReadWriteBuffer<Int3> threes, ReadWriteBuffer<Int4> fours, ReadWriteBuffer<VectorCell> cells, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 40;

    [GroupShared]
    private static readonly Int3[] Tile = new Int3[64];

    private static int Same(int value) => value;

    private static Int3 Raise(Int3 value, int by)
    {
        value += new Int3(by);
        return value;
    }

    private static void RaiseInPlace(ref Int3 value, int by)
    {
        value += new Int3(by, by, by);
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;

        // Locals: the value is a constructor.
        Int2 l2 = new Int2(a, b);
        l2 += new Int2(1, 2);
        output[o + 0] = l2.X;
        output[o + 1] = l2.Y;

        Int3 l3 = new Int3(a, b, a);
        l3 += new Int3(1, 2, 3);
        output[o + 2] = l3.X;
        output[o + 3] = l3.Y;
        output[o + 4] = l3.Z;

        Int4 l4 = new Int4(a, b, a, b);
        l4 *= new Int4(3);
        output[o + 5] = l4.X;
        output[o + 6] = l4.W;

        Int3 m3 = new Int3(a, b, a);
        m3 -= new Int3(b);
        m3 += new Int3(5);
        output[o + 7] = m3.X;
        output[o + 8] = m3.Y;

        // Locals: a component, the value is a call.
        Int3 c3 = new Int3(a, b, a);
        c3.Y += Same(b);
        c3.XZ *= new Int2(3, 5);
        output[o + 9] = c3.X;
        output[o + 10] = c3.Y;
        output[o + 11] = c3.Z;

        // Locals: scalars, for comparison.
        int s = a;
        s += Same(b);
        long w = a;
        w += Same(b);
        w *= Same(b);
        output[o + 12] = s;
        output[o + 13] = w;

        // Parameters of a helper.
        Int3 raised = Raise(new Int3(a, b, 7), b);
        output[o + 14] = raised.X;
        output[o + 15] = raised.Z;
        Int3 inPlace = new Int3(a, b, 9);
        RaiseInPlace(ref inPlace, a);
        output[o + 16] = inPlace.Y;
        output[o + 17] = inPlace.Z;

        // Buffers, with a read before and after and nothing between.
        twos[i] = new Int2(a, b);
        Int2 two = twos[i];
        twos[i] += new Int2(b, a);
        Int2 twoAfter = twos[i];
        output[o + 18] = two.X;
        output[o + 19] = twoAfter.X;
        output[o + 20] = twoAfter.Y;

        threes[i] = new Int3(a, b, a);
        Int3 three = threes[i];
        threes[i] += new Int3(b, a, b);
        Int3 threeAfter = threes[i];
        output[o + 21] = three.X;
        output[o + 22] = threeAfter.X;
        output[o + 23] = threeAfter.Z;

        fours[i] = new Int4(a, b, a, b);
        Int4 four = fours[i];
        fours[i] *= new Int4(b);
        fours[i].W += Same(a);
        Int4 fourAfter = fours[i];
        output[o + 24] = four.X;
        output[o + 25] = fourAfter.X;
        output[o + 26] = fourAfter.W;

        // A struct in a buffer and a local struct.
        cells[i].A = new Int2(a, b);
        cells[i].B = new Int3(a, b, a);
        cells[i].C = new Int4(a, b, a, b);
        VectorCell before = cells[i];
        cells[i].A += new Int2(b);
        cells[i].B -= new Int3(b, 1, 2);
        cells[i].C *= new Int4(3, 5, 7, 9);
        VectorCell after = cells[i];
        output[o + 27] = before.A.X;
        output[o + 28] = after.A.X;
        output[o + 29] = after.B.X;
        output[o + 30] = after.C.W;

        VectorCell local = before;
        local.A += new Int2(b);
        local.B -= new Int3(b, 1, 2);
        local.C *= new Int4(3, 5, 7, 9);
        local.D += Same(b);
        output[o + 31] = local.A.X;
        output[o + 32] = local.B.X;
        output[o + 33] = local.C.W;
        output[o + 34] = local.D;

        // Group-shared.
        int g = GroupIds.X;
        Tile[g] = new Int3(a, b, a);
        Int3 tile = Tile[g];
        Tile[g] += new Int3(b);
        Tile[g].Y *= Same(b);
        Int3 tileAfter = Tile[g];
        output[o + 35] = tile.X;
        output[o + 36] = tileAfter.X;
        output[o + 37] = tileAfter.Y;

        // A local array of vectors.
        Span<Int3> window = stackalloc Int3[2];
        window[0] = new Int3(a, b, a);
        window[1] = window[0];
        window[1] += new Int3(b, b, b);
        output[o + 38] = window[1].X;
        output[o + 39] = window[0].X;
    }
}
