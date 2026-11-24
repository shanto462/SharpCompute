using System;
using System.Diagnostics;
using System.Globalization;
using SharpCompute.Benchmarks.Workloads;

namespace SharpCompute.Benchmarks;

/// <summary>
/// The one-off costs a steady-state benchmark hides: creating the device, and the first dispatch of
/// a shader, which compiles its MSL into a pipeline. Meant to run in a fresh process (Runner spawns
/// one per sample), timing the 1920×1080 Mandelbrot with a plain Stopwatch.
/// </summary>
public sealed record FirstDispatch(double DeviceMs, double FirstMs, double SecondMs, double SteadyMs)
{
    public const string Prefix = "first-dispatch:";

    public static FirstDispatch Measure()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        GraphicsDevice device = GraphicsDevice.GetDefault(); // MTLCreateSystemDefaultDevice + command queue
        double deviceMs = stopwatch.Elapsed.TotalMilliseconds;

        using Mandelbrot mandelbrot = new(1920, 1080, device);

        stopwatch.Restart();
        mandelbrot.Gpu();
        double firstMs = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        mandelbrot.Gpu();
        double secondMs = stopwatch.Elapsed.TotalMilliseconds;

        // A short steady-state sample from the same process, for the "compile ≈ first − steady" estimate.
        const int Runs = 20;
        stopwatch.Restart();

        for (int i = 0; i < Runs; i++)
        {
            mandelbrot.Gpu();
        }

        return new FirstDispatch(deviceMs, firstMs, secondMs, stopwatch.Elapsed.TotalMilliseconds / Runs);
    }

    public static FirstDispatch? TryParse(string line)
    {
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        double[] v = Array.ConvertAll(line[Prefix.Length..].Trim().Split(' '), s => double.Parse(s, CultureInfo.InvariantCulture));
        return new FirstDispatch(v[0], v[1], v[2], v[3]);
    }

    public void Print()
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Prefix} {DeviceMs:F3} {FirstMs:F3} {SecondMs:F3} {SteadyMs:F3}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"device {DeviceMs:F1} ms, first Mandelbrot dispatch (compile + run) {FirstMs:F1} ms, second {SecondMs:F2} ms, steady {SteadyMs:F2} ms"));
    }
}
