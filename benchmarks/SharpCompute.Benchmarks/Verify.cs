using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SharpCompute.Benchmarks.Shaders;
using SharpCompute.Benchmarks.Workloads;

namespace SharpCompute.Benchmarks;

/// <summary>One comparison made by --verify.</summary>
public sealed record Check(string Workload, string Size, string Variant, string Reference, string Metric, double MaxError, double Tolerance, string Detail)
{
    public bool Passed => MaxError <= Tolerance;
}

/// <summary>
/// Runs every workload once on the CPU and the GPU and compares them, following the project's parity
/// rule: bit-exactness is only expected of single arithmetic ops, so float results are compared with
/// a relative tolerance of 1e-5 (and bit-exact matches are reported as a detail). Mandelbrot compares
/// iteration counts (±1 allowed) against a CPU reference that fuses the same multiply-adds Metal's
/// default FP contraction does.
/// </summary>
public static class Verify
{
    public const double RelativeTolerance = 1e-5;

    public static List<Check> Run(GraphicsDevice device, Action<string>? log = null)
    {
        List<Check> checks = [];

        void Add(Check check)
        {
            checks.Add(check);
            log?.Invoke(Format(check));
        }

        string generatedEntry = ShaderInfo.CheckGeneratorRan();
        log?.Invoke($"generator: SaxpyGenerated -> kernel '{generatedEntry}' ({ShaderInfo.MslSource<SaxpyGenerated>().Length} chars of MSL), hand-written -> '{ShaderInfo.EntryPoint<SaxpyHandWritten>()}'");

        foreach (int n in Sizes.Saxpy)
        {
            VerifySaxpy(device, n, Add);
        }

        foreach (string size in Sizes.Mandelbrot)
        {
            VerifyMandelbrot(device, size, Add);
        }

        VerifyBlur(device, Add);
        VerifyMatMul(device, Add);

        foreach (int n in Sizes.NBody)
        {
            VerifyNBody(device, n, Add);
        }

        return checks;
    }

    public static string Format(Check c) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{(c.Passed ? "PASS" : "FAIL")}  {c.Workload,-11} {c.Size,-10} {c.Variant,-28} vs {c.Reference,-22} {c.Metric} = {c.MaxError:G3} (tol {c.Tolerance:G3})  {c.Detail}");

    public static string ToMarkdown(IReadOnlyList<Check> checks)
    {
        StringBuilder sb = new();
        sb.AppendLine("| Workload | Size | Variant | Reference | Metric | Max error | Tolerance | Detail | |");
        sb.AppendLine("|---|---|---|---|---|---:|---:|---|---|");

        foreach (Check c in checks)
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {c.Workload} | {c.Size} | {c.Variant} | {c.Reference} | {Cell(c.Metric)} | {c.MaxError:G3} | {c.Tolerance:G3} | {Cell(c.Detail)} | {(c.Passed ? "pass" : "**FAIL**")} |"));
        }

        return sb.ToString();

        static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static void VerifySaxpy(GraphicsDevice device, int n, Action<Check> add)
    {
        using Saxpy saxpy = new(n, device);
        string size = n.ToString("N0", CultureInfo.InvariantCulture);

        float[] fused = (float[])saxpy.Y.Clone();
        Saxpy.CpuFused(saxpy.X, fused);

        saxpy.CpuScalar();
        float[] unfused = (float[])saxpy.Y.Clone();

        saxpy.Reset();
        saxpy.CpuSimd();
        add(Compare("SAXPY", size, "CPU SIMD (TensorPrimitives)", "CPU scalar", saxpy.Y, unfused));

        saxpy.Reset();
        saxpy.CpuParallelSimd();
        add(Compare("SAXPY", size, "CPU Parallel.For + SIMD", "CPU scalar", saxpy.Y, unfused));

        saxpy.Reset();
        saxpy.GpuGenerated();
        float[] generated = saxpy.GpuY.ToArray();
        Check gpu = Compare("SAXPY", size, "GPU generated", "CPU scalar", generated, unfused);
        add(gpu with { Detail = $"{gpu.Detail}; {Compare("", "", "", "", generated, fused).Detail} vs CPU fma form" });

        saxpy.Reset();
        saxpy.GpuHandWritten();
        float[] handWritten = saxpy.GpuY.ToArray();
        add(Compare("SAXPY", size, "GPU hand-written MSL", "CPU scalar", handWritten, unfused));
        add(Compare("SAXPY", size, "GPU generated", "GPU hand-written MSL", generated, handWritten) with { Tolerance = 0, Metric = "max rel" });
    }

    private static void VerifyMandelbrot(GraphicsDevice device, string size, Action<Check> add)
    {
        (int width, int height) = Sizes.Parse(size);
        using Mandelbrot mandelbrot = new(width, height, device);

        mandelbrot.CpuScalar();
        int[] scalar = Counts(mandelbrot.Pixels);

        Array.Clear(mandelbrot.Pixels);
        mandelbrot.CpuParallel();
        add(CompareIterations(size, "CPU Parallel.For", "CPU scalar", Counts(mandelbrot.Pixels), scalar, ""));

        uint[] scalarPixels = (uint[])mandelbrot.Pixels.Clone();
        Array.Clear(mandelbrot.Pixels);
        mandelbrot.CpuParallelSimd();
        int mismatched = 0;

        for (int i = 0; i < scalarPixels.Length; i++)
        {
            mismatched += mandelbrot.Pixels[i] != scalarPixels[i] ? 1 : 0;
        }

        // Same unfused math in four lanes: every packed pixel must equal the scalar one.
        add(new Check("Mandelbrot", size, "CPU Parallel.For + SIMD", "CPU scalar", "pixels not identical", mismatched, 0,
            string.Create(CultureInfo.InvariantCulture, $"{mismatched} of {scalarPixels.Length} packed RGBA pixels differ (exact match required)")));

        mandelbrot.Gpu();
        int[] gpu = Counts(mandelbrot.GpuPixels.AsSpan());
        (int differing, int maxDelta) = Diff(gpu, scalar);
        string unfused = string.Create(CultureInfo.InvariantCulture,
            $"; vs unfused CPU scalar: {differing} pixels ({100.0 * differing / gpu.Length:F3} %) differ, max Δ {maxDelta} (chaotic boundary orbits)");
        add(CompareIterations(size, "GPU generated", "CPU fma form", gpu, mandelbrot.CpuFusedIterations(), unfused));
    }

    private static int[] Counts(ReadOnlySpan<uint> pixels)
    {
        int[] counts = new int[pixels.Length];

        for (int i = 0; i < counts.Length; i++)
        {
            counts[i] = Mandelbrot.Iterations(pixels[i]);
        }

        return counts;
    }

    private static (int Differing, int MaxDelta) Diff(int[] actual, int[] expected)
    {
        int differing = 0, maxDelta = 0;

        for (int i = 0; i < expected.Length; i++)
        {
            int delta = Math.Abs(actual[i] - expected[i]);
            differing += delta != 0 ? 1 : 0;
            maxDelta = Math.Max(maxDelta, delta);
        }

        return (differing, maxDelta);
    }

    private static Check CompareIterations(string size, string variant, string reference, int[] actual, int[] expected, string extra)
    {
        (int differing, int maxDelta) = Diff(actual, expected);
        string detail = string.Create(CultureInfo.InvariantCulture, $"{differing} of {expected.Length} pixels differ{extra}");
        return new Check("Mandelbrot", size, variant, reference, "max |Δiterations|", maxDelta, 1, detail);
    }

    private static void VerifyBlur(GraphicsDevice device, Action<Check> add)
    {
        using GaussianBlur blur = new(Sizes.BlurWidth, Sizes.BlurHeight, Sizes.BlurRadius, device);
        string size = $"{Sizes.BlurWidth}x{Sizes.BlurHeight}";

        blur.CpuScalar();
        float[] reference = (float[])blur.Output.Clone();

        Array.Clear(blur.Output);
        blur.CpuParallel();
        add(Compare("Blur", size, "CPU Parallel.For", "CPU scalar", blur.Output, reference));

        Array.Clear(blur.Output);
        blur.CpuParallelSimd();
        add(Compare("Blur", size, "CPU Parallel.For + SIMD", "CPU scalar", blur.Output, reference));

        blur.Gpu();
        add(Compare("Blur", size, "GPU generated", "CPU scalar", blur.GpuOutput.AsSpan(), reference));
    }

    private static void VerifyMatMul(GraphicsDevice device, Action<Check> add)
    {
        using MatMul matmul = new(Sizes.MatMul, device);
        string size = $"{Sizes.MatMul}x{Sizes.MatMul}";

        matmul.CpuParallel();
        float[] reference = (float[])matmul.C.Clone();

        Array.Clear(matmul.C);
        matmul.CpuParallelSimd();
        add(Compare("MatMul", size, "CPU Parallel.For + SIMD", "CPU Parallel.For", matmul.C, reference));

        matmul.Gpu();
        add(Compare("MatMul", size, "GPU generated", "CPU Parallel.For", matmul.GpuC.AsSpan(), reference));
    }

    private static void VerifyNBody(GraphicsDevice device, int n, Action<Check> add)
    {
        using NBody nbody = new(n, device);
        string size = $"{n} bodies";

        nbody.CpuParallel();
        Float4[] refPositions = (Float4[])nbody.NextPositions.Clone();
        Float4[] refVelocities = (Float4[])nbody.NextVelocities.Clone();

        Array.Clear(nbody.NextPositions);
        Array.Clear(nbody.NextVelocities);
        nbody.CpuParallelSimd();
        add(CompareVectors(size, "CPU Parallel.For + SIMD", "positions", nbody.NextPositions, refPositions));
        add(CompareVectors(size, "CPU Parallel.For + SIMD", "velocities", nbody.NextVelocities, refVelocities));

        nbody.Gpu();
        add(CompareVectors(size, "GPU generated", "positions", nbody.GpuNextPositions.AsSpan(), refPositions));
        add(CompareVectors(size, "GPU generated", "velocities", nbody.GpuNextVelocities.AsSpan(), refVelocities));
    }

    /// <summary>Element-wise relative error |a − e| / |e|, plus how many elements are bit-identical.</summary>
    private static Check Compare(string workload, string size, string variant, string reference, ReadOnlySpan<float> actual, ReadOnlySpan<float> expected)
    {
        double maxRel = 0;
        int exact = 0;

        for (int i = 0; i < expected.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(actual[i]) == BitConverter.SingleToInt32Bits(expected[i]))
            {
                exact++;
                continue;
            }

            double e = expected[i];
            double rel = Math.Abs(actual[i] - e) / Math.Max(Math.Abs(e), 1e-30);
            maxRel = double.IsNaN(rel) ? double.PositiveInfinity : Math.Max(maxRel, rel);
        }

        string detail = string.Create(CultureInfo.InvariantCulture, $"{100.0 * exact / expected.Length:F2} % bit-exact");
        return new Check(workload, size, variant, reference, "max rel", maxRel, RelativeTolerance, detail);
    }

    /// <summary>
    /// Norm-wise relative error of the xyz part per body, ‖a − e‖ / max(‖e‖, rms‖e‖). The floor at the
    /// RMS magnitude over all bodies keeps a body that happens to sit near the origin (or at rest) from
    /// turning a rounding-level difference into a huge relative one. The w lane is copied through and
    /// must match exactly.
    /// </summary>
    private static Check CompareVectors(string size, string variant, string what, ReadOnlySpan<Float4> actual, ReadOnlySpan<Float4> expected)
    {
        double sumSquares = 0;

        foreach (Float4 e in expected)
        {
            sumSquares += ((double)e.X * e.X) + ((double)e.Y * e.Y) + ((double)e.Z * e.Z);
        }

        double rms = Math.Sqrt(sumSquares / expected.Length);
        double maxRel = 0;

        for (int i = 0; i < expected.Length; i++)
        {
            Float4 a = actual[i], e = expected[i];
            double dx = a.X - e.X, dy = a.Y - e.Y, dz = a.Z - e.Z;
            double norm = Math.Sqrt(((double)e.X * e.X) + ((double)e.Y * e.Y) + ((double)e.Z * e.Z));
            double rel = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / Math.Max(Math.Max(norm, rms), 1e-30);

            if (a.W != e.W || double.IsNaN(rel))
            {
                rel = double.PositiveInfinity;
            }

            maxRel = Math.Max(maxRel, rel);
        }

        string detail = string.Create(CultureInfo.InvariantCulture, $"per body, floor = rms ‖{what}‖ = {rms:G3}");
        return new Check("N-body", size, variant, "CPU Parallel.For", $"max rel ‖Δ{what}‖", maxRel, RelativeTolerance, detail);
    }
}
