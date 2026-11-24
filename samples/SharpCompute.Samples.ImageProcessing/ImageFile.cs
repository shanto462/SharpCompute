// Original SharpCompute sample (MIT).
using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpCompute.Samples.ImageProcessing.Interop;
using SharpCompute.Samples.Imaging;
using SharpCompute.Samples.Shaders;

namespace SharpCompute.Samples.ImageProcessing;

/// <summary>An image as premultiplied <see cref="Float4"/> pixels, row-major from the top-left.</summary>
public sealed class ImageData
{
    public ImageData(int width, int height, Float4[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != (long)width * height)
        {
            throw new ArgumentException($"Expected {width}×{height} = {(long)width * height} pixels, got {pixels.Length}.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public Float4[] Pixels { get; }
}

/// <summary>
/// Reads any format macOS ImageIO decodes (JPEG, PNG, HEIC, TIFF, ...) and writes PNG (with the
/// sample PNG encoder) or JPEG/HEIC (with ImageIO), all in sRGB.
/// </summary>
public static unsafe class ImageFile
{
    private const double JpegQuality = 0.92;

    /// <summary>
    /// Decodes the first image in <paramref name="path"/>: <c>CGImageSourceCreateWithURL</c> →
    /// <c>CGImageSourceCreateImageAtIndex</c>, drawn into an sRGB RGBA8 premultiplied bitmap context
    /// (which converts from the file's color space). An EXIF orientation is applied, so camera
    /// photos come out upright.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="InvalidDataException">ImageIO can't decode it.</exception>
    public static ImageData Load(string path)
    {
        nint image = Decode(path);

        try
        {
            int width = checked((int)Native.CGImageGetWidth(image));
            int height = checked((int)Native.CGImageGetHeight(image));
            byte[] bytes = new byte[checked(width * height * 4)];

            fixed (byte* data = bytes)
            {
                Rasterize(image, data, width, height);
            }

            Float4[] pixels = new Float4[width * height];
            PixelConversion.FromPremultipliedRgba8(bytes, pixels);
            return new ImageData(width, height, pixels);
        }
        finally
        {
            Native.CGImageRelease(image);
        }
    }

    /// <summary>
    /// Like <see cref="Load"/>, but decodes straight into a new GPU buffer of premultiplied RGBA8
    /// pixels (one <c>uint</c> per pixel, bytes R, G, B, A): CoreGraphics draws into the buffer's
    /// shared memory, so there is no intermediate array and no upload copy. Convert it to floats on
    /// the GPU with <see cref="UnpackRgba8"/>.
    /// </summary>
    /// <inheritdoc cref="Load" path="/exception"/>
    public static ReadWriteBuffer<uint> LoadRgba8(string path, GraphicsDevice device, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        nint image = Decode(path);

        try
        {
            width = checked((int)Native.CGImageGetWidth(image));
            height = checked((int)Native.CGImageGetHeight(image));
            ReadWriteBuffer<uint> pixels = device.AllocateReadWriteBuffer<uint>(checked(width * height));

            try
            {
                fixed (uint* data = pixels.AsSpan())
                {
                    Rasterize(image, data, width, height);
                }

                return pixels;
            }
            catch
            {
                pixels.Dispose();
                throw;
            }
        }
        finally
        {
            Native.CGImageRelease(image);
        }
    }

    /// <summary>
    /// True when <see cref="SaveRgba8"/> expects straight-alpha pixels for <paramref name="path"/>
    /// (PNG); false when it expects premultiplied ones (JPEG, HEIC).
    /// </summary>
    public static bool UsesStraightAlpha(string path) => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Like <see cref="Save"/>, but encodes 8-bit RGBA pixels in place (for example a GPU buffer's
    /// <see cref="Buffer{T}.AsReadOnlySpan"/>), without converting or copying them first. They must be
    /// straight alpha when <see cref="UsesStraightAlpha"/> is true and premultiplied otherwise;
    /// <see cref="PackRgba8"/> produces either on the GPU.
    /// </summary>
    public static void SaveRgba8(string path, ReadOnlySpan<uint> pixels, int width, int height)
    {
        if (pixels.Length != (long)width * height)
        {
            throw new ArgumentException($"Expected {width}×{height} = {(long)width * height} pixels, got {pixels.Length}.", nameof(pixels));
        }

        string fullPath = PrepareOutput(path);

        switch (Path.GetExtension(fullPath).ToLowerInvariant())
        {
            case ".png":
                PngEncoder.Write(fullPath, MemoryMarshal.Cast<uint, Rgba32>(pixels), width, height);
                break;

            case ".jpg" or ".jpeg":
                fixed (uint* data = pixels)
                {
                    WithImageIO(fullPath, "public.jpeg", data, width, height);
                }

                break;

            case ".heic":
                fixed (uint* data = pixels)
                {
                    WithImageIO(fullPath, "public.heic", data, width, height);
                }

                break;

            default:
                throw new NotSupportedException($"Can't write '{Path.GetExtension(fullPath)}' files; use .png, .jpg or .heic.");
        }
    }

    /// <summary>A +1 <c>CGImageRef</c> for the first image in <paramref name="path"/>, with its EXIF orientation applied.</summary>
    private static nint Decode(string path)
    {
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"No such file: {fullPath}", fullPath);
        }

        nint url = Native.CreateUrl(fullPath);
        nint source = 0;
        nint image = 0;

        try
        {
            source = Native.CGImageSourceCreateWithURL(url, 0);

            if (source == 0 || Native.CGImageSourceGetCount(source) == 0)
            {
                throw new InvalidDataException($"ImageIO can't read '{fullPath}' as an image.");
            }

            image = Native.CGImageSourceCreateImageAtIndex(source, 0, 0);

            if (image == 0)
            {
                throw new InvalidDataException($"ImageIO failed to decode '{fullPath}'.");
            }

            if (Orientation(source) is > 1 and <= 8)
            {
                nint upright = CreateUpright(source, image);

                if (upright != 0)
                {
                    Native.CGImageRelease(image);
                    image = upright;
                }
            }

            nint result = image;
            image = 0;
            return result;
        }
        finally
        {
            if (image != 0)
            {
                Native.CGImageRelease(image);
            }

            Native.Release(source);
            Native.Release(url);
        }
    }

    /// <summary>Writes <paramref name="image"/> as PNG, JPEG (quality 0.92) or HEIC, chosen by the extension.</summary>
    /// <exception cref="NotSupportedException">The extension is not .png, .jpg, .jpeg or .heic.</exception>
    public static void Save(string path, ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        string fullPath = PrepareOutput(path);

        switch (Path.GetExtension(fullPath).ToLowerInvariant())
        {
            case ".png":
                PngEncoder.Write(fullPath, PixelConversion.ToStraightRgba32(image.Pixels), image.Width, image.Height);
                break;

            case ".jpg" or ".jpeg":
                WithImageIO(fullPath, "public.jpeg", image);
                break;

            case ".heic":
                WithImageIO(fullPath, "public.heic", image);
                break;

            default:
                throw new NotSupportedException($"Can't write '{Path.GetExtension(fullPath)}' files; use .png, .jpg or .heic.");
        }
    }

    private static string PrepareOutput(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    private static int Orientation(nint source)
    {
        nint properties = Native.CGImageSourceCopyPropertiesAtIndex(source, 0, 0);

        if (properties == 0)
        {
            return 1;
        }

        try
        {
            // Borrowed (a Get function): not released.
            nint number = Native.CFDictionaryGetValue(properties, Native.Constant(Native.ImageIO, "kCGImagePropertyOrientation"));
            int value = 1;
            return number != 0 && Native.CFNumberGetValue(number, Native.CFNumberIntType, &value) != 0 ? value : 1;
        }
        finally
        {
            Native.CFRelease(properties);
        }
    }

    /// <summary>A +1 full-size copy with the EXIF orientation applied, via ImageIO's thumbnail path (or 0).</summary>
    private static nint CreateUpright(nint source, nint image)
    {
        nuint size = Math.Max(Native.CGImageGetWidth(image), Native.CGImageGetHeight(image));
        nint yes = Native.Constant(Native.CoreFoundation, "kCFBooleanTrue");
        nint maxSize = Native.CreateNumber(size);

        try
        {
            nint options = Native.CreateDictionary(
                [
                    Native.Constant(Native.ImageIO, "kCGImageSourceCreateThumbnailFromImageAlways"),
                    Native.Constant(Native.ImageIO, "kCGImageSourceCreateThumbnailWithTransform"),
                    Native.Constant(Native.ImageIO, "kCGImageSourceThumbnailMaxPixelSize"),
                ],
                [yes, yes, maxSize]);

            try
            {
                return Native.CGImageSourceCreateThumbnailAtIndex(source, 0, options);
            }
            finally
            {
                Native.Release(options);
            }
        }
        finally
        {
            Native.Release(maxSize);
        }
    }

    /// <summary>Draws <paramref name="image"/> into <paramref name="width"/> × <paramref name="height"/> premultiplied sRGB RGBA8 pixels at <paramref name="data"/>.</summary>
    private static void Rasterize(nint image, void* data, int width, int height)
    {
        nint space = Native.CGColorSpaceCreateWithName(Native.Constant(Native.CoreGraphics, "kCGColorSpaceSRGB"));

        try
        {
            nint context = Native.CGBitmapContextCreate(data, (nuint)width, (nuint)height, 8, (nuint)(width * 4), space, Native.BitmapInfoPremultipliedRgba);

            if (context == 0)
            {
                throw new InvalidDataException($"CoreGraphics could not create a {width}×{height} RGBA8 bitmap context.");
            }

            try
            {
                // Row 0 of a bitmap context's memory is the top of the drawn image.
                Native.CGContextDrawImage(context, new CGRect(0, 0, width, height), image);
            }
            finally
            {
                Native.CGContextRelease(context);
            }
        }
        finally
        {
            Native.CGColorSpaceRelease(space);
        }
    }

    private static void WithImageIO(string path, string typeIdentifier, ImageData image)
    {
        byte[] bytes = PixelConversion.ToPremultipliedRgba8(image.Pixels);

        fixed (byte* data = bytes)
        {
            WithImageIO(path, typeIdentifier, data, image.Width, image.Height);
        }
    }

    /// <summary>Encodes premultiplied RGBA8 pixels at <paramref name="data"/> with ImageIO.</summary>
    private static void WithImageIO(string path, string typeIdentifier, void* data, int width, int height)
    {
        nint space = Native.CGColorSpaceCreateWithName(Native.Constant(Native.CoreGraphics, "kCGColorSpaceSRGB"));
        nint cgImage = 0;
        nint url = 0;
        nint type = 0;
        nint destination = 0;
        nint quality = 0;
        nint properties = 0;

        try
        {
            nint context = Native.CGBitmapContextCreate(data, (nuint)width, (nuint)height, 8, (nuint)(width * 4), space, Native.BitmapInfoPremultipliedRgba);

            if (context == 0)
            {
                throw new InvalidOperationException("CoreGraphics could not create a bitmap context for saving.");
            }

            // CreateImage copies the pixels or shares them copy-on-write, so the context can go right
            // after; either way the caller keeps the memory alive until the encode below has finished.
            cgImage = Native.CGBitmapContextCreateImage(context);
            Native.CGContextRelease(context);

            url = Native.CreateUrl(path);
            type = Native.CreateString(typeIdentifier);
            destination = Native.CGImageDestinationCreateWithURL(url, type, 1, 0);

            if (cgImage == 0 || destination == 0)
            {
                throw new IOException($"ImageIO can't write '{path}' as {typeIdentifier}.");
            }

            quality = Native.CreateNumber(JpegQuality);
            properties = Native.CreateDictionary([Native.Constant(Native.ImageIO, "kCGImageDestinationLossyCompressionQuality")], [quality]);
            Native.CGImageDestinationAddImage(destination, cgImage, properties);

            if (Native.CGImageDestinationFinalize(destination) == 0)
            {
                throw new IOException($"ImageIO failed to write '{path}'.");
            }
        }
        finally
        {
            Native.Release(properties);
            Native.Release(quality);
            Native.Release(destination);
            Native.Release(type);
            Native.Release(url);

            if (cgImage != 0)
            {
                Native.CGImageRelease(cgImage);
            }

            Native.CGColorSpaceRelease(space);
        }
    }
}
