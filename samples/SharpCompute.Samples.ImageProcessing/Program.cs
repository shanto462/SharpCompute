// Original SharpCompute sample (MIT).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using SharpCompute.Imaging;
using SharpCompute.Samples.Imaging;
using static System.FormattableString;

namespace SharpCompute.Samples.ImageProcessing;

/// <summary>
/// <c>imgproc</c>: decodes a JPEG/PNG/HEIC with SharpCompute.Imaging straight into a texture, runs a
/// GPU filter and encodes the result from a texture in GPU memory, with no copies in between; with
/// <c>--compare</c> it also runs the same filter on the CPU and reports the speedups.
/// </summary>
public static class Program
{
    /// <summary>Exit code when <c>--compare</c> finds the GPU and CPU disagree by more than <see cref="ParityLimit"/>.</summary>
    public const int ParityFailure = 4;

    /// <summary>The largest per-channel GPU/CPU difference <c>--compare</c> accepts: one 8-bit step.</summary>
    public const float ParityLimit = 1f / 255f;

    private const int TimedRuns = 5;

    private const string Usage = """
        SharpCompute image processing (imgproc): GPU filters on photos, timed against the CPU.

          imgproc <input> <output> <filter> [args...] [--compare]
          imgproc --list
          imgproc --make-test-image <output> <W>x<H>

        <input>   any image macOS ImageIO reads (JPEG, PNG, HEIC, TIFF, ...)
        <output>  .png, .jpg/.jpeg or .heic
        --compare also run the filter on the CPU (Parallel.For, same math), time the whole pipeline
                  (decoded texture → filter → texture to encode) zero copy, with copies and on the
                  CPU, and check that no channel differs by more than 1/255

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

        GraphicsDevice device = GraphicsDevice.GetDefault();

        // Zero copy end to end: ImageIO decodes into the input texture's shared memory, the GPU
        // converts to premultiplied floats, filters and converts back into the output texture, and
        // ImageIO encodes that texture in place.
        long start = Stopwatch.GetTimestamp();
        using ReadOnlyTexture2D<Rgba32> input = ImageLoader.LoadTexture2D<Rgba32>(device, inputPath);
        double loadMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        int width = input.Width;
        int height = input.Height;
        int pixelCount = width * height;

        Console.WriteLine(Invariant($"Input:   {Path.GetFileName(inputPath)} {width}×{height} ({pixelCount / 1e6:F1} MP), decoded into a texture in {loadMs:F0} ms"));
        Console.WriteLine($"Filter:  {filter} on {device.Name}");

        using ReadWriteBuffer<Float4> source = device.AllocateReadWriteBuffer<Float4>(pixelCount);
        using ReadWriteBuffer<Float4> destination = device.AllocateReadWriteBuffer<Float4>(pixelCount);
        using ReadWriteTexture2D<Rgba32> output = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using GpuFilters gpu = new(device, width, height);

        void ZeroCopyPipeline()
        {
            device.For(width, height, new UnpackTexture(input, source));
            gpu.Apply(filter, source, destination);
            device.For(width, height, new PackTexture(destination, output));
        }

        double firstMs = Time(ZeroCopyPipeline);
        int exitCode = 0;

        if (!compare)
        {
            Console.WriteLine(Invariant($"GPU:     {firstMs:F1} ms (texture → floats → filter → texture, first run, includes compiling the shaders)"));
        }
        else
        {
            exitCode = Compare(filter, gpu, input, source, destination, width, height, ZeroCopyPipeline, firstMs);
        }

        start = Stopwatch.GetTimestamp();
        ImageSaver.Save(output, ImageFile.PrepareOutput(outputPath), jpegQuality: ImageFile.Quality);
        Console.WriteLine(Invariant($"Saved:   {Path.GetFullPath(outputPath)} in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms (encoded from the output texture in place)"));
        return exitCode;
    }

    /// <summary>
    /// Times three ways to get from decoded RGBA8 pixels to filtered RGBA8 pixels, all warm and best
    /// of <see cref="TimedRuns"/>: the zero-copy GPU pipeline, the same GPU filter fed through
    /// managed arrays (CPU conversions plus copies in and out of the buffers, the only option without
    /// decoding into GPU memory), and the CPU. Then checks GPU/CPU parity on the filter output.
    /// </summary>
    private static int Compare(
        FilterSpec filter, GpuFilters gpu, ReadOnlyTexture2D<Rgba32> input, ReadWriteBuffer<Float4> source,
        ReadWriteBuffer<Float4> destination, int width, int height, Action zeroCopyPipeline, double firstMs)
    {
        Rgba32[] decoded = input.ToArray();

        double zeroCopyMs = Best(zeroCopyPipeline);
        double kernelMs = Best(() => gpu.Apply(filter, source, destination));

        // Every array is allocated once up front, so the timings measure conversions and copies,
        // not the allocator or the GC (a 4K image is a 133 MB Float4 array).
        Float4[] cpuInput = new Float4[source.Length];
        Float4[] staged = new Float4[source.Length];
        Float4[] cpuResult = new Float4[source.Length];
        Rgba32[] encoded = new Rgba32[source.Length];

        void Encode(Float4[] pixels) => PixelConversion.ToStraightRgba32(pixels, encoded);

        void CopyPathPipeline()
        {
            PixelConversion.FromStraightRgba32(decoded, staged);
            source.CopyFrom(staged);
            gpu.Apply(filter, source, destination);
            destination.CopyTo(staged);
            Encode(staged);
        }

        void CpuPipeline()
        {
            PixelConversion.FromStraightRgba32(decoded, cpuInput);
            CpuFilters.Apply(filter, cpuInput, cpuResult, width, height);
            Encode(cpuResult);
        }

        CopyPathPipeline();
        double copyPathMs = Best(CopyPathPipeline);

        PixelConversion.FromStraightRgba32(decoded, cpuInput);
        CpuFilters.Apply(filter, cpuInput, cpuResult, width, height);
        double cpuKernelMs = Best(() => CpuFilters.Apply(filter, cpuInput, cpuResult, width, height));
        CpuPipeline();
        double cpuMs = Best(CpuPipeline);

        // The timed runs above overwrote the outputs; refresh them from the zero-copy pipeline last,
        // so what is saved and checked is the GPU's.
        zeroCopyPipeline();
        CpuFilters.Apply(filter, cpuInput, cpuResult, width, height);

        Console.WriteLine(Invariant($"GPU:     filter {kernelMs:F2} ms (best of {TimedRuns}; the first full run took {firstMs:F1} ms including shader compilation)"));
        Console.WriteLine(Invariant($"CPU:     filter {cpuKernelMs:F1} ms (best of {TimedRuns} after a warm-up; Parallel.For over rows, {Environment.ProcessorCount} threads, same plain Float4 math, not SIMD-tuned)"));
        Console.WriteLine("Pipeline: decoded RGBA8 → floats → filter → RGBA8 ready to encode (best of 5, warm):");
        Console.WriteLine(Invariant($"  GPU, zero copy    {zeroCopyMs,8:F2} ms  conversions on the GPU, from the decoded texture to the one ImageIO encodes"));
        Console.WriteLine(Invariant($"  GPU, with copies  {copyPathMs,8:F2} ms  CPU conversions + copies into and out of the GPU buffers"));
        Console.WriteLine(Invariant($"  CPU               {cpuMs,8:F2} ms  CPU conversions + CPU filter"));
        Console.WriteLine(Invariant($"Speedup: {cpuKernelMs / kernelMs:F1}× filter only; pipeline {cpuMs / zeroCopyMs:F1}× zero copy, {cpuMs / copyPathMs:F1}× with copies"));

        float difference = MaxDifference(destination.AsReadOnlySpan(), cpuResult);

        if (difference <= ParityLimit)
        {
            Console.WriteLine(Invariant($"Parity:  max |GPU − CPU| = {difference:G3} per channel (limit 1/255 = {ParityLimit:G3}) ✓"));
            return 0;
        }

        ReadOnlySpan<Float4> gpuResult = destination.AsReadOnlySpan();
        int over = 0;

        for (int i = 0; i < cpuResult.Length; i++)
        {
            over += MaxDifference(gpuResult.Slice(i, 1), cpuResult.AsSpan(i, 1)) > ParityLimit ? 1 : 0;
        }

        Console.WriteLine(Invariant(
            $"Parity:  FAILED: max |GPU − CPU| = {difference:G3} per channel exceeds 1/255; {over} of {cpuResult.Length} pixels differ by more"));
        return ParityFailure;
    }

    private static double Best(Action action) => Enumerable.Range(0, TimedRuns).Select(_ => Time(action)).Min();

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
