using System;
using System.IO;
using System.Linq;
using SharpCompute.Imaging;
using SharpCompute.Interop;
using SharpCompute.Samples.Shaders;
using SharpCompute.Tests.NativeInterop;

namespace SharpCompute.Tests.ImagingLibrary;

/// <summary>SharpCompute.Imaging: loading images into textures and saving textures as images, through ImageIO.</summary>
public sealed class ImageLoaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("sharpcompute-imaging-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static GraphicsDevice Device => GraphicsDevice.GetDefault();

    [MetalFact]
    public void Png_RoundTripsRgba32_BitExactly_IncludingTranslucentPixels()
    {
        const int width = 37, height = 23;
        Rgba32[] pixels = TextureTests.RandomPixels(width * height, seed: 5);

        // Every alpha value, each with a color that premultiplying would round away. Alpha 0 keeps no
        // color in a PNG (encoders may clear it), so it is fully transparent black here.
        for (int a = 0; a < 256; a++)
        {
            pixels[a] = a == 0 ? default : new Rgba32((byte)(255 - a), (byte)a, 77, (byte)a);
        }

        using ReadOnlyTexture2D<Rgba32> source = Device.AllocateReadOnlyTexture2D<Rgba32>(pixels, width, height);
        string path = Path.Combine(_directory, "round-trip.png");
        ImageSaver.Save(source, path);

        using ReadOnlyTexture2D<Rgba32> loaded = ImageLoader.LoadTexture2D<Rgba32>(Device, path);

        Assert.Equal((width, height), (loaded.Width, loaded.Height));
        Assert.Equal(pixels, loaded.ToArray());

        // The same bytes as Rgba32Srgb, and through a read-write texture.
        using ReadOnlyTexture2D<Rgba32Srgb> srgb = ImageLoader.LoadTexture2D<Rgba32Srgb>(Device, path);
        Assert.Equal(pixels.Select(static p => (p.R, p.G, p.B, p.A)), srgb.ToArray().Select(static p => (p.R, p.G, p.B, p.A)));

        using ReadWriteTexture2D<Rgba32> readWrite = ImageLoader.LoadReadWriteTexture2D<Rgba32>(Device, path);
        Assert.Equal(pixels, readWrite.ToArray());
    }

    [MetalFact]
    public void Png_RoundTripsR8_BitExactly()
    {
        const int width = 64, height = 4;
        R8[] pixels = [.. Enumerable.Range(0, width * height).Select(static i => new R8((byte)((i * 7) % 256)))];

        using ReadOnlyTexture2D<R8> source = Device.AllocateReadOnlyTexture2D<R8>(pixels, width, height);
        string path = Path.Combine(_directory, "gray.png");
        ImageSaver.Save(source, path);

        Assert.Equal(ImageColorModel.Gray, ImageLoader.GetInfo(path).ColorModel);

        using ReadOnlyTexture2D<R8> loaded = ImageLoader.LoadTexture2D<R8>(Device, path);
        Assert.Equal(pixels.Select(static p => p.R), loaded.ToArray().Select(static p => p.R));
    }

    [MetalFact]
    public void Opaque_Rgba32_IsSavedWithoutAnAlphaChannel()
    {
        Rgba32[] pixels = [.. TextureTests.RandomPixels(16, seed: 3).Select(static p => p with { A = 255 })];
        using ReadOnlyTexture2D<Rgba32> opaque = Device.AllocateReadOnlyTexture2D<Rgba32>(pixels, 4, 4);
        string path = Path.Combine(_directory, "opaque.png");
        ImageSaver.Save(opaque, path);

        Assert.False(ImageLoader.GetInfo(path).HasAlpha);

        using ReadOnlyTexture2D<Rgba32> loaded = ImageLoader.LoadTexture2D<Rgba32>(Device, path);
        Assert.Equal(pixels, loaded.ToArray());

        pixels[5].A = 254;
        using ReadOnlyTexture2D<Rgba32> translucent = Device.AllocateReadOnlyTexture2D<Rgba32>(pixels, 4, 4);
        ImageSaver.Save(translucent, path);
        Assert.True(ImageLoader.GetInfo(path).HasAlpha);
    }

    [MetalFact]
    public void Jpeg_RoundTripsWithinTolerance_AndQualityChangesTheSize()
    {
        const int width = 128, height = 96;
        Rgba32[] pixels = Gradient(width, height);
        using ReadOnlyTexture2D<Rgba32> source = Device.AllocateReadOnlyTexture2D<Rgba32>(pixels, width, height);

        string path = Path.Combine(_directory, "photo.jpg");
        ImageSaver.Save(source, path);
        using ReadOnlyTexture2D<Rgba32> loaded = ImageLoader.LoadTexture2D<Rgba32>(Device, path);

        Rgba32[] result = loaded.ToArray();
        double error = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            error += Math.Abs(result[i].R - pixels[i].R) + Math.Abs(result[i].G - pixels[i].G) + Math.Abs(result[i].B - pixels[i].B);
            Assert.Equal(255, result[i].A);
        }

        Assert.InRange(error / (pixels.Length * 3), 0, 3);

        string small = Path.Combine(_directory, "small.jpeg");
        ImageSaver.Save(source, small, jpegQuality: 10);
        Assert.True(new FileInfo(small).Length < new FileInfo(path).Length);

        ImageInfo info = ImageLoader.GetInfo(path);
        Assert.Equal(new ImageInfo(width, height, ImageColorModel.Rgb, false, ImageOrientation.Up, 8, "public.jpeg"), info);
    }

    [MetalFact]
    public void Heic_RoundTripsWithinTolerance()
    {
        const int width = 64, height = 64;
        Rgba32[] pixels = Gradient(width, height);
        using ReadOnlyTexture2D<Rgba32> source = Device.AllocateReadOnlyTexture2D<Rgba32>(pixels, width, height);

        string path = Path.Combine(_directory, "photo.heic");
        ImageSaver.Save(source, path);
        Assert.Equal("public.heic", ImageLoader.GetInfo(path).TypeIdentifier);

        using ReadOnlyTexture2D<Rgba32> loaded = ImageLoader.LoadTexture2D<Rgba32>(Device, path);
        Rgba32[] result = loaded.ToArray();
        double error = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            error += Math.Abs(result[i].R - pixels[i].R) + Math.Abs(result[i].G - pixels[i].G) + Math.Abs(result[i].B - pixels[i].B);
        }

        Assert.InRange(error / (pixels.Length * 3), 0, 3);
    }

    [MetalFact]
    public void KnownColors_LoadAsSrgbBytes_LinearFloats_AndLuminance()
    {
        // White, black, mid-gray 128, orange (255, 128, 0), half-transparent and opaque pure green.
        Rgba32[] pixels = [new(255, 255, 255, 255), new(0, 0, 0, 255), new(128, 128, 128, 255), new(255, 128, 0, 255), new(0, 255, 0, 128), new(0, 255, 0, 255)];
        string path = TestEncoder.Write(_directory, "colors.png", TestEncoder.Rgba8(pixels, pixels.Length, 1));

        float mid = SrgbByteToLinear(128);
        Float4[] expected = [new(1, 1, 1, 1), new(0, 0, 0, 1), new(mid, mid, mid, 1), new(1, mid, 0, 1), new(0, 1, 0, 128 / 255f), new(0, 1, 0, 1)];

        using ReadOnlyTexture2D<Float4> linear = ImageLoader.LoadTexture2D<Float4>(Device, path);
        AssertClose(expected, linear.ToArray(), 2e-4f);

        using ReadOnlyTexture2D<Rgba16Float> half = ImageLoader.LoadTexture2D<Rgba16Float>(Device, path);
        AssertClose(expected, [.. half.ToArray().Select(static p => new Float4((float)p.R, (float)p.G, (float)p.B, (float)p.A))], 1e-3f);

        // The bytes are kept as stored, and an Rgba32Srgb read (on the CPU here) decodes them to the same linear values.
        using ReadOnlyTexture2D<Rgba32> bytes = ImageLoader.LoadTexture2D<Rgba32>(Device, path);
        Assert.Equal(pixels, bytes.ToArray());

        using ReadOnlyTexture2D<Rgba32Srgb> srgb = ImageLoader.LoadTexture2D<Rgba32Srgb>(Device, path);
        AssertClose(expected, [.. Enumerable.Range(0, pixels.Length).Select(x => srgb[x, 0])], 1e-6f);

        // R8: gray stays gray, pure green is its luminance (0.7152 in linear light, about 219 with a
        // 2.2 gamma), and a translucent pixel is composited over black, in the gamma-encoded values.
        using ReadOnlyTexture2D<R8> gray = ImageLoader.LoadTexture2D<R8>(Device, path);
        byte[] luma = [.. gray.ToArray().Select(static p => p.R)];
        Assert.Equal(255, luma[0]);
        Assert.Equal(0, luma[1]);
        Assert.InRange(luma[2], 126, 130);
        Assert.InRange(luma[5], 210, 228);
        Assert.InRange(luma[4], (luma[5] / 2) - 2, (luma[5] / 2) + 2);
    }

    [MetalFact]
    public void WideGamutSource_LoadsOutsideZeroToOne_InFloat_AndClipsInBytes()
    {
        // Pure Display P3 red is outside sRGB: extended linear sRGB gives it negative green and blue.
        Rgba32[] red = [new(255, 0, 0, 255)];
        string path = TestEncoder.Write(_directory, "p3.png", TestEncoder.Rgba8(red, 1, 1, space: "kCGColorSpaceDisplayP3"));

        using ReadOnlyTexture2D<Float4> linear = ImageLoader.LoadTexture2D<Float4>(Device, path);
        Float4 p = linear[0, 0];
        Assert.True(p.X > 1.05f, $"red {p.X}");
        Assert.True(p.Y < -0.01f && p.Z < -0.01f, $"green {p.Y}, blue {p.Z}");

        using ReadOnlyTexture2D<Rgba32> bytes = ImageLoader.LoadTexture2D<Rgba32>(Device, path);
        Assert.Equal(new Rgba32(255, 0, 0, 255), bytes.ToArray()[0]);
    }

    [MetalFact]
    public void SixteenBitPng_KeepsPrecisionBeyondEightBits_InFloatTextures()
    {
        // 256 levels 16 apart (an 8-bit step is 257), from 32768: 8 bits keep only 17 of them.
        const int width = 256;
        ushort[] channels = new ushort[width * 4];

        for (int i = 0; i < width; i++)
        {
            ushort v = (ushort)(32768 + (i * 16));
            (channels[i * 4], channels[(i * 4) + 1], channels[(i * 4) + 2], channels[(i * 4) + 3]) = (v, v, v, 65535);
        }

        string path = TestEncoder.Write(_directory, "deep.png", TestEncoder.Rgba16(channels, width, 1));
        Assert.Equal(16, ImageLoader.GetInfo(path).BitsPerComponent);

        using ReadOnlyTexture2D<Float4> single = ImageLoader.LoadTexture2D<Float4>(Device, path);
        float[] red = [.. single.ToArray().Select(static p => p.X)];

        for (int i = 0; i < width; i++)
        {
            Assert.Equal(SrgbToLinear((32768 + (i * 16)) / 65535.0), red[i], 5e-5f);
        }

        Assert.All(red.Zip(red.Skip(1)), static pair => Assert.True(pair.Second >= pair.First, $"{pair.First} then {pair.Second}"));
        int distinctSingle = red.Distinct().Count();
        Assert.True(distinctSingle >= 200, $"{distinctSingle} distinct float values");

        using ReadOnlyTexture2D<Rgba16Float> half = ImageLoader.LoadTexture2D<Rgba16Float>(Device, path);
        int distinct = half.ToArray().Select(static p => p.R).Distinct().Count();
        Assert.True(distinct > 100, $"{distinct} distinct half values");

        using ReadOnlyTexture2D<Rgba32> bytes = ImageLoader.LoadTexture2D<Rgba32>(Device, path);
        Assert.InRange(bytes.ToArray().Select(static p => p.R).Distinct().Count(), 16, 17);
    }

    [MetalFact]
    public void FloatTextures_SaveAsSixteenBitPng_AndRoundTripWithinASixteenBitStep()
    {
        const int width = 300;
        Float4[] pixels = [.. Enumerable.Range(0, width).Select(static i => new Float4(i / 299f, SrgbToLinear(i / 299.0), 0.25f, 1f - (i / 600f)))];
        using ReadOnlyTexture2D<Float4> source = Device.AllocateReadOnlyTexture2D<Float4>(pixels, width, 1);

        string path = Path.Combine(_directory, "float.png");
        ImageSaver.Save(source, path);

        ImageInfo info = ImageLoader.GetInfo(path);
        Assert.Equal((16, true), (info.BitsPerComponent, info.HasAlpha));

        using ReadOnlyTexture2D<Float4> loaded = ImageLoader.LoadTexture2D<Float4>(Device, path);

        // One 16-bit sRGB step is at most 2.4 / 65535 in linear light (the slope of the curve at 1).
        AssertClose(pixels, loaded.ToArray(), 6e-5f);

        using ReadOnlyTexture2D<Rgba16Float> halfSource = Device.AllocateReadOnlyTexture2D<Rgba16Float>(
            [.. pixels.Select(static p => new Rgba16Float((Half)p.X, (Half)p.Y, (Half)p.Z, (Half)p.W))], width, 1);
        ImageSaver.Save(halfSource, path);
        using ReadOnlyTexture2D<Float4> fromHalf = ImageLoader.LoadTexture2D<Float4>(Device, path);
        AssertClose(pixels, fromHalf.ToArray(), 1e-3f);
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ExifOrientation_IsApplied(int orientation)
    {
        // A stored 3 × 2 picture with six different (opaque) pixels.
        const int w = 3, h = 2;
        Rgba32[] stored = [.. Enumerable.Range(0, w * h).Select(static i => new Rgba32((byte)(40 * i), (byte)(250 - (40 * i)), (byte)(i * 13), 255))];
        string path = TestEncoder.Write(_directory, $"oriented-{orientation}.png", TestEncoder.Rgba8(stored, w, h, orientation: orientation));

        ImageInfo info = ImageLoader.GetInfo(path);
        Assert.Equal((ImageOrientation)orientation, info.Orientation);

        using ReadOnlyTexture2D<Rgba32> loaded = ImageLoader.LoadTexture2D<Rgba32>(Device, path);
        bool transposed = orientation >= 5;
        Assert.Equal(transposed ? (h, w) : (w, h), (loaded.Width, loaded.Height));
        Assert.Equal((info.Width, info.Height), (loaded.Width, loaded.Height));
        Assert.Equal(transposed, info.IsTransposed);

        Rgba32[] upright = loaded.ToArray();

        for (int y = 0; y < loaded.Height; y++)
        {
            for (int x = 0; x < loaded.Width; x++)
            {
                // Where pixel (x, y) of the upright picture is stored, from the EXIF definitions.
                (int sx, int sy) = orientation switch
                {
                    1 => (x, y),
                    2 => (w - 1 - x, y),
                    3 => (w - 1 - x, h - 1 - y),
                    4 => (x, h - 1 - y),
                    5 => (y, x),
                    6 => (y, h - 1 - x),
                    7 => (w - 1 - y, h - 1 - x),
                    _ => (w - 1 - y, x),
                };

                Assert.Equal(stored[(sy * w) + sx], upright[(y * loaded.Width) + x]);
            }
        }
    }

    [MetalFact]
    public void BytesAndStreams_LoadLikeFiles()
    {
        Rgba32[] pixels = TextureTests.RandomPixels(20 * 10, seed: 9);
        byte[] png = TestEncoder.Rgba8(pixels, 20, 10);

        using (ReadOnlyTexture2D<Rgba32> fromBytes = ImageLoader.LoadTexture2D<Rgba32>(Device, png))
        {
            Assert.Equal(pixels, fromBytes.ToArray());
        }

        // A MemoryStream is read from its position; another stream is read to its end.
        using MemoryStream memory = new();
        memory.Write([1, 2, 3]);
        memory.Write(png);
        memory.Position = 3;

        using (ReadWriteTexture2D<Rgba32> fromMemory = ImageLoader.LoadReadWriteTexture2D<Rgba32>(Device, memory))
        {
            Assert.Equal(pixels, fromMemory.ToArray());
            Assert.Equal(memory.Length, memory.Position);
        }

        string path = TestEncoder.Write(_directory, "stream.png", png);

        using (FileStream file = File.OpenRead(path))
        using (ReadOnlyTexture2D<Rgba32> fromFile = ImageLoader.LoadTexture2D<Rgba32>(Device, new BufferedStream(file, 7)))
        {
            Assert.Equal(pixels, fromFile.ToArray());
        }

        Assert.Equal(ImageLoader.GetInfo(path), ImageLoader.GetInfo(png));
        Assert.Equal(new ImageInfo(20, 10, ImageColorModel.Rgb, true, ImageOrientation.Up, 8, "public.png"), ImageLoader.GetInfo(png));

        // And saving to a stream writes what saving to a file does.
        using ReadOnlyTexture2D<Rgba32> texture = Device.AllocateReadOnlyTexture2D<Rgba32>(pixels, 20, 10);
        using MemoryStream encoded = new();
        ImageSaver.Save(texture, encoded, ImageFormat.Png);
        using ReadOnlyTexture2D<Rgba32> decoded = ImageLoader.LoadTexture2D<Rgba32>(Device, encoded.ToArray());
        Assert.Equal(pixels, decoded.ToArray());
    }

    [MetalFact]
    public void Save_ExplicitFormat_OverridesTheExtension()
    {
        using ReadOnlyTexture2D<Rgba32> texture = Device.AllocateReadOnlyTexture2D<Rgba32>(Gradient(8, 8), 8, 8);
        string path = Path.Combine(_directory, "really-a-jpeg.png");
        ImageSaver.Save(texture, path, ImageFormat.Jpeg);
        Assert.Equal("public.jpeg", ImageLoader.GetInfo(path).TypeIdentifier);
    }

    [MetalFact]
    public void Save_ReadsANativeTextureTheCpuCannotAddress_ThroughCopyTo()
    {
        GraphicsDevice device = Device;
        Rgba32[] pixels = TextureTests.RandomPixels(8 * 5, seed: 2);
        nint shared = NativeMetal.NewTexture(device, 8, 5, storageMode: NativeMetal.ModeShared);
        nint gpuOnly = NativeMetal.NewTexture(device, 8, 5, storageMode: NativeMetal.ModePrivate);

        try
        {
            using ReadWriteTexture2D<Rgba32> texture = device.WrapNativeReadWriteTexture2D<Rgba32>(shared);
            Assert.False(texture.IsCpuAccessible);
            texture.CopyFrom(pixels);

            string path = Path.Combine(_directory, "native.png");
            ImageSaver.Save(texture, path);
            using ReadOnlyTexture2D<Rgba32> loaded = ImageLoader.LoadTexture2D<Rgba32>(device, path);
            Assert.Equal(pixels, loaded.ToArray());

            using ReadWriteTexture2D<Rgba32> privateTexture = device.WrapNativeReadWriteTexture2D<Rgba32>(gpuOnly);
            Assert.Throws<NotSupportedException>(() => ImageSaver.Save(privateTexture, path));
        }
        finally
        {
            ObjC.Release(shared);
            ObjC.Release(gpuOnly);
        }
    }

    [MetalFact]
    public void Errors_AreClear()
    {
        GraphicsDevice device = Device;
        string missing = Path.Combine(_directory, "missing.png");
        Assert.Throws<FileNotFoundException>(() => ImageLoader.LoadTexture2D<Rgba32>(device, missing));
        Assert.Throws<FileNotFoundException>(() => ImageLoader.GetInfo(missing));

        // Not an image at all: an unsupported format.
        string text = Path.Combine(_directory, "not-an-image.png");
        File.WriteAllText(text, "hello, this is text");
        InvalidDataException unknown = Assert.Throws<InvalidDataException>(() => ImageLoader.LoadTexture2D<Rgba32>(device, text));
        Assert.Contains("not in an image format ImageIO reads", unknown.Message);
        Assert.Throws<InvalidDataException>(() => ImageLoader.GetInfo(text));
        Assert.Throws<InvalidDataException>(() => ImageLoader.LoadTexture2D<Rgba32>(device, ReadOnlySpan<byte>.Empty));

        // A PNG with its header and no pixel data: recognized, but it does not decode.
        byte[] png = TestEncoder.Rgba8(TextureTests.RandomPixels(64 * 64, seed: 1), 64, 64);
        string truncated = TestEncoder.Write(_directory, "header-only.png", png[..33]);
        InvalidDataException broken = Assert.Throws<InvalidDataException>(() => ImageLoader.LoadTexture2D<Rgba32>(device, truncated));
        Assert.DoesNotContain("not in an image format", broken.Message);

        // Larger than a texture can be: rejected from the header, before decoding.
        string wide = Path.Combine(_directory, "wide.png");
        PngEncoder.Write(wide, new Rgba32[ImageLoader.MaxDimension + 1], ImageLoader.MaxDimension + 1, 1);
        Assert.Equal(ImageLoader.MaxDimension + 1, ImageLoader.GetInfo(wide).Width);
        NotSupportedException large = Assert.Throws<NotSupportedException>(() => ImageLoader.LoadTexture2D<Rgba32>(device, wide));
        Assert.Contains("16384", large.Message);

        // Element types images don't load into, and Rgba32Srgb, which the GPU can't write.
        string good = TestEncoder.Write(_directory, "good.png", png);
        Assert.Throws<NotSupportedException>(() => ImageLoader.LoadTexture2D<Half>(device, good));
        Assert.Throws<NotSupportedException>(() => ImageLoader.LoadTexture2D<Rg16>(device, good));
        Assert.Throws<NotSupportedException>(() => ImageLoader.LoadReadWriteTexture2D<Rgba32Srgb>(device, good));
        Assert.Throws<ArgumentNullException>(() => ImageLoader.LoadTexture2D<Rgba32>(null!, good));

        // Saving.
        using ReadOnlyTexture2D<Rgba32> texture = ImageLoader.LoadTexture2D<Rgba32>(device, good);
        Assert.Throws<NotSupportedException>(() => ImageSaver.Save(texture, Path.Combine(_directory, "image.bmp")));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageSaver.Save(texture, Path.Combine(_directory, "q.jpg"), jpegQuality: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageSaver.Save(texture, Path.Combine(_directory, "q.jpg"), jpegQuality: 101));
        Assert.Throws<DirectoryNotFoundException>(() => ImageSaver.Save(texture, Path.Combine(_directory, "no", "such", "dir.png")));
        Assert.Throws<ArgumentException>(() => ImageSaver.Save(texture, new MemoryStream(), ImageFormat.FromExtension));

        using ReadOnlyTexture2D<Half> half = device.AllocateReadOnlyTexture2D<Half>(2, 2);
        Assert.Throws<NotSupportedException>(() => ImageSaver.Save(half, Path.Combine(_directory, "half.png")));

        ReadOnlyTexture2D<Rgba32> disposed = device.AllocateReadOnlyTexture2D<Rgba32>(2, 2);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => ImageSaver.Save(disposed, Path.Combine(_directory, "disposed.png")));
    }

    [MetalFact]
    public void Load_AllocatesLittle_WhateverTheImageSize()
    {
        string small = Path.Combine(_directory, "small.png");
        string large = Path.Combine(_directory, "large.png");

        using (ReadOnlyTexture2D<Rgba32> s = Device.AllocateReadOnlyTexture2D<Rgba32>(Gradient(64, 64), 64, 64))
        using (ReadOnlyTexture2D<Rgba32> l = Device.AllocateReadOnlyTexture2D<Rgba32>(Gradient(2048, 2048), 2048, 2048))
        {
            ImageSaver.Save(s, small);
            ImageSaver.Save(l, large);
        }

        // Warm up: the first load looks up the ImageIO constants and JIT-compiles the paths.
        ImageLoader.LoadTexture2D<Rgba32>(Device, small).Dispose();
        ImageLoader.LoadTexture2D<Float4>(Device, small).Dispose();

        long smallBytes = Allocated(small);
        long largeBytes = Allocated(large);

        // The 2048 × 2048 image is 16 MB of Rgba32 (64 MB as Float4): a copy through managed memory,
        // or anything that grows with the image, would be megabytes. A few kilobytes are the texture
        // objects (and, now and then, an allocation of the runtime's on the same thread).
        Assert.True(smallBytes < 32_768, $"loading 64 × 64 allocated {smallBytes} bytes");
        Assert.True(largeBytes < 32_768, $"loading 2048 × 2048 allocated {largeBytes} bytes");

        // The least of a few loads: a one-off allocation of the runtime's (a finalizer list growing,
        // say) is not the loader's.
        static long Allocated(string path) => Enumerable.Range(0, 3).Min(_ =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            ReadOnlyTexture2D<Float4> texture = ImageLoader.LoadTexture2D<Float4>(Device, path);
            long after = GC.GetAllocatedBytesForCurrentThread();
            texture.Dispose();
            return after - before;
        });
    }

    [MetalFact]
    public void ReadmeExample_LoadsSamplesAndSaves()
    {
        GraphicsDevice device = Device;
        string input = Path.Combine(_directory, "photo.png");

        using (ReadOnlyTexture2D<Rgba32> source = device.AllocateReadOnlyTexture2D<Rgba32>(Gradient(96, 64), 96, 64))
        {
            ImageSaver.Save(source, input);
        }

        using ReadOnlyTexture2D<Rgba32> photo = ImageLoader.LoadTexture2D<Rgba32>(device, input);
        using ReadWriteTexture2D<Rgba32> result = device.AllocateReadWriteTexture2D<Rgba32>(photo.Width, photo.Height);

        device.ForEach(result, new Vignette(photo));
        string output = Path.Combine(_directory, "vignette.jpg");
        ImageSaver.Save(result, output, jpegQuality: 85);

        Assert.Equal((96, 64), (ImageLoader.GetInfo(output).Width, ImageLoader.GetInfo(output).Height));

        // The middle keeps its color, the corners are darker.
        Assert.True(result[48, 32].Y > 0.4f);
        Assert.True(result[0, 0].X + result[0, 0].Y + result[0, 0].Z < 0.4f * (photo[0, 0].X + photo[0, 0].Y + photo[0, 0].Z + 0.01f) + 0.05f);
    }

    private static Rgba32[] Gradient(int width, int height) =>
        [.. Enumerable.Range(0, width * height).Select(i => new Rgba32((byte)(i % width * 255 / Math.Max(1, width - 1)), (byte)(i / width * 255 / Math.Max(1, height - 1)), 96, 255))];

    private static float SrgbToLinear(double c) => (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));

    private static float SrgbByteToLinear(byte value) => SrgbToLinear(value / 255.0);

    private static void AssertClose(Float4[] expected, Float4[] actual, float tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            Float4 d = actual[i] - expected[i];
            float max = MathF.Max(MathF.Max(MathF.Abs(d.X), MathF.Abs(d.Y)), MathF.Max(MathF.Abs(d.Z), MathF.Abs(d.W)));
            Assert.True(max <= tolerance, $"pixel {i}: expected {expected[i]}, got {actual[i]} (off by {max})");
        }
    }
}

/// <summary>The example shader of docs/imaging.md: darkens a photo toward its corners.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Vignette(ReadOnlyTexture2D<Rgba32> photo) : IComputeShader<Float4>
{
    public Float4 Execute()
    {
        Float2 uv = ThreadIds.Normalized.XY;
        float edge = Gpu.Length(uv - 0.5f);
        return new Float4(photo.Sample(Sampler.LinearClamp, uv).XYZ * Gpu.Saturate(1.25f - (2f * edge * edge)), 1f);
    }
}
