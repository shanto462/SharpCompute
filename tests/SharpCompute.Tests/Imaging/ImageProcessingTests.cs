using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Tests.Imaging;

/// <summary>The ImageProcessing sample: GPU filters against their CPU reference, ImageIO load/save, and the CLI.</summary>
public sealed class ImageProcessingTests : IDisposable
{
    private const string Sample = SampleAssembly.ImageProcessing;
    private const int Width = 64;
    private const int Height = 48;

    // The CLI accepts up to 1/255; the shared math actually agrees to float rounding (fused multiply-adds).
    private const float Tolerance = 1e-5f;

    private readonly string _directory = Directory.CreateTempSubdirectory("sharpcompute-imgproc-").FullName;

    /// <summary>Every filter, some with two argument sets, as typed on the command line.</summary>
    public static TheoryData<string, string[]> Filters => new()
    {
        { "blur", ["1"] },
        { "blur", ["8"] },
        { "bokeh", ["5", "6"] },
        { "bokeh", ["2.5", "0"] },
        { "sharpen", ["1.5"] },
        { "edges", [] },
        { "grade", ["0.5", "1.3", "1.6"] },
        { "grade", ["-1", "0.7", "0"] },
        { "pixelate", ["7"] },
        { "invert", [] },
        { "posterize", ["4"] },
        { "grayscale", [] },
    };

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [SampleFact(Sample)]
    public void EveryFilter_HasAParityCase()
    {
        IEnumerable<dynamic> all = (IEnumerable<dynamic>)SampleAssembly.Call(Sample, "SharpCompute.Samples.Imaging.FilterSpec", "get_All")!;
        string[] names = [.. all.Select(static info => (string)info.Name)];
        string[] covered = [.. Filters.Select(static row => (string)row[0]).Distinct()];

        Assert.Equal(names.Order(), covered.Order());
    }

    [SampleGpuTheory(Sample)]
    [MemberData(nameof(Filters))]
    public void Filter_OnTheGpu_MatchesTheCpuReference(string name, string[] arguments)
    {
        object filter = Parse(name, arguments);
        Float4[] input = TestImage(Width, Height);

        Float4[] gpu = RunGpu(filter, input);
        Float4[] cpu = new Float4[input.Length];
        SampleAssembly.Call(Sample, "SharpCompute.Samples.Imaging.CpuFilters", "Apply", filter, input, cpu, Width, Height);

        float difference = MaxDifference(gpu, cpu);
        Assert.True(difference <= Tolerance, $"{name} {string.Join(' ', arguments)}: max |GPU − CPU| = {difference}");

        // And the filter did something.
        Assert.True(MaxDifference(gpu, input) > 0.01f, $"{name} left the image unchanged.");
        Assert.All(gpu, static p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && float.IsFinite(p.W)));
    }

    [SampleGpuFact(Sample)]
    public void Blur_OfAConstantImage_IsThatConstant_AndInvertTwiceIsIdentity()
    {
        Float4 color = new(0.25f, 0.5f, 0.125f, 1f);
        Float4[] flat = [.. Enumerable.Repeat(color, Width * Height)];

        Float4[] blurred = RunGpu(Parse("blur", ["12"]), flat);
        Assert.True(MaxDifference(blurred, flat) < 1e-6f);

        Float4[] input = TestImage(Width, Height);
        object invert = Parse("invert", []);
        Assert.True(MaxDifference(RunGpu(invert, RunGpu(invert, input)), input) < 1e-6f);
    }

    [SampleFact(Sample)]
    public void GaussianWeights_AreSymmetricAndNormalized()
    {
        float[] weights = (float[])SampleAssembly.Call(Sample, "SharpCompute.Samples.Imaging.FilterSpec", "GaussianWeights", 6)!;

        Assert.Equal(13, weights.Length);
        Assert.Equal(1.0, weights.Sum(static w => (double)w), 5);

        for (int k = 0; k < 6; k++)
        {
            Assert.Equal(weights[k], weights[12 - k]);
            Assert.True(weights[k] < weights[k + 1]);
        }
    }

    [SampleFact(Sample)]
    public void Parse_RejectsUnknownFiltersAndOutOfRangeArguments()
    {
        Assert.False(TryParse("melt", [], out string error));
        Assert.Contains("Unknown filter", error, StringComparison.Ordinal);

        Assert.False(TryParse("blur", ["2.5"], out error));
        Assert.Contains("integer", error, StringComparison.Ordinal);

        Assert.False(TryParse("bokeh", ["1000"], out error));
        Assert.False(TryParse("grade", ["1", "1", "1", "1"], out _));
        Assert.False(TryParse("sharpen", ["lots"], out _));

        // Missing trailing arguments take their defaults.
        Assert.True(TryParse("grade", ["1"], out _));
    }

    [SampleFact(Sample)]
    public void Png_RoundTripsThroughImageIO_Exactly()
    {
        Float4[] pixels = TestImage(Width, Height);
        string path = Path.Combine(_directory, "round-trip.png");

        Save(path, pixels);
        (int width, int height, Float4[] loaded) = Load(path);

        Assert.Equal((Width, Height), (width, height));

        // Opaque pixels survive as their 8-bit quantization.
        Assert.True(MaxDifference(loaded, pixels) <= (0.5f / 255f) + 1e-6f);
    }

    [SampleFact(Sample)]
    public void Jpeg_RoundTripsThroughImageIO_Approximately()
    {
        Float4[] pixels = TestImage(256, 192);
        string path = Path.Combine(_directory, "round-trip.jpg");

        Save(path, pixels, 256, 192);
        (int width, int height, Float4[] loaded) = Load(path);

        Assert.Equal((256, 192), (width, height));

        double error = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            Float4 d = loaded[i] - pixels[i];
            error += Math.Abs(d.X) + Math.Abs(d.Y) + Math.Abs(d.Z);
            Assert.Equal(1f, loaded[i].W);
        }

        Assert.InRange(error / (pixels.Length * 3), 0, 0.02);
    }

    [SampleFact(Sample)]
    public void Load_ReportsMissingAndUndecodableFiles()
    {
        Assert.Throws<FileNotFoundException>(() => Load(Path.Combine(_directory, "missing.png")));

        string text = Path.Combine(_directory, "not-an-image.png");
        File.WriteAllText(text, "hello");
        Assert.Throws<InvalidDataException>(() => Load(text));
    }

    [SampleGpuFact(Sample)]
    public void Cli_MakesATestImage_AndComparesGpuWithCpu()
    {
        string input = Path.Combine(_directory, "cli-input.png");
        string output = Path.Combine(_directory, "cli-output.jpg");

        Assert.Equal(0, RunCli("--make-test-image", input, "96x64"));
        Assert.Equal(0, RunCli(input, output, "bokeh", "4", "--compare"));
        Assert.True(new FileInfo(output).Length > 0);

        Assert.Equal(1, RunCli(input, output, "melt"));
        Assert.Equal(1, RunCli("--make-test-image", input, "0x5"));
        Assert.Equal(0, RunCli("--list"));
    }

    [SampleGpuFact(Sample)]
    public void Rgba8_UnpackThenPackOnTheGpu_RoundTripsPremultipliedPixelsExactly()
    {
        Random random = new(7);
        uint[] input = new uint[10_000];

        for (int i = 0; i < input.Length; i++)
        {
            // Valid premultiplied pixels: no color channel exceeds alpha.
            int a = random.Next(256);
            input[i] = (uint)random.Next(a + 1) | ((uint)random.Next(a + 1) << 8) | ((uint)random.Next(a + 1) << 16) | ((uint)a << 24);
        }

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> packed = device.AllocateReadWriteBuffer<uint>(input);
        using ReadWriteBuffer<Float4> pixels = device.AllocateReadWriteBuffer<Float4>(input.Length);
        using ReadWriteBuffer<uint> repacked = device.AllocateReadWriteBuffer<uint>(input.Length);

        Dispatch(device, "UnpackRgba8", input.Length, packed, pixels);
        Dispatch(device, "PackRgba8", input.Length, pixels, repacked, 0);

        Assert.Equal(input, repacked.ToArray());

        // Same floats as the CPU conversion.
        byte[] bytes = new byte[input.Length * 4];
        Buffer.BlockCopy(input, 0, bytes, 0, bytes.Length);
        Float4[] cpu = SampleAssembly.Type(Sample, "SharpCompute.Samples.Imaging.PixelConversion")
            .GetMethod("FromPremultipliedRgba8", [typeof(ReadOnlySpan<byte>)])!
            .CreateDelegate<Func<ReadOnlySpan<byte>, Float4[]>>()(bytes);
        Assert.True(MaxDifference(pixels.ToArray(), cpu) <= 1e-7f);
    }

    [SampleGpuFact(Sample)]
    public void PackRgba8_OnTheGpu_MatchesTheCpuConversions()
    {
        Random random = new(11);
        Float4[] pixels = new Float4[20_000];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Float4(Value(), Value(), Value(), Value());
        }

        pixels[0] = new Float4(float.NaN, 0.5f, 0.25f, 0.5f);
        pixels[1] = new Float4(0.5f, 0.5f, 0.5f, 0f);
        pixels[2] = new Float4(0.3f, 0.2f, 0.1f, float.NaN);

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> source = device.AllocateReadWriteBuffer<Float4>(pixels);
        using ReadWriteBuffer<uint> premultiplied = device.AllocateReadWriteBuffer<uint>(pixels.Length);
        using ReadWriteBuffer<uint> straight = device.AllocateReadWriteBuffer<uint>(pixels.Length);

        Dispatch(device, "PackRgba8", pixels.Length, source, premultiplied, 0);
        Dispatch(device, "PackRgba8", pixels.Length, source, straight, 1);

        byte[] cpuPremultiplied = (byte[])SampleAssembly.Call(Sample, "SharpCompute.Samples.Imaging.PixelConversion", "ToPremultipliedRgba8", [pixels])!;
        Rgba32[] cpuStraight = (Rgba32[])SampleAssembly.Call(Sample, "SharpCompute.Samples.Imaging.PixelConversion", "ToStraightRgba32", [pixels])!;
        byte[] gpuPremultiplied = new byte[pixels.Length * 4];
        byte[] gpuStraight = new byte[pixels.Length * 4];
        Buffer.BlockCopy(premultiplied.ToArray(), 0, gpuPremultiplied, 0, gpuPremultiplied.Length);
        Buffer.BlockCopy(straight.ToArray(), 0, gpuStraight, 0, gpuStraight.Length);

        // Rounding x * 255 + 0.5 may be fused on the GPU, and the straight-alpha division is not
        // correctly rounded there, so a value right at a rounding boundary can land one step away.
        for (int i = 0; i < gpuPremultiplied.Length; i++)
        {
            Assert.InRange(gpuPremultiplied[i] - cpuPremultiplied[i], -1, 1);
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            Rgba32 c = cpuStraight[i];
            Assert.InRange(gpuStraight[i * 4] - c.R, -1, 1);
            Assert.InRange(gpuStraight[(i * 4) + 1] - c.G, -1, 1);
            Assert.InRange(gpuStraight[(i * 4) + 2] - c.B, -1, 1);
            Assert.InRange(gpuStraight[(i * 4) + 3] - c.A, -1, 1);
        }

        // NaN color channels and NaN or zero alpha are exact, not off by one.
        Assert.Equal(cpuPremultiplied.AsSpan(0, 12).ToArray(), gpuPremultiplied.AsSpan(0, 12).ToArray());
        Assert.Equal(0u, straight[1]);
        Assert.Equal(0u, straight[2]);

        float Value() => (float)((random.NextDouble() * 1.4) - 0.2);
    }

    [SampleGpuFact(Sample)]
    public void LoadRgba8_AndSaveRgba8_MatchTheArrayPaths()
    {
        Float4[] pixels = TestImage(Width, Height);
        string png = Path.Combine(_directory, "zero-copy-input.png");
        string copy = Path.Combine(_directory, "zero-copy-output.png");
        Save(png, pixels);

        GraphicsDevice device = GraphicsDevice.GetDefault();
        object?[] args = [png, device, 0, 0];
        using ReadWriteBuffer<uint> loaded = (ReadWriteBuffer<uint>)SampleAssembly.Call(Sample, "ImageFile", "LoadRgba8", args)!;

        Assert.Equal((Width, Height), ((int)args[2]!, (int)args[3]!));

        using ReadWriteBuffer<Float4> unpacked = device.AllocateReadWriteBuffer<Float4>(loaded.Length);
        Dispatch(device, "UnpackRgba8", loaded.Length, loaded, unpacked);
        Assert.True(MaxDifference(unpacked.ToArray(), Load(png).Pixels) <= 1e-7f);

        // The test image is opaque, so its premultiplied and straight-alpha bytes are the same.
        Assert.True((bool)SampleAssembly.Call(Sample, "ImageFile", "UsesStraightAlpha", copy)!);
        Assert.False((bool)SampleAssembly.Call(Sample, "ImageFile", "UsesStraightAlpha", "x.JPG")!);
        SaveRgba8(copy, loaded.ToArray(), Width, Height);
        Assert.Equal(Load(png).Pixels, Load(copy).Pixels);

        string jpeg = Path.Combine(_directory, "zero-copy-output.jpg");
        SaveRgba8(jpeg, loaded.ToArray(), Width, Height);
        Assert.Equal((Width, Height), (Load(jpeg).Width, Load(jpeg).Height));
    }

    private static void SaveRgba8(string path, uint[] pixels, int width, int height)
    {
        MethodInfo method = SampleAssembly.Type(Sample, "ImageFile").GetMethod("SaveRgba8")!;
        SaveRgba8Delegate save = method.CreateDelegate<SaveRgba8Delegate>();
        save(path, pixels, width, height);
    }

    private delegate void SaveRgba8Delegate(string path, ReadOnlySpan<uint> pixels, int width, int height);

    /// <summary>Runs a sample shader through <c>GraphicsDevice.For(int, in T)</c>, which tests can't name directly.</summary>
    private static void Dispatch(GraphicsDevice device, string shader, int count, params object[] args)
    {
        Type type = SampleAssembly.Type(Sample, $"SharpCompute.Samples.Imaging.{shader}");
        MethodInfo method = typeof(GraphicsDevice).GetMethods()
            .Single(static m => m.Name == "For" && m.GetParameters().Length == 2)
            .MakeGenericMethod(type);

        method.Invoke(device, [count, Activator.CreateInstance(type, args)]);
    }

    private static int RunCli(params string[] args) => (int)SampleAssembly.Call(Sample, "Program", "Main", [args])!;

    private static object Parse(string name, string[] arguments)
    {
        Assert.True(TryParse(name, arguments, out string error, out object? filter), error);
        return filter!;
    }

    private static bool TryParse(string name, string[] arguments, out string error) => TryParse(name, arguments, out error, out _);

    private static bool TryParse(string name, string[] arguments, out string error, out object? filter)
    {
        object?[] args = [name, (IReadOnlyList<string>)arguments, null, null];
        bool ok = (bool)SampleAssembly.Call(Sample, "SharpCompute.Samples.Imaging.FilterSpec", "TryParse", args)!;
        filter = args[2];
        error = (string)args[3]!;
        return ok;
    }

    private static Float4[] TestImage(int width, int height)
    {
        dynamic image = SampleAssembly.Call(Sample, "TestImage", "Create", width, height)!;
        return (Float4[])image.Pixels;
    }

    private static Float4[] RunGpu(object filter, Float4[] input)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> source = device.AllocateReadWriteBuffer<Float4>(input);
        using ReadWriteBuffer<Float4> destination = device.AllocateReadWriteBuffer<Float4>(input.Length);
        using IDisposable filters = SampleAssembly.Create(Sample, "SharpCompute.Samples.Imaging.GpuFilters", device, Width, Height);

        ((dynamic)filters).Apply((dynamic)filter, source, destination);
        return destination.ToArray();
    }

    private static void Save(string path, Float4[] pixels, int width = Width, int height = Height)
    {
        object image = SampleAssembly.Create(Sample, "ImageData", width, height, pixels);
        SampleAssembly.Call(Sample, "ImageFile", "Save", path, image);
    }

    private static (int Width, int Height, Float4[] Pixels) Load(string path)
    {
        dynamic image = SampleAssembly.Call(Sample, "ImageFile", "Load", path)!;
        return ((int)image.Width, (int)image.Height, (Float4[])image.Pixels);
    }

    private static float MaxDifference(Float4[] a, Float4[] b)
    {
        Assert.Equal(a.Length, b.Length);
        float max = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            Float4 d = a[i] - b[i];
            max = MathF.Max(max, MathF.Max(MathF.Max(MathF.Abs(d.X), MathF.Abs(d.Y)), MathF.Max(MathF.Abs(d.Z), MathF.Abs(d.W))));
        }

        return max;
    }
}
