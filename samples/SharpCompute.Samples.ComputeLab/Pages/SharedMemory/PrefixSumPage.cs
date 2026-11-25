// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages.SharedMemory;

/// <summary>
/// A prefix sum, used for what it is best at: stream compaction that keeps the order. Of a million
/// elements, the ones that pass a test get a flag of 1; the exclusive prefix sum of the flags is, for
/// every kept element, the number of kept elements before it, which is exactly its slot in the output.
/// </summary>
/// <remarks>
/// The scan is Blelloch's work-efficient one, in group-shared memory: each threadgroup of 256 loads 256
/// flags, sweeps up (partial sums in place) and down (prefixes), with a barrier between the rounds, and
/// stores its total. The totals are scanned the same way (4,096, then 16), and each level's prefixes
/// are added back to the level below. Unlike a compaction through an atomic counter, the output is in
/// index order, the same on every run.
/// </remarks>
public sealed class PrefixSumPage : ILabPage
{
    /// <summary>The field is <see cref="Size"/> × <see cref="Size"/>: 2^20 elements.</summary>
    public const int Size = 1024;
    public const int Count = Size * Size;

    private static readonly string[] s_variants = ["1,048,576 ints, 256 per threadgroup"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<uint> _source;
    private readonly ReadWriteBuffer<int> _flags;
    private readonly ReadWriteBuffer<int> _offsets;
    private readonly ReadWriteBuffer<uint> _output;
    private readonly GpuScan _scan;
    private readonly int[] _cpuOffsets = new int[Count];

    public PrefixSumPage(GraphicsDevice device)
    {
        _device = device;
        _source = device.AllocateReadWriteBuffer<uint>(Count);
        _flags = device.AllocateReadWriteBuffer<int>(Count);
        _offsets = device.AllocateReadWriteBuffer<int>(Count);
        _output = device.AllocateReadWriteBuffer<uint>(Count);
        _scan = new GpuScan(device, Count);
    }

    public string Name => "PrefixSum";

    public string Feature => "Blelloch scan";


    public string Topic => LabTopics.SharedMemory;


    /// <summary>The check's C# reference runs on one core, as its summary says.</summary>

    public string CpuNote => "one core";

    public string Help => "Up/Down threshold";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>Elements with a value at or above this (of 65,535) are kept.</summary>
    public uint Cutoff { get; private set; } = 36000;

    /// <summary>How many elements the GPU kept in the last <see cref="Compute"/>: the total of the scan.</summary>
    public int Kept { get; private set; }

    public void Compute(LabFrame frame)
    {
        _device.For(Size, Size, new GenerateScanField(_source, _flags, Size, frame.Time, Cutoff));

        long start = Stopwatch.GetTimestamp();
        Kept = _scan.Run(_flags, _offsets, Count);
        GpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // Every kept element knows its slot; no two share one, so the scatter needs no atomics.
        _device.For(Count, new ScatterKept(_flags, _offsets, _output));
    }

    public LabCheck Check()
    {
        ReadOnlySpan<int> flags = _flags.AsReadOnlySpan();
        Span<int> expected = _cpuOffsets;

        long start = Stopwatch.GetTimestamp();
        int total = CpuScan.Exclusive(flags, expected);
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        bool scanMatches = Kept == total && _offsets.AsReadOnlySpan().SequenceEqual(expected);

        // The compaction, the plain way: the indices that pass, in index order.
        ReadOnlySpan<uint> source = _source.AsReadOnlySpan();
        ReadOnlySpan<uint> output = _output.AsReadOnlySpan();
        bool inOrder = scanMatches;
        int slot = 0;

        for (int i = 0; i < source.Length && inOrder; i++)
        {
            if (source[i] >= Cutoff)
            {
                inOrder = slot < Kept && output[slot++] == (uint)i;
            }
        }

        inOrder &= slot == Kept;

        string summary = !scanMatches
            ? Invariant($"the scan differs from the C# loop (GPU total {Kept:N0}, C# {total:N0})")
            : !inOrder
                ? Invariant($"the scan matches, but the {Kept:N0} kept elements are not the C# ones in order")
                : Invariant($"all {Count:N0} prefix sums = the C# loop; kept {Kept:N0}, in index order (CPU: one core)");

        return new LabCheck(scanMatches && inOrder, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawPrefixSum(canvas.Pixels, _source, _offsets, _output, LabCanvas.Width, Size, Kept, Cutoff));

        int labelY = DrawPrefixSum.PanelY - 24;
        int below = DrawPrefixSum.PanelY + DrawPrefixSum.PanelSize + 8;
        canvas.Label(DrawPrefixSum.PanelX(0), labelY, "1. Input: 1M values");
        canvas.Label(DrawPrefixSum.PanelX(0), below, Invariant($"gold: kept (value >= {Cutoff * 100 / 65535}%)"), TextColor.Accent);
        canvas.Label(DrawPrefixSum.PanelX(1), labelY, "2. Prefix sum of the flags");
        canvas.Label(DrawPrefixSum.PanelX(1), below, "= each kept element's slot", TextColor.Cool);
        canvas.Label(DrawPrefixSum.PanelX(2), labelY, "3. Output slots, in order");
        canvas.Label(DrawPrefixSum.PanelX(2), below, "color: where the slot came from", TextColor.Dim);

        int text = below + 40;
        canvas.Label(DrawPrefixSum.PanelX(0), text, Invariant($"Kept {Kept:N0} of {Count:N0} elements. Each threadgroup scans 256 flags in shared memory:"), TextColor.White);
        canvas.Label(DrawPrefixSum.PanelX(0) + (4 * LabCanvas.CellWidth), text + 16, "up-sweep, barrier, down-sweep, barrier ... then: if (flag) output[offsets[i]] = i;", TextColor.Cool);
        canvas.Label(DrawPrefixSum.PanelX(0), text + 48, "The slots come out in index order, the same on every run: panel 3 is a smooth gradient.", TextColor.Accent);
        canvas.Label(DrawPrefixSum.PanelX(0), text + 64, "(A compaction through an atomic counter is simpler, but hands slots out in scheduling order.)", TextColor.Dim);
    }

    public bool OnKeyDown(Key key)
    {
        if (key is not (Key.Up or Key.Down))
        {
            return false;
        }

        Cutoff = (uint)Math.Clamp((int)Cutoff + (key == Key.Up ? 3000 : -3000), 6000, 60000);
        return true;
    }

    public void Dispose()
    {
        _source.Dispose();
        _flags.Dispose();
        _offsets.Dispose();
        _output.Dispose();
        _scan.Dispose();
    }
}

/// <summary>
/// An exclusive prefix sum of an int buffer on the GPU. Each pass scans blocks of 256 in group-shared
/// memory and stores every block's total; the totals are scanned by the next pass, and so on until one
/// block is left; then each level's prefixes are added to the blocks of the level below. Works for any
/// count. Not thread-safe; owns its intermediate buffers.
/// </summary>
public sealed class GpuScan : IDisposable
{
    public const int BlockSize = 256;

    private readonly GraphicsDevice _device;

    // _totals[k] holds the block totals of level k (the input of level k + 1); _scanned[k] the scan of _totals[k - 1].
    private readonly List<ReadWriteBuffer<int>> _totals = [];
    private readonly List<ReadWriteBuffer<int>> _scanned = [];

    /// <param name="device">The device the buffers to scan are allocated from.</param>
    /// <param name="capacity">The largest count <see cref="Run"/> will be called with.</param>
    public GpuScan(GraphicsDevice device, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _device = device;
        Capacity = capacity;

        for (int size = Blocks(capacity); ; size = Blocks(size))
        {
            _totals.Add(device.AllocateReadWriteBuffer<int>(size));
            _scanned.Add(device.AllocateReadWriteBuffer<int>(size));

            if (size == 1)
            {
                break;
            }
        }
    }

    public int Capacity { get; }

    private static int Blocks(int count) => (count + BlockSize - 1) / BlockSize;

    /// <summary>
    /// Writes the exclusive prefix sum of the first <paramref name="count"/> elements of
    /// <paramref name="input"/> to <paramref name="output"/> (which may not be the same buffer) and
    /// returns their total.
    /// </summary>
    public int Run(ReadWriteBuffer<int> input, ReadWriteBuffer<int> output, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Capacity);

        if (count <= 0)
        {
            return 0;
        }

        // Down: scan each level's blocks, until a level is one block.
        List<(ReadWriteBuffer<int> Scan, int Count)> levels = [];
        (ReadWriteBuffer<int> source, ReadWriteBuffer<int> scan, int n) = (input, output, count);

        for (int level = 0; ; level++)
        {
            int blocks = Blocks(n);
            _device.For(blocks * BlockSize, new ScanBlocks(source, scan, _totals[level], n));
            levels.Add((scan, n));

            if (blocks == 1)
            {
                break;
            }

            (source, scan, n) = (_totals[level], _scanned[level], blocks);
        }

        // Up: a level's scan is, per block of the level below, the sum of everything before that block.
        for (int level = levels.Count - 1; level > 0; level--)
        {
            _device.For(levels[level - 1].Count, new AddBlockOffsets(levels[level - 1].Scan, levels[level].Scan));
        }

        return _totals[levels.Count - 1][0];
    }

    public void Dispose()
    {
        foreach (ReadWriteBuffer<int> buffer in _totals)
        {
            buffer.Dispose();
        }

        foreach (ReadWriteBuffer<int> buffer in _scanned)
        {
            buffer.Dispose();
        }
    }
}

/// <summary>The prefix sum in plain C#.</summary>
public static class CpuScan
{
    /// <summary>Writes the exclusive prefix sum of <paramref name="input"/> to <paramref name="output"/> and returns the total.</summary>
    public static int Exclusive(ReadOnlySpan<int> input, Span<int> output)
    {
        int sum = 0;

        for (int i = 0; i < input.Length; i++)
        {
            output[i] = sum;
            sum += input[i];
        }

        return sum;
    }
}

/// <summary>The input: a smooth animated field with grain, as values 0..65,535, and the flag of every element that passes the cutoff.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GenerateScanField(ReadWriteBuffer<uint> source, ReadWriteBuffer<int> flags, int size, float time, uint cutoff) : IComputeShader
{
    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float2 uv = new Float2(x, y) / size;

        // Interfering waves around two wandering centers.
        Float2 c1 = new Float2(0.5f + (0.35f * Gpu.Cos(time * 0.31f)), 0.5f + (0.35f * Gpu.Sin(time * 0.43f)));
        Float2 c2 = new Float2(0.5f - (0.3f * Gpu.Sin(time * 0.27f)), 0.5f + (0.3f * Gpu.Cos(time * 0.19f)));
        float waves = Gpu.Sin((Gpu.Length(uv - c1) * 38f) - (time * 1.7f)) + Gpu.Sin((Gpu.Length(uv - c2) * 27f) + (time * 1.1f));
        float grain = (Hash((uint)x + (Hash((uint)y) * 31u)) >> 8) * (1f / 16777216f);
        uint value = (uint)(Gpu.Saturate(0.5f + (0.22f * waves) + ((grain - 0.5f) * 0.18f)) * 65535f);
        int i = (y * size) + x;

        source[i] = value;
        flags[i] = value >= cutoff ? 1 : 0;
    }
}

/// <summary>
/// The feature kernel: the exclusive prefix sum of each block of 256 elements, in group-shared memory,
/// and each block's total. The up-sweep leaves the sum of every 2, 4, ... 256 elements in the last
/// element of that span; the down-sweep then hands every span the sum of everything before it. Each
/// round is one barrier apart from the next, and touches half as many (then twice as many) elements.
/// </summary>
/// <remarks>
/// Dispatched over a multiple of 256 threads; threads past <paramref name="count"/> load 0. Every
/// thread calls every barrier: the conditions are around the work, never around a barrier.
/// </remarks>
[ThreadGroupSize(GpuScan.BlockSize)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ScanBlocks(ReadWriteBuffer<int> input, ReadWriteBuffer<int> output, ReadWriteBuffer<int> totals, int count) : IComputeShader
{
    [GroupShared]
    private static readonly int[] tile = new int[GpuScan.BlockSize];

    public void Execute()
    {
        int i = ThreadIds.X;
        int local = GroupIds.X;

        tile[local] = i < count ? input[i] : 0;
        Gpu.GroupMemoryBarrierWithGroupSync();

        // Up-sweep: the last element of every span of 2 * stride gets the span's sum.
        for (int stride = 1; stride < GpuScan.BlockSize; stride <<= 1)
        {
            if (((local + 1) & ((stride << 1) - 1)) == 0)
            {
                tile[local] += tile[local - stride];
            }

            Gpu.GroupMemoryBarrierWithGroupSync();
        }

        if (local == GpuScan.BlockSize - 1)
        {
            totals[ThreadGroupIds.X] = tile[local];
            tile[local] = 0;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        // Down-sweep: a span passes its prefix to its left half, and prefix + left half's sum to its right half.
        for (int stride = GpuScan.BlockSize / 2; stride > 0; stride >>= 1)
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

/// <summary>Adds to every element the prefix of its block: <paramref name="blockOffsets"/> is the scan of the block totals.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AddBlockOffsets(ReadWriteBuffer<int> scan, ReadWriteBuffer<int> blockOffsets) : IComputeShader
{
    public void Execute() => scan[ThreadIds.X] += blockOffsets[ThreadIds.X / GpuScan.BlockSize];
}

/// <summary>The compaction: every kept element writes its index to its slot. The slots are distinct, so no two threads write the same element.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ScatterKept(ReadWriteBuffer<int> flags, ReadWriteBuffer<int> offsets, ReadWriteBuffer<uint> output) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        if (flags[i] != 0)
        {
            output[offsets[i]] = (uint)i;
        }
    }
}

/// <summary>Draws the page's three panels.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawPrefixSum(
    ReadWriteBuffer<uint> pixels, ReadWriteBuffer<uint> source, ReadWriteBuffer<int> offsets, ReadWriteBuffer<uint> output, int width, int size, int kept, uint cutoff) : IComputeShader
{
    public const int PanelY = 104;
    public const int PanelSize = 384;
    public const int PanelPitch = 416;
    public const int FirstPanelX = 32;

    /// <summary>The left edge of panel 0, 1 or 2.</summary>
    public static int PanelX(int panel) => FirstPanelX + (panel * PanelPitch);

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int py = y - PanelY;
        int px = x - FirstPanelX;

        if (py >= 0 && py < PanelSize && px >= 0)
        {
            int panel = px / PanelPitch;
            int u = px % PanelPitch;

            if (panel < 3 && u < PanelSize)
            {
                // The panel shows the field at 3/8 scale: element (u, v) * 8 / 3.
                int element = ((py * 8 / 3) * size) + (u * 8 / 3);
                bool keep = source[element] >= cutoff;

                if (panel == 0)
                {
                    float value = source[element] * (1f / 65535f);
                    color = keep ? new Float3(1f, 0.62f + (0.3f * value), 0.12f) * (0.45f + (0.55f * value)) : new Float3(value * 0.55f);
                }
                else if (panel == 1)
                {
                    // The running count of kept elements, from 0 at the top left to `kept` at the
                    // bottom right; brighter where the element itself is kept.
                    float t = offsets[element] / (float)Gpu.Max(1, kept);
                    Float3 ramp = Gpu.Lerp(Gpu.Lerp(new Float3(0.05f, 0.1f, 0.45f), new Float3(0.1f, 0.75f, 0.7f), Gpu.Saturate(t * 2f)), new Float3(1f, 0.9f, 0.45f), Gpu.Saturate((t * 2f) - 1f));
                    color = ramp * (keep ? 1f : 0.55f);
                }
                else
                {
                    // Every stride-th slot, row by row, colored by the position its element came from.
                    int stride = Gpu.Max(1, kept / (PanelSize * PanelSize));
                    int slot = ((py * PanelSize) + u) * stride;

                    if (slot < kept)
                    {
                        int index = (int)output[slot];
                        float sx = (index % size) / (float)size;
                        float sy = (index / size) / (float)size;
                        color = new Float3(0.15f + (0.85f * sy), 0.25f + (0.5f * sx), 1f - (0.8f * sy));
                    }
                    else
                    {
                        color = new Float3(0.03f, 0.04f, 0.06f);
                    }
                }
            }
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
