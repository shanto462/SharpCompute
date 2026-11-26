namespace SharpCompute.Tests.Conformance.Semantics;

/// <summary>
/// Arithmetic that only amounts to the negation of an integer vector (<c>~v + 1</c>, <c>v - 2 * v</c>,
/// <c>v - (v + v)</c>, <c>v - (v &lt;&lt; 1)</c> and the like), each with a lane of the result never read
/// and the same vector subtracted in a chain afterwards, for <c>Int3</c>, <c>Int2</c>, <c>Int4</c> and
/// <c>UInt3</c>. The Metal compiler turns each into the vector negation it miscompiles, and the
/// generator cannot tell them from other arithmetic, so it computes every sum and difference of
/// integer vectors lane by lane instead. Each vector is built from scalars (a vector that is itself a
/// sum does not show the miscompile). No arithmetic overflows.
/// </summary>
[ThreadGroupSize(64, 1, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct VectorNegationShapesShader(ReadOnlyBuffer<int> pairs, ReadWriteBuffer<Int3> vectors, ReadWriteBuffer<NegationCell> cells, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Slots = 148;

    [GroupShared]
    private static readonly Int3[] Tile = new Int3[64];

    /// <summary>-1, which the compiler only learns after it has inlined this: the generator sees a variable.</summary>
    private static int MinusOne(int value) => (value & 0) - 1;

    /// <summary>Its argument. A call in an operand is a side effect as far as the generator can tell.</summary>
    private static int Same(int value) => value;

    public void Execute()
    {
        int i = ThreadIds.X;
        int a = pairs[2 * i];
        int b = pairs[(2 * i) + 1];
        int o = i * Slots;

        // Int3: ~v + 1
        {
            Int3 w = new Int3(a + 1, b - 1, (a ^ b) + 1);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = ~w + 1;
            output[o + 0] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 1] = t.X;
            output[o + 2] = t.Y;
            output[o + 3] = t.Z;
        }

        // Int3: v - 2 * v
        {
            Int3 w = new Int3(a + 2, b - 2, (a ^ b) + 2);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = w - (2 * w);
            output[o + 4] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 5] = t.X;
            output[o + 6] = t.Y;
            output[o + 7] = t.Z;
        }

        // Int3: v - (v + v)
        {
            Int3 w = new Int3(a + 3, b - 3, (a ^ b) + 3);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = w - (w + w);
            output[o + 8] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 9] = t.X;
            output[o + 10] = t.Y;
            output[o + 11] = t.Z;
        }

        // Int3: v - (v << 1)
        {
            Int3 w = new Int3(a + 4, b - 4, (a ^ b) + 4);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = w - (w << 1);
            output[o + 12] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 13] = t.X;
            output[o + 14] = t.Y;
            output[o + 15] = t.Z;
        }

        // Int3: -(-v)
        {
            Int3 w = new Int3(a + 5, b - 5, (a ^ b) + 5);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = -(-w);
            output[o + 16] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 17] = t.X;
            output[o + 18] = t.Y;
            output[o + 19] = t.Z;
        }

        // Int3: v + (-v)
        {
            Int3 w = new Int3(a + 6, b - 6, (a ^ b) + 6);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = w + (-w);
            output[o + 20] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 21] = t.X;
            output[o + 22] = t.Y;
            output[o + 23] = t.Z;
        }

        // Int3: (0 - v) - v
        {
            Int3 w = new Int3(a + 7, b - 7, (a ^ b) + 7);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = (new Int3(0) - w) - w;
            output[o + 24] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 25] = t.X;
            output[o + 26] = t.Y;
            output[o + 27] = t.Z;
        }

        // Int2: ~v + 1
        {
            Int2 w = new Int2(a + 8, b - 8);
            Int2 c = new Int2(1000, 2000);
            Int2 n = ~w + 1;
            output[o + 28] = n.X;
            Int2 t = ((w + c) * 3) - w;
            output[o + 29] = t.X;
            output[o + 30] = t.Y;
        }

        // Int2: v - 2 * v
        {
            Int2 w = new Int2(a + 9, b - 9);
            Int2 c = new Int2(1000, 2000);
            Int2 n = w - (2 * w);
            output[o + 31] = n.X;
            Int2 t = ((w + c) * 3) - w;
            output[o + 32] = t.X;
            output[o + 33] = t.Y;
        }

        // Int2: v - (v + v)
        {
            Int2 w = new Int2(a + 10, b - 10);
            Int2 c = new Int2(1000, 2000);
            Int2 n = w - (w + w);
            output[o + 34] = n.X;
            Int2 t = ((w + c) * 3) - w;
            output[o + 35] = t.X;
            output[o + 36] = t.Y;
        }

        // Int2: v - (v << 1)
        {
            Int2 w = new Int2(a + 11, b - 11);
            Int2 c = new Int2(1000, 2000);
            Int2 n = w - (w << 1);
            output[o + 37] = n.X;
            Int2 t = ((w + c) * 3) - w;
            output[o + 38] = t.X;
            output[o + 39] = t.Y;
        }

        // Int2: -(-v)
        {
            Int2 w = new Int2(a + 12, b - 12);
            Int2 c = new Int2(1000, 2000);
            Int2 n = -(-w);
            output[o + 40] = n.X;
            Int2 t = ((w + c) * 3) - w;
            output[o + 41] = t.X;
            output[o + 42] = t.Y;
        }

        // Int2: v + (-v)
        {
            Int2 w = new Int2(a + 13, b - 13);
            Int2 c = new Int2(1000, 2000);
            Int2 n = w + (-w);
            output[o + 43] = n.X;
            Int2 t = ((w + c) * 3) - w;
            output[o + 44] = t.X;
            output[o + 45] = t.Y;
        }

        // Int2: (0 - v) - v
        {
            Int2 w = new Int2(a + 14, b - 14);
            Int2 c = new Int2(1000, 2000);
            Int2 n = (new Int2(0) - w) - w;
            output[o + 46] = n.X;
            Int2 t = ((w + c) * 3) - w;
            output[o + 47] = t.X;
            output[o + 48] = t.Y;
        }

        // Int4: ~v + 1
        {
            Int4 w = new Int4(a + 15, b - 15, (a ^ b) + 15, a + b + 15);
            Int4 c = new Int4(1000, 2000, 3000, 4000);
            Int4 n = ~w + 1;
            output[o + 49] = n.X + n.W;
            Int4 t = ((w + c) * 3) - w;
            output[o + 50] = t.X;
            output[o + 51] = t.Y;
            output[o + 52] = t.Z;
            output[o + 53] = t.W;
        }

        // Int4: v - 2 * v
        {
            Int4 w = new Int4(a + 16, b - 16, (a ^ b) + 16, a + b + 16);
            Int4 c = new Int4(1000, 2000, 3000, 4000);
            Int4 n = w - (2 * w);
            output[o + 54] = n.X + n.W;
            Int4 t = ((w + c) * 3) - w;
            output[o + 55] = t.X;
            output[o + 56] = t.Y;
            output[o + 57] = t.Z;
            output[o + 58] = t.W;
        }

        // Int4: v - (v + v)
        {
            Int4 w = new Int4(a + 17, b - 17, (a ^ b) + 17, a + b + 17);
            Int4 c = new Int4(1000, 2000, 3000, 4000);
            Int4 n = w - (w + w);
            output[o + 59] = n.X + n.W;
            Int4 t = ((w + c) * 3) - w;
            output[o + 60] = t.X;
            output[o + 61] = t.Y;
            output[o + 62] = t.Z;
            output[o + 63] = t.W;
        }

        // Int4: v - (v << 1)
        {
            Int4 w = new Int4(a + 18, b - 18, (a ^ b) + 18, a + b + 18);
            Int4 c = new Int4(1000, 2000, 3000, 4000);
            Int4 n = w - (w << 1);
            output[o + 64] = n.X + n.W;
            Int4 t = ((w + c) * 3) - w;
            output[o + 65] = t.X;
            output[o + 66] = t.Y;
            output[o + 67] = t.Z;
            output[o + 68] = t.W;
        }

        // Int4: -(-v)
        {
            Int4 w = new Int4(a + 19, b - 19, (a ^ b) + 19, a + b + 19);
            Int4 c = new Int4(1000, 2000, 3000, 4000);
            Int4 n = -(-w);
            output[o + 69] = n.X + n.W;
            Int4 t = ((w + c) * 3) - w;
            output[o + 70] = t.X;
            output[o + 71] = t.Y;
            output[o + 72] = t.Z;
            output[o + 73] = t.W;
        }

        // Int4: v + (-v)
        {
            Int4 w = new Int4(a + 20, b - 20, (a ^ b) + 20, a + b + 20);
            Int4 c = new Int4(1000, 2000, 3000, 4000);
            Int4 n = w + (-w);
            output[o + 74] = n.X + n.W;
            Int4 t = ((w + c) * 3) - w;
            output[o + 75] = t.X;
            output[o + 76] = t.Y;
            output[o + 77] = t.Z;
            output[o + 78] = t.W;
        }

        // Int4: (0 - v) - v
        {
            Int4 w = new Int4(a + 21, b - 21, (a ^ b) + 21, a + b + 21);
            Int4 c = new Int4(1000, 2000, 3000, 4000);
            Int4 n = (new Int4(0) - w) - w;
            output[o + 79] = n.X + n.W;
            Int4 t = ((w + c) * 3) - w;
            output[o + 80] = t.X;
            output[o + 81] = t.Y;
            output[o + 82] = t.Z;
            output[o + 83] = t.W;
        }

        // UInt3: ~v + 1
        {
            UInt3 w = new UInt3((uint)(a + 22), (uint)(b - 22), (uint)((a ^ b) + 22));
            UInt3 c = new UInt3(1000u, 2000u, 3000u);
            UInt3 n = ~w + 1u;
            output[o + 84] = (int)(n.X + n.Z);
            UInt3 t = ((w + c) * 3u) - w;
            output[o + 85] = (int)(t.X);
            output[o + 86] = (int)(t.Y);
            output[o + 87] = (int)(t.Z);
        }

        // UInt3: v - 2 * v
        {
            UInt3 w = new UInt3((uint)(a + 23), (uint)(b - 23), (uint)((a ^ b) + 23));
            UInt3 c = new UInt3(1000u, 2000u, 3000u);
            UInt3 n = w - (2u * w);
            output[o + 88] = (int)(n.X + n.Z);
            UInt3 t = ((w + c) * 3u) - w;
            output[o + 89] = (int)(t.X);
            output[o + 90] = (int)(t.Y);
            output[o + 91] = (int)(t.Z);
        }

        // UInt3: v - (v + v)
        {
            UInt3 w = new UInt3((uint)(a + 24), (uint)(b - 24), (uint)((a ^ b) + 24));
            UInt3 c = new UInt3(1000u, 2000u, 3000u);
            UInt3 n = w - (w + w);
            output[o + 92] = (int)(n.X + n.Z);
            UInt3 t = ((w + c) * 3u) - w;
            output[o + 93] = (int)(t.X);
            output[o + 94] = (int)(t.Y);
            output[o + 95] = (int)(t.Z);
        }

        // UInt3: v - (v << 1)
        {
            UInt3 w = new UInt3((uint)(a + 25), (uint)(b - 25), (uint)((a ^ b) + 25));
            UInt3 c = new UInt3(1000u, 2000u, 3000u);
            UInt3 n = w - (w << 1);
            output[o + 96] = (int)(n.X + n.Z);
            UInt3 t = ((w + c) * 3u) - w;
            output[o + 97] = (int)(t.X);
            output[o + 98] = (int)(t.Y);
            output[o + 99] = (int)(t.Z);
        }

        // UInt3: -(-v)
        {
            UInt3 w = new UInt3((uint)(a + 26), (uint)(b - 26), (uint)((a ^ b) + 26));
            UInt3 c = new UInt3(1000u, 2000u, 3000u);
            UInt3 n = new UInt3(0u) - (new UInt3(0u) - w);
            output[o + 100] = (int)(n.X + n.Z);
            UInt3 t = ((w + c) * 3u) - w;
            output[o + 101] = (int)(t.X);
            output[o + 102] = (int)(t.Y);
            output[o + 103] = (int)(t.Z);
        }

        // UInt3: v + (-v)
        {
            UInt3 w = new UInt3((uint)(a + 27), (uint)(b - 27), (uint)((a ^ b) + 27));
            UInt3 c = new UInt3(1000u, 2000u, 3000u);
            UInt3 n = w + (new UInt3(0u) - w);
            output[o + 104] = (int)(n.X + n.Z);
            UInt3 t = ((w + c) * 3u) - w;
            output[o + 105] = (int)(t.X);
            output[o + 106] = (int)(t.Y);
            output[o + 107] = (int)(t.Z);
        }

        // UInt3: (0 - v) - v
        {
            UInt3 w = new UInt3((uint)(a + 28), (uint)(b - 28), (uint)((a ^ b) + 28));
            UInt3 c = new UInt3(1000u, 2000u, 3000u);
            UInt3 n = (new UInt3(0u) - w) - w;
            output[o + 108] = (int)(n.X + n.Z);
            UInt3 t = ((w + c) * 3u) - w;
            output[o + 109] = (int)(t.X);
            output[o + 110] = (int)(t.Y);
            output[o + 111] = (int)(t.Z);
        }

        // Int3: ~(v - 1)
        {
            Int3 w = new Int3(a + 29, b - 29, (a ^ b) + 29);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = ~(w - 1);
            output[o + 112] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 113] = t.X;
            output[o + 114] = t.Y;
            output[o + 115] = t.Z;
        }

        // Int3: v * s, where s is -1
        {
            Int3 w = new Int3(a + 30, b - 30, (a ^ b) + 30);
            Int3 c = new Int3(1000, 2000, 3000);
            int s = MinusOne(a);
            Int3 n = w * s;
            output[o + 116] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 117] = t.X;
            output[o + 118] = t.Y;
            output[o + 119] = t.Z;
        }

        // Int3: v / s, where s is -1
        {
            Int3 w = new Int3(a + 31, b - 31, (a ^ b) + 31);
            Int3 c = new Int3(1000, 2000, 3000);
            int s = MinusOne(a);
            Int3 n = w / s;
            output[o + 120] = n.X + n.Z;
            Int3 t = ((w + c) * 3) - w;
            output[o + 121] = t.X;
            output[o + 122] = t.Y;
            output[o + 123] = t.Z;
        }

        // Int3: a chain of sums that subtracts through ~v + 1
        {
            Int3 w = new Int3(a + 32, b - 32, (a ^ b) + 32);
            Int3 c = new Int3(1000, 2000, 3000);
            int s = MinusOne(a);
            Int3 n = w * s;
            output[o + 124] = n.X + n.Z;
            Int3 t = (w * 3) + c + ~w + 1;
            output[o + 125] = t.X;
            output[o + 126] = t.Y;
            output[o + 127] = t.Z;
        }

        // Int3: a chain of compound assignments
        {
            Int3 w = new Int3(a + 33, b - 33, (a ^ b) + 33);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = ~w + 1;
            output[o + 128] = n.X + n.Z;
            Int3 t = w;
            t += c;
            t *= 3;
            t -= w;
            output[o + 129] = t.X;
            output[o + 130] = t.Y;
            output[o + 131] = t.Z;
        }

        // Int3: a chain of compound assignments whose values have side effects
        {
            Int3 w = new Int3(a + 34, b - 34, (a ^ b) + 34);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = ~w + 1;
            output[o + 132] = n.X + n.Z;
            Int3 t = w;
            t += new Int3(Same(1000), Same(2000), Same(3000));
            t *= 3;
            t -= new Int3(Same(w.X), w.Y, w.Z);
            output[o + 133] = t.X;
            output[o + 134] = t.Y;
            output[o + 135] = t.Z;
        }

        // Int3: a chain of compound assignments on a buffer element whose index has side effects
        {
            Int3 w = new Int3(a + 35, b - 35, (a ^ b) + 35);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = w - (2 * w);
            output[o + 136] = n.X + n.Z;
            vectors[i] = w;
            vectors[Same(i)] += c;
            vectors[i] *= 3;
            vectors[Same(i)] -= w;
            Int3 t = vectors[i];
            output[o + 137] = t.X;
            output[o + 138] = t.Y;
            output[o + 139] = t.Z;
        }

        // Int3: a chain of compound assignments on a struct field in a buffer
        {
            Int3 w = new Int3(a + 36, b - 36, (a ^ b) + 36);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = w - (w + w);
            output[o + 140] = n.X + n.Z;
            cells[i].V = w;
            cells[Same(i)].V += c;
            cells[i].V *= 3;
            cells[Same(i)].V -= w;
            Int3 t = cells[i].V;
            output[o + 141] = t.X;
            output[o + 142] = t.Y;
            output[o + 143] = t.Z;
        }

        // Int3: a chain of compound assignments on group-shared memory
        {
            Int3 w = new Int3(a + 37, b - 37, (a ^ b) + 37);
            Int3 c = new Int3(1000, 2000, 3000);
            Int3 n = w - (w << 1);
            output[o + 144] = n.X + n.Z;
            int g = GroupIds.X;
            Tile[g] = w;
            Tile[Same(g)] += c;
            Tile[g] *= 3;
            Tile[Same(g)] -= w;
            Int3 t = Tile[g];
            output[o + 145] = t.X;
            output[o + 146] = t.Y;
            output[o + 147] = t.Z;
        }
    }
}
