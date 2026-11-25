// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SharpCompute.Presentation;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages.SharedMemory;

/// <summary>
/// A matrix product, <c>C = A × B</c> for 1024 × 1024 floats, two ways. Naive: every thread walks its
/// row of A and its column of B in the buffers, a million threads each reading 2,048 values from all
/// over device memory. Tiled: a threadgroup computes a 16 × 16 tile of C, and for each step along the
/// shared dimension its 256 threads together load one tile of A and one of B into group-shared memory,
/// one value each, then every thread multiplies its row by its column out of that small, fast memory.
/// </summary>
/// <remarks>
/// Both do the same 2 × 1024³ floating-point operations, so the page reports GFLOP/s for each. The
/// tiled kernel reads 2 values per thread and step from the buffers instead of 32.
/// </remarks>
public sealed class MatMulPage : ILabPage, IRatedPage
{
    public const int N = 1024;

    /// <summary>How many rows of C the check recomputes in double precision (evenly spaced).</summary>
    public const int CheckedRows = 32;

    /// <summary>Floating-point operations of one product: a multiply and an add per term.</summary>
    public const double Flops = 2.0 * N * N * N;

    // The matrices move in steps, and the product is only recomputed when they have changed: a
    // gigaflop-scale kernel every frame would keep the GPU busy for nothing.
    private const float StepsPerSecond = 4f;
    private const int OtherVariantInterval = 20;

    private static readonly string[] s_variants = ["16 x 16 tiles in shared memory", "naive: every thread reads its row and column"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<float> _a;
    private readonly ReadWriteBuffer<float> _b;
    private readonly ReadWriteBuffer<float> _c;
    private readonly double[] _variantMilliseconds = new double[2];
    private readonly float[] _cpuA = new float[N * N];
    private readonly float[] _cpuB = new float[N * N];
    private float _computedStep = float.NaN;
    private int _computedVariant = -1;
    private int _products;

    public MatMulPage(GraphicsDevice device)
    {
        _device = device;
        _a = device.AllocateReadWriteBuffer<float>(N * N);
        _b = device.AllocateReadWriteBuffer<float>(N * N);
        _c = device.AllocateReadWriteBuffer<float>(N * N);
    }

    public string Name => "MatMul";

    public string Feature => "tiled product";


    public string Topic => LabTopics.SharedMemory;

    public string Help => "V variant";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>The largest difference between the GPU's and the double-precision elements at the last <see cref="Check"/>.</summary>
    public double MaxError { get; private set; }

    /// <summary>GFLOP/s of a product that took <paramref name="milliseconds"/>.</summary>
    public static double GigaFlops(double milliseconds) => milliseconds <= 0 ? 0 : Flops / (milliseconds * 1e6);

    public string Rate(double gpuMilliseconds, double cpuMilliseconds) =>
        Invariant($"GPU {GigaFlops(gpuMilliseconds):F0} GFLOP/s   CPU {GigaFlops(cpuMilliseconds):F0} GFLOP/s");

    public void Compute(LabFrame frame)
    {
        float step = frame.Deterministic ? frame.Time : MathF.Floor(frame.Time * StepsPerSecond) / StepsPerSecond;

        if (!frame.Deterministic && step == _computedStep && Variant == _computedVariant)
        {
            return;
        }

        _device.For(N, N, new GenerateMatrices(_a, _b, N, step));

        if (!frame.Deterministic && _products++ % OtherVariantInterval == 0)
        {
            _variantMilliseconds[1 - Variant] = Multiply(1 - Variant);
        }

        GpuMilliseconds = Multiply(Variant);
        _variantMilliseconds[Variant] = GpuMilliseconds;
        (_computedStep, _computedVariant) = (step, Variant);
    }

    private double Multiply(int variant)
    {
        long start = Stopwatch.GetTimestamp();

        if (variant == 0)
        {
            int size = (N + MatMulTiled.Tile - 1) / MatMulTiled.Tile * MatMulTiled.Tile;
            _device.For(size, size, new MatMulTiled(_a, _b, _c, N));
        }
        else
        {
            _device.For(N, N, new MatMulNaive(_a, _b, _c, N));
        }

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public LabCheck Check()
    {
        float[] a = _cpuA, b = _cpuB;
        _a.CopyTo(a);
        _b.CopyTo(b);
        float[] gpu = _c.ToArray();

        // The same product in C#, for a sample of the rows: one in every 32. That is 3 % of the work,
        // enough to time the CPU and to catch a wrong tile anywhere, without a second of all cores.
        float[] cpuRows = new float[CheckedRows * N];
        long start = Stopwatch.GetTimestamp();
        Parallel.For(0, CheckedRows, r => CpuMatMul.Row(a, b, N, r * (N / CheckedRows), cpuRows.AsSpan(r * N, N)));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds * (N / (double)CheckedRows);

        // And in double precision, with the bound a float sum of 1024 products can be off by.
        double[] errors = new double[CheckedRows];
        bool[] within = new bool[CheckedRows];

        Parallel.For(0, CheckedRows, r =>
        {
            int row = r * (N / CheckedRows);
            double[] exact = new double[N];
            double[] magnitude = new double[N];
            CpuMatMul.RowExact(a, b, N, row, exact, magnitude);

            bool ok = true;
            double worst = 0;

            for (int column = 0; column < N; column++)
            {
                double error = Math.Abs(gpu[(row * N) + column] - exact[column]);
                worst = Math.Max(worst, error);
                ok &= error <= (1e-4 * magnitude[column]) + 1e-5 && Math.Abs(cpuRows[(r * N) + column] - exact[column]) <= (1e-4 * magnitude[column]) + 1e-5;
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
            ? Invariant($"{CheckedRows} of {N} rows match C# doubles, max error {MaxError:E1} (CPU: all cores, scaled from {CheckedRows} rows)")
            : Invariant($"{CheckedRows} of {N} rows checked: off by up to {MaxError:E1}, more than float rounding allows");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawMatMul(canvas.Pixels, _a, _b, _c, LabCanvas.Width, N));

        int labelY = DrawMatMul.PanelY - 24;
        int below = DrawMatMul.PanelY + DrawMatMul.PanelSize + 8;
        canvas.Label(DrawMatMul.PanelX(0), labelY, "A: 1024 x 1024 floats");
        canvas.Label(DrawMatMul.PanelX(1), labelY, "B");
        canvas.Label(DrawMatMul.PanelX(2), labelY, "C = A x B");
        canvas.Label(DrawMatMul.PanelX(0), below, "blue: negative   gold: positive", TextColor.Dim);
        canvas.Label(DrawMatMul.PanelX(2), below, "one 16 x 16 tile per threadgroup", TextColor.Accent);

        int text = below + 40;
        canvas.Label(DrawMatMul.PanelX(0), text, "For each of the 64 tiles along k, the 256 threads of a group load one value of A and one of B each:", TextColor.White);
        canvas.Label(DrawMatMul.PanelX(0) + (4 * LabCanvas.CellWidth), text + 16, "tileA[y, x] = A[row, k0 + x];  tileB[y, x] = B[k0 + y, column];  barrier();", TextColor.Cool);
        canvas.Label(DrawMatMul.PanelX(0) + (4 * LabCanvas.CellWidth), text + 32, "for (j = 0; j < 16; j++) sum += tileA[y, j] * tileB[j, x];        barrier();", TextColor.Cool);

        if (!frame.Deterministic && _variantMilliseconds[0] > 0 && _variantMilliseconds[1] > 0)
        {
            canvas.Label(
                DrawMatMul.PanelX(0),
                text + 64,
                Invariant($"shared-memory tiles {_variantMilliseconds[0],7:F1} ms = {GigaFlops(_variantMilliseconds[0]),6:F1} GFLOP/s"),
                Variant == 0 ? TextColor.White : TextColor.Dim);
            canvas.Label(
                DrawMatMul.PanelX(1) + (9 * LabCanvas.CellWidth),
                text + 64,
                Invariant($"naive {_variantMilliseconds[1],7:F1} ms = {GigaFlops(_variantMilliseconds[1]),6:F1} GFLOP/s"),
                Variant == 1 ? TextColor.White : TextColor.Dim);
        }
        else
        {
            canvas.Label(DrawMatMul.PanelX(0), text + 64, "Both kernels do the same 2 x 1024^3 = 2.1 billion floating-point operations per product.", TextColor.Accent);
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
        _a.Dispose();
        _b.Dispose();
        _c.Dispose();
    }
}

/// <summary>The matrix product in plain C#, one row of the result at a time.</summary>
public static class CpuMatMul
{
    /// <summary>
    /// Row <paramref name="row"/> of <c>a × b</c> in floats. Walks <c>b</c> row by row (not column by
    /// column), so the inner loop is contiguous and the JIT can vectorize it.
    /// </summary>
    public static void Row(ReadOnlySpan<float> a, ReadOnlySpan<float> b, int n, int row, Span<float> result)
    {
        result.Clear();

        for (int k = 0; k < n; k++)
        {
            float scale = a[(row * n) + k];
            ReadOnlySpan<float> source = b.Slice(k * n, n);

            for (int column = 0; column < result.Length; column++)
            {
                result[column] += scale * source[column];
            }
        }
    }

    /// <summary>
    /// The same row in double precision, and for every element the sum of the magnitudes of its terms:
    /// a float sum of <c>n</c> products can differ from the exact one by about <c>n × 2^-24</c> of that.
    /// </summary>
    public static void RowExact(ReadOnlySpan<float> a, ReadOnlySpan<float> b, int n, int row, Span<double> result, Span<double> magnitude)
    {
        result.Clear();
        magnitude.Clear();

        for (int k = 0; k < n; k++)
        {
            double scale = a[(row * n) + k];
            ReadOnlySpan<float> source = b.Slice(k * n, n);

            for (int column = 0; column < result.Length; column++)
            {
                double term = scale * source[column];
                result[column] += term;
                magnitude[column] += Math.Abs(term);
            }
        }
    }
}

/// <summary>The inputs: two smooth, slowly moving matrices with some grain, values in -1..1.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GenerateMatrices(ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, int n, float time) : IComputeShader
{
    private static float Grain(uint x, uint y)
    {
        uint h = x + (y * 0x9E3779B9u);
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return ((h >> 8) * (1f / 8388608f)) - 1f;
    }

    public void Execute()
    {
        int column = ThreadIds.X;
        int row = ThreadIds.Y;
        Float2 uv = new Float2(column, row) / n;
        int i = (row * n) + column;

        // A: diagonal bands (close to a blur along the diagonal); B: rings around a wandering center.
        float bands = Gpu.Cos(((uv.X - uv.Y) * 9f) + (time * 0.7f)) * Gpu.Exp(-6f * Gpu.Abs(uv.X - uv.Y));
        Float2 center = new Float2(0.5f + (0.25f * Gpu.Cos(time * 0.5f)), 0.5f + (0.25f * Gpu.Sin(time * 0.4f)));
        float rings = Gpu.Sin((Gpu.Length(uv - center) * 24f) - time);

        a[i] = Gpu.Clamp((0.85f * bands) + (0.15f * Grain((uint)column, (uint)row)), -1f, 1f);
        b[i] = Gpu.Clamp((0.8f * rings) + (0.2f * Grain((uint)row + 7919u, (uint)column)), -1f, 1f);
    }
}

/// <summary>
/// The feature kernel: one 16 × 16 tile of <c>c = a × b</c> per threadgroup. For each tile along the
/// shared dimension, every thread loads one element of the tile of <c>a</c> its row needs and one of
/// the tile of <c>b</c> its column needs into group-shared memory; after a barrier each thread adds
/// the 16 products of its row and its column from there; a second barrier keeps the next tile's loads
/// from overwriting values that are still being read.
/// </summary>
/// <remarks>
/// Dispatched over a multiple of 16 in both dimensions; elements outside the matrices load 0, so any
/// <paramref name="n"/> works. Every thread runs the same number of loop iterations and barriers.
/// </remarks>
[ThreadGroupSize(MatMulTiled.Tile, MatMulTiled.Tile)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatMulTiled(ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, ReadWriteBuffer<float> c, int n) : IComputeShader
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

/// <summary>The same product without shared memory: every thread reads its whole row and column from the buffers.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatMulNaive(ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, ReadWriteBuffer<float> c, int n) : IComputeShader
{
    public void Execute()
    {
        int column = ThreadIds.X;
        int row = ThreadIds.Y;
        float sum = 0f;

        for (int k = 0; k < n; k++)
        {
            sum += a[(row * n) + k] * b[(k * n) + column];
        }

        c[(row * n) + column] = sum;
    }
}

/// <summary>Draws the three matrices as heat maps.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawMatMul(ReadWriteBuffer<uint> pixels, ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, ReadWriteBuffer<float> c, int width, int n) : IComputeShader
{
    public const int PanelY = 104;
    public const int PanelSize = 384;
    public const int PanelPitch = 416;
    public const int FirstPanelX = 32;

    /// <summary>The left edge of panel 0, 1 or 2.</summary>
    public static int PanelX(int panel) => FirstPanelX + (panel * PanelPitch);

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
        int py = y - PanelY;
        int px = x - FirstPanelX;

        if (py >= 0 && py < PanelSize && px >= 0)
        {
            int panel = px / PanelPitch;
            int u = px % PanelPitch;

            if (panel < 3 && u < PanelSize)
            {
                // The panel shows the matrix at 3/8 scale: element (u, v) * 8 / 3, row down, column right.
                int element = (Gpu.Min(n - 1, py * n / PanelSize) * n) + Gpu.Min(n - 1, u * n / PanelSize);

                if (panel == 0)
                {
                    color = Heat(a[element]);
                }
                else if (panel == 1)
                {
                    color = Heat(b[element]);
                }
                else
                {
                    // An element of C sums n products of values up to 1; these matrices reach about n / 16.
                    color = Heat(c[element] * (16f / n));
                }
            }
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
