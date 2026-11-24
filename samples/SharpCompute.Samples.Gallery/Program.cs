using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SharpCompute;
using SharpCompute.Samples.Gallery;
using SharpCompute.Samples.Shaders;
using ShaderGallery = SharpCompute.Samples.Shaders.Gallery;

Options options;

try
{
    options = Options.Parse(args);
}
catch (FormatException e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    Console.Error.WriteLine("Run with --help for usage.");
    return 2;
}

switch (options.Mode)
{
    case Mode.Help:
        Console.WriteLine(Options.Usage);
        return 0;
    case Mode.List:
        Commands.List();
        return 0;
}

if (!GraphicsDevice.IsSupported)
{
    Console.Error.WriteLine("error: no usable Metal device (SharpCompute needs macOS 15+ on Apple Silicon).");
    return 1;
}

IReadOnlyList<GalleryShader> shaders;

try
{
    shaders = options.Shader is { } name ? [Commands.Find(name)] : ShaderGallery.All;
}
catch (FormatException e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 2;
}

GraphicsDevice device = GraphicsDevice.GetDefault();

try
{
    switch (options.Mode)
    {
        case Mode.Render:
            Commands.Render(device, shaders, options);
            break;
        case Mode.Frames:
            await Commands.FramesAsync(device, shaders, options);
            break;
        case Mode.Bench:
            Commands.Bench(device, shaders, options);
            break;
    }
}
catch (Exception e) when (e is ShaderCompilationException or MetalException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

return 0;

namespace SharpCompute.Samples.Gallery
{
    internal static class Commands
    {
        private const int WarmUpFrames = 10;
        private const int DefaultBenchFrames = 100;

        public static void List()
        {
            int width = ShaderGallery.All.Max(static s => s.Name.Length);

            foreach (GalleryShader shader in ShaderGallery.All)
            {
                Console.WriteLine($"{shader.Name.PadRight(width)}  {shader.Author}  {shader.SourceUrl}");
            }
        }

        public static GalleryShader Find(string name) =>
            ShaderGallery.All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException($"Unknown shader '{name}'. Available: {string.Join(", ", ShaderGallery.All.Select(static s => s.Name))}.");

        /// <summary><c>--shader</c> (one PNG at <c>--out</c>) or <c>--all</c> (one PNG per shader in <c>--outdir</c>).</summary>
        public static void Render(GraphicsDevice device, IReadOnlyList<GalleryShader> shaders, Options options)
        {
            using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(options.Width, options.Height);
            Rgba32[] pixels = new Rgba32[options.Width * options.Height];

            if (options.All)
            {
                Directory.CreateDirectory(options.OutDir);
            }

            foreach (GalleryShader shader in shaders)
            {
                string path = options.All ? Path.Combine(options.OutDir, shader.Name + ".png") : options.Out ?? shader.Name + ".png";

                Stopwatch stopwatch = Stopwatch.StartNew();
                shader.Render(device, texture, options.Time);
                double renderMs = stopwatch.Elapsed.TotalMilliseconds;

                texture.CopyTo(pixels);
                PngEncoder.Write(path, pixels, options.Width, options.Height);

                Console.WriteLine(Invariant($"{shader.Name}: {options.Width}x{options.Height} @ t={options.Time} → {path} (render {renderMs:F1} ms incl. first-use compile)"));
            }
        }

        /// <summary><c>--frames N --fps F</c>: a PNG sequence per shader, then an mp4 when ffmpeg is available.</summary>
        public static async Task FramesAsync(GraphicsDevice device, IReadOnlyList<GalleryShader> shaders, Options options)
        {
            int frames = options.Frames!.Value;
            string? ffmpeg = FindOnPath("ffmpeg");

            using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(options.Width, options.Height);

            foreach (GalleryShader shader in shaders)
            {
                string directory = Path.Combine(options.OutDir, shader.Name);
                Directory.CreateDirectory(directory);

                // The GPU renders frame i+1 while the CPU compresses earlier frames; a bounded
                // window of encode tasks keeps memory flat for long sequences.
                Queue<Task> encoding = new();
                int window = Math.Max(2, Environment.ProcessorCount);
                Stopwatch stopwatch = Stopwatch.StartNew();

                for (int i = 0; i < frames; i++)
                {
                    float time = (float)(options.Time + i / options.Fps);
                    shader.Render(device, texture, time);

                    Rgba32[] pixels = texture.ToArray();
                    string path = Path.Combine(directory, FrameFileName(shader.Name, i));
                    (int width, int height) = (options.Width, options.Height);

                    if (encoding.Count >= window)
                    {
                        await encoding.Dequeue();
                    }

                    encoding.Enqueue(Task.Run(() => PngEncoder.Write(path, pixels, width, height)));
                }

                await Task.WhenAll(encoding);

                Console.WriteLine(Invariant($"{shader.Name}: {frames} frames at {options.Fps} fps → {directory} ({stopwatch.Elapsed.TotalSeconds:F1} s)"));

                if (ffmpeg is null)
                {
                    continue;
                }

                string video = Path.Combine(options.OutDir, shader.Name + ".mp4");

                if (await EncodeVideoAsync(ffmpeg, Path.Combine(directory, $"{shader.Name}_%05d.png"), options.Fps, video))
                {
                    Console.WriteLine($"{shader.Name}: encoded {video}");
                }
            }

            if (ffmpeg is null)
            {
                string pattern = Path.Combine(options.OutDir, "<name>", "<name>_%05d.png");
                Console.WriteLine(Invariant($"ffmpeg was not found on PATH, so no mp4 was made. Install it (e.g. `brew install ffmpeg`) or run:"));
                Console.WriteLine(Invariant($"  ffmpeg -framerate {options.Fps} -i {pattern} -c:v libx264 -pix_fmt yuv420p <name>.mp4"));
            }
        }

        /// <summary><c>--bench</c>: wall-clock time of each <c>Render</c> (one synchronous <c>ForEach</c>) after a warm-up.</summary>
        public static void Bench(GraphicsDevice device, IReadOnlyList<GalleryShader> shaders, Options options)
        {
            int frames = options.Frames ?? DefaultBenchFrames;
            int nameWidth = Math.Max(6, shaders.Max(static s => s.Name.Length));

            using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(options.Width, options.Height);

            Console.WriteLine(Invariant($"{device.Name}, {options.Width}x{options.Height}, {frames} frames after {WarmUpFrames} warm-up frames (wall clock around ForEach)"));
            Console.WriteLine($"{"shader".PadRight(nameWidth)}  {"median ms",10}  {"p95 ms",10}  {"fps (median)",12}");

            double[] samples = new double[frames];

            foreach (GalleryShader shader in shaders)
            {
                // Warm-up compiles the pipeline and lets the GPU clock up; none of it is measured.
                for (int i = 0; i < WarmUpFrames; i++)
                {
                    shader.Render(device, texture, options.Time + i / 60f);
                }

                for (int i = 0; i < frames; i++)
                {
                    float time = options.Time + (WarmUpFrames + i) / 60f;
                    long start = Stopwatch.GetTimestamp();
                    shader.Render(device, texture, time);
                    samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }

                Array.Sort(samples);
                double median = frames % 2 == 1 ? samples[frames / 2] : (samples[frames / 2 - 1] + samples[frames / 2]) / 2;
                double p95 = samples[Math.Max(0, (int)Math.Ceiling(0.95 * frames) - 1)];

                Console.WriteLine(Invariant($"{shader.Name.PadRight(nameWidth)}  {median,10:F3}  {p95,10:F3}  {1000 / median,12:F1}"));
            }
        }

        internal static string FrameFileName(string shaderName, int index) => $"{shaderName}_{index:D5}.png";

        private static async Task<bool> EncodeVideoAsync(string ffmpeg, string inputPattern, double fps, string output)
        {
            ProcessStartInfo info = new(ffmpeg)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            string rate = fps.ToString(CultureInfo.InvariantCulture);

            foreach (string arg in (string[])[
                "-hide_banner", "-loglevel", "error", "-y",
                "-framerate", rate, "-i", inputPattern,
                // yuv420p (needed by most players) requires even dimensions.
                "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2",
                // CRF 26 keeps busy content (e.g. the deep Mandelbrot zoom) to a reasonable size.
                "-c:v", "libx264", "-crf", "26", "-preset", "medium", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-r", rate,
                output])
            {
                info.ArgumentList.Add(arg);
            }

            using Process process = Process.Start(info) ?? throw new IOException($"Could not start '{ffmpeg}'.");

            Task<string> stderr = process.StandardError.ReadToEndAsync();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine($"warning: ffmpeg exited with code {process.ExitCode}; the PNG frames are still in place.");
                Console.Error.WriteLine((await stderr).Trim());
                return false;
            }

            await stdout;
            return true;
        }

        private static string? FindOnPath(string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory, executable);

                if (File.Exists(candidate) && !OperatingSystem.IsWindows() && (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string Invariant(FormattableString value) => FormattableString.Invariant(value);
    }
}
