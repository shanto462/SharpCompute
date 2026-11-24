using System;
using System.Globalization;
using SharpCompute.Benchmarks.Shaders;
using SharpCompute.Benchmarks.Workloads;

namespace SharpCompute.Benchmarks;

// --verify for the CPU+GPU split benchmarks: the shaders run by ForCpu (their C# on the CPU) and by
// ForSplit (a fixed half on each side, so both parts are always exercised) against the CPU references.
public static partial class Verify
{
    private const double SplitFraction = 0.5;

    private static void VerifyCpuAndSplit(GraphicsDevice device, Action<Check> add)
    {
        VerifyMandelbrotCpuAndSplit(device, add);
        VerifyBlurCpuAndSplit(device, add);
        VerifyNBodyCpuAndSplit(device, add);
    }

    private static void VerifyMandelbrotCpuAndSplit(GraphicsDevice device, Action<Check> add)
    {
        const string size = "1920x1080";
        using Mandelbrot mandelbrot = new(1920, 1080, device);

        mandelbrot.CpuScalar();
        uint[] scalar = (uint[])mandelbrot.Pixels.Clone();
        mandelbrot.Gpu();
        uint[] gpu = mandelbrot.GpuPixels.ToArray();

        // ForCpu runs the shader's C#, which does the CPU scalar loop's unfused arithmetic: identical pixels.
        mandelbrot.GpuPixels.AsSpan().Clear();
        device.ForCpu(mandelbrot.Width, mandelbrot.Height, mandelbrot.Shader());
        int differing = CountDifferent(mandelbrot.GpuPixels.AsSpan(), scalar, 0, scalar.Length);
        add(new Check("Mandelbrot", size, "CPU generated shader (ForCpu)", "CPU scalar", "pixels not identical", differing, 0,
            string.Create(CultureInfo.InvariantCulture, $"{differing} of {scalar.Length} packed RGBA pixels differ (exact match required)")));

        // ForSplit: the CPU's rows must equal the CPU scalar loop, the GPU's rows the GPU-only dispatch.
        mandelbrot.GpuPixels.AsSpan().Clear();
        SplitDispatchInfo info = device.ForSplit(mandelbrot.Width, mandelbrot.Height, mandelbrot.Shader(), SplitFraction);
        int boundary = info.CpuExtent * mandelbrot.Width;
        int cpuRows = CountDifferent(mandelbrot.GpuPixels.AsSpan(), scalar, 0, boundary);
        int gpuRows = CountDifferent(mandelbrot.GpuPixels.AsSpan(), gpu, boundary, gpu.Length);
        add(new Check("Mandelbrot", size, "CPU+GPU split (ForSplit 0.5)", "CPU scalar / GPU", "pixels not identical", cpuRows + gpuRows, 0,
            string.Create(CultureInfo.InvariantCulture, $"rows 0..{info.CpuExtent - 1} on the CPU: {cpuRows} differ from CPU scalar; rows {info.CpuExtent}..{mandelbrot.Height - 1} on the GPU: {gpuRows} differ from GPU-only")));
    }

    private static void VerifyBlurCpuAndSplit(GraphicsDevice device, Action<Check> add)
    {
        using GaussianBlur blur = new(Sizes.BlurWidth, Sizes.BlurHeight, Sizes.BlurRadius, device);
        string size = $"{Sizes.BlurWidth}x{Sizes.BlurHeight}";

        blur.CpuScalar();
        float[] reference = (float[])blur.Output.Clone();

        BlurHorizontal horizontal = new(blur.GpuSource, blur.GpuTemp, blur.GpuWeights, blur.Width, blur.Radius);
        BlurVertical vertical = new(blur.GpuTemp, blur.GpuOutput, blur.GpuWeights, blur.Width, blur.Height, blur.Radius);

        device.ForCpu(blur.Width, blur.Height, horizontal);
        device.ForCpu(blur.Width, blur.Height, vertical);
        add(Compare("Blur", size, "CPU generated shader (ForCpu)", "CPU scalar", blur.GpuOutput.AsSpan(), reference));

        blur.GpuTemp.AsSpan().Clear();
        blur.GpuOutput.AsSpan().Clear();
        device.ForSplit(blur.Width, blur.Height, horizontal, SplitFraction);
        device.ForSplit(blur.Width, blur.Height, vertical, SplitFraction);
        add(Compare("Blur", size, "CPU+GPU split (ForSplit 0.5)", "CPU scalar", blur.GpuOutput.AsSpan(), reference));
    }

    private static void VerifyNBodyCpuAndSplit(GraphicsDevice device, Action<Check> add)
    {
        const int count = 16384;
        using NBody nbody = new(count, device);
        string size = $"{count} bodies";

        nbody.CpuParallel();
        Float4[] positions = (Float4[])nbody.NextPositions.Clone();
        Float4[] velocities = (Float4[])nbody.NextVelocities.Clone();
        NBodyStep step = new(nbody.GpuPositions, nbody.GpuVelocities, nbody.GpuNextPositions, nbody.GpuNextVelocities, count, NBody.Dt, NBody.SofteningSquared);

        device.ForCpu(count, step);
        add(CompareVectors(size, "CPU generated shader (ForCpu)", "positions", nbody.GpuNextPositions.AsSpan(), positions));
        add(CompareVectors(size, "CPU generated shader (ForCpu)", "velocities", nbody.GpuNextVelocities.AsSpan(), velocities));

        nbody.GpuNextPositions.AsSpan().Clear();
        nbody.GpuNextVelocities.AsSpan().Clear();
        device.ForSplit(count, step, SplitFraction);
        add(CompareVectors(size, "CPU+GPU split (ForSplit 0.5)", "positions", nbody.GpuNextPositions.AsSpan(), positions));
        add(CompareVectors(size, "CPU+GPU split (ForSplit 0.5)", "velocities", nbody.GpuNextVelocities.AsSpan(), velocities));
    }

    private static int CountDifferent(ReadOnlySpan<uint> actual, uint[] expected, int start, int end)
    {
        int differing = 0;

        for (int i = start; i < end; i++)
        {
            differing += actual[i] != expected[i] ? 1 : 0;
        }

        return differing;
    }
}
