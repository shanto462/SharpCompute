// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages.SharedMemory;

/// <summary>
/// A reduction: the sum of 16.7 million floats. Every threadgroup of 256 threads loads 256 values into
/// group-shared memory and folds them in a tree, eight rounds with a barrier after each, so one
/// dispatch turns 16,777,216 values into 65,536 block sums, a second into 256 and a third into one.
/// </summary>
/// <remarks>
/// The second variant does the same three passes without shared memory: one thread per block adds its
/// 256 values in a loop. Which of the two is faster depends on the GPU; the page shows both times.
/// What the tree always wins is accuracy: adding pairs keeps the operands of every addition similar in
/// size, while a running float total of millions of values drops most of each small addend's bits.
/// </remarks>
public sealed class ReductionPage : ILabPage
{
    /// <summary>The values are a <see cref="Side"/> × <see cref="Side"/> field: 2^24 floats.</summary>
    public const int Side = 4096;
    public const int Count = Side * Side;

    /// <summary>In the window, the variant that is not shown is run every this many frames, to keep its time current.</summary>
    private const int OtherVariantInterval = 90;

    private static readonly string[] s_variants = ["tree reduction in shared memory", "serial loop per thread, no shared memory"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<float> _values;
    private readonly GpuSum _sum;
    private readonly double[] _variantMilliseconds = new double[2];
    private readonly float[] _cpuValues = new float[Count];
    private int _computes;

    public ReductionPage(GraphicsDevice device)
    {
        _device = device;
        _values = device.AllocateReadWriteBuffer<float>(Count);
        _sum = new GpuSum(device, Count);
    }

    public string Name => "Reduction";

    public string Feature => "tree sum";


    public string Topic => LabTopics.SharedMemory;

    public string Help => "V variant";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>The GPU's sum of the last <see cref="Compute"/>.</summary>
    public float Total { get; private set; }

    /// <summary>The double-precision sum, the float tree in C# and a running float total, from the last <see cref="Check"/>.</summary>
    public double Reference { get; private set; }

    public float RunningFloatTotal { get; private set; }

    public void Compute(LabFrame frame)
    {
        _device.For(Side, Side, new GenerateValues(_values, Side, frame.Time));

        // The other variant now and then, for the side-by-side times in the window.
        if (!frame.Deterministic && _computes++ % OtherVariantInterval == 0)
        {
            long other = Stopwatch.GetTimestamp();
            _sum.Run(_values, Count, shared: Variant != 0);
            _variantMilliseconds[1 - Variant] = Stopwatch.GetElapsedTime(other).TotalMilliseconds;
        }

        long start = Stopwatch.GetTimestamp();
        Total = _sum.Run(_values, Count, shared: Variant == 0);
        GpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _variantMilliseconds[Variant] = GpuMilliseconds;
    }

    public LabCheck Check()
    {
        float[] values = _cpuValues;
        _values.CopyTo(values);

        long start = Stopwatch.GetTimestamp();
        double reference = CpuSum.Double(values);
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // The same additions in the same order as the kernels, in C#: floats, so it must match to the bit.
        float sameOrder = Variant == 0 ? CpuSum.Tree(values) : CpuSum.SerialBlocks(values);
        float running = CpuSum.RunningFloat(values);

        Reference = reference;
        RunningFloatTotal = running;

        double error = Math.Abs(Total - reference) / Math.Max(1, Math.Abs(reference));
        bool exact = Total.Equals(sameOrder);
        bool passed = exact && error < 1e-5;

        string summary = !exact
            ? Invariant($"GPU {Total:R} differs from the same additions in C# ({sameOrder:R})")
            : Invariant($"sum {Total:F1} = the same adds in C#; {error * 1e6:F2} ppm off the double sum (CPU: all cores)");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawReduction(canvas.Pixels, _values, _sum.Level(2), LabCanvas.Width, Side));

        int labelY = DrawReduction.PanelY - 24;
        int below = DrawReduction.PanelY + DrawReduction.PanelSize + 8;
        int textX = DrawReduction.BarsX + DrawReduction.BarsWidth + 32;

        canvas.Label(DrawReduction.PanelX, labelY, "Input: 4096 x 4096 floats");
        canvas.Label(DrawReduction.BarsX, labelY, "After two passes: 256 sums");
        canvas.Label(DrawReduction.BarsX, below, "each bar = 16 rows = 65,536 values", TextColor.Accent);
        canvas.Label(DrawReduction.PanelX, below, "16,777,216 -> 65,536 -> 256 -> 1", TextColor.Cool);

        int y = DrawReduction.PanelY;
        canvas.Label(textX, y, "In every threadgroup of 256:", TextColor.White);
        canvas.Label(textX, y + 32, "tile[j] = values[i];", TextColor.Cool);
        canvas.Label(textX, y + 48, "barrier();", TextColor.Cool);
        canvas.Label(textX, y + 64, "for (half = 128; half > 0; half /= 2)", TextColor.Cool);
        canvas.Label(textX, y + 80, "  if (j < half)", TextColor.Cool);
        canvas.Label(textX, y + 96, "    tile[j] += tile[j + half];", TextColor.Cool);
        canvas.Label(textX, y + 112, "  barrier();", TextColor.Cool);
        canvas.Label(textX, y + 128, "if (j == 0) sums[group] = tile[0];", TextColor.Cool);

        canvas.Label(textX, y + 176, Invariant($"GPU sum      {Total,16:F1}"), TextColor.White);

        if (Reference != 0)
        {
            canvas.Label(textX, y + 192, Invariant($"double sum   {Reference,16:F1}"), TextColor.Dim);
            canvas.Label(textX, y + 208, Invariant($"GPU error    {Total - Reference,16:F1}"), TextColor.Good);
            canvas.Label(textX, y + 240, "One running float total instead:", TextColor.Dim);
            canvas.Label(textX, y + 256, Invariant($"float loop   {RunningFloatTotal,16:F1}"), TextColor.Dim);
            canvas.Label(textX, y + 272, Invariant($"its error    {RunningFloatTotal - Reference,16:F1}"), TextColor.Bad);
        }

        canvas.Label(textX, y + 320, "A tree adds numbers of similar size,", TextColor.Accent);
        canvas.Label(textX, y + 336, "so it rounds far less than a loop.", TextColor.Accent);

        if (!frame.Deterministic && _variantMilliseconds[0] > 0 && _variantMilliseconds[1] > 0)
        {
            canvas.Label(textX, y + 384, Invariant($"shared-memory tree   {_variantMilliseconds[0],7:F2} ms"), Variant == 0 ? TextColor.White : TextColor.Dim);
            canvas.Label(textX, y + 400, Invariant($"serial loop / thread {_variantMilliseconds[1],7:F2} ms"), Variant == 1 ? TextColor.White : TextColor.Dim);
        }
    }

    public bool OnKeyDown(Key key)
    {
        if (key != Key.V)
        {
            return false;
        }

        Variant = 1 - Variant;
        return true;
    }

    public void Dispose()
    {
        _values.Dispose();
        _sum.Dispose();
    }
}

/// <summary>
/// The sum of a float buffer on the GPU, by repeated passes that each turn 256 values into one, until
/// one is left. Works for any count: a pass is dispatched over whole threadgroups and the threads past
/// the end add 0. Not thread-safe; owns its intermediate buffers.
/// </summary>
public sealed class GpuSum : IDisposable
{
    public const int BlockSize = 256;

    private readonly GraphicsDevice _device;
    private readonly List<ReadWriteBuffer<float>> _levels = [];

    /// <param name="device">The device the buffers to sum are allocated from.</param>
    /// <param name="capacity">The largest count <see cref="Run"/> will be called with.</param>
    public GpuSum(GraphicsDevice device, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _device = device;
        Capacity = capacity;

        // Level 1 holds a sum per block of the input, level 2 a sum per block of level 1, ... down to one value.
        for (int size = Blocks(capacity); ; size = Blocks(size))
        {
            _levels.Add(device.AllocateReadWriteBuffer<float>(size));

            if (size == 1)
            {
                break;
            }
        }
    }

    public int Capacity { get; }

    /// <summary>The number of passes a sum of <see cref="Capacity"/> values takes.</summary>
    public int Passes => _levels.Count;

    /// <summary>The sums after pass <paramref name="pass"/> (1-based) of the last <see cref="Run"/> over <see cref="Capacity"/> values.</summary>
    public ReadWriteBuffer<float> Level(int pass) => _levels[Math.Clamp(pass, 1, _levels.Count) - 1];

    private static int Blocks(int count) => (count + BlockSize - 1) / BlockSize;

    /// <summary>
    /// Sums the first <paramref name="count"/> elements of <paramref name="values"/>.
    /// <paramref name="shared"/> picks the kernel: a tree in group-shared memory, or one thread per
    /// block that adds its 256 values in a loop.
    /// </summary>
    public float Run(ReadWriteBuffer<float> values, int count, bool shared)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Capacity);

        if (count <= 0)
        {
            return 0f;
        }

        ReadWriteBuffer<float> input = values;

        for (int level = 0; ; level++)
        {
            int blocks = Blocks(count);
            ReadWriteBuffer<float> output = _levels[level];

            if (shared)
            {
                _device.For(blocks * BlockSize, new SumBlocksShared(input, output, count));
            }
            else
            {
                _device.For(blocks, new SumBlocksSerial(input, output, count));
            }

            if (blocks == 1)
            {
                return output[0];
            }

            (input, count) = (output, blocks);
        }
    }

    public void Dispose()
    {
        foreach (ReadWriteBuffer<float> level in _levels)
        {
            level.Dispose();
        }
    }
}

/// <summary>The reduction in plain C#: the reference, and the kernels' additions in the kernels' order.</summary>
public static class CpuSum
{
    private const int BlockSize = GpuSum.BlockSize;

    /// <summary>
    /// The sum in double precision, on all cores. The chunks are fixed (not whatever the scheduler
    /// picks) and added up in order, so the result is the same on every run.
    /// </summary>
    public static double Double(float[] values)
    {
        const int Chunk = 1 << 16;
        double[] partial = new double[(values.Length + Chunk - 1) / Chunk];

        Parallel.For(0, partial.Length, chunk =>
        {
            double sum = 0;

            foreach (float value in values.AsSpan(chunk * Chunk, Math.Min(Chunk, values.Length - (chunk * Chunk))))
            {
                sum += value;
            }

            partial[chunk] = sum;
        });

        double total = 0;

        foreach (double sum in partial)
        {
            total += sum;
        }

        return total;
    }

    /// <summary>One float accumulator over all values: what a naive loop computes.</summary>
    public static float RunningFloat(ReadOnlySpan<float> values)
    {
        float sum = 0;

        foreach (float value in values)
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>The additions of <see cref="SumBlocksShared"/>, pass after pass: bit for bit what the GPU computes.</summary>
    public static float Tree(float[] values) => Reduce(values, static (block, tile) =>
    {
        tile.Clear();
        block.CopyTo(tile);

        for (int half = BlockSize / 2; half > 0; half >>= 1)
        {
            for (int j = 0; j < half; j++)
            {
                tile[j] += tile[j + half];
            }
        }

        return tile[0];
    });

    /// <summary>The additions of <see cref="SumBlocksSerial"/>, pass after pass.</summary>
    public static float SerialBlocks(float[] values) => Reduce(values, static (block, _) => RunningFloat(block));

    private delegate float BlockSum(ReadOnlySpan<float> block, Span<float> scratch);

    private static float Reduce(float[] values, BlockSum sum)
    {
        if (values.Length == 0)
        {
            return 0f;
        }

        float[] input = values;

        while (true)
        {
            float[] source = input;
            float[] output = new float[(source.Length + BlockSize - 1) / BlockSize];

            Parallel.For(
                0,
                output.Length,
                static () => new float[BlockSize],
                (block, _, scratch) =>
                {
                    int start = block * BlockSize;
                    output[block] = sum(source.AsSpan(start, Math.Min(BlockSize, source.Length - start)), scratch);
                    return scratch;
                },
                static _ => { });

            if (output.Length == 1)
            {
                return output[0];
            }

            input = output;
        }
    }
}

/// <summary>The input: a smooth animated field with grain, as floats in 0..1.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GenerateValues(ReadWriteBuffer<float> values, int side, float time) : IComputeShader
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
        Float2 uv = new Float2(x, y) / side;

        Float2 c1 = new Float2(0.5f + (0.3f * Gpu.Cos(time * 0.37f)), 0.5f + (0.3f * Gpu.Sin(time * 0.29f)));
        Float2 c2 = new Float2(0.5f - (0.35f * Gpu.Sin(time * 0.23f)), 0.5f + (0.25f * Gpu.Cos(time * 0.41f)));
        float waves = Gpu.Sin((Gpu.Length(uv - c1) * 31f) - (time * 1.3f)) + Gpu.Sin((Gpu.Length(uv - c2) * 19f) + (time * 0.9f));
        float grain = (Hash((uint)x + (Hash((uint)y) * 31u)) >> 8) * (1f / 16777216f);

        values[(y * side) + x] = Gpu.Saturate(0.5f + (0.21f * waves) + ((grain - 0.5f) * 0.16f));
    }
}

/// <summary>
/// The feature kernel: the sum of each block of 256 values, by a tree in group-shared memory. Every
/// thread loads one value; then, eight times, the lower half of the live range adds the upper half to
/// itself, with a barrier after each round so that the next one reads finished sums.
/// </summary>
/// <remarks>
/// Dispatched over a multiple of 256 threads; threads past <paramref name="count"/> load 0, which keeps
/// the last block's tree complete. The barrier is called by every thread, whether it added or not:
/// a barrier inside the <c>if</c> would be undefined.
/// </remarks>
[ThreadGroupSize(GpuSum.BlockSize)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SumBlocksShared(ReadWriteBuffer<float> input, ReadWriteBuffer<float> sums, int count) : IComputeShader
{
    [GroupShared]
    private static readonly float[] tile = new float[GpuSum.BlockSize];

    public void Execute()
    {
        int i = ThreadIds.X;
        int local = GroupIds.X;

        tile[local] = i < count ? input[i] : 0f;
        Gpu.GroupMemoryBarrierWithGroupSync();

        for (int half = GpuSum.BlockSize / 2; half > 0; half >>= 1)
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

/// <summary>The same pass without shared memory: one thread per block adds its 256 values in a loop.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SumBlocksSerial(ReadWriteBuffer<float> input, ReadWriteBuffer<float> sums, int count) : IComputeShader
{
    public void Execute()
    {
        int first = ThreadIds.X * GpuSum.BlockSize;
        int end = Gpu.Min(first + GpuSum.BlockSize, count);
        float sum = 0f;

        for (int i = first; i < end; i++)
        {
            sum += input[i];
        }

        sums[ThreadIds.X] = sum;
    }
}

/// <summary>Draws the input field and, next to the rows they sum, the 256 block sums of the second pass.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawReduction(ReadWriteBuffer<uint> pixels, ReadWriteBuffer<float> values, ReadWriteBuffer<float> sums, int width, int side) : IComputeShader
{
    public const int PanelX = 32;
    public const int PanelY = 104;
    public const int PanelSize = 512;
    public const int BarsX = PanelX + PanelSize + 24;
    public const int BarsWidth = 208;

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int py = y - PanelY;

        if (py >= 0 && py < PanelSize)
        {
            // The panel shows every 8th value of every 8th row, so one of its rows is 8 rows of the
            // field and two are the 16 rows (65,536 values) that one second-pass sum covers.
            int scale = side / PanelSize;
            int px = x - PanelX;
            int bx = x - BarsX;

            if (px >= 0 && px < PanelSize)
            {
                float value = values[((py * scale) * side) + (px * scale)];
                color = Gpu.Lerp(new Float3(0.03f, 0.05f, 0.16f), new Float3(1f, 0.82f, 0.4f), value);
            }
            else if (bx >= 0 && bx < BarsWidth)
            {
                int bar = py / 2;
                float mean = sums[bar] / (16f * side);
                bool filled = bx < (int)(mean * BarsWidth);

                color = filled
                    ? Gpu.Lerp(new Float3(0.1f, 0.35f, 0.75f), new Float3(1f, 0.82f, 0.4f), mean) * ((bar & 1) == 0 ? 1f : 0.82f)
                    : new Float3(0.03f, 0.04f, 0.06f);
            }
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
