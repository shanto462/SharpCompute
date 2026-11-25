using System.Runtime.InteropServices;
using SharpCompute;

namespace SharpCompute.Tests.Threadgroups;

// Shaders for the threadgroup tests: group-shared memory, barriers and the group built-ins. Each one
// runs unchanged on the GPU (For), on the CPU's group executor (ForCpu) and split between both (ForSplit).

/// <summary>Records the group built-ins of every thread of a 3D grid: 8 ints per thread.</summary>
[ThreadGroupSize(4, 2, 2)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct RecordGroupBuiltins3D(ReadWriteBuffer<int> values) : IComputeShader
{
    public const int Stride = 8;

    public void Execute()
    {
        Int3 tid = ThreadIds.XYZ;
        Int3 grid = DispatchSize.XYZ;
        Int3 size = GroupSize.XYZ;
        Int3 groups = ThreadGroupCount.XYZ;
        int i = ((((tid.Z * grid.Y) + tid.Y) * grid.X) + tid.X) * Stride;

        values[i + 0] = size.X;
        values[i + 1] = size.Y;
        values[i + 2] = size.Z;
        values[i + 3] = GroupSize.Count;
        values[i + 4] = GroupIds.Index;
        values[i + 5] = groups.X;
        values[i + 6] = groups.Y;
        values[i + 7] = groups.Z;
    }
}

/// <summary>The 2D form, with the runtime's default threadgroup size (16 × 16) and the scalar and XY accessors.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct RecordGroupBuiltins2D(ReadWriteBuffer<int> values) : IComputeShader
{
    public const int Stride = 8;

    public void Execute()
    {
        int i = ((ThreadIds.Y * DispatchSize.X) + ThreadIds.X) * Stride;

        values[i + 0] = GroupSize.X;
        values[i + 1] = GroupSize.XY.Y;
        values[i + 2] = GroupSize.Z;
        values[i + 3] = GroupSize.Count;
        values[i + 4] = GroupIds.Index;
        values[i + 5] = ThreadGroupCount.XY.X;
        values[i + 6] = ThreadGroupCount.Y;
        values[i + 7] = ThreadGroupCount.Z;
    }
}

/// <summary>The 1D form, with a threadgroup size the grid is rarely a multiple of; the built-ins reach a helper.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct RecordGroupBuiltins1D(ReadWriteBuffer<int> values) : IComputeShader
{
    public const int Stride = 4;

    public void Execute()
    {
        int i = ThreadIds.X * Stride;

        values[i + 0] = GroupSize.X;
        values[i + 1] = Count();
        values[i + 2] = GroupIds.Index;
        values[i + 3] = ThreadGroupCount.X;
    }

    private static int Count() => GroupSize.Count + (GroupIds.Index * 0);
}

/// <summary>
/// The sum of each block of 256 inputs, by a tree reduction in group-shared memory: every thread loads
/// one element, then the upper half of the live range is folded onto the lower half eight times.
/// Dispatched over a multiple of 256 threads; threads past <paramref name="count"/> load 0.
/// </summary>
[ThreadGroupSize(256)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlockSum(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> sums, int count) : IComputeShader
{
    public const int BlockSize = 256;

    [GroupShared]
    private static readonly float[] tile = new float[BlockSize];

    public void Execute()
    {
        int i = ThreadIds.X;
        int local = GroupIds.X;

        tile[local] = i < count ? input[i] : 0f;
        Gpu.GroupMemoryBarrierWithGroupSync();

        for (int half = tile.Length / 2; half > 0; half >>= 1)
        {
            if (local < half)
            {
                tile[local] += tile[local + half];
            }

            Gpu.GroupMemoryBarrierWithGroupSync();
        }

        if (local == 0)
        {
            sums[ThreadGroupIds.X] = tile[0];
        }
    }
}

/// <summary>
/// The same reduction dispatched over exactly as many threads as there are inputs: the last group is
/// partial, and only its first <see cref="GroupSize.Count"/> elements of the tile are ever written, so
/// the fold reads no element past them. Uses a helper that reaches the tile through a hidden pointer.
/// </summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct PartialGroupSum(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> sums) : IComputeShader
{
    [GroupShared]
    private static readonly int[] tile = new int[64];

    public void Execute()
    {
        int local = GroupIds.Index;
        int live = GroupSize.Count;

        tile[local] = input[ThreadIds.X];
        Gpu.GroupMemoryBarrierWithGroupSync();

        // A fixed number of rounds, so every thread of every group calls the same barriers.
        for (int half = tile.Length / 2; half > 0; half >>= 1)
        {
            Fold(local, half, live);
            Gpu.GroupMemoryBarrierWithGroupSync();
        }

        if (local == 0)
        {
            sums[ThreadGroupIds.X] = tile[0];
        }
    }

    private static void Fold(int local, int half, int live)
    {
        if (local < half && local + half < live)
        {
            tile[local] += tile[local + half];
        }
    }
}

/// <summary>
/// An exclusive prefix sum of each block of 256 inputs (Blelloch's work-efficient scan: an up-sweep
/// that builds partial sums in place, then a down-sweep that turns them into prefixes), plus each
/// block's total. Dispatched over a multiple of 256 threads.
/// </summary>
[ThreadGroupSize(256)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlockScan(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output, ReadWriteBuffer<int> totals, int count) : IComputeShader
{
    public const int BlockSize = 256;

    [GroupShared]
    private static readonly int[] tile = new int[BlockSize];

    public void Execute()
    {
        int i = ThreadIds.X;
        int local = GroupIds.X;

        tile[local] = i < count ? input[i] : 0;
        Gpu.GroupMemoryBarrierWithGroupSync();

        // Up-sweep: the last element of each 2·stride block gets the sum of the block.
        for (int stride = 1; stride < BlockSize; stride <<= 1)
        {
            if (((local + 1) & ((stride << 1) - 1)) == 0)
            {
                tile[local] += tile[local - stride];
            }

            Gpu.GroupMemoryBarrierWithGroupSync();
        }

        if (local == BlockSize - 1)
        {
            totals[ThreadGroupIds.X] = tile[local];
            tile[local] = 0;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        // Down-sweep: each block passes its prefix to its left half and prefix + left sum to its right half.
        for (int stride = BlockSize / 2; stride > 0; stride >>= 1)
        {
            if (((local + 1) & ((stride << 1) - 1)) == 0)
            {
                int left = tile[local - stride];
                tile[local - stride] = tile[local];
                tile[local] += left;
            }

            Gpu.GroupMemoryBarrierWithGroupSync();
        }

        if (i < count)
        {
            output[i] = tile[local];
        }
    }
}

/// <summary>
/// <c>c = a × b</c> for <paramref name="n"/> × <paramref name="n"/> matrices, one 16 × 16 tile of
/// <c>c</c> per threadgroup: for each tile along the shared dimension the group loads a tile of
/// <c>a</c> and one of <c>b</c> into shared memory, then every thread multiplies its row by its column.
/// Dispatched over a multiple of 16 in both dimensions; elements outside the matrices load 0.
/// </summary>
[ThreadGroupSize(16, 16)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct TiledMatMul(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c, int n) : IComputeShader
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
        int tiles = (n + Tile - 1) / Tile;
        float sum = 0f;

        for (int t = 0; t < tiles; t++)
        {
            int k = (t * Tile) + x;
            int r = (t * Tile) + y;

            tileA[(y * Tile) + x] = row < n && k < n ? a[(row * n) + k] : 0f;
            tileB[(y * Tile) + x] = r < n && column < n ? b[(r * n) + column] : 0f;
            Gpu.GroupMemoryBarrierWithGroupSync();

            for (int j = 0; j < Tile; j++)
            {
                sum += tileA[(y * Tile) + j] * tileB[(j * Tile) + x];
            }

            Gpu.GroupMemoryBarrierWithGroupSync();
        }

        if (row < n && column < n)
        {
            c[(row * n) + column] = sum;
        }
    }
}

/// <summary>A user struct as a group-shared element.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Sample
{
    public Float3 Position;
    public int Weight;
}

/// <summary>
/// Group-shared arrays of a packed vector and of a user struct: each thread writes its own element,
/// then reads its neighbour's (the next thread of the group, wrapping around).
/// </summary>
[ThreadGroupSize(8, 4)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct NeighbourExchange(ReadWriteBuffer<Float3> positions, ReadWriteBuffer<int> weights) : IComputeShader
{
    private const int Threads = 32;

    [GroupShared]
    private static readonly Float3[] vectors = new Float3[Threads];

    [GroupShared]
    private static readonly Sample[] samples = new Sample[Threads];

    public void Execute()
    {
        int local = GroupIds.Index;
        int i = (ThreadIds.Y * DispatchSize.X) + ThreadIds.X;

        vectors[local] = new Float3(ThreadIds.X, ThreadIds.Y, local);

        Sample sample;
        sample.Position = new Float3(i, 2f * i, -i);
        sample.Weight = i + 1;
        samples[local] = sample;
        samples[local].Weight += 1000;

        Gpu.AllMemoryBarrierWithGroupSync();

        int next = (local + 1) % Threads;
        positions[i] = vectors[next] + samples[next].Position;
        weights[i] = samples[next].Weight;
    }
}

/// <summary>
/// A barrier-heavy shader: every round each thread takes over its neighbour's value plus one, through a
/// second array, with a barrier after each of the two steps. After <paramref name="rounds"/> rounds
/// thread <c>j</c> holds <c>start[(j + rounds) % 32] + rounds</c>; a single missed barrier changes that.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct Rotate(ReadWriteBuffer<int> values, int rounds) : IComputeShader
{
    public const int Threads = 32;

    [GroupShared]
    private static readonly int[] front = new int[Threads];

    [GroupShared]
    private static readonly int[] back = new int[Threads];

    public void Execute()
    {
        int local = GroupIds.X;

        front[local] = values[ThreadIds.X];
        Gpu.GroupMemoryBarrierWithGroupSync();

        for (int round = 0; round < rounds; round++)
        {
            back[local] = front[(local + 1) % Threads] + 1;
            Gpu.GroupMemoryBarrierWithGroupSync();
            front[local] = back[local];
            Gpu.GroupMemoryBarrierWithGroupSync();
        }

        values[ThreadIds.X] = front[local];
    }
}

/// <summary>
/// Device-memory barriers and no group-shared memory: each thread writes its element of a buffer, and
/// after the barrier reads the element its neighbour in the group wrote.
/// </summary>
[ThreadGroupSize(16)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct DeviceBarrier(ReadWriteBuffer<int> scratch, ReadWriteBuffer<int> output) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int first = i - GroupIds.X;

        scratch[i] = (i * 3) + 1;
        Gpu.DeviceMemoryBarrierWithGroupSync();
        output[i] = scratch[first + ((GroupIds.X + 1) % GroupSize.Count)];
    }
}

/// <summary>A pixel shader with group-shared memory: every pixel shows the mean of its 8 × 8 tile's pattern.</summary>
[ThreadGroupSize(8, 8)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct TileMean : IComputeShader<Float4>
{
    [GroupShared]
    private static readonly float[] tile = new float[64];

    public Float4 Execute()
    {
        int local = GroupIds.Index;

        tile[local] = ((ThreadIds.X * 7) + (ThreadIds.Y * 13)) % 32 / 32f;
        Gpu.GroupMemoryBarrierWithGroupSync();

        float sum = 0f;

        for (int j = 0; j < GroupSize.Count; j++)
        {
            sum += tile[j];
        }

        float mean = sum / GroupSize.Count;
        return new Float4(mean, tile[local], 0f, 1f);
    }
}

/// <summary>The largest threadgroup a GPU accepts: 1024 threads, each of which gets a CPU thread in <c>ForCpu</c>.</summary>
[ThreadGroupSize(32, 32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct LargestGroup(ReadWriteBuffer<int> output) : IComputeShader
{
    [GroupShared]
    private static readonly int[] tile = new int[1024];

    public void Execute()
    {
        int local = GroupIds.Index;

        tile[local] = local;
        Gpu.GroupMemoryBarrierWithGroupSync();
        output[(ThreadIds.Y * DispatchSize.X) + ThreadIds.X] = tile[1023 - local];
    }
}

/// <summary>
/// Shared memory and an atomic in one shader: each group counts its odd values in its tile, and one
/// thread adds the group's count to a global counter with <see cref="Gpu.AtomicAdd(ref int, int)"/>.
/// </summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct CountOddPerGroup(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> total, int count) : IComputeShader
{
    [GroupShared]
    private static readonly int[] tile = new int[64];

    public void Execute()
    {
        int i = ThreadIds.X;

        tile[GroupIds.X] = i < count ? input[i] & 1 : 0;
        Gpu.GroupMemoryBarrierWithGroupSync();

        if (GroupIds.X == 0)
        {
            int sum = 0;

            for (int j = 0; j < tile.Length; j++)
            {
                sum += tile[j];
            }

            Gpu.AtomicAdd(ref total[0], sum);
        }
    }
}

// ---- CPU-only shaders: what they do is undefined or an error on the GPU ---------------------------------

/// <summary>
/// Threads that disagree about the barriers they call: odd threads return before the first one. That is
/// undefined on the GPU; the CPU's executor must still finish, with the even threads synchronizing
/// among themselves.
/// </summary>
[ThreadGroupSize(16)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct UnevenBarriers(ReadWriteBuffer<int> output) : IComputeShader
{
    [GroupShared]
    private static readonly int[] tile = new int[16];

    public void Execute()
    {
        int local = GroupIds.X;

        tile[local] = local;

        if ((local & 1) == 1)
        {
            output[ThreadIds.X] = -1;
            return;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();
        int sum = tile[local] + tile[(local + 2) % 16];
        Gpu.GroupMemoryBarrierWithGroupSync();
        output[ThreadIds.X] = sum;
    }
}

/// <summary>One thread indexes its buffer out of range (which the CPU checks) while the rest of its group waits at a barrier.</summary>
[ThreadGroupSize(16)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ThrowsBeforeBarrier(ReadWriteBuffer<int> output, int failingThread) : IComputeShader
{
    [GroupShared]
    private static readonly int[] tile = new int[16];

    public void Execute()
    {
        int i = ThreadIds.X;

        tile[GroupIds.X] = i == failingThread ? output[-1 - i] : i;
        Gpu.GroupMemoryBarrierWithGroupSync();
        output[i] = tile[GroupIds.X];
        Gpu.GroupMemoryBarrierWithGroupSync();
    }
}

/// <summary>More group-shared memory than any Apple GPU has: accepted by a hand-written descriptor, rejected when dispatched.</summary>
public readonly struct TooMuchThreadgroupMemory : IComputeShader, IComputeShaderDescriptor<TooMuchThreadgroupMemory>
{
    public static string EntryPoint => "TooMuch";

    public static string MslSource => "#include <metal_stdlib>\nkernel void TooMuch() { }\n";

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static int ThreadgroupMemoryLength => 1 << 20;

    public static void SetArguments(in TooMuchThreadgroupMemory shader, ref ShaderArgumentWriter writer)
    {
    }

    public void Execute()
    {
    }
}

/// <summary>A hand-written descriptor that calls a barrier without reporting <c>UsesThreadgroupMemory</c>.</summary>
public readonly struct UnreportedBarrier : IComputeShader, IComputeShaderDescriptor<UnreportedBarrier>
{
    public static string EntryPoint => "Unreported";

    public static string MslSource => "#include <metal_stdlib>\nkernel void Unreported() { }\n";

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in UnreportedBarrier shader, ref ShaderArgumentWriter writer)
    {
    }

    public void Execute() => Gpu.GroupMemoryBarrierWithGroupSync();
}
