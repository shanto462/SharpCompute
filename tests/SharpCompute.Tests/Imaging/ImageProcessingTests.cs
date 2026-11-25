using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SharpCompute.Imaging;

namespace SharpCompute.Tests.Imaging;

/// <summary>The ImageProcessing sample: GPU filters against their CPU reference, ImageIO load/save, and the CLI.</summary>
public sealed class ImageProcessingTests : IDisposable
{
    private const string Sample = SampleAssembly.ImageProcessing;
    private const int Width = 64;
    private const int Height = 48;

    // The CLI accepts up to 1/255; the shared math actually agrees to float rounding (fused multiply-adds).
    private const float Tolerance = 1e-5f;

    // Except for bilinear sampling, where the GPU rounds its weights to 1/256 (see ReadOnlyTexture2D.Sample).
    private const float SamplingTolerance = 1f / 255f;

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
        { "lens", ["0.3", "0.006"] },
        { "lens", ["-0.4", "0.03"] },
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
        Assert.True(difference <= (name == "lens" ? SamplingTolerance : Tolerance), $"{name} {string.Join(' ', arguments)}: max |GPU − CPU| = {difference}");

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

    [SampleGpuFact(Sample)]
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

    [SampleGpuFact(Sample)]
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

    [SampleGpuFact(Sample)]
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
    public void UnpackTextureThenPackTexture_OnTheGpu_RoundTripsStraightPixelsExactly()
    {
        Random random = new(7);
        const int width = 100, height = 100;
        Rgba32[] input = new Rgba32[width * height];

        for (int i = 0; i < input.Length; i++)
        {
            // Straight alpha; a fully transparent pixel keeps no color.
            byte a = (byte)random.Next(256);
            input[i] = a == 0 ? default : new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), a);
        }

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyTexture2D<Rgba32> image = device.AllocateReadOnlyTexture2D<Rgba32>(input, width, height);
        using ReadWriteBuffer<Float4> pixels = device.AllocateReadWriteBuffer<Float4>(input.Length);
        using ReadWriteTexture2D<Rgba32> repacked = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        Dispatch2D(device, "UnpackTexture", width, height, image, pixels);
        Dispatch2D(device, "PackTexture", width, height, pixels, repacked);

        Assert.Equal(input, repacked.ToArray());

        // Same floats as the CPU conversion.
        Float4[] cpu = SampleAssembly.Type(Sample, "SharpCompute.Samples.Imaging.PixelConversion")
            .GetMethod("FromStraightRgba32", [typeof(ReadOnlySpan<Rgba32>)])!
            .CreateDelegate<Func<ReadOnlySpan<Rgba32>, Float4[]>>()(input);
        Assert.True(MaxDifference(pixels.ToArray(), cpu) <= 1e-7f);
    }

    [SampleGpuFact(Sample)]
    public void PackTexture_OnTheGpu_MatchesTheCpuConversion()
    {
        Random random = new(11);
        const int width = 200, height = 100;
        Float4[] pixels = new Float4[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Float4(Value(), Value(), Value(), Value());
        }

        pixels[1] = new Float4(0.5f, 0.5f, 0.5f, 0f);
        pixels[2] = new Float4(0.3f, 0.2f, 0.1f, float.NaN);

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> source = device.AllocateReadWriteBuffer<Float4>(pixels);
        using ReadWriteTexture2D<Rgba32> straight = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        Dispatch2D(device, "PackTexture", width, height, source, straight);

        Rgba32[] cpu = (Rgba32[])SampleAssembly.Call(Sample, "SharpCompute.Samples.Imaging.PixelConversion", "ToStraightRgba32", [pixels])!;
        Rgba32[] gpu = straight.ToArray();

        // The texture write rounds to nearest like the CPU, but the straight-alpha division is not
        // correctly rounded on the GPU, so a value right at a rounding boundary can land one step away.
        for (int i = 0; i < pixels.Length; i++)
        {
            Assert.InRange(gpu[i].R - cpu[i].R, -1, 1);
            Assert.InRange(gpu[i].G - cpu[i].G, -1, 1);
            Assert.InRange(gpu[i].B - cpu[i].B, -1, 1);
            Assert.InRange(gpu[i].A - cpu[i].A, -1, 1);
        }

        // Zero or NaN alpha is transparent black on both, exactly.
        Assert.Equal(default, gpu[1]);
        Assert.Equal(default, gpu[2]);
        Assert.Equal(default, cpu[1]);
        Assert.Equal(default, cpu[2]);

        float Value() => (float)((random.NextDouble() * 1.4) - 0.2);
    }

    [SampleGpuFact(Sample)]
    public void TextureLoadAndSave_MatchTheArrayPaths()
    {
        Float4[] pixels = TestImage(Width, Height);
        string png = Path.Combine(_directory, "zero-copy-input.png");
        string copy = Path.Combine(_directory, "zero-copy-output.png");
        Save(png, pixels);

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyTexture2D<Rgba32> loaded = ImageLoader.LoadTexture2D<Rgba32>(device, png);

        Assert.Equal((Width, Height), (loaded.Width, loaded.Height));

        using ReadWriteBuffer<Float4> unpacked = device.AllocateReadWriteBuffer<Float4>(Width * Height);
        Dispatch2D(device, "UnpackTexture", Width, Height, loaded, unpacked);
        Assert.True(MaxDifference(unpacked.ToArray(), Load(png).Pixels) <= 1e-7f);

        // Packed back into a texture and saved from it, the pixels are those of the input.
        using ReadWriteTexture2D<Rgba32> output = device.AllocateReadWriteTexture2D<Rgba32>(Width, Height);
        Dispatch2D(device, "PackTexture", Width, Height, unpacked, output);
        ImageSaver.Save(output, copy);
        Assert.Equal(Load(png).Pixels, Load(copy).Pixels);

        string jpeg = Path.Combine(_directory, "zero-copy-output.jpg");
        ImageSaver.Save(output, jpeg);
        Assert.Equal((Width, Height), (Load(jpeg).Width, Load(jpeg).Height));
    }

    [SampleFact(Sample)]
    public void LensSourceUv_KeepsTheCenterAndTheCorners()
    {
        MethodInfo uv = SampleAssembly.Type(Sample, "SharpCompute.Samples.Imaging.LensDistortion").GetMethod("SourceUv")!;
        Float2 size = new(200f, 100f);
        Float2 At(Float2 pixel, float strength) => (Float2)uv.Invoke(null, [pixel, size, strength, 1f])!;

        foreach (float strength in new[] { 0.3f, 1f, -0.4f })
        {
            Float2 center = At(new Float2(100f, 50f), strength);
            Assert.Equal((0.5f, 0.5f), (center.X, center.Y));
        }

        // A barrel keeps its corners where they are, and the middle of an edge samples from inside the image.
        Float2 corner = At(new Float2(200f, 100f), 0.3f);
        Assert.Equal(1f, corner.X, 1e-6f);
        Assert.Equal(1f, corner.Y, 1e-6f);
        Assert.True(At(new Float2(200f, 50f), 0.3f).X < 1f);

        // A pincushion samples inside the image everywhere.
        Float2 pinched = At(new Float2(200f, 100f), -0.4f);
        Assert.True(pinched.X < 1f && pinched.Y < 1f);
    }

    /// <summary>Runs a sample shader through <c>GraphicsDevice.For(int, int, in T)</c>, which tests can't name directly.</summary>
    private static void Dispatch2D(GraphicsDevice device, string shader, int width, int height, params object[] args)
    {
        Type type = SampleAssembly.Type(Sample, $"SharpCompute.Samples.Imaging.{shader}");
        MethodInfo method = typeof(GraphicsDevice).GetMethods()
            .Single(static m => m.Name == "For" && m.GetParameters() is [{ ParameterType: var p0 }, { ParameterType: var p1 }, { ParameterType.IsByRef: true }] && p0 == typeof(int) && p1 == typeof(int))
            .MakeGenericMethod(type);

        method.Invoke(device, [width, height, Activator.CreateInstance(type, args)]);
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
