// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SharpCompute.Presentation;
using SharpCompute.Samples.ComputeLab.Pages.SharedMemory;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages.Simd;

/// <summary>
/// A matrix product on the GPU's matrix units: <c>C = A × B</c> for random floats, 1024 × 1024 and
/// 2048 × 2048, by three kernels. Two are the MatMul page's: one thread per element that walks its row
/// and column, and 16 × 16 tiles in shared memory. The third holds the tiles as
/// <see cref="WaveMatrix8x8"/>, 8 × 8 matrices that the 32 lanes of a SIMD group carry together, and
/// multiplies them with <see cref="WaveMatrix8x8.MultiplyAccumulate(WaveMatrix8x8, WaveMatrix8x8, WaveMatrix8x8, string, int)"/>,
/// which is one step of the hardware for 512 multiply-adds.
/// </summary>
/// <remarks>
/// <para>
/// All three do the same <c>2 n³</c> floating-point operations, so the page shows GFLOP/s: a bar per
/// kernel. In the window the bars are this machine's; a reproducible picture must not hold a timing,
/// so there they are the rates recorded on an Apple M4 Pro (<see cref="RecordedGigaFlops"/>), and say so.
/// </para>
/// <para>
/// The matrix-unit kernel is short enough (0.4 ms at 1024) that the round trip of one command buffer
/// would be a quarter of its time, so it is dispatched <see cref="WaveRepeats"/> times in one command
/// buffer and timed per product; the other two take long enough to be timed one at a time.
/// </para>
/// </remarks>
public sealed class MatrixUnitsPage : ILabPage, IRatedPage
{
    /// <summary>The kernels, in the order of the bars and of the variants of each size.</summary>
    public const int Wave = 0, Tiled = 1, Naive = 2;

    public const int Kernels = 3;

    /// <summary>How many rows of C the check recomputes in double precision (evenly spaced).</summary>
    public const int CheckedRows = 32;

    /// <summary>Products of the matrix-unit kernel per command buffer; its time is that of one.</summary>
    public const int WaveRepeats = 4;

    private const float StepsPerSecond = 2f;
    private const int OtherKernelsInterval = 12;

    private static readonly int[] s_sizes = [1024, 2048];
    private static readonly string[] s_kernels = ["wave matrices", "tiles in shared memory", "naive"];

    private static readonly string[] s_variants =
    [
        "1024 x 1024: wave matrices", "1024 x 1024: tiles in shared memory", "1024 x 1024: naive",
        "2048 x 2048: wave matrices", "2048 x 2048: tiles in shared memory", "2048 x 2048: naive",
    ];

    // Short: each shares its line with the kernel's rate.
    private static readonly string[] s_descriptions =
    [
        "wave matrices on the matrix units",
        "16 x 16 tiles in shared memory",
        "naive: a thread per element",
    ];

    /// <summary>
    /// GFLOP/s of the three kernels (<see cref="Wave"/>, <see cref="Tiled"/>, <see cref="Naive"/>) at
    /// 1024 and at 2048, as this page's headless summary measured them on an Apple M4 Pro: commit to
    /// completion, the median of five runs, each the median of seven timings.
    /// </summary>
    public static readonly double[][] RecordedGigaFlops = [[4620, 920, 460], [5120, 980, 450]];

    private readonly GraphicsDevice _device;
    private readonly ComputeContext _context;
    private readonly ReadWriteBuffer<float> _a;
    private readonly ReadWriteBuffer<float> _b;
    private readonly ReadWriteBuffer<float> _c;
    private readonly ReadWriteBuffer<float> _bars;
    private readonly double[][] _milliseconds = [new double[Kernels], new double[Kernels]];
    private readonly double[][] _smoothed = [new double[Kernels], new double[Kernels]];
    private readonly float[] _shown = new float[Kernels];
    private float[]? _cpuB;
    private float _computedStep = float.NaN;
    private int _computedVariant = -1;

    // Whether the last product was computed for the window, which also keeps the other kernels' times fresh.
    private bool _computedLive;
    private int _products;

    public MatrixUnitsPage(GraphicsDevice device)
    {
        int largest = s_sizes[^1];

        _device = device;
        _context = device.CreateComputeContext();
        _a = device.AllocateReadWriteBuffer<float>(largest * largest);
        _b = device.AllocateReadWriteBuffer<float>(largest * largest);
        _c = device.AllocateReadWriteBuffer<float>(largest * largest);
        _bars = device.AllocateReadWriteBuffer<float>(Kernels);
    }

    public string Name => "Matrix units";

    public string Feature => "wave matrices";

    public string Topic => LabTopics.Simd;

    public string Help => "V kernel   N size";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>The largest difference between the GPU's and the double-precision elements at the last <see cref="Check"/>.</summary>
    public double MaxError { get; private set; }

    /// <summary>The matrix size of the active variant: 1024 or 2048.</summary>
    public int N => s_sizes[Variant / Kernels];

    /// <summary>The kernel of the active variant: <see cref="Wave"/>, <see cref="Tiled"/> or <see cref="Naive"/>.</summary>
    public int Kernel => Variant % Kernels;

    /// <summary>GFLOP/s of an <paramref name="n"/> × <paramref name="n"/> product that took <paramref name="milliseconds"/>: a multiply and an add per term.</summary>
    public static double GigaFlops(int n, double milliseconds) => milliseconds <= 0 ? 0 : 2.0 * n * n * n / (milliseconds * 1e6);

    public string Rate(double gpuMilliseconds, double cpuMilliseconds) =>
        Invariant($"GPU {GigaFlops(N, gpuMilliseconds):F0} GFLOP/s   CPU {GigaFlops(N, cpuMilliseconds):F0} GFLOP/s");

    public void Compute(LabFrame frame)
    {
        // New random matrices now and then, and the product only when something changed: at these
        // sizes a product every frame would keep the GPU busy for nothing.
        float step = frame.Deterministic ? frame.Time : MathF.Floor(frame.Time * StepsPerSecond) / StepsPerSecond;

        if (!frame.Deterministic && _computedLive && step == _computedStep && Variant == _computedVariant)
        {
            return;
        }

        int n = N, size = Variant / Kernels;
        _device.For(n, n, new GenerateRandomMatrices(_a, _b, n, (uint)(int)(step * 64f)));

        // The window's bars are live for all three kernels, the other two refreshed less often. A
        // reproducible frame runs the active one alone: its bars are the recorded rates.
        if (!frame.Deterministic && (_products++ % OtherKernelsInterval == 0 || _milliseconds[size][(Kernel + 1) % Kernels] == 0))
        {
            for (int other = 0; other < Kernels; other++)
            {
                if (other != Kernel)
                {
                    Multiply(other, size);
                }
            }
        }

        // The active kernel last: its product is what the check compares and the page draws.
        Multiply(Kernel, size);
        GpuMilliseconds = _milliseconds[size][Kernel];
        (_computedStep, _computedVariant, _computedLive) = (step, Variant, !frame.Deterministic);
    }

    private void Multiply(int kernel, int size)
    {
        int n = s_sizes[size];
        long start = Stopwatch.GetTimestamp();
        int products = 1;

        switch (kernel)
        {
            case Wave:
                for (products = 0; products < WaveRepeats; products++)
                {
                    WaveMatMul.Multiply(_context, _a, _b, _c, n);
                }

                _context.Execute();
                break;

            case Tiled:
                _device.For(n, n, new MatMulTiled(_a, _b, _c, n));
                break;

            default:
                _device.For(n, n, new MatMulNaive(_a, _b, _c, n));
                break;
        }

        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds / products;
        _milliseconds[size][kernel] = milliseconds;
        _smoothed[size][kernel] = _smoothed[size][kernel] == 0 ? milliseconds : (_smoothed[size][kernel] * 0.8) + (milliseconds * 0.2);
    }

    public LabCheck Check()
    {
        int n = N;
        float[] b = _cpuB ??= new float[s_sizes[^1] * s_sizes[^1]];
        _b.AsReadOnlySpan()[..(n * n)].CopyTo(b);

        // The rows of A and of the GPU's C that are checked: one in every n / 32.
        float[] aRows = new float[CheckedRows * n], gpuRows = new float[CheckedRows * n];
        ReadOnlySpan<float> a = _a.AsReadOnlySpan(), c = _c.AsReadOnlySpan();

        for (int r = 0; r < CheckedRows; r++)
        {
            int row = r * (n / CheckedRows);
            a.Slice(row * n, n).CopyTo(aRows.AsSpan(r * n, n));
            c.Slice(row * n, n).CopyTo(gpuRows.AsSpan(r * n, n));
        }

        // The same rows in C#: 3 % (or 1.6 %) of the product, enough to time the CPU and to catch a
        // wrong tile anywhere, without seconds of all cores.
        float[] cpuRows = new float[CheckedRows * n];
        long start = Stopwatch.GetTimestamp();
        Parallel.For(0, CheckedRows, r => CpuMatMul.Row(aRows.AsSpan(r * n, n), b, n, 0, cpuRows.AsSpan(r * n, n)));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds * (n / (double)CheckedRows);

        // And in double precision, with the bound a float sum of n products can be off by.
        double[] errors = new double[CheckedRows];
        bool[] within = new bool[CheckedRows];

        Parallel.For(0, CheckedRows, r =>
        {
            double[] exact = new double[n];
            double[] magnitude = new double[n];
            CpuMatMul.RowExact(aRows.AsSpan(r * n, n), b, n, 0, exact, magnitude);

            bool ok = true;
            double worst = 0;

            for (int column = 0; column < n; column++)
            {
                double error = Math.Abs(gpuRows[(r * n) + column] - exact[column]);
                worst = Math.Max(worst, error);
                ok &= error <= (1e-4 * magnitude[column]) + 1e-5 && Math.Abs(cpuRows[(r * n) + column] - exact[column]) <= (1e-4 * magnitude[column]) + 1e-5;
            }

            errors[r] = worst;
            within[r] = ok;
        });

        MaxError = 0;
        bool passed = true;

        for (int r = 0; r < CheckedRows; r++)
        {
            MaxError = Math.Max(MaxError, errors[r]);
            passed &= within[r];
        }

        string summary = passed
            ? Invariant($"{CheckedRows} of {n} rows match C# doubles, max error {MaxError:E1} (CPU: all cores, scaled from {CheckedRows} rows)")
            : Invariant($"{CheckedRows} of {n} rows checked: off by up to {MaxError:E1}, more than float rounding allows");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        int n = N, size = Variant / Kernels;
        bool live = !frame.Deterministic && _smoothed[size][Wave] > 0 && _smoothed[size][Tiled] > 0 && _smoothed[size][Naive] > 0;
        Span<double> rates = stackalloc double[Kernels];
        double fastest = 0;

        for (int kernel = 0; kernel < Kernels; kernel++)
        {
            rates[kernel] = live ? GigaFlops(n, _smoothed[size][kernel]) : RecordedGigaFlops[size][kernel];
            fastest = Math.Max(fastest, rates[kernel]);
        }

        Span<float> bars = _bars.AsSpan();

        for (int kernel = 0; kernel < Kernels; kernel++)
        {
            // A live bar eases toward its value; a reproducible one is at it.
            float target = (float)(rates[kernel] / fastest);
            _shown[kernel] = live && _shown[kernel] > 0 ? _shown[kernel] + ((target - _shown[kernel]) * 0.2f) : target;
            bars[kernel] = _shown[kernel];
        }

        // An element of C is a sum of n products of values in -1..1: about sqrt(n) / 3.
        float scale = 1f / (2.5f * MathF.Sqrt(n) / 3f);
        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawMatrixUnits(canvas.Pixels, _c, _bars, LabCanvas.Width, n, Kernel, scale));

        canvas.Text(2, 3, Invariant($"C = A x B for {n} x {n} random floats: 2 x {n}^3 = {2.0 * n * n * n / 1e9:F1} billion floating-point operations"), TextColor.White);
        canvas.Text(2, 4, "OPS: WaveMatrix8x8.Load, WaveMatrix8x8.MultiplyAccumulate, Store", TextColor.Accent);

        // Left: the corner of C under the grid of the matrices that computed it.
        int below = DrawMatrixUnits.PanelY + DrawMatrixUnits.PanelSize + LabCanvas.CellHeight;
        canvas.Label(DrawMatrixUnits.PanelX, DrawMatrixUnits.PanelY - (2 * LabCanvas.CellHeight), "C: its first 96 x 96 elements");
        canvas.Label(DrawMatrixUnits.PanelX, below, "cell: an 8 x 8 wave matrix", TextColor.Dim);
        canvas.Label(DrawMatrixUnits.PanelX, below + LabCanvas.CellHeight, "blue: a SIMD group's 16 x 32", TextColor.Cool);
        canvas.Label(DrawMatrixUnits.PanelX, below + (2 * LabCanvas.CellHeight), "gold: a threadgroup's 32 x 64", TextColor.Accent);

        // Right: a bar per kernel, its description above it and its rate at the end of that line.
        int x = DrawMatrixUnits.BarX;
        int end = (x + DrawMatrixUnits.BarWidth) / LabCanvas.CellWidth;
        canvas.Label(x, DrawMatrixUnits.PanelY - (2 * LabCanvas.CellHeight), live ? "GFLOP/s on this machine, commit to completion" : "GFLOP/s recorded on Apple M4 Pro; the window shows this machine's");

        for (int kernel = 0; kernel < Kernels; kernel++)
        {
            int row = (DrawMatrixUnits.BarY(kernel) - LabCanvas.TextTop) / LabCanvas.CellHeight - 1;
            bool active = kernel == Kernel;
            string rate = live
                ? Invariant($"{rates[kernel]:N0} GFLOP/s  {_smoothed[size][kernel]:F2} ms")
                : Invariant($"{rates[kernel]:N0} GFLOP/s");

            canvas.Text(x / LabCanvas.CellWidth, row, s_descriptions[kernel], active ? TextColor.White : TextColor.Dim);
            canvas.Text(end - rate.Length, row, rate, active ? TextColor.Accent : TextColor.Cool);
        }

        int text = DrawMatrixUnits.BarY(Kernels) + LabCanvas.CellHeight;
        canvas.Label(x, text, "a0 = WaveMatrix8x8.Load(a, row * n + k, n);", TextColor.Cool);
        canvas.Label(x, text + 16, "b0 = WaveMatrix8x8.Load(b, k * n + column, n);", TextColor.Cool);
        canvas.Label(x, text + 32, "c00 = WaveMatrix8x8.MultiplyAccumulate(a0, b0, c00);", TextColor.Cool);
        canvas.Label(x, text + 48, "c00.Store(c, row * n + column, n);", TextColor.Cool);

        canvas.Label(x, text + 80, "A threadgroup: 4 SIMD groups of 32 lanes, a 32 x 64 tile of C.", TextColor.White);
        canvas.Label(x, text + 96, "A SIMD group: 2 x 4 accumulators of 8 x 8 (16 spill: 7x slower).", TextColor.Dim);
        canvas.Label(x, text + 112, "Operands load straight from the buffers, not via shared memory.", TextColor.Dim);
        canvas.Label(x, text + 128, "An offset is a sum: each term is one pointer addition.", TextColor.Dim);
    }

    public bool OnKeyDown(Key key)
    {
        switch (key)
        {
            case Key.V:
                Variant = (Variant / Kernels * Kernels) + ((Kernel + 1) % Kernels);
                return true;

            case Key.N:
                Variant = (Variant + Kernels) % s_variants.Length;
                return true;

            default:
                return false;
        }
    }

    public void Dispose()
    {
        _context.Dispose();
        _a.Dispose();
        _b.Dispose();
        _c.Dispose();
        _bars.Dispose();
    }
}

/// <summary>
/// The matrix product on wave matrices, and what a caller needs around the kernel: the size the
/// kernel works on and the dispatch that covers it.
/// </summary>
/// <remarks>
/// <b>Partial tiles are the caller's job.</b> A wave matrix is 8 × 8 and is loaded and stored whole,
/// and the kernel's threadgroups each own a 32 × 64 tile of the product, so the matrices have to be a
/// multiple of 64 on a side. For any other <c>n</c>, pad them: allocate <see cref="PaddedSize"/> squared
/// elements, place the <c>n × n</c> values at the top left with <see cref="Pad"/> (the rest zero, which
/// adds nothing to any sum), multiply at the padded size, and read the top left of the result with
/// <see cref="Unpad"/>.
/// </remarks>
public static class WaveMatMul
{
    /// <summary><paramref name="n"/> rounded up to a multiple of 64, the columns of a threadgroup's tile.</summary>
    public static int PaddedSize(int n) => (n + MatMulWaveMatrix.TileColumns - 1) / MatMulWaveMatrix.TileColumns * MatMulWaveMatrix.TileColumns;

    /// <summary><c>c = a × b</c> for row-major <paramref name="size"/> × <paramref name="size"/> matrices, <paramref name="size"/> a multiple of 64.</summary>
    public static void Multiply(GraphicsDevice device, ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, ReadWriteBuffer<float> c, int size)
    {
        CheckSize(size);
        device.For(size / MatMulWaveMatrix.TileColumns * MatMulWaveMatrix.GroupThreads, size / MatMulWaveMatrix.TileRows, new MatMulWaveMatrix(a, b, c, size));
    }

    /// <summary>The same dispatch recorded into <paramref name="context"/>, to run with whatever else it holds.</summary>
    public static void Multiply(ComputeContext context, ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, ReadWriteBuffer<float> c, int size)
    {
        CheckSize(size);
        context.For(size / MatMulWaveMatrix.TileColumns * MatMulWaveMatrix.GroupThreads, size / MatMulWaveMatrix.TileRows, new MatMulWaveMatrix(a, b, c, size));
    }

    /// <summary>
    /// The same product on the CPU (<c>ForCpu</c>): every lane a thread, every matrix operation a
    /// meeting of 32 of them. For checking the kernel at small sizes, not for speed.
    /// </summary>
    public static void MultiplyOnCpu(GraphicsDevice device, ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, ReadWriteBuffer<float> c, int size)
    {
        CheckSize(size);
        device.ForCpu(size / MatMulWaveMatrix.TileColumns * MatMulWaveMatrix.GroupThreads, size / MatMulWaveMatrix.TileRows, new MatMulWaveMatrix(a, b, c, size));
    }

    /// <summary>A row-major <paramref name="n"/> × <paramref name="n"/> matrix at the top left of a zero matrix of <see cref="PaddedSize"/> on a side.</summary>
    public static float[] Pad(ReadOnlySpan<float> matrix, int n)
    {
        int size = PaddedSize(n);
        float[] padded = new float[size * size];

        for (int row = 0; row < n; row++)
        {
            matrix.Slice(row * n, n).CopyTo(padded.AsSpan(row * size, n));
        }

        return padded;
    }

    /// <summary>The top-left <paramref name="n"/> × <paramref name="n"/> of a padded row-major matrix.</summary>
    public static float[] Unpad(ReadOnlySpan<float> padded, int n)
    {
        int size = PaddedSize(n);
        float[] matrix = new float[n * n];

        for (int row = 0; row < n; row++)
        {
            padded.Slice(row * size, n).CopyTo(matrix.AsSpan(row * n, n));
        }

        return matrix;
    }

    private static void CheckSize(int size)
    {
        if (size <= 0 || size % MatMulWaveMatrix.TileColumns != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size), size, $"The wave-matrix product works on matrices whose side is a multiple of {MatMulWaveMatrix.TileColumns}; pad the matrices to PaddedSize(n).");
        }
    }
}

/// <summary>
/// The feature kernel: a 32 × 64 tile of <c>c = a × b</c> per threadgroup of 128 threads, which is
/// four SIMD groups in a 2 × 2 arrangement, each computing 16 × 32 elements as 2 × 4 wave matrices.
/// For every 8 columns of <c>a</c> (and rows of <c>b</c>) a SIMD group loads the two 8 × 8 matrices of
/// <c>a</c> its rows need and the four of <c>b</c> its columns need, and adds their eight products to
/// its eight accumulators.
/// </summary>
/// <remarks>
/// <para>
/// The shape is what makes it fast (measured on Apple M4 Pro): eight accumulators per SIMD group stay
/// in registers where sixteen (a 32 × 32 block per group) spill and run seven times slower; the
/// operands come straight from the buffers, which beats staging them in <c>[GroupShared]</c> memory;
/// and the loop is unrolled twice by hand, so a SIMD group has two sets of loads in flight.
/// </para>
/// <para>
/// Everything a matrix operation is given is the same in all 32 lanes: it depends on the threadgroup's
/// position, the SIMD group's index, the loop counter and constants, never on the thread's own
/// position. <paramref name="stride"/> is the side of the matrices and must be a multiple of 64;
/// <see cref="WaveMatMul"/> has the dispatch and the padding for sizes that are not.
/// </para>
/// </remarks>
[ThreadGroupSize(MatMulWaveMatrix.GroupThreads)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatMulWaveMatrix(ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, ReadWriteBuffer<float> c, int stride) : IComputeShader
{
    /// <summary>Threads of a threadgroup: four SIMD groups of 32 lanes.</summary>
    public const int GroupThreads = 128;

    /// <summary>Rows of <c>c</c> a threadgroup computes: two SIMD groups of 16.</summary>
    public const int TileRows = 32;

    /// <summary>Columns of <c>c</c> a threadgroup computes: two SIMD groups of 32.</summary>
    public const int TileColumns = 64;

    public void Execute()
    {
        // The SIMD groups of a threadgroup tile its 32 x 64 elements 2 x 2.
        int group = SimdGroupIds.Index;
        int row = (ThreadGroupIds.Y * TileRows) + (group / 2 * 16);
        int column = (ThreadGroupIds.X * TileColumns) + (group % 2 * 32);

        WaveMatrix8x8 c00 = default;
        WaveMatrix8x8 c01 = default;
        WaveMatrix8x8 c02 = default;
        WaveMatrix8x8 c03 = default;
        WaveMatrix8x8 c10 = default;
        WaveMatrix8x8 c11 = default;
        WaveMatrix8x8 c12 = default;
        WaveMatrix8x8 c13 = default;

        for (int k = 0; k < stride; k += 16)
        {
            WaveMatrix8x8 a0 = WaveMatrix8x8.Load(a, row * stride + k, stride);
            WaveMatrix8x8 a1 = WaveMatrix8x8.Load(a, (row + 8) * stride + k, stride);
            WaveMatrix8x8 b0 = WaveMatrix8x8.Load(b, k * stride + column, stride);
            WaveMatrix8x8 b1 = WaveMatrix8x8.Load(b, k * stride + column + 8, stride);
            WaveMatrix8x8 b2 = WaveMatrix8x8.Load(b, k * stride + column + 16, stride);
            WaveMatrix8x8 b3 = WaveMatrix8x8.Load(b, k * stride + column + 24, stride);

            c00 = WaveMatrix8x8.MultiplyAccumulate(a0, b0, c00);
            c01 = WaveMatrix8x8.MultiplyAccumulate(a0, b1, c01);
            c02 = WaveMatrix8x8.MultiplyAccumulate(a0, b2, c02);
            c03 = WaveMatrix8x8.MultiplyAccumulate(a0, b3, c03);
            c10 = WaveMatrix8x8.MultiplyAccumulate(a1, b0, c10);
            c11 = WaveMatrix8x8.MultiplyAccumulate(a1, b1, c11);
            c12 = WaveMatrix8x8.MultiplyAccumulate(a1, b2, c12);
            c13 = WaveMatrix8x8.MultiplyAccumulate(a1, b3, c13);

            // The next 8 columns of a and rows of b, in the same iteration.
            a0 = WaveMatrix8x8.Load(a, row * stride + k + 8, stride);
            a1 = WaveMatrix8x8.Load(a, (row + 8) * stride + k + 8, stride);
            b0 = WaveMatrix8x8.Load(b, (k + 8) * stride + column, stride);
            b1 = WaveMatrix8x8.Load(b, (k + 8) * stride + column + 8, stride);
            b2 = WaveMatrix8x8.Load(b, (k + 8) * stride + column + 16, stride);
            b3 = WaveMatrix8x8.Load(b, (k + 8) * stride + column + 24, stride);

            c00 = WaveMatrix8x8.MultiplyAccumulate(a0, b0, c00);
            c01 = WaveMatrix8x8.MultiplyAccumulate(a0, b1, c01);
            c02 = WaveMatrix8x8.MultiplyAccumulate(a0, b2, c02);
            c03 = WaveMatrix8x8.MultiplyAccumulate(a0, b3, c03);
            c10 = WaveMatrix8x8.MultiplyAccumulate(a1, b0, c10);
            c11 = WaveMatrix8x8.MultiplyAccumulate(a1, b1, c11);
            c12 = WaveMatrix8x8.MultiplyAccumulate(a1, b2, c12);
            c13 = WaveMatrix8x8.MultiplyAccumulate(a1, b3, c13);
        }

        c00.Store(c, row * stride + column, stride);
        c01.Store(c, row * stride + column + 8, stride);
        c02.Store(c, row * stride + column + 16, stride);
        c03.Store(c, row * stride + column + 24, stride);
        c10.Store(c, (row + 8) * stride + column, stride);
        c11.Store(c, (row + 8) * stride + column + 8, stride);
        c12.Store(c, (row + 8) * stride + column + 16, stride);
        c13.Store(c, (row + 8) * stride + column + 24, stride);
    }
}

/// <summary>The inputs: two matrices of random floats in -1..1, different for every <paramref name="seed"/>.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GenerateRandomMatrices(ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, int n, uint seed) : IComputeShader
{
    /// <summary>24 random bits of <paramref name="hash"/> as a float in -1..1.</summary>
    private static float Signed(uint hash) => ((hash >> 8) * (1f / 8388608f)) - 1f;

    public void Execute()
    {
        int i = (ThreadIds.Y * n) + ThreadIds.X;
        uint h = ShaderMath.Hash((uint)i + (seed * 0x9E3779B9u));

        a[i] = Signed(h);
        b[i] = Signed(ShaderMath.Hash(h ^ 0x85EBCA6Bu));
    }
}

/// <summary>Draws the corner of the product under the grid of the wave matrices that computed it, and the bars of the three kernels.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawMatrixUnits(ReadWriteBuffer<uint> pixels, ReadWriteBuffer<float> c, ReadWriteBuffer<float> bars, int width, int n, int active, float scale) : IComputeShader
{
    // Everything sits on the canvas's 16-pixel text rows, so that labels land next to what they name.
    public const int PanelX = 32;
    public const int PanelY = 128;
    public const int PanelSize = 384;

    /// <summary>Elements of C along a side of the panel, and the pixels each one takes.</summary>
    public const int Elements = 96;
    public const int Zoom = PanelSize / Elements;

    public const int BarX = 456;
    public const int BarWidth = 792;
    public const int BarHeight = 24;
    public const int FirstBarY = 164;
    public const int BarPitch = 64;

    /// <summary>The top of the bar of kernel <paramref name="kernel"/>; with the number of kernels, where the text below the bars starts.</summary>
    public static int BarY(int kernel) => FirstBarY + (kernel * BarPitch);

    /// <summary>Blue for negative, gold for positive, dark at zero; <paramref name="value"/> in -1..1.</summary>
    private static Float3 Heat(float value)
    {
        float t = Gpu.Saturate(Gpu.Abs(value));
        Float3 hue = value < 0f ? new Float3(0.2f, 0.5f, 1f) : new Float3(1f, 0.78f, 0.3f);

        return Gpu.Lerp(new Float3(0.03f, 0.04f, 0.06f), hue, Gpu.Sqrt(t));
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int px = x - PanelX;
        int py = y - PanelY;

        if (px >= 0 && px < PanelSize && py >= 0 && py < PanelSize)
        {
            int column = px / Zoom;
            int row = py / Zoom;
            color = Heat(c[(row * n) + column] * scale);

            // The grid: every wave matrix, then the block of a SIMD group, then a threadgroup's tile.
            bool onColumn = px % Zoom == 0;
            bool onRow = py % Zoom == 0;

            if ((onColumn && column % 8 == 0) || (onRow && row % 8 == 0))
            {
                color = Gpu.Lerp(color, new Float3(0.02f, 0.03f, 0.05f), 0.75f);
            }

            if ((onColumn && column % 32 == 0) || (onRow && row % 16 == 0))
            {
                color = new Float3(0.3f, 0.55f, 0.95f);
            }

            if ((onColumn && column % MatMulWaveMatrix.TileColumns == 0) || (onRow && row % MatMulWaveMatrix.TileRows == 0))
            {
                color = new Float3(1f, 0.8f, 0.35f);
            }
        }

        int bx = x - BarX;
        int by = y - FirstBarY;

        if (bx >= 0 && bx < BarWidth && by >= 0 && by < 3 * BarPitch && by % BarPitch < BarHeight)
        {
            int kernel = by / BarPitch;
            Float3 fill = kernel == active ? new Float3(1f, 0.8f, 0.35f) : new Float3(0.3f, 0.55f, 0.95f);

            // The track of the bar, and the part of it the kernel's rate fills.
            color = bx < bars[kernel] * BarWidth ? fill : new Float3(0.1f, 0.12f, 0.16f);
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
