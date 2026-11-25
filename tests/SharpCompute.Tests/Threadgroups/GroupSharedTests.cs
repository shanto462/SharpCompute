using System;
using System.Linq;
using static SharpCompute.Tests.CpuExecution.CpuComparison;

namespace SharpCompute.Tests.Threadgroups;

/// <summary>
/// Group-shared memory and barriers on the GPU: reductions, scans and tiled matrix products, at sizes
/// that are and are not multiples of the threadgroup size, checked against plain C#.
/// </summary>
public class GroupSharedTests
{
    internal static int RoundUp(int value, int multiple) => (value + multiple - 1) / multiple * multiple;

    internal static float[] Floats(int count, int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => (float)((random.NextDouble() * 2) - 1)).ToArray();
    }

    internal static int[] Ints(int count, int seed, int max = 100)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => random.Next(-max, max)).ToArray();
    }

    /// <summary>The per-block sums the tree reduction produces, folded in the same order (so floats match exactly).</summary>
    internal static float[] BlockSums(float[] input)
    {
        int blocks = RoundUp(input.Length, BlockSum.BlockSize) / BlockSum.BlockSize;
        float[] sums = new float[blocks];
        float[] tile = new float[BlockSum.BlockSize];

        for (int block = 0; block < blocks; block++)
        {
            for (int j = 0; j < tile.Length; j++)
            {
                int i = (block * tile.Length) + j;
                tile[j] = i < input.Length ? input[i] : 0f;
            }

            for (int half = tile.Length / 2; half > 0; half >>= 1)
            {
                for (int j = 0; j < half; j++)
                {
                    tile[j] += tile[j + half];
                }
            }

            sums[block] = tile[0];
        }

        return sums;
    }

    internal static float[] RunBlockSum(GraphicsDevice device, float[] input, Action<int, BlockSum> dispatch)
    {
        int threads = RoundUp(input.Length, BlockSum.BlockSize);
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<float> sums = device.AllocateReadWriteBuffer<float>(threads / BlockSum.BlockSize);

        dispatch(threads, new BlockSum(source, sums, input.Length));
        return sums.ToArray();
    }

    [MetalTheory]
    [InlineData(256)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(257)]
    [InlineData(1000)]
    [InlineData(100_003)]
    public void TreeReduction_SumsEveryBlock(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] input = Floats(count, seed: count);

        float[] sums = RunBlockSum(device, input, (threads, shader) => device.For(threads, shader));

        // The same additions in the same order: exact, not just close.
        Assert.Equal(BlockSums(input), sums);
        Assert.Equal(input.Sum(static v => (double)v), sums.Sum(static v => (double)v), 1e-2);
    }

    [MetalTheory]
    [InlineData(64)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(1000)]
    public void TreeReduction_OverAPartialLastGroup_OnlyReadsWhatItsThreadsWrote(int count)
    {
        // Dispatched over exactly `count` threads: the last group has fewer than 64, and the shader
        // folds only GroupSize.Count elements. Integers, so any stray element would show.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] input = Ints(count, seed: count);
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<int> sums = device.AllocateReadWriteBuffer<int>((count + 63) / 64);

        device.For(count, new PartialGroupSum(source, sums));

        int[] expected = input.Chunk(64).Select(static block => block.Sum()).ToArray();
        Assert.Equal(expected, sums.ToArray());
    }

    internal static (int[] Scan, int[] Totals) BlockScans(int[] input)
    {
        int[] scan = new int[input.Length];
        int[] totals = new int[RoundUp(input.Length, BlockScan.BlockSize) / BlockScan.BlockSize];

        for (int i = 0; i < input.Length; i++)
        {
            int block = i / BlockScan.BlockSize;
            scan[i] = totals[block];
            totals[block] += input[i];
        }

        return (scan, totals);
    }

    internal static (int[] Scan, int[] Totals) RunBlockScan(GraphicsDevice device, int[] input, Action<int, BlockScan> dispatch)
    {
        int threads = RoundUp(input.Length, BlockScan.BlockSize);
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(input.Length);
        using ReadWriteBuffer<int> totals = device.AllocateReadWriteBuffer<int>(threads / BlockScan.BlockSize);

        dispatch(threads, new BlockScan(source, output, totals, input.Length));
        return (output.ToArray(), totals.ToArray());
    }

    [MetalTheory]
    [InlineData(256)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(257)]
    [InlineData(1000)]
    [InlineData(100_003)]
    public void BlellochScan_IsTheExclusivePrefixSumOfEveryBlock(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] input = Ints(count, seed: count);

        (int[] scan, int[] totals) = RunBlockScan(device, input, (threads, shader) => device.For(threads, shader));

        (int[] expectedScan, int[] expectedTotals) = BlockScans(input);
        Assert.Equal(expectedScan, scan);
        Assert.Equal(expectedTotals, totals);
    }

    internal static float[] Multiply(float[] a, float[] b, int n)
    {
        float[] c = new float[n * n];

        for (int row = 0; row < n; row++)
        {
            for (int column = 0; column < n; column++)
            {
                double sum = 0;

                for (int k = 0; k < n; k++)
                {
                    sum += (double)a[(row * n) + k] * b[(k * n) + column];
                }

                c[(row * n) + column] = (float)sum;
            }
        }

        return c;
    }

    internal static float[] RunMatMul(GraphicsDevice device, float[] a, float[] b, int n, Action<int, TiledMatMul> dispatch)
    {
        using ReadOnlyBuffer<float> bufferA = device.AllocateReadOnlyBuffer(a);
        using ReadOnlyBuffer<float> bufferB = device.AllocateReadOnlyBuffer(b);
        using ReadWriteBuffer<float> bufferC = device.AllocateReadWriteBuffer<float>(n * n);

        dispatch(RoundUp(n, TiledMatMul.Tile), new TiledMatMul(bufferA, bufferB, bufferC, n));
        return bufferC.ToArray();
    }

    [MetalTheory]
    [InlineData(16)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(17)]
    [InlineData(40)]
    [InlineData(100)]
    public void TiledMatrixProduct_MatchesTheTripleLoop(int n)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] a = Floats(n * n, seed: n);
        float[] b = Floats(n * n, seed: n + 1);

        float[] c = RunMatMul(device, a, b, n, (size, shader) => device.For(size, size, shader));

        AssertClose(Multiply(a, b, n), c, relative: 1e-4f, absolute: 1e-4f);
    }

    [MetalTheory]
    [InlineData(8, 4)]
    [InlineData(16, 12)]
    [InlineData(24, 4)]
    public void VectorsAndUserStructs_AreSharedBetweenTheThreadsOfAGroup(int x, int y)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float3> positions = device.AllocateReadWriteBuffer<Float3>(x * y);
        using ReadWriteBuffer<int> weights = device.AllocateReadWriteBuffer<int>(x * y);

        device.For(x, y, new NeighbourExchange(positions, weights));

        (Float3[] expectedPositions, int[] expectedWeights) = ExpectedExchange(x, y);
        Assert.Equal(Flatten(expectedPositions), Flatten(positions.ToArray()));
        Assert.Equal(expectedWeights, weights.ToArray());
    }

    /// <summary>What <see cref="NeighbourExchange"/> computes: every thread reports what the next thread of its 8 × 4 group stored.</summary>
    internal static (Float3[] Positions, int[] Weights) ExpectedExchange(int x, int y)
    {
        Float3[] positions = new Float3[x * y];
        int[] weights = new int[x * y];

        for (int ty = 0; ty < y; ty++)
        {
            for (int tx = 0; tx < x; tx++)
            {
                int next = ((ty % 4 * 8) + (tx % 8) + 1) % 32;
                int nx = (tx / 8 * 8) + (next % 8);
                int ny = (ty / 4 * 4) + (next / 8);
                int ni = (ny * x) + nx;

                positions[(ty * x) + tx] = new Float3(nx + ni, ny + (2f * ni), next - ni);
                weights[(ty * x) + tx] = ni + 1001;
            }
        }

        return (positions, weights);
    }

    internal static int[] ExpectedRotation(int[] start, int rounds) =>
        start.Select((_, i) => start[(i / Rotate.Threads * Rotate.Threads) + (((i % Rotate.Threads) + rounds) % Rotate.Threads)] + rounds).ToArray();

    [MetalTheory]
    [InlineData(32, 1)]
    [InlineData(320, 33)]
    [InlineData(3200, 200)]
    public void ManyBarriers_KeepEveryRoundInStep(int count, int rounds)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] start = Ints(count, seed: rounds);
        using ReadWriteBuffer<int> values = device.AllocateReadWriteBuffer(start);

        device.For(count, new Rotate(values, rounds));

        Assert.Equal(ExpectedRotation(start, rounds), values.ToArray());
    }

    internal static int[] ExpectedDeviceBarrier(int count) =>
        Enumerable.Range(0, count).Select(i =>
        {
            int first = i / 16 * 16;
            int extent = Math.Min(16, count - first);
            return ((first + (((i % 16) + 1) % extent)) * 3) + 1;
        }).ToArray();

    [MetalTheory]
    [InlineData(16)]
    [InlineData(100)]
    [InlineData(4099)]
    public void DeviceMemoryBarrier_OrdersBufferWritesWithinAGroup(int count)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> scratch = device.AllocateReadWriteBuffer<int>(count);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(count);

        device.For(count, new DeviceBarrier(scratch, output));

        Assert.Equal(ExpectedDeviceBarrier(count), output.ToArray());
    }

    [MetalFact]
    public void PixelShader_CanUseGroupSharedMemory()
    {
        // 20 × 12: partial 8 × 8 tiles on the right and at the bottom.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(20, 12);

        device.ForEach(texture, default(TileMean));

        Rgba32[] pixels = texture.ToArray();
        AssertPixelsClose(ExpectedTileMean(20, 12), pixels, maxStep: 1, outlierFraction: 0, "TileMean on the GPU");
    }

    internal static Rgba32[] ExpectedTileMean(int width, int height)
    {
        static float Pattern(int x, int y) => ((x * 7) + (y * 13)) % 32 / 32f;
        Rgba32[] pixels = new Rgba32[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int x0 = x / 8 * 8, y0 = y / 8 * 8;
                int x1 = Math.Min(width, x0 + 8), y1 = Math.Min(height, y0 + 8);
                float sum = 0;

                for (int ty = y0; ty < y1; ty++)
                {
                    for (int tx = x0; tx < x1; tx++)
                    {
                        sum += Pattern(tx, ty);
                    }
                }

                float mean = sum / ((x1 - x0) * (y1 - y0));
                pixels[(y * width) + x] = new Rgba32(CpuDispatcher.ToUnorm8(mean), CpuDispatcher.ToUnorm8(Pattern(x, y)), 0, 255);
            }
        }

        return pixels;
    }

    [MetalFact]
    public void LargestThreadgroup_Runs()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(64 * 32);

        device.For(64, 32, new LargestGroup(output));

        Assert.Equal(ExpectedLargestGroup(64, 32), output.ToArray());
    }

    internal static int[] ExpectedLargestGroup(int x, int y) =>
        Enumerable.Range(0, x * y).Select(i => 1023 - (((i / x) % 32 * 32) + (i % x % 32))).ToArray();

    [MetalFact]
    public void MoreThreadgroupMemoryThanTheDeviceHas_IsRejectedBeforeCompiling()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.For(1, default(TooMuchThreadgroupMemory)));

        Assert.Contains("1048576 bytes of threadgroup", exception.Message);
        Assert.Contains(device.MaxThreadgroupMemoryLength.ToString(), exception.Message);
        Assert.Contains(nameof(TooMuchThreadgroupMemory), exception.Message);
    }

    [MetalFact]
    public void TheDeviceLimit_IsWhatTheGeneratorAssumes()
    {
        // SCG0043 rejects more than 32,768 bytes at compile time; a device with less would fail at dispatch instead.
        Assert.True(GraphicsDevice.GetDefault().MaxThreadgroupMemoryLength >= 32768);
    }
}
