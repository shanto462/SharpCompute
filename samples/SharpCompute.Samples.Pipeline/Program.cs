// A double-buffered GPU → CPU pipeline. Each frame of a Mandelbrot zoom is rendered on the GPU and
// then post-processed on the CPU (auto-levels from a luminance histogram, plus a vignette). Run
// synchronously, the CPU waits while the GPU renders and the GPU idles while the CPU processes. With
// ForAsync and two frame buffers, the GPU renders frame N+1 while the CPU processes frame N.
//
//   SharpCompute.Samples.Pipeline [--frames 120] [--size 1920x1080] [--iterations 1000] [--out dir]
//
// Prints the cost of each stage alone, then sync vs async throughput, and checks that both loops
// produced identical frames. With --out, the last post-processed frame is saved as a PNG.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using SharpCompute;
using SharpCompute.Samples.Pipeline;
using SharpCompute.Samples.Shaders;

if (!GraphicsDevice.IsSupported)
{
    Console.WriteLine("Metal is not available (SharpCompute needs macOS 15+ on Apple Silicon).");
    return 1;
}

int frameCount = 120;
int width = 1920, height = 1080;
int iterations = 1000;
string? outDir = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--frames": frameCount = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--iterations": iterations = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--out": outDir = args[++i]; break;
        case "--size":
            string[] parts = args[++i].Split('x');
            width = int.Parse(parts[0], CultureInfo.InvariantCulture);
            height = int.Parse(parts[1], CultureInfo.InvariantCulture);
            break;
        default:
            Console.Error.WriteLine($"unknown option '{args[i]}'; usage: [--frames N] [--size WxH] [--iterations N] [--out dir]");
            return 2;
    }
}

if (frameCount < 2 || width < 16 || height < 16 || iterations < 1)
{
    Console.Error.WriteLine("--frames must be at least 2, --size at least 16x16 and --iterations at least 1.");
    return 2;
}

GraphicsDevice device = GraphicsDevice.GetDefault();
Console.WriteLine($"Device: {device.Name}");
Console.WriteLine($"{frameCount} frames of {width}×{height}, {iterations} iterations max; CPU post-processing on one thread.");

using ReadWriteBuffer<uint> frameA = device.AllocateReadWriteBuffer<uint>(width * height);
using ReadWriteBuffer<uint> frameB = device.AllocateReadWriteBuffer<uint>(width * height);
ReadWriteBuffer<uint>[] frames = [frameA, frameB];
PostProcessor post = new(width, height);

// Warm-up: compiles the pipeline and JITs the post-processing, so neither is in the timings.
device.For(width, height, FrameShader(frameA, 0));
post.Process(frameA);

// Each stage alone, to know what overlapping them can at best achieve.
Stopwatch stopwatch = Stopwatch.StartNew();
for (int k = 0; k < frameCount; k++)
{
    device.For(width, height, FrameShader(frameA, k));
}

double gpuMs = stopwatch.Elapsed.TotalMilliseconds / frameCount;

stopwatch.Restart();
for (int k = 0; k < frameCount; k++)
{
    post.Process(frameA);
}

double cpuMs = stopwatch.Elapsed.TotalMilliseconds / frameCount;

// Synchronous: render frame k, wait, process it; the GPU and CPU take turns.
ulong syncChecksum = 0;
stopwatch.Restart();
for (int k = 0; k < frameCount; k++)
{
    device.For(width, height, FrameShader(frameA, k));
    syncChecksum = Mix(syncChecksum, post.Process(frameA));
}

double syncMs = stopwatch.Elapsed.TotalMilliseconds / frameCount;

// Double-buffered: while the CPU processes frames[k % 2], the GPU is already rendering frame k + 1
// into the other buffer. The CPU only touches a buffer after awaiting the dispatch that wrote it.
ulong asyncChecksum = 0;
stopwatch.Restart();
Task rendering = device.ForAsync(width, height, FrameShader(frames[0], 0));

for (int k = 0; k < frameCount; k++)
{
    await rendering;

    rendering = k + 1 < frameCount
        ? device.ForAsync(width, height, FrameShader(frames[(k + 1) % 2], k + 1))
        : Task.CompletedTask;

    asyncChecksum = Mix(asyncChecksum, post.Process(frames[k % 2]));
}

await rendering;
double asyncMs = stopwatch.Elapsed.TotalMilliseconds / frameCount;

Console.WriteLine();
Console.WriteLine("| Stage | ms per frame | frames/s |");
Console.WriteLine("|---|---:|---:|");
Row("GPU render alone (device.For)", gpuMs);
Row("CPU post-process alone", cpuMs);
Row("Sync: For, then process", syncMs);
Row("Async: ForAsync N+1 while processing N", asyncMs);
Row("Perfect overlap estimate: max(GPU alone, CPU alone)", Math.Max(gpuMs, cpuMs));
Console.WriteLine();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"Async vs sync: {syncMs / asyncMs:F2}× the throughput (perfect overlap of the stages measured alone would give {(gpuMs + cpuMs) / Math.Max(gpuMs, cpuMs):F2}×)."));

if (syncChecksum != asyncChecksum)
{
    Console.Error.WriteLine($"MISMATCH: the sync and async loops produced different frames ({syncChecksum:X16} vs {asyncChecksum:X16}).");
    return 1;
}

Console.WriteLine($"Both loops produced identical frames (checksum {syncChecksum:X16}).");

if (outDir is not null)
{
    Directory.CreateDirectory(outDir);
    string path = Path.Combine(outDir, "pipeline-last-frame.png");
    PngEncoder.Write(path, post.Output, width, height);
    Console.WriteLine($"Last post-processed frame -> {path}");
}

return 0;

// Frame k of a zoom into the Seahorse Valley, from a view 3 units high down to about 4e-4.
Mandelbrot FrameShader(ReadWriteBuffer<uint> target, int k)
{
    float scale = 1.5f * MathF.Pow(2e-4f / 1.5f, k / (float)(frameCount - 1));
    return new Mandelbrot(target, width, height, iterations, MandelbrotZoom.Center, scale, colorOffset: k * 0.01f);
}

static ulong Mix(ulong hash, ulong value) => (hash ^ value) * 0x100000001B3ul;

static void Row(string label, double ms) =>
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {label} | {ms:F2} | {1000 / ms:F1} |"));
