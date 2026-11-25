// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// Indirect dispatch: a pass on the GPU decides how much work the next pass does. Of a million
/// elements a compaction pass keeps the ones above a threshold and counts them with an atomic; a
/// one-thread pass turns that count into <see cref="DispatchArguments"/>; and the follow-up pass is
/// dispatched with <c>ForIndirect</c> over exactly the survivors. All of it is one
/// <see cref="ComputeContext"/> execution: the count never comes back to the CPU.
/// </summary>
/// <remarks>
/// <para>
/// The page runs the job three ways, with the same result each time:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Compact, then dispatch the survivors indirectly</b>: the feature.
/// </description></item>
/// <item><description>
/// <b>Compact, then dispatch the full array</b> over the compacted list, each thread checking its
/// index against the count. This is what there is without indirect dispatch when the count must
/// stay on the GPU. It measures about the same as the first on an Apple M4 Pro: threads that stop
/// at once cost around ten microseconds per million.
/// </description></item>
/// <item><description>
/// <b>No compaction</b>: one dispatch over the full array in which every thread tests its own
/// element and the survivors do their work where they stand. This one is slower once the
/// survivors' work is worth anything, because a GPU runs neighbouring threads together: with the
/// survivors scattered, the idle threads next to each one wait for it, while a compacted pass keeps
/// every thread busy.
/// </description></item>
/// </list>
/// <para>
/// So the speed-up comes from compaction; indirect dispatch's gain is no CPU read-back: the thread
/// count follows the data without the count leaving the GPU, and without dispatching (or sizing a
/// dispatch for) the whole capacity of the array.
/// </para>
/// <para>
/// An indirect dispatch runs whole threadgroups, so up to 255 threads more than the count: the
/// follow-up shader checks its index against the count, and so must any shader dispatched this way.
/// </para>
/// </remarks>
public sealed class IndirectPage : ILabPage
{
    /// <summary>The field is <see cref="Size"/> × <see cref="Size"/>: 2^20 elements.</summary>
    public const int Size = 1024;
    public const int Count = Size * Size;

    // How many times Compute runs each way; see there.
    private const int Repeats = 3;

    private static readonly string[] s_variants =
    [
        "indirect: compact, dispatch the survivors",
        "compact, dispatch all 1,048,576 threads",
        "no compaction: every thread tests itself",
    ];

    private readonly GraphicsDevice _device;
    private readonly ComputeContext _context;
    private readonly ReadWriteBuffer<uint> _source;
    private readonly ReadWriteBuffer<uint> _survivors;
    private readonly ReadWriteBuffer<int> _counter;
    private readonly ReadWriteBuffer<DispatchArguments> _arguments;
    private readonly ReadWriteBuffer<uint> _shaded;
    private readonly ReadWriteBuffer<uint> _cpuSurvivors;
    private readonly ReadWriteBuffer<int> _cpuCounter;
    private readonly ReadWriteBuffer<DispatchArguments> _cpuArguments;
    private readonly ReadWriteBuffer<uint> _cpuShaded;

    public IndirectPage(GraphicsDevice device)
    {
        _device = device;
        _context = device.CreateComputeContext();
        _source = device.AllocateReadWriteBuffer<uint>(Count);
        _survivors = device.AllocateReadWriteBuffer<uint>(Count);
        _counter = device.AllocateReadWriteBuffer<int>(1);
        _arguments = device.AllocateReadWriteBuffer<DispatchArguments>(1);
        _shaded = device.AllocateReadWriteBuffer<uint>(Count);
        _cpuSurvivors = device.AllocateReadWriteBuffer<uint>(Count);
        _cpuCounter = device.AllocateReadWriteBuffer<int>(1);
        _cpuArguments = device.AllocateReadWriteBuffer<DispatchArguments>(1);
        _cpuShaded = device.AllocateReadWriteBuffer<uint>(Count);

        // Compiled here rather than by the first frame that uses them.
        device.Prepare(
            ShaderReference.Of<GenerateField>(),
            ShaderReference.Of<CompactAbove>(),
            ShaderReference.Of<ShadeSurvivors>(),
            ShaderReference.Of<ShadeInPlace>(),
            ShaderReference.Of<DrawIndirect>());
    }

    public string Name => "Indirect";

    public string Feature => "indirect dispatch";


    public string Topic => LabTopics.Dispatch;

    public string Help => "Up/Down threshold   D next way";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>
    /// How long each of the three ways took in the last <see cref="Compute"/>, by variant index (its
    /// fastest execution, like <see cref="GpuMilliseconds"/>, which is the shown variant's entry).
    /// </summary>
    public IReadOnlyList<double> Milliseconds => _milliseconds;

    private readonly double[] _milliseconds = new double[3];

    /// <summary>Elements with a value at or above this (of 65,535) survive.</summary>
    public uint Cutoff { get; private set; } = 45000;

    /// <summary>How many elements survived in the last <see cref="Compute"/>, as counted on the GPU by the compaction.</summary>
    public int Kept { get; private set; }

    /// <summary>The threadgroup counts the GPU wrote for the indirect follow-up pass in the last <see cref="Compute"/>.</summary>
    public DispatchArguments Arguments { get; private set; }

    /// <summary>The pass that does the survivors' work: the kernel an indirect dispatch runs is precompiled like any other.</summary>
    public PipelineOrigin KernelOrigin => Variant == 2 ? _device.GetPipelineOrigin<ShadeInPlace>() : _device.GetPipelineOrigin<ShadeSurvivors>();

    /// <summary>How many threads the shown variant's pass that does the survivors' work ran in the last <see cref="Compute"/>.</summary>
    public int ThreadsDispatched => Variant == 0 ? (int)Arguments.ThreadGroupsX * ShadeSurvivors.GroupSize : Count;

    /// <summary>The shown variant's output: one value per element, 0 where the element did not survive.</summary>
    public ReadOnlySpan<uint> Shaded => _shaded.AsReadOnlySpan();

    public void Compute(LabFrame frame)
    {
        _device.For(Size, Size, new GenerateField(_source, Size, frame.Time));

        // All three ways every time, taking turns, the shown one last so that its output is what is
        // drawn and checked. Each execution is well under a millisecond, where one sample says more
        // about what the GPU was doing just before than about the work, so each way runs a few times
        // and its fastest execution is the one reported.
        Array.Fill(_milliseconds, double.MaxValue);

        for (int repeat = 0; repeat < Repeats; repeat++)
        {
            for (int step = 1; step <= s_variants.Length; step++)
            {
                int way = (Variant + step) % s_variants.Length;
                _milliseconds[way] = Math.Min(_milliseconds[way], Run(way));
            }
        }

        GpuMilliseconds = _milliseconds[Variant];
        Kept = _counter[0];
        Arguments = _arguments[0];
    }

    /// <summary>Records one of the three ways into the context and returns how long the GPU took for its single execution.</summary>
    private double Run(int way)
    {
        ShadeSurvivors shade = new(_survivors, _counter, _source, _shaded);

        _context.Clear(_shaded);

        if (way == 2)
        {
            // No compaction and no indirect dispatch: every thread of the array tests its element.
            _context.For(Count, new ShadeInPlace(_source, _shaded, Cutoff));
        }
        else
        {
            _context.Clear(_counter);
            _context.For(Count, new CompactAbove(_source, _survivors, _counter, Cutoff));

            // Written in both, so that the arguments on show always belong to the survivors on show.
            _context.WriteDispatchArguments<ShadeSurvivors>(_counter, _arguments);

            if (way == 0)
            {
                // The feature: nothing here reads the count; the GPU hands it from pass to pass.
                _context.ForIndirect(_arguments, shade);
            }
            else
            {
                // The same pass over the whole array: every thread past the count stops at its check.
                _context.For(Count, shade);
            }
        }

        _context.Execute();
        return _context.LastGpuTime.TotalMilliseconds;
    }

    public LabCheck Check()
    {
        // The shown variant's work on the CPU, with the same shaders.
        long start = Stopwatch.GetTimestamp();
        _cpuCounter[0] = 0;
        _cpuShaded.AsSpan().Clear();

        if (Variant == 2)
        {
            _device.ForCpu(Count, new ShadeInPlace(_source, _cpuShaded, Cutoff));
        }
        else
        {
            ShadeSurvivors shade = new(_cpuSurvivors, _cpuCounter, _source, _cpuShaded);

            _device.ForCpu(Count, new CompactAbove(_source, _cpuSurvivors, _cpuCounter, Cutoff));
            _cpuArguments[0] = DispatchArguments.FromCount(_cpuCounter[0], ShadeSurvivors.GroupSize);

            if (Variant == 0)
            {
                _device.ForIndirectCpu(_cpuArguments, shade);
            }
            else
            {
                _device.ForCpu(Count, shade);
            }
        }

        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // The plain C# answer, element by element (on all cores, but with no shader and no compaction).
        int expectedKept = 0, gpuWrong = 0, cpuWrong = 0;
        uint cutoff = Cutoff;

        Parallel.For(0, Size, row =>
        {
            ReadOnlySpan<uint> source = _source.AsReadOnlySpan().Slice(row * Size, Size);
            ReadOnlySpan<uint> gpu = _shaded.AsReadOnlySpan().Slice(row * Size, Size);
            ReadOnlySpan<uint> cpu = _cpuShaded.AsReadOnlySpan().Slice(row * Size, Size);
            int kept = 0, wrongOnGpu = 0, wrongOnCpu = 0;

            for (int x = 0; x < Size; x++)
            {
                bool keep = source[x] >= cutoff;
                uint expected = keep ? ShadeSurvivors.Shade(source[x], (uint)((row * Size) + x)) : 0u;

                kept += keep ? 1 : 0;
                wrongOnGpu += gpu[x] != expected ? 1 : 0;
                wrongOnCpu += cpu[x] != expected ? 1 : 0;
            }

            Interlocked.Add(ref expectedKept, kept);
            Interlocked.Add(ref gpuWrong, wrongOnGpu);
            Interlocked.Add(ref cpuWrong, wrongOnCpu);
        });

        // The GPU's count, and the arguments it derived from it, against plain C# (and the CPU chain's own).
        DispatchArguments expectedArguments = DispatchArguments.FromCount(expectedKept, ShadeSurvivors.GroupSize);
        bool countsMatch = Kept == expectedKept && Arguments == expectedArguments
            && (Variant == 2 || (_cpuCounter[0] == expectedKept && _cpuArguments[0] == expectedArguments));
        bool passed = countsMatch && gpuWrong == 0 && cpuWrong == 0;

        string summary = !countsMatch
            ? Invariant($"the GPU counted {Kept:N0} survivors and wrote {Arguments}; plain C# has {expectedKept:N0} and {expectedArguments}")
            : !passed
                ? Invariant($"{gpuWrong:N0} elements differ on the GPU and {cpuWrong:N0} on the CPU, of {Count:N0}")
                : Invariant($"{Kept:N0} survivors, {ThreadsDispatched:N0} threads dispatched: GPU, CPU and plain C# agree");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        int dispatched = ThreadsDispatched;

        _device.For(
            LabCanvas.Width,
            LabCanvas.Height,
            new DrawIndirect(canvas.Pixels, _source, _shaded, LabCanvas.Width, Size, Kept, dispatched, Cutoff, compacted: Variant == 2 ? 0 : 1));

        int labelY = DrawIndirect.PanelY - 24;
        int below = DrawIndirect.PanelY + DrawIndirect.PanelSize + 8;
        canvas.Label(DrawIndirect.PanelX(0), labelY, "1. Input: 1M values");
        canvas.Label(DrawIndirect.PanelX(0), below, Invariant($"gold: survives (value >= {Cutoff * 100 / 65535}%)"), TextColor.Accent);
        canvas.Label(DrawIndirect.PanelX(1), labelY, "2. The survivors' work, in place");
        canvas.Label(DrawIndirect.PanelX(1), below, "written only where gold is", TextColor.Cool);
        canvas.Label(DrawIndirect.PanelX(1), below + 16, LabView.Describe(KernelOrigin), KernelOrigin == PipelineOrigin.Precompiled ? TextColor.Good : TextColor.Dim);
        canvas.Label(DrawIndirect.PanelX(2), labelY, "3. The threads that did it");
        canvas.Label(DrawIndirect.PanelX(2), below, "cyan: worked   dark: never ran", TextColor.Dim);
        canvas.Label(DrawIndirect.PanelX(2), below + 16, "red: ran, stopped at its check", TextColor.Dim);

        int text = below + 56;
        string groups = Invariant($"{Arguments.ThreadGroupsX:N0} x {ShadeSurvivors.GroupSize}");
        canvas.Label(
            DrawIndirect.PanelX(0),
            text,
            Variant switch
            {
                0 => Invariant($"{Kept:N0} survivors -> {groups} = {dispatched:N0} threads, counted and dispatched on the GPU:"),
                1 => Invariant($"{Kept:N0} survivors packed first, but all {dispatched:N0} threads run; the rest stop at once:"),
                _ => Invariant($"{Kept:N0} survivors, but {dispatched:N0} threads: the busy ones are scattered among the idle."),
            },
            TextColor.White);
        canvas.Label(
            DrawIndirect.PanelX(0) + (4 * LabCanvas.CellWidth),
            text + 16,
            Variant switch
            {
                0 => "context.WriteDispatchArguments<Shade>(counter, args); context.ForIndirect(args, shade);",
                1 => "context.For(1048576, shade);  // after the same compaction; each thread checks the count",
                _ => "context.For(1048576, shadeInPlace);  // no compaction, no indirect dispatch",
            },
            TextColor.Cool);
        canvas.Label(DrawIndirect.PanelX(0), text + 48, "Caveat: an indirect dispatch runs whole threadgroups, so up to 255 threads past the count,", TextColor.Accent);
        canvas.Label(DrawIndirect.PanelX(0), text + 64, "and DispatchSize is groups x 256. The shader checks its index against the count itself.", TextColor.Accent);
        canvas.Label(DrawIndirect.PanelX(0), text + 96, "The speed-up comes from compaction; indirect dispatch's gain is no CPU read-back.", TextColor.White);

        // Timings change from run to run, so they stay out of the headless picture.
        if (!frame.Deterministic)
        {
            canvas.Label(
                DrawIndirect.PanelX(0),
                text + 128,
                Invariant($"This frame: compact + indirect {_milliseconds[0]:F2} ms | compact + full array {_milliseconds[1]:F2} ms | no compaction {_milliseconds[2]:F2} ms"),
                TextColor.Good);
        }
    }

    public bool OnKeyDown(Key key)
    {
        switch (key)
        {
            case Key.Up or Key.Down:
                Cutoff = (uint)Math.Clamp((int)Cutoff + (key == Key.Up ? 3000 : -3000), 6000, 63000);
                return true;

            case Key.D:
                Variant = (Variant + 1) % s_variants.Length;
                return true;

            default:
                return false;
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        _source.Dispose();
        _survivors.Dispose();
        _counter.Dispose();
        _arguments.Dispose();
        _shaded.Dispose();
        _cpuSurvivors.Dispose();
        _cpuCounter.Dispose();
        _cpuArguments.Dispose();
        _cpuShaded.Dispose();
    }
}

/// <summary>
/// The follow-up pass: one thread per survivor reads the survivor's index from the compacted list,
/// does its work on that element and stores the result at the element's own position, so the result
/// does not depend on the order the compaction handed out its slots in.
/// </summary>
/// <remarks>
/// The threadgroup size is explicit because the dispatch arguments are counted in threadgroups:
/// <c>WriteDispatchArguments&lt;ShadeSurvivors&gt;</c> divides the count by it.
/// </remarks>
[ThreadGroupSize(GroupSize)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct ShadeSurvivors(
    ReadWriteBuffer<uint> survivors, ReadWriteBuffer<int> counter, ReadWriteBuffer<uint> source, ReadWriteBuffer<uint> shaded) : IComputeShader
{
    public const int GroupSize = 256;

    /// <summary>How many mixing rounds a survivor costs: enough for its work to outweigh the compaction that packs it.</summary>
    public const int Rounds = 256;

    /// <summary>
    /// The work done per survivor: integer mixing of its value and position, so the GPU, the CPU and
    /// plain C# give the same bits. Never 0, which marks an element that was not written.
    /// </summary>
    public static uint Shade(uint value, uint index)
    {
        uint h = value ^ (index * 0x9E3779B9u);

        for (int round = 0; round < Rounds; round++)
        {
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
        }

        return h | 1u;
    }

    public void Execute()
    {
        int slot = ThreadIds.X;

        // The dispatch covers whole threadgroups (indirect) or the whole array, so more threads than
        // there are survivors: the rest must not read a slot that was never written.
        if (slot < counter[0])
        {
            uint index = survivors[slot];
            shaded[(int)index] = Shade(source[(int)index], index);
        }
    }
}

/// <summary>
/// The same work without compaction: one thread per element of the whole array, which tests its own
/// element and, if it survives, does the work on the spot.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ShadeInPlace(ReadWriteBuffer<uint> source, ReadWriteBuffer<uint> shaded, uint cutoff) : IComputeShader
{
    // ShadeSurvivors.Shade, repeated: a shader can only call its own methods. The page's check
    // compares both against that one in plain C#, so they cannot drift apart unnoticed.
    private static uint Shade(uint value, uint index)
    {
        uint h = value ^ (index * 0x9E3779B9u);

        for (int round = 0; round < ShadeSurvivors.Rounds; round++)
        {
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
        }

        return h | 1u;
    }

    public void Execute()
    {
        int i = ThreadIds.X;

        if (source[i] >= cutoff)
        {
            shaded[i] = Shade(source[i], (uint)i);
        }
    }
}

/// <summary>Draws the page's three panels.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawIndirect(
    ReadWriteBuffer<uint> pixels, ReadWriteBuffer<uint> source, ReadWriteBuffer<uint> shaded, int width, int size, int kept, int dispatched, uint cutoff, int compacted) : IComputeShader
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
                // The panel shows the whole field, scaled down: element (u, v) * size / PanelSize.
                int element = ((py * size / PanelSize) * size) + (u * size / PanelSize);
                bool keep = source[element] >= cutoff;

                if (panel == 0)
                {
                    float value = source[element] * (1f / 65535f);
                    color = keep ? new Float3(1f, 0.62f + (0.3f * value), 0.12f) * (0.45f + (0.55f * value)) : new Float3(value * 0.55f);
                }
                else if (panel == 1)
                {
                    uint result = shaded[element];

                    if (result != 0u)
                    {
                        // The low bits of the mixed value as a color: fine grain, since every survivor's differs.
                        Float4 grain = Gpu.UnpackUnorm4x8(result);
                        float value = source[element] * (1f / 65535f);
                        color = (new Float3(0.15f, 0.55f, 0.85f) * (0.5f + (0.5f * value))) + (grain.XYZ * 0.3f);
                    }
                    else
                    {
                        color = new Float3(0.03f, 0.04f, 0.06f);
                    }
                }
                else
                {
                    // The square is the full array of threads, row by row. Compacted, the first `kept`
                    // threads do the work; in place, the thread of each surviving element does.
                    bool worked = compacted != 0 ? element < kept : keep;

                    if (worked)
                    {
                        color = new Float3(0.3f, 0.85f, 1f);
                    }
                    else if (element < dispatched)
                    {
                        color = new Float3(0.6f, 0.16f, 0.14f);
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
