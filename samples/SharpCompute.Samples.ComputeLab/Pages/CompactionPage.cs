// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// Atomics, part two: stream compaction. Of a million elements, keep the ones that pass a test,
/// packed at the start of an output array. Every thread whose element passes reserves the next free
/// slot with <see cref="Gpu.AtomicAdd(ref int, int)"/> on one shared counter and writes its index there.
/// </summary>
/// <remarks>
/// The counter ends at exactly the number of kept elements, and the output holds exactly those
/// elements: that is what the check compares with the CPU. What it does <b>not</b> have is their
/// order. Slots are handed out in whatever order the threads reach the counter, which differs from
/// the CPU's and from one run to the next. Code that needs the order must sort afterwards, or
/// compact with a prefix sum instead. The third panel shows the order the GPU produced.
/// </remarks>
public sealed class CompactionPage : ILabPage
{
    /// <summary>The field is <see cref="Size"/> × <see cref="Size"/>: 2^20 elements.</summary>
    public const int Size = 1024;
    public const int Count = Size * Size;

    private static readonly string[] s_variants = ["1,048,576 elements"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<uint> _source;
    private readonly ReadWriteBuffer<uint> _output;
    private readonly ReadWriteBuffer<int> _counter;
    private readonly ReadWriteBuffer<uint> _marks;
    private readonly ReadWriteBuffer<uint> _cpuOutput;
    private readonly ReadWriteBuffer<int> _cpuCounter;
    private readonly ReadWriteBuffer<uint> _sorted;
    private readonly uint[] _gpuScratch = new uint[Count];
    private readonly uint[] _cpuScratch = new uint[Count];
    private bool _sortedIsCurrent;

    public CompactionPage(GraphicsDevice device)
    {
        _device = device;
        _source = device.AllocateReadWriteBuffer<uint>(Count);
        _output = device.AllocateReadWriteBuffer<uint>(Count);
        _counter = device.AllocateReadWriteBuffer<int>(1);
        _marks = device.AllocateReadWriteBuffer<uint>(Count);
        _cpuOutput = device.AllocateReadWriteBuffer<uint>(Count);
        _cpuCounter = device.AllocateReadWriteBuffer<int>(1);
        _sorted = device.AllocateReadWriteBuffer<uint>(Count);
    }

    public string Name => "Compaction";

    public string Feature => "atomics";

    public string Help => "Up/Down threshold";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>Elements with a value at or above this (of 65,535) are kept.</summary>
    public uint Cutoff { get; private set; } = 36000;

    /// <summary>How many elements the GPU kept in the last <see cref="Compute"/>.</summary>
    public int Kept { get; private set; }

    /// <summary>Whether the GPU's output happened to be in index order at the last <see cref="Check"/> (it practically never is).</summary>
    public bool InIndexOrder { get; private set; }

    public void Compute(LabFrame frame)
    {
        _device.For(Size, Size, new GenerateField(_source, Size, frame.Time));
        _counter[0] = 0;

        long start = Stopwatch.GetTimestamp();
        _device.For(Count, new CompactAbove(_source, _output, _counter, Cutoff));
        GpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        Kept = _counter[0];
        _sortedIsCurrent = false;

        // Scatter the output back to where each element came from: if the compaction is right this
        // reproduces the "kept" mask exactly, whatever order the slots were handed out in.
        _marks.AsSpan().Clear();

        if (Kept > 0)
        {
            _device.For(Kept, new ScatterBack(_output, _marks));
        }
    }

    public LabCheck Check()
    {
        _cpuCounter[0] = 0;

        long start = Stopwatch.GetTimestamp();
        _device.ForCpu(Count, new CompactAbove(_source, _cpuOutput, _cpuCounter, Cutoff));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        int cpuKept = _cpuCounter[0];

        // The plain C# answer: the indices that pass, in index order.
        ReadOnlySpan<uint> source = _source.AsReadOnlySpan();
        Span<uint> reference = _cpuScratch;
        int expected = 0;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] >= Cutoff)
            {
                reference[expected++] = (uint)i;
            }
        }

        bool countsMatch = Kept == expected && cpuKept == expected;
        bool sameSet = false;

        if (countsMatch)
        {
            // Neither parallel run keeps the order, so compare as sets: sorted, both must be the reference.
            Span<uint> gpu = _gpuScratch.AsSpan(0, expected);
            _output.AsReadOnlySpan()[..expected].CopyTo(gpu);
            InIndexOrder = gpu.SequenceEqual(reference[..expected]);
            gpu.Sort();
            sameSet = gpu.SequenceEqual(reference[..expected]);

            if (sameSet)
            {
                // The sorted output, for a drawing that is the same on every run (the headless PNG).
                gpu.CopyTo(_sorted.AsSpan());
                _sortedIsCurrent = true;

                Span<uint> cpu = _gpuScratch.AsSpan(0, expected);
                _cpuOutput.AsReadOnlySpan()[..expected].CopyTo(cpu);
                cpu.Sort();
                sameSet = cpu.SequenceEqual(reference[..expected]);
            }
        }

        string summary = !countsMatch
            ? Invariant($"counts differ: GPU kept {Kept:N0}, ForCpu {cpuKept:N0}, plain C# {expected:N0}")
            : !sameSet
                ? Invariant($"kept {expected:N0} elements on all three, but not the same ones")
                : Invariant($"kept {expected:N0} of {Count:N0}: GPU, ForCpu and plain C# agree on the count and on the set");

        return new LabCheck(countsMatch && sameSet, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        // The headless PNG must not depend on thread scheduling: it shows the sorted output instead.
        bool sorted = frame.Deterministic && _sortedIsCurrent;
        ReadWriteBuffer<uint> order = sorted ? _sorted : _output;

        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawCompaction(canvas.Pixels, _source, _marks, order, LabCanvas.Width, Size, Kept, Cutoff));

        int labelY = DrawCompaction.PanelY - 24;
        int below = DrawCompaction.PanelY + DrawCompaction.PanelSize + 8;
        canvas.Label(DrawCompaction.PanelX(0), labelY, "1. Input: 1M values");
        canvas.Label(DrawCompaction.PanelX(0), below, Invariant($"gold: kept (value >= {Cutoff * 100 / 65535}%)"), TextColor.Accent);
        canvas.Label(DrawCompaction.PanelX(1), labelY, "2. Output, scattered back");
        canvas.Label(DrawCompaction.PanelX(1), below, "matches the gold mask exactly", TextColor.Cool);
        canvas.Label(DrawCompaction.PanelX(2), labelY, sorted ? "3. Output slots (sorted here)" : "3. Output slots, in GPU order");
        canvas.Label(DrawCompaction.PanelX(2), below, "color: where the slot came from", TextColor.Dim);

        int text = below + 40;
        canvas.Label(DrawCompaction.PanelX(0), text, Invariant($"Kept {Kept:N0} of {Count:N0} elements. Every passing thread takes the next free slot:"), TextColor.White);
        canvas.Label(DrawCompaction.PanelX(0) + (4 * LabCanvas.CellWidth), text + 16, "output[Gpu.AtomicAdd(ref counter[0], 1)] = index;", TextColor.Cool);
        canvas.Label(DrawCompaction.PanelX(0), text + 48, "Caveat: the count and the set are exact, the ORDER is not. A slot goes to whichever thread", TextColor.Accent);
        canvas.Label(
            DrawCompaction.PanelX(0),
            text + 64,
            frame.Deterministic
                ? "asks first, so it changes from run to run (panel 3 is sorted here to keep this picture reproducible)."
                : InIndexOrder ? "asks first, so it changes from run to run. (This run happened to come out in index order.)" : "asks first, so it changes from run to run: panel 3 would be a smooth gradient if it were kept.",
            TextColor.Accent);
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
        _output.Dispose();
        _counter.Dispose();
        _marks.Dispose();
        _cpuOutput.Dispose();
        _cpuCounter.Dispose();
        _sorted.Dispose();
    }
}

/// <summary>The compaction's input: a smooth animated field with grain, as values 0..65,535.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GenerateField(ReadWriteBuffer<uint> source, int size, float time) : IComputeShader
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
        float value = Gpu.Saturate(0.5f + (0.22f * waves) + ((grain - 0.5f) * 0.18f));

        source[(y * size) + x] = (uint)(value * 65535f);
    }
}

/// <summary>
/// The feature kernel: a thread whose element passes reserves the next output slot and stores its
/// index there. The add returns the counter's value <i>before</i> the add, which is the slot.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct CompactAbove(ReadWriteBuffer<uint> source, ReadWriteBuffer<uint> output, ReadWriteBuffer<int> counter, uint cutoff) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        if (source[i] >= cutoff)
        {
            output[Gpu.AtomicAdd(ref counter[0], 1)] = (uint)i;
        }
    }
}

/// <summary>Marks the source position of every output slot. The slots hold distinct indices, so no two threads write the same mark.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ScatterBack(ReadWriteBuffer<uint> output, ReadWriteBuffer<uint> marks) : IComputeShader
{
    public void Execute() => marks[(int)output[ThreadIds.X]] = 1u;
}

/// <summary>Draws the page's three panels.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawCompaction(
    ReadWriteBuffer<uint> pixels, ReadWriteBuffer<uint> source, ReadWriteBuffer<uint> marks, ReadWriteBuffer<uint> order, int width, int size, int kept, uint cutoff) : IComputeShader
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
                    bool marked = marks[element] != 0u;

                    // Cyan where the output holds the element. Red would be a kept element the output lacks, or one it should not have.
                    color = marked == keep ? (marked ? new Float3(0.3f, 0.85f, 1f) : new Float3(0.03f, 0.04f, 0.06f)) : new Float3(1f, 0.1f, 0.1f);
                }
                else
                {
                    // Every stride-th slot, row by row, colored by the position its element came from.
                    int stride = Gpu.Max(1, kept / (PanelSize * PanelSize));
                    int slot = ((py * PanelSize) + u) * stride;

                    if (slot < kept)
                    {
                        int index = (int)order[slot];
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
