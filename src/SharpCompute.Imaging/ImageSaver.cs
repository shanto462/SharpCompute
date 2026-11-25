using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpCompute.Imaging.Interop;
using SharpCompute.Interop;

namespace SharpCompute.Imaging;

/// <summary>
/// Saves textures as PNG, JPEG or HEIC through macOS ImageIO, encoding straight from the texture's
/// shared memory.
/// </summary>
/// <remarks>
/// <para>
/// The pixels are taken to be what <see cref="ImageLoader"/> makes of an image, so a load and a
/// save round-trip: straight alpha, and the color space the element type stands for there.
/// <list type="table">
/// <item><term><see cref="Rgba32"/>, <see cref="Rgba32Srgb"/></term><description>8-bit sRGB, encoded in place. A PNG of a texture whose alpha is all 255 is written without an alpha channel.</description></item>
/// <item><term><see cref="R8"/></term><description>8-bit gray (2.2 gamma), encoded in place.</description></item>
/// <item><term><see cref="Rgba16Float"/>, <see cref="Float4"/></term><description>Linear extended sRGB, converted to 16-bit sRGB (clamped to [0, 1]) in a temporary native buffer first: a PNG is 16 bits per channel, JPEG and HEIC get what ImageIO makes of 16-bit sRGB.</description></item>
/// </list>
/// JPEG has no alpha channel, so ImageIO drops it. A native texture the CPU cannot address directly
/// (<see cref="Texture2D{T}.IsCpuAccessible"/> is false) is read with <see cref="Texture2D{T}.CopyTo"/>
/// into a temporary native buffer; one in private storage cannot be saved (copy it to a texture
/// SharpCompute allocated with <c>GraphicsDevice.Copy</c> first).
/// </para>
/// <para>
/// Thread safety: the methods may be called from several threads at once. Like
/// <see cref="Texture2D{T}.CopyTo"/>, a save does not wait for a dispatch that writes the texture:
/// do not save a texture while one is in flight.
/// </para>
/// </remarks>
public static unsafe class ImageSaver
{
    /// <summary>The JPEG and HEIC quality used when none is given, on the 1 to 100 scale of <c>jpegQuality</c>.</summary>
    public const int DefaultQuality = 90;

    /// <summary>
    /// Encodes <paramref name="texture"/> and writes it to <paramref name="path"/>, replacing any
    /// existing file.
    /// </summary>
    /// <param name="texture">The texture: <see cref="Rgba32"/>, <see cref="Rgba32Srgb"/>, <see cref="R8"/>, <see cref="Rgba16Float"/> or <see cref="Float4"/>.</param>
    /// <param name="path">The file to write. Its directory must exist.</param>
    /// <param name="format">The file format; by default chosen from the extension of <paramref name="path"/>.</param>
    /// <param name="jpegQuality">
    /// The lossy quality for JPEG and HEIC, 1 (smallest) to 100 (best); ignored for PNG.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// The element type is not one of those above, the extension names no format <see cref="ImageSaver"/> writes,
    /// or the texture is a native one in private (GPU-only) storage.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="jpegQuality"/> is not in 1..100.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory of <paramref name="path"/> does not exist.</exception>
    /// <exception cref="IOException">ImageIO could not write the file.</exception>
    /// <exception cref="ObjectDisposedException">The texture has been disposed.</exception>
    public static void Save<T>(Texture2D<T> texture, string path, ImageFormat format = ImageFormat.FromExtension, int jpegQuality = DefaultQuality)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(path);
        string fullPath = Path.GetFullPath(path);
        ImageFormat resolved = ImageFormats.Resolve(format, fullPath);
        ValidateArguments(texture, jpegQuality);

        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Can't save '{fullPath}': the directory '{directory}' does not exist.");
        }

        using AutoreleasePool pool = AutoreleasePool.Push();
        nint url = Native.CreateUrl(fullPath);
        nint type = Native.CreateString(ImageFormats.TypeIdentifier(resolved));

        try
        {
            nint destination = Native.CGImageDestinationCreateWithURL(url, type, 1, 0);

            if (destination == 0)
            {
                throw new IOException($"ImageIO can't write '{fullPath}' as {ImageFormats.TypeIdentifier(resolved)}.");
            }

            try
            {
                Encode(texture, destination, resolved, jpegQuality, fullPath);
            }
            finally
            {
                Native.CFRelease(destination);
            }
        }
        finally
        {
            Native.CFRelease(type);
            Native.CFRelease(url);
        }
    }

    /// <summary>Encodes <paramref name="texture"/> and writes the file's bytes to <paramref name="stream"/>, which is not closed.</summary>
    /// <param name="texture">The texture; see <see cref="Save{T}(Texture2D{T}, string, ImageFormat, int)"/>.</param>
    /// <param name="stream">Where the encoded file goes.</param>
    /// <param name="format">The file format: <see cref="ImageFormat.Png"/>, <see cref="ImageFormat.Jpeg"/> or <see cref="ImageFormat.Heic"/>.</param>
    /// <param name="jpegQuality">The lossy quality for JPEG and HEIC, 1 to 100; ignored for PNG.</param>
    /// <exception cref="ArgumentException"><paramref name="format"/> is <see cref="ImageFormat.FromExtension"/>.</exception>
    /// <exception cref="NotSupportedException">The element type is not supported, or the texture is a native one in private storage.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="jpegQuality"/> is not in 1..100.</exception>
    /// <exception cref="IOException">ImageIO could not encode the image.</exception>
    /// <exception cref="ObjectDisposedException">The texture has been disposed.</exception>
    public static void Save<T>(Texture2D<T> texture, Stream stream, ImageFormat format, int jpegQuality = DefaultQuality)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(stream);
        ImageFormat resolved = ImageFormats.Validate(format);
        ValidateArguments(texture, jpegQuality);

        using AutoreleasePool pool = AutoreleasePool.Push();
        nint data = Native.CFDataCreateMutable(0, 0);
        nint type = Native.CreateString(ImageFormats.TypeIdentifier(resolved));

        try
        {
            nint destination = Native.CGImageDestinationCreateWithData(data, type, 1, 0);

            if (destination == 0)
            {
                throw new IOException($"ImageIO can't encode {ImageFormats.TypeIdentifier(resolved)}.");
            }

            try
            {
                Encode(texture, destination, resolved, jpegQuality, "the stream");
            }
            finally
            {
                Native.CFRelease(destination);
            }

            stream.Write(new ReadOnlySpan<byte>(Native.CFDataGetBytePtr(data), checked((int)Native.CFDataGetLength(data))));
        }
        finally
        {
            Native.CFRelease(type);
            Native.CFRelease(data);
        }
    }

    private static void ValidateArguments<T>(Texture2D<T> texture, int jpegQuality)
        where T : unmanaged
    {
        if (PixelLayouts.Get<T>() is null)
        {
            throw PixelLayouts.Unsupported<T>();
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(jpegQuality, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(jpegQuality, 100);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
    }

    /// <summary>Makes a <c>CGImage</c> over the texture's pixels (or a converted copy) and finalizes <paramref name="destination"/> with it.</summary>
    private static void Encode<T>(Texture2D<T> texture, nint destination, ImageFormat format, int quality, string name)
        where T : unmanaged
    {
        int width = texture.Width;
        int height = texture.Height;
        byte* copy = null;

        // The lease keeps the memory alive even if another thread disposes the texture mid-encode.
        using ObjCHandle.Lease lease = texture.BufferHandle.Acquire(texture);

        try
        {
            byte* pixels;
            nuint bytesPerRow;

            if (texture.IsCpuAccessible)
            {
                pixels = texture.Contents;
                bytesPerRow = texture.BytesPerRow;
            }
            else
            {
                // getBytes into tightly packed rows; throws for private storage.
                bytesPerRow = (nuint)width * (nuint)sizeof(T);
                copy = (byte*)NativeMemory.Alloc(bytesPerRow * (nuint)height);
                texture.CopyTo(new Span<T>(copy, width * height));
                pixels = copy;
            }

            VImageCGImageFormat layout = PixelLayouts.Get<T>()!.Value;

            if (PixelLayouts.IsFloat<T>())
            {
                // Linear floats become 16-bit sRGB, which every viewer reads; ImageIO left to itself
                // would pick Display P3 for them.
                VImageCGImageFormat srgb16 = PixelLayouts.Srgb16();
                nuint rowBytes = (nuint)width * 8;
                byte* converted = (byte*)NativeMemory.Alloc(rowBytes * (nuint)height);

                try
                {
                    Convert(layout, pixels, bytesPerRow, srgb16, converted, rowBytes, width, height);
                    AddImage(destination, converted, rowBytes, width, height, srgb16, format, quality, name);
                }
                finally
                {
                    NativeMemory.Free(converted);
                }

                return;
            }

            if ((typeof(T) == typeof(Rgba32) || typeof(T) == typeof(Rgba32Srgb)) && IsOpaque(pixels, bytesPerRow, width, height))
            {
                // No alpha channel in the file: a smaller PNG, and nothing for a JPEG encoder to drop.
                layout.BitmapInfo = Native.AlphaNoneSkipLast;
            }

            AddImage(destination, pixels, bytesPerRow, width, height, layout, format, quality, name);
        }
        finally
        {
            NativeMemory.Free(copy);
        }
    }

    /// <summary>Wraps the pixels in a <c>CGImage</c> (no copy), adds it to <paramref name="destination"/> and finalizes it.</summary>
    private static void AddImage(
        nint destination, byte* pixels, nuint bytesPerRow, int width, int height, VImageCGImageFormat layout, ImageFormat format, int quality, string name)
    {
        // No release callback: the caller owns the memory and keeps it alive until Finalize has
        // returned, after which nothing refers to the image.
        nint provider = Native.CGDataProviderCreateWithData(0, pixels, bytesPerRow * (nuint)height, 0);
        nint image = 0;
        nint qualityNumber = 0;
        nint properties = 0;

        try
        {
            if (provider == 0)
            {
                throw new IOException("CoreGraphics could not wrap the texture's pixels.");
            }

            image = Native.CGImageCreate(
                (nuint)width, (nuint)height, layout.BitsPerComponent, layout.BitsPerPixel, bytesPerRow, layout.ColorSpace, layout.BitmapInfo,
                provider, null, 0, 0);

            if (image == 0)
            {
                throw new IOException($"CoreGraphics could not make a {width}×{height} image of the texture's pixels.");
            }

            if (format != ImageFormat.Png)
            {
                qualityNumber = Native.CreateNumber(quality / 100.0);
                properties = Native.CreateDictionary([Constants.LossyCompressionQuality], [qualityNumber]);
            }

            Native.CGImageDestinationAddImage(destination, image, properties);

            if (Native.CGImageDestinationFinalize(destination) == 0)
            {
                throw new IOException($"ImageIO failed to encode {ImageFormats.TypeIdentifier(format)} for {name}.");
            }
        }
        finally
        {
            Native.Release(properties);
            Native.Release(qualityNumber);
            Native.Release(image);
            Native.Release(provider);
        }
    }

    /// <summary>Converts pixels between two layouts with vImage's converter (ColorSync does the color).</summary>
    private static void Convert(
        VImageCGImageFormat sourceFormat, byte* source, nuint sourceRowBytes, VImageCGImageFormat destinationFormat, byte* destination, nuint destinationRowBytes,
        int width, int height)
    {
        nint error = 0;
        nint converter = Native.vImageConverter_CreateWithCGImageFormat(&sourceFormat, &destinationFormat, null, 0, &error);

        if (converter == 0)
        {
            throw new IOException($"vImage can't convert the texture's pixels for encoding (vImage error {error}).");
        }

        try
        {
            VImageBuffer from = new() { Data = source, Width = (nuint)width, Height = (nuint)height, RowBytes = sourceRowBytes };
            VImageBuffer to = new() { Data = destination, Width = (nuint)width, Height = (nuint)height, RowBytes = destinationRowBytes };
            error = Native.vImageConvert_AnyToAny(converter, &from, &to, null, 0);

            if (error != 0)
            {
                throw new IOException($"vImage failed to convert the texture's pixels for encoding (vImage error {error}).");
            }
        }
        finally
        {
            Native.vImageConverter_Release(converter);
        }
    }

    /// <summary>True when every pixel's alpha byte (the fourth of each four) is 255.</summary>
    private static bool IsOpaque(byte* pixels, nuint bytesPerRow, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            uint* row = (uint*)(pixels + ((nuint)y * bytesPerRow));

            for (int x = 0; x < width; x++)
            {
                // Bytes R, G, B, A in memory: A is the top byte of a little-endian uint.
                if (row[x] < 0xFF000000u)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
