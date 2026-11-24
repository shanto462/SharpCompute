// Original SharpCompute sample (MIT).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SharpCompute.Samples.Imaging;
using static System.FormattableString;

namespace SharpCompute.Samples.ImageProcessing;

/// <summary>
/// <c>imgproc</c>: loads a JPEG/PNG/HEIC through macOS ImageIO, runs a GPU filter and saves the
/// result; with <c>--compare</c> it also runs the same filter on the CPU and reports the speedup.
/// </summary>
public static class Program
{
    /// <summary>Exit code when <c>--compare</c> finds the GPU and CPU disagree by more than <see cref="ParityLimit"/>.</summary>
    public const int ParityFailure = 4;

    /// <summary>The largest per-channel GPU/CPU difference <c>--compare</c> accepts: one 8-bit step.</summary>
    public const float ParityLimit = 1f / 255f;

    private const int TimedRuns = 3;

    private const string Usage = """
        SharpCompute image processing (imgproc): GPU filters on photos, timed against the CPU.

          imgproc <input> <output> <filter> [args...] [--compare]
          imgproc --list
          imgproc --make-test-image <output> <W>x<H>

        <input>   any image macOS ImageIO reads (JPEG, PNG, HEIC, TIFF, ...)
        <output>  .png, .jpg/.jpeg or .heic
        --compare also run the filter on the CPU (Parallel.For, same math), print both timings and
                  the speedup, and check that no channel differs by more than 1/255

        Run it with: dotnet run -c Release --project samples/SharpCompute.Samples.ImageProcessing -- <args>
        """;

    public static int Main(string[] args)
    {
        bool compare = args.Contains("--compare");
        string[] positional = [.. args.Where(static a => a != "--compare")];

        try
        {
            switch (positional)
            {
                case [] or ["-h" or "--help"]:
                    Console.WriteLine(Usage);
                    return positional.Length == 0 ? 1 : 0;

                case ["--list"]:
                    foreach (FilterInfo info in FilterSpec.All)
                    {
                        string defaults = info.Parameters.Count == 0
                            ? ""
                            : $"  (defaults: {string.Join(", ", info.Parameters.Select(static p => Invariant($"{p.Name} {p.Default}")))})";
                        Console.WriteLine($"  {info.Usage,-40} {info.Description}{defaults}");
                    }

                    return 0;

                case ["--make-test-image", string path, string size]:
                    return MakeTestImage(path, size);

                case [string input, string output, string filter, .. string[] filterArgs] when !input.StartsWith("--", StringComparison.Ordinal):
                    if (!FilterSpec.TryParse(filter, filterArgs, out FilterSpec? spec, out string error))
                    {
                        Console.Error.WriteLine(error);
                        return 1;
                    }

                    return Process(input, output, spec!, compare);

                default:
                    Console.Error.WriteLine($"Unrecognized arguments: {string.Join(' ', args)}");
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    /// <summary>The largest absolute per-channel difference between two images.</summary>
    public static float MaxDifference(ReadOnlySpan<Float4> a, ReadOnlySpan<Float4> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("The images differ in size.");
        }

        float max = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            Float4 d = a[i] - b[i];
            max = MathF.Max(max, MathF.Max(MathF.Max(MathF.Abs(d.X), MathF.Abs(d.Y)), MathF.Max(MathF.Abs(d.Z), MathF.Abs(d.W))));
        }

        return max;
    }

    private static int MakeTestImage(string path, string size)
    {
        if (!TryParseSize(size, out int width, out int height))
        {
            Console.Error.WriteLine($"Invalid size '{size}': expected <W>x<H>, each 1..16384.");
            return 1;
        }

        long start = Stopwatch.GetTimestamp();
        ImageData image = TestImage.Create(width, height);
        ImageFile.Save(path, image);
        Console.WriteLine(Invariant($"Wrote {width}×{height} test image to {Path.GetFullPath(path)} in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms"));
        return 0;
    }

    private static int Process(string inputPath, string outputPath, FilterSpec filter, bool compare)
    {
        if (!GraphicsDevice.IsSupported)
        {
            Console.Error.WriteLine("No Metal device: SharpCompute needs macOS 15+ on Apple Silicon.");
            return 1;
        }

        long start = Stopwatch.GetTimestamp();
        ImageData input = ImageFile.Load(inputPath);
        double loadMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        int width = input.Width;
        int height = input.Height;

        Console.WriteLine(Invariant($"Input:   {Path.GetFileName(inputPath)} {width}×{height} ({width * (double)height / 1e6:F1} MP), loaded in {loadMs:F0} ms"));

        GraphicsDevice device = GraphicsDevice.GetDefault();
        Console.WriteLine($"Filter:  {filter} on {device.Name}");

        // Upload = allocating the shared buffers and copying the pixels in (unified memory, but still a copy).
        long uploadStart = Stopwatch.GetTimestamp();
        using ReadWriteBuffer<Float4> source = device.AllocateReadWriteBuffer<Float4>(input.Pixels);
        using ReadWriteBuffer<Float4> destination = device.AllocateReadWriteBuffer<Float4>(input.Pixels.Length);
        double uploadMs = Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds;
        using GpuFilters gpu = new(device, width, height);

        double firstMs = Time(() => gpu.Apply(filter, source, destination));
        double gpuMs = firstMs;

        if (!compare)
        {
            Console.WriteLine(Invariant($"GPU:     {firstMs:F1} ms (first run, includes compiling the shaders)"));
        }
        else
        {
            gpuMs = Enumerable.Range(0, TimedRuns).Select(_ => Time(() => gpu.Apply(filter, source, destination))).Min();
            Console.WriteLine(Invariant($"GPU:     {gpuMs:F2} ms (best of {TimedRuns}; the first run took {firstMs:F1} ms including shader compilation)"));
        }

        long readBackStart = Stopwatch.GetTimestamp();
        ImageData output = new(width, height, destination.ToArray());
        double readBackMs = Stopwatch.GetElapsedTime(readBackStart).TotalMilliseconds;
        int exitCode = 0;

        if (compare)
        {
            // Like-for-like with the GPU: one warm-up run, then best of TimedRuns.
            Float4[] cpuResult = new Float4[input.Pixels.Length];
            CpuFilters.Apply(filter, input.Pixels, cpuResult, width, height);
            double cpuMs = Enumerable.Range(0, TimedRuns).Select(_ => Time(() => CpuFilters.Apply(filter, input.Pixels, cpuResult, width, height))).Min();
            double endToEndMs = uploadMs + gpuMs + readBackMs;

            Console.WriteLine(Invariant($"Copies:  upload {uploadMs:F1} ms, read-back {readBackMs:F1} ms (not in the GPU time above; a GPU pipeline can avoid read-back with AsSpan)"));
            Console.WriteLine(Invariant($"CPU:     {cpuMs:F1} ms (best of {TimedRuns} after a warm-up; Parallel.For over rows, {Environment.ProcessorCount} threads, same plain Float4 math, not SIMD-tuned)"));
            Console.WriteLine(Invariant($"Speedup: {cpuMs / gpuMs:F1}× kernel only, {cpuMs / endToEndMs:F1}× end to end (upload + kernel + read-back)"));

            float difference = MaxDifference(output.Pixels, cpuResult);

            if (difference <= ParityLimit)
            {
                Console.WriteLine(Invariant($"Parity:  max |GPU − CPU| = {difference:G3} per channel (limit 1/255 = {ParityLimit:G3}) ✓"));
            }
            else
            {
                int over = 0;

                for (int i = 0; i < cpuResult.Length; i++)
                {
                    over += MaxDifference([output.Pixels[i]], [cpuResult[i]]) > ParityLimit ? 1 : 0;
                }

                Console.WriteLine(Invariant(
                    $"Parity:  FAILED: max |GPU − CPU| = {difference:G3} per channel exceeds 1/255; {over} of {cpuResult.Length} pixels differ by more"));
                exitCode = ParityFailure;
            }
        }

        start = Stopwatch.GetTimestamp();
        ImageFile.Save(outputPath, output);
        Console.WriteLine(Invariant($"Saved:   {Path.GetFullPath(outputPath)} in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms"));
        return exitCode;
    }

    private static double Time(Action action)
    {
        long start = Stopwatch.GetTimestamp();
        action();
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static bool TryParseSize(string text, out int width, out int height)
    {
        string[] parts = text.Split('x', 'X');
        width = height = 0;

        return parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height) &&
            width is > 0 and <= 16384 && height is > 0 and <= 16384;
    }
}
