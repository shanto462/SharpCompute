using SharpCompute;

namespace SharpCompute.Benchmarks.Shaders;

// The generated shaders that only the native baselines dispatch (--native-baselines); each has a
// hand-written twin in HandWrittenKernels.cs. MatMulNaive, NBodyStep and SaxpyGenerated, which the
// baselines also time, are in Shaders.cs.

/// <summary>
/// The fastest <c>C = A · B</c> a SharpCompute shader can be written as today: one 16 × 16 tile of C
/// per threadgroup, with the tiles of A and B it needs staged in threadgroup memory, so each element
/// is read from the buffers once per tile instead of once per product. (The kernel of ComputeLab's
/// MatMul page, for n a multiple of 16.)
/// </summary>
[ThreadGroupSize(MatMulTiled.Tile, MatMulTiled.Tile)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatMulTiled(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c, int n) : IComputeShader
{
    public const int Tile = 16;

    [GroupShared]
    private static readonly float[] tileA = new float[Tile * Tile];

    [GroupShared]
    private static readonly float[] tileB = new float[Tile * Tile];

    public void Execute()
    {
        int column = ThreadIds.X;
        int row = ThreadIds.Y;
        int x = GroupIds.X;
        int y = GroupIds.Y;
        int tiles = n / Tile;
        float sum = 0f;

        for (int t = 0; t < tiles; t++)
        {
            tileA[(y * Tile) + x] = a[(row * n) + (t * Tile) + x];
            tileB[(y * Tile) + x] = b[(((t * Tile) + y) * n) + column];
            Gpu.GroupMemoryBarrierWithGroupSync();

            for (int j = 0; j < Tile; j++)
            {
                sum += tileA[(y * Tile) + j] * tileB[(j * Tile) + x];
            }

            Gpu.GroupMemoryBarrierWithGroupSync();
        }

        c[(row * n) + column] = sum;
    }
}

/// <summary>
/// The integer workload: <c>rounds</c> rounds of a 32-bit mix (the MurmurHash3 finalizer, then a
/// rotate and a round constant) on every element. Shifts, xors, multiplies and adds on <c>uint</c>,
/// each round depending on the one before it, with no floating point and one load and one store per
/// thread: what is timed is the integer code the generator emits for the loop.
/// </summary>
[ThreadGroupSize(256)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct HashRounds(ReadOnlyBuffer<uint> input, ReadWriteBuffer<uint> output, int rounds) : IComputeShader
{
    /// <summary>Integer operations per round: 3 shift-xors (2 each), 2 multiplies, the rotate (3) and the round constant (2).</summary>
    public const int OperationsPerRound = 13;

    public void Execute()
    {
        int i = ThreadIds.X;
        uint h = input[i];

        for (int r = 0; r < rounds; r++)
        {
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            h = (h << 5) | (h >> 27);
            h += (uint)r * 0x9E3779B9u;
        }

        output[i] = h;
    }
}
