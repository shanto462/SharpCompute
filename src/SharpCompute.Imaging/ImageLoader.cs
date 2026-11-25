using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpCompute.Imaging.Interop;
using SharpCompute.Interop;

namespace SharpCompute.Imaging;

/// <summary>
/// Loads images into textures through macOS ImageIO: PNG, JPEG, HEIC, and anything else ImageIO
/// reads (TIFF, GIF, BMP, WebP, camera RAW, ...). The pixels are decoded straight into the
/// texture's shared memory, with no intermediate copy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Element types and color.</b> The texture's element type picks what the pixels are converted
/// to, through ColorSync, from whatever color space the file is in:
/// <list type="table">
/// <item><term><see cref="Rgba32"/></term><description>sRGB bytes, as stored in an sRGB file. A shader reads <c>byte / 255</c>, still sRGB-encoded.</description></item>
/// <item><term><see cref="Rgba32Srgb"/></term><description>The same bytes; a shader reads them decoded to linear by the GPU. Read-only (no <see cref="LoadReadWriteTexture2D{T}(GraphicsDevice, string)"/>).</description></item>
/// <item><term><see cref="R8"/></term><description>Gray with a 2.2 gamma; a color image is converted to its luminance, and translucent pixels are composited over black.</description></item>
/// <item><term><see cref="Rgba16Float"/>, <see cref="Float4"/></term><description>Linear light in extended sRGB: sRGB primaries, and values below 0 or above 1 where a wide-gamut (Display P3) or HDR source has colors sRGB cannot hold. 16-bit sources keep their precision.</description></item>
/// </list>
/// An 8-bit target clips colors outside sRGB.
/// </para>
/// <para>
/// <b>Alpha is straight</b> (not premultiplied) in every element type, as PNG stores it and as
/// shaders usually expect: a half-transparent red reads <c>(1, 0, 0, 0.5)</c>. Images without alpha
/// load with alpha 1. Premultiply in the shader where blending needs it.
/// </para>
/// <para>
/// <b>Orientation.</b> The EXIF orientation (<c>kCGImagePropertyOrientation</c>) is applied, so a
/// camera's portrait photo loads upright, with its width and height swapped where the tag says so.
/// ImageIO turns such an image through its thumbnail path, whose result is premultiplied: an oriented
/// image with translucent pixels can lose a little precision in their color (an untagged one, or one
/// tagged <see cref="ImageOrientation.Up"/>, never does).
/// </para>
/// <para>
/// <b>Errors.</b> A file ImageIO does not recognize, and one it recognizes but cannot decode (a
/// header without pixel data, say), throw <see cref="InvalidDataException"/>. A file cut off partway
/// through its pixel data is not detected: ImageIO decodes what is there without reporting an error,
/// and the rest of the texture stays zero.
/// </para>
/// <para>
/// <b>How.</b> ImageIO decodes the first image of the file, and vImage's converter
/// (<c>vImageBuffer_InitWithCGImage</c>) writes it, converted, into the rows of the new texture's
/// backing buffer with the texture's own row pitch. A CoreGraphics bitmap context would do the same
/// for premultiplied pixels only. The only managed allocations are the texture objects themselves,
/// whatever the size of the image.
/// </para>
/// <para>
/// Thread safety: every method may be called from several threads at once.
/// </para>
/// </remarks>
public static unsafe class ImageLoader
{
    /// <summary>The largest width or height of an image that loads: the largest 2D texture on Apple GPUs.</summary>
    public const int MaxDimension = 16384;

    private const string EncodedName = "the encoded image";

    /// <summary>Loads the first image in the file at <paramref name="path"/> into a new texture that shaders read and sample.</summary>
    /// <typeparam name="T"><see cref="Rgba32"/>, <see cref="Rgba32Srgb"/>, <see cref="R8"/>, <see cref="Rgba16Float"/> or <see cref="Float4"/>; see the class remarks for what each one holds.</typeparam>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is not in a format ImageIO reads, or ImageIO recognizes the format but cannot decode it.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="T"/> is not one of the types above, or the image is wider or taller than <see cref="MaxDimension"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="device"/> has been disposed.</exception>
    public static ReadOnlyTexture2D<T> LoadTexture2D<T>(GraphicsDevice device, string path)
        where T : unmanaged => (ReadOnlyTexture2D<T>)LoadFile<T>(device, path, readWrite: false);

    /// <summary>Loads the first image in <paramref name="encoded"/>, the bytes of an image file, into a new texture that shaders read and sample.</summary>
    /// <inheritdoc cref="LoadTexture2D{T}(GraphicsDevice, string)" path="/typeparam"/>
    /// <exception cref="InvalidDataException">The bytes are not in a format ImageIO reads, or ImageIO cannot decode them.</exception>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="T"/> is not a supported element type, or the image is wider or taller than <see cref="MaxDimension"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="device"/> has been disposed.</exception>
    public static ReadOnlyTexture2D<T> LoadTexture2D<T>(GraphicsDevice device, ReadOnlySpan<byte> encoded)
        where T : unmanaged => (ReadOnlyTexture2D<T>)LoadBytes<T>(device, encoded, readWrite: false);

    /// <summary>
    /// Reads <paramref name="stream"/> to its end and loads the first image in it into a new texture
    /// that shaders read and sample. The stream is not closed.
    /// </summary>
    /// <inheritdoc cref="LoadTexture2D{T}(GraphicsDevice, ReadOnlySpan{byte})" path="/typeparam"/>
    /// <inheritdoc cref="LoadTexture2D{T}(GraphicsDevice, ReadOnlySpan{byte})" path="/exception"/>
    public static ReadOnlyTexture2D<T> LoadTexture2D<T>(GraphicsDevice device, Stream stream)
        where T : unmanaged => (ReadOnlyTexture2D<T>)LoadStream<T>(device, stream, readWrite: false);

    /// <summary>
    /// Like <see cref="LoadTexture2D{T}(GraphicsDevice, string)"/>, into a texture that shaders read
    /// and write (for an image a filter edits in place, say).
    /// </summary>
    /// <typeparam name="T"><see cref="Rgba32"/>, <see cref="R8"/>, <see cref="Rgba16Float"/> or <see cref="Float4"/> (Apple GPUs cannot write <see cref="Rgba32Srgb"/>).</typeparam>
    /// <inheritdoc cref="LoadTexture2D{T}(GraphicsDevice, string)" path="/exception"/>
    public static ReadWriteTexture2D<T> LoadReadWriteTexture2D<T>(GraphicsDevice device, string path)
        where T : unmanaged => (ReadWriteTexture2D<T>)LoadFile<T>(device, path, readWrite: true);

    /// <summary>Like <see cref="LoadTexture2D{T}(GraphicsDevice, ReadOnlySpan{byte})"/>, into a texture that shaders read and write.</summary>
    /// <inheritdoc cref="LoadReadWriteTexture2D{T}(GraphicsDevice, string)" path="/typeparam"/>
    /// <inheritdoc cref="LoadTexture2D{T}(GraphicsDevice, ReadOnlySpan{byte})" path="/exception"/>
    public static ReadWriteTexture2D<T> LoadReadWriteTexture2D<T>(GraphicsDevice device, ReadOnlySpan<byte> encoded)
        where T : unmanaged => (ReadWriteTexture2D<T>)LoadBytes<T>(device, encoded, readWrite: true);

    /// <summary>Like <see cref="LoadTexture2D{T}(GraphicsDevice, Stream)"/>, into a texture that shaders read and write.</summary>
    /// <inheritdoc cref="LoadReadWriteTexture2D{T}(GraphicsDevice, string)" path="/typeparam"/>
    /// <inheritdoc cref="LoadTexture2D{T}(GraphicsDevice, ReadOnlySpan{byte})" path="/exception"/>
    public static ReadWriteTexture2D<T> LoadReadWriteTexture2D<T>(GraphicsDevice device, Stream stream)
        where T : unmanaged => (ReadWriteTexture2D<T>)LoadStream<T>(device, stream, readWrite: true);

    /// <summary>
    /// Reads the size, color model, alpha and orientation of the first image in the file at
    /// <paramref name="path"/> from its header, without decoding the pixels.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not in a format ImageIO reads, or its header is corrupt.</exception>
    public static ImageInfo GetInfo(string path)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();
        nint source = OpenFile(path);

        try
        {
            return ReadInfo(source, path);
        }
        finally
        {
            Native.CFRelease(source);
        }
    }

    /// <summary>Like <see cref="GetInfo(string)"/>, for the bytes of an image file.</summary>
    /// <exception cref="InvalidDataException">The bytes are not in a format ImageIO reads, or the header is corrupt.</exception>
    public static ImageInfo GetInfo(ReadOnlySpan<byte> encoded)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();

        fixed (byte* bytes = encoded)
        {
            nint source = OpenBytes(bytes, encoded.Length);

            try
            {
                return ReadInfo(source, EncodedName);
            }
            finally
            {
                Native.CFRelease(source);
            }
        }
    }

    private static Texture2D<T> LoadFile<T>(GraphicsDevice device, string path, bool readWrite)
        where T : unmanaged
    {
        VImageCGImageFormat format = Validate<T>(device, readWrite);
        using AutoreleasePool pool = AutoreleasePool.Push();
        nint source = OpenFile(path);

        try
        {
            return Load<T>(device, source, path, format, readWrite);
        }
        finally
        {
            Native.CFRelease(source);
        }
    }

    private static Texture2D<T> LoadBytes<T>(GraphicsDevice device, ReadOnlySpan<byte> encoded, bool readWrite)
        where T : unmanaged
    {
        VImageCGImageFormat format = Validate<T>(device, readWrite);
        using AutoreleasePool pool = AutoreleasePool.Push();

        // The CFData points into the pinned span (no copy). Nothing outlives this block: the source,
        // its image and the CFData are all released before the span is unpinned.
        fixed (byte* bytes = encoded)
        {
            nint source = OpenBytes(bytes, encoded.Length);

            try
            {
                return Load<T>(device, source, EncodedName, format, readWrite);
            }
            finally
            {
                Native.CFRelease(source);
            }
        }
    }

    private static Texture2D<T> LoadStream<T>(GraphicsDevice device, Stream stream, bool readWrite)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stream);
        Validate<T>(device, readWrite);

        // A MemoryStream's own buffer is decoded in place.
        if (stream is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> segment))
        {
            int start = (int)memory.Position;
            ReadOnlySpan<byte> rest = segment.AsSpan(Math.Min(start, segment.Count));
            Texture2D<T> texture = LoadBytes<T>(device, rest, readWrite);
            memory.Position = memory.Length;
            return texture;
        }

        // Anything else is read into native memory, so even a large file costs no managed allocation.
        nint length = 0;
        nint capacity = 1 << 16;
        byte* buffer = (byte*)NativeMemory.Alloc((nuint)capacity);

        try
        {
            while (true)
            {
                if (length == capacity)
                {
                    capacity = checked(capacity * 2);
                    buffer = (byte*)NativeMemory.Realloc(buffer, (nuint)capacity);
                }

                int read = stream.Read(new Span<byte>(buffer + length, (int)Math.Min(capacity - length, int.MaxValue)));

                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            return LoadBytes<T>(device, new ReadOnlySpan<byte>(buffer, checked((int)length)), readWrite);
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    /// <summary>Everything that can be checked before touching the image: the device and the element type.</summary>
    private static VImageCGImageFormat Validate<T>(GraphicsDevice device, bool readWrite)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(device);
        VImageCGImageFormat format = PixelLayouts.Get<T>() ?? throw PixelLayouts.Unsupported<T>();

        if (readWrite && TextureFormats.Get<T>() is { SupportsReadWrite: false } texture)
        {
            throw new NotSupportedException(
                $"ReadWriteTexture2D<{typeof(T).Name}> is not supported: Apple GPUs cannot read and write {texture.Name} in the same shader. " +
                "Load it with LoadTexture2D, or into Rgba32 for a read-write texture.");
        }

        return format;
    }

    /// <summary>Decodes the first image of <paramref name="source"/> into a new texture.</summary>
    private static Texture2D<T> Load<T>(GraphicsDevice device, nint source, string name, VImageCGImageFormat format, bool readWrite)
        where T : unmanaged
    {
        ImageInfo info = ReadInfo(source, name);
        ThrowIfTooLarge(info.Width, info.Height, name);

        nint image = CreateUprightImage(source, info, name);

        try
        {
            int width = checked((int)Native.CGImageGetWidth(image));
            int height = checked((int)Native.CGImageGetHeight(image));
            ThrowIfTooLarge(width, height, name);

            Texture2D<T> texture = readWrite
                ? device.AllocateReadWriteTexture2D<T>(width, height)
                : device.AllocateReadOnlyTexture2D<T>(width, height);

            try
            {
                Decode(image, texture, format, name);
                return texture;
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }
        finally
        {
            Native.CFRelease(image);
        }
    }

    /// <summary>Converts <paramref name="image"/> into the rows of <paramref name="texture"/>'s backing buffer.</summary>
    private static void Decode<T>(nint image, Texture2D<T> texture, VImageCGImageFormat format, string name)
        where T : unmanaged
    {
        using ObjCHandle.Lease lease = texture.BufferHandle.Acquire(texture);

        VImageBuffer buffer = new()
        {
            Data = texture.Contents,
            Width = (nuint)texture.Width,
            Height = (nuint)texture.Height,
            RowBytes = texture.BytesPerRow,
        };

        nint error = Native.vImageBuffer_InitWithCGImage(&buffer, &format, null, image, Native.VImageNoAllocate);

        if (error != 0)
        {
            throw new InvalidDataException($"vImage could not convert {Describe(name)} to {typeof(T).Name} pixels (vImage error {error}).");
        }
    }

    /// <summary>A +1 <c>CGImageSourceRef</c> for a file, which ImageIO reads lazily.</summary>
    private static nint OpenFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"No such file: {fullPath}", fullPath);
        }

        nint url = Native.CreateUrl(fullPath);

        try
        {
            nint source = Native.CGImageSourceCreateWithURL(url, 0);
            return source != 0 ? source : throw new InvalidDataException($"ImageIO can't open '{fullPath}'.");
        }
        finally
        {
            Native.CFRelease(url);
        }
    }

    /// <summary>A +1 <c>CGImageSourceRef</c> over <paramref name="length"/> bytes that stay pinned while it is alive.</summary>
    private static nint OpenBytes(byte* bytes, int length)
    {
        if (length == 0)
        {
            throw new InvalidDataException("The encoded image is empty.");
        }

        nint data = Native.CFDataCreateWithBytesNoCopy(0, bytes, length, Constants.AllocatorNull);

        if (data == 0)
        {
            throw new InvalidOperationException("CoreFoundation could not wrap the encoded image.");
        }

        try
        {
            nint source = Native.CGImageSourceCreateWithData(data, 0);
            return source != 0 ? source : throw new InvalidDataException("ImageIO can't read the encoded image.");
        }
        finally
        {
            // The source retains the data for as long as it needs it.
            Native.CFRelease(data);
        }
    }

    /// <summary>Reads the header of the first image, or throws the error for data ImageIO can't read.</summary>
    private static ImageInfo ReadInfo(nint source, string name)
    {
        nint type = Native.CGImageSourceGetType(source);

        if (type == 0)
        {
            throw new InvalidDataException(
                $"{Capitalize(Describe(name))} is not in an image format ImageIO reads (PNG, JPEG, HEIC, TIFF, GIF, BMP, WebP and others).");
        }

        string typeIdentifier = Native.GetString(type) ?? "";
        nint properties = Native.CGImageSourceGetCount(source) == 0 ? 0 : Native.CGImageSourceCopyPropertiesAtIndex(source, 0, 0);

        if (properties == 0)
        {
            throw new InvalidDataException(
                $"ImageIO recognizes {Describe(name)} as {typeIdentifier} but can't read an image from it ({DescribeStatus(Native.CGImageSourceGetStatus(source))}).");
        }

        try
        {
            int? width = Native.GetInt(Native.CFDictionaryGetValue(properties, Constants.PixelWidth));
            int? height = Native.GetInt(Native.CFDictionaryGetValue(properties, Constants.PixelHeight));

            if (width is not > 0 || height is not > 0)
            {
                throw new InvalidDataException($"ImageIO recognizes {Describe(name)} as {typeIdentifier} but its header has no valid size.");
            }

            int orientationValue = Native.GetInt(Native.CFDictionaryGetValue(properties, Constants.Orientation)) ?? 1;
            ImageOrientation orientation = orientationValue is >= 1 and <= 8 ? (ImageOrientation)orientationValue : ImageOrientation.Up;
            ImageColorModel model = ImageFormats.ParseColorModel(Native.GetString(Native.CFDictionaryGetValue(properties, Constants.ColorModel)));
            bool hasAlpha = Native.GetBool(Native.CFDictionaryGetValue(properties, Constants.HasAlpha)) ?? false;
            int depth = Native.GetInt(Native.CFDictionaryGetValue(properties, Constants.Depth)) ?? 0;

            ImageInfo info = new(width.Value, height.Value, model, hasAlpha, orientation, depth, typeIdentifier);
            return info.IsTransposed ? info with { Width = height.Value, Height = width.Value } : info;
        }
        finally
        {
            Native.CFRelease(properties);
        }
    }

    /// <summary>A +1 <c>CGImageRef</c> of the first image, turned upright when its orientation says so.</summary>
    private static nint CreateUprightImage(nint source, ImageInfo info, string name)
    {
        if (info.Orientation == ImageOrientation.Up)
        {
            nint image = Native.CGImageSourceCreateImageAtIndex(source, 0, 0);

            return image != 0
                ? image
                : throw new InvalidDataException(
                    $"ImageIO failed to decode {Describe(name)} ({info.TypeIdentifier}, {DescribeStatus(Native.CGImageSourceGetStatus(source))}).");
        }

        // ImageIO applies the orientation on its thumbnail path; a thumbnail as large as the image is
        // the whole image, turned.
        nint maxSize = Native.CreateNumber(Math.Max(info.Width, info.Height));

        try
        {
            nint options = Native.CreateDictionary(
                [Constants.ThumbnailFromImageAlways, Constants.ThumbnailWithTransform, Constants.ThumbnailMaxPixelSize],
                [Constants.True, Constants.True, maxSize]);

            try
            {
                nint image = Native.CGImageSourceCreateThumbnailAtIndex(source, 0, options);

                return image != 0
                    ? image
                    : throw new InvalidDataException(
                        $"ImageIO failed to decode {Describe(name)} ({info.TypeIdentifier}, orientation {info.Orientation}, {DescribeStatus(Native.CGImageSourceGetStatus(source))}).");
            }
            finally
            {
                Native.CFRelease(options);
            }
        }
        finally
        {
            Native.CFRelease(maxSize);
        }
    }

    private static void ThrowIfTooLarge(int width, int height, string name)
    {
        if (width > MaxDimension || height > MaxDimension)
        {
            throw new NotSupportedException(
                $"{Capitalize(Describe(name))} is {width}×{height}, larger than the {MaxDimension}×{MaxDimension} pixels a texture can hold on Apple GPUs.");
        }
    }

    private static string Describe(string name) => ReferenceEquals(name, EncodedName) ? name : $"'{name}'";

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private static string DescribeStatus(int status) => status switch
    {
        0 => "complete",
        -1 => "incomplete data",
        -2 => "still reading the header",
        -3 => "unknown format",
        -4 => "invalid data",
        -5 => "unexpected end of file",
        _ => $"ImageIO status {status}",
    };
}
