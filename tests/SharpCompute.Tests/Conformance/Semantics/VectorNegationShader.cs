namespace SharpCompute.Tests.Conformance.Semantics;

public struct NegationCell
{
    public Int3 V;
    public Int2 P;
    public UInt4 U;
}

/// <summary>
/// Every way of negating an integer vector, each with one lane of the negation never read and the same
/// vector subtracted in a chain afterwards. Each vector is built from scalars (a vector that is itself
/// a sum does not show the miscompile). No arithmetic overflows.
/// </summary>
[ThreadGroupSize(64, 1, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct VectorNegationShader(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<Int3> vectors, ReadWriteBuffer<NegationCell> cells, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Slots = 96;

    [GroupShared]
    private static readonly Int3[] Tile = new Int3[64];

    private static Int3 Flip(Int3 value) => -value;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;
        Int3 v = new Int3(a, b, a ^ b);
        Int3 c = new Int3(1000, 2000, 3000);

        // 0: -v
        Int3 n0 = -v;
        output[o + 0] = n0.X + n0.Z;
        Int3 t0 = v;
        t0 += c;
        t0 *= 3;
        t0 -= v;
        output[o + 1] = t0.X;
        output[o + 2] = t0.Y;
        output[o + 3] = t0.Z;

        // 1: 0 - w
        Int3 w1 = new Int3(a + 1, b - 1, (a ^ b) + 1);
        Int3 n1 = new Int3(0) - w1;
        output[o + 4] = n1.X + n1.Y;
        Int3 t1 = ((w1 + c) * 5) - w1;
        output[o + 5] = t1.X;
        output[o + 6] = t1.Y;
        output[o + 7] = t1.Z;

        // 2: w * -1
        Int3 w2 = new Int3(a + 2, b - 2, (a ^ b) + 2);
        Int3 n2 = w2 * -1;
        output[o + 8] = n2.Y + n2.Z;
        Int3 t2 = (w2 * 7) + c - w2;
        output[o + 9] = t2.X;
        output[o + 10] = t2.Y;
        output[o + 11] = t2.Z;

        // 3: -1 * w
        Int3 w3 = new Int3(a + 3, b - 3, (a ^ b) + 3);
        Int3 n3 = -1 * w3;
        output[o + 12] = n3.X;
        Int3 t3 = c - w3 + (w3 * 3);
        output[o + 13] = t3.X;
        output[o + 14] = t3.Y;
        output[o + 15] = t3.Z;

        // 4: w / -1
        Int3 w4 = new Int3(a + 4, b - 4, (a ^ b) + 4);
        Int3 n4 = w4 / -1;
        output[o + 16] = n4.Z;
        Int3 t4 = ((w4 + c) * 3) - w4;
        output[o + 17] = t4.X;
        output[o + 18] = t4.Y;
        output[o + 19] = t4.Z;

        // 5: w *= -1 on a copy
        Int3 w5 = new Int3(a + 5, b - 5, (a ^ b) + 5);
        Int3 n5 = w5;
        n5 *= -1;
        output[o + 20] = n5.X + n5.Z;
        Int3 t5 = ((w5 + c) * 3) - w5;
        output[o + 21] = t5.X;
        output[o + 22] = t5.Y;
        output[o + 23] = t5.Z;

        // 6: Int2 and Int4
        Int2 p = new Int2(a + 6, b + 6);
        Int2 np = -p;
        output[o + 24] = np.X;
        Int2 tp = ((p + new Int2(100, 200)) * 3) - p;
        output[o + 25] = tp.X;
        output[o + 26] = tp.Y;
        Int4 q = new Int4(a + 7, b + 7, a + b, a - b);
        Int4 nq = -q;
        output[o + 27] = nq.X + nq.W;
        Int4 tq = ((q + new Int4(10, 20, 30, 40)) * 3) - q;
        output[o + 28] = tq.X;
        output[o + 29] = tq.Y;
        output[o + 30] = tq.Z;
        output[o + 31] = tq.W;

        // 7: unsigned
        UInt3 u = new UInt3((uint)a + 8u, (uint)b + 8u, (uint)(a ^ b) + 8u);
        UInt3 nu = new UInt3(0u) - u;
        output[o + 32] = (int)(nu.X + nu.Z);
        UInt3 tu = ((u + new UInt3(1000u, 2000u, 3000u)) * 3u) - u;
        output[o + 33] = (int)tu.X;
        output[o + 34] = (int)tu.Y;
        output[o + 35] = (int)tu.Z;
        UInt3 u2 = new UInt3((uint)a + 9u, (uint)b + 9u, (uint)(a ^ b) + 9u);
        UInt3 nu2 = u2 * uint.MaxValue;
        output[o + 36] = (int)nu2.Y;
        UInt3 tu2 = ((u2 + new UInt3(1000u, 2000u, 3000u)) * 3u) - u2;
        output[o + 37] = (int)tu2.X;
        output[o + 38] = (int)tu2.Y;
        output[o + 39] = (int)tu2.Z;

        // 8: a packed vector from a buffer, from a struct in a buffer, from group-shared memory
        vectors[i] = new Int3(a + 9, b + 9, (a ^ b) + 9);
        Int3 n8 = -vectors[i];
        output[o + 40] = n8.X + n8.Z;
        Int3 t8 = ((vectors[i] + c) * 3) - vectors[i];
        output[o + 41] = t8.X;
        output[o + 42] = t8.Y;
        output[o + 43] = t8.Z;
        cells[i].V = new Int3(a + 10, b + 10, (a ^ b) + 10);
        cells[i].P = new Int2(a + 10, b + 10);
        Int3 n9 = -cells[i].V;
        Int2 n9p = -cells[i].P;
        output[o + 44] = n9.Y + n9p.Y;
        Int3 t9 = ((cells[i].V + c) * 3) - cells[i].V;
        Int2 t9p = ((cells[i].P + new Int2(5, 6)) * 3) - cells[i].P;
        output[o + 45] = t9.X;
        output[o + 46] = t9.Y;
        output[o + 47] = t9.Z;
        output[o + 48] = t9p.X;
        output[o + 49] = t9p.Y;
        int g = GroupIds.X;
        Tile[g] = new Int3(a + 11, b + 11, (a ^ b) + 11);
        Int3 n10 = -Tile[g];
        output[o + 50] = n10.X;
        Int3 t10 = ((Tile[g] + c) * 3) - Tile[g];
        output[o + 51] = t10.X;
        output[o + 52] = t10.Y;
        output[o + 53] = t10.Z;

        // 9: in a helper, in a chain, and in place on a struct field and a buffer element
        Int3 w11 = new Int3(a + 12, b - 12, (a ^ b) + 12);
        Int3 n11 = Flip(w11);
        output[o + 54] = n11.X + n11.Z;
        Int3 t11 = ((w11 + c) * 3) - w11;
        output[o + 55] = t11.X;
        output[o + 56] = t11.Y;
        output[o + 57] = t11.Z;
        Int3 w12 = new Int3(a + 13, b - 13, (a ^ b) + 13);
        Int3 n12 = c + (-w12) + c;
        output[o + 58] = n12.Z;
        Int3 t12 = ((w12 + c) * 3) - w12;
        output[o + 59] = t12.X;
        output[o + 60] = t12.Y;
        output[o + 61] = t12.Z;
        NegationCell cell = cells[i];
        cell.V *= -1;
        output[o + 62] = cell.V.X;
        Int3 t13 = ((cells[i].V + c) * 9) - cells[i].V;
        output[o + 63] = t13.X;
        output[o + 64] = t13.Y;
        output[o + 65] = t13.Z;
        vectors[i] *= -1;
        output[o + 66] = vectors[i].Z;
        Int3 back = -vectors[i];
        Int3 t14 = ((back + c) * 11) - back;
        output[o + 67] = t14.X;
        output[o + 68] = t14.Y;
        output[o + 69] = t14.Z;

        // 10: every lane read (nothing to go wrong), for the values themselves
        Int3 w15 = new Int3(a + 14, b - 14, (a ^ b) + 14);
        Int3 n15 = -w15;
        output[o + 70] = n15.X;
        output[o + 71] = n15.Y;
        output[o + 72] = n15.Z;
        UInt4 uq = new UInt4((uint)a, (uint)b, 5u, 7u);
        cells[i].U = new UInt4(0u) - uq;
        output[o + 73] = (int)cells[i].U.X;
        output[o + 74] = (int)cells[i].U.W;

        // 11: forms that are not a negation of the whole vector
        Int3 w16 = new Int3(a + 15, b - 15, (a ^ b) + 15);
        Int3 n16 = new Int3(0, 5, 0) - w16;
        output[o + 75] = n16.X + n16.Z;
        Int3 t16 = ((w16 + c) * 3) - w16;
        output[o + 76] = t16.X;
        output[o + 77] = t16.Y;
        output[o + 78] = t16.Z;
        Int3 w17 = new Int3(a + 16, b - 16, (a ^ b) + 16);
        Int3 n17 = w17 * new Int3(-1, 7, -1);
        output[o + 79] = n17.X + n17.Z;
        Int3 t17 = ((w17 + c) * 3) - w17;
        output[o + 80] = t17.X;
        output[o + 81] = t17.Y;
        output[o + 82] = t17.Z;
        Int3 w18 = new Int3(a + 17, b - 17, (a ^ b) + 17);
        Int3 n18 = ~w18 + 1;
        output[o + 83] = n18.X + n18.Z;
        Int3 t18 = ((w18 + c) * 3) - w18;
        output[o + 84] = t18.X;
        output[o + 85] = t18.Y;
        output[o + 86] = t18.Z;
        Int3 w19 = new Int3(a + 18, b - 18, (a ^ b) + 18);
        Int3 n19 = w19 - (2 * w19);
        output[o + 87] = n19.X + n19.Z;
        Int3 t19 = ((w19 + c) * 3) - w19;
        output[o + 88] = t19.X;
        output[o + 89] = t19.Y;
        output[o + 90] = t19.Z;

        // 12: floats keep their operator
        Float3 f = new Float3(a, b, a + b);
        Float3 nf = -f;
        output[o + 91] = (int)(nf.X + nf.Z);
        Float3 tf = ((f + new Float3(1f, 2f, 3f)) * 3f) - f;
        output[o + 92] = (int)tf.X;
        output[o + 93] = (int)tf.Y;
        output[o + 94] = (int)tf.Z;
        output[o + 95] = 1;
    }
}
