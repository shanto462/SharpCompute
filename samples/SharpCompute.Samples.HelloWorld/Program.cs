using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpCompute;
using SharpCompute.Samples.HelloWorld;
using SharpCompute.Samples.Shaders;

if (!GraphicsDevice.IsSupported)
{
    Console.WriteLine("Metal is not available (SharpCompute needs macOS 15+ on Apple Silicon).");
    return 1;
}

GraphicsDevice device = GraphicsDevice.GetDefault();
Console.WriteLine($"Device: {device.Name}");

// 1. The classic: multiply every element of a buffer by two, on the GPU.
using (ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3, 4, 5, 6, 7, 8]))
{
    device.For(buffer.Length, new MultiplyByTwo(buffer));
    Console.WriteLine($"MultiplyByTwo: [{string.Join(", ", buffer.ToArray())}]");
}

// 2. SAXPY over a million elements: y = a * x + y.
const int N = 1 << 20;
float[] xs = new float[N], ys = new float[N];
for (int i = 0; i < N; i++) { xs[i] = i; ys[i] = 1f; }
using (ReadOnlyBuffer<float> x = device.AllocateReadOnlyBuffer<float>(xs))
using (ReadWriteBuffer<float> y = device.AllocateReadWriteBuffer<float>(ys))
{
    device.For(N, new Saxpy(x, y, 0.5f));
    Console.WriteLine($"SAXPY: y[0] = {y[0]}, y[{N - 1}] = {y[N - 1]}");
}

// 3. A Mandelbrot set rendered into a buffer of packed RGBA pixels, saved as a PNG.
const int Width = 1920, Height = 1080;
using (ReadWriteBuffer<uint> pixels = device.AllocateReadWriteBuffer<uint>(Width * Height))
{
    Mandelbrot shader = new(pixels, Width, Height, maxIterations: 512, center: MandelbrotZoom.Center, scale: 0.0045f);
    device.For(Width, Height, shader); // first dispatch compiles the pipeline

    Stopwatch stopwatch = Stopwatch.StartNew();
    device.For(Width, Height, shader);
    stopwatch.Stop();

    string path = args.Length > 0 ? args[0] : "mandelbrot.png";
    PngEncoder.Write(path, MemoryMarshal.Cast<uint, Rgba32>(pixels.AsSpan()), Width, Height);
    Console.WriteLine($"Mandelbrot: {Width}x{Height}, 512 iterations, {stopwatch.Elapsed.TotalMilliseconds:F2} ms per dispatch -> {path}");
}

return 0;
