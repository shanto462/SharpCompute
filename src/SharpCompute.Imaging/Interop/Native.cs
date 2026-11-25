using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpCompute.Imaging.Interop;

/// <summary>
/// The CoreFoundation, CoreGraphics, ImageIO and vImage functions the loader and saver call,
/// through <see cref="LibraryImportAttribute"/> (no bindings, AOT-safe).
/// </summary>
/// <remarks>
/// CoreFoundation's ownership rule applies throughout: a function with <c>Create</c> or <c>Copy</c>
/// in its name returns a +1 reference the caller releases (<see cref="Release"/>); a <c>Get</c>
/// function returns a borrowed reference that must not be released. Every function here is
/// thread-safe on distinct objects, and the immutable objects (images, sources, color spaces) may be
/// shared between threads.
/// </remarks>
internal static unsafe partial class Native
{
    internal const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    internal const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    internal const string ImageIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    internal const string Accelerate = "/System/Library/Frameworks/Accelerate.framework/Accelerate";

    internal const uint CFStringEncodingUTF8 = 0x08000100;
    internal const nint CFNumberIntType = 9;
    internal const nint CFNumberFloat64Type = 6;

    // CGImageAlphaInfo, in the low five bits of a CGBitmapInfo.
    internal const uint AlphaNone = 0;
    internal const uint AlphaLast = 3;
    internal const uint AlphaNoneSkipLast = 5;

    // The rest of CGBitmapInfo.
    internal const uint FloatComponents = 1 << 8;
    internal const uint ByteOrder16Little = 1 << 12;
    internal const uint ByteOrder32Little = 2 << 12;

    /// <summary><c>kvImageNoAllocate</c>: write into the buffer the caller passes instead of allocating one.</summary>
    internal const uint VImageNoAllocate = 512;

    /// <summary><c>kCGImageStatusUnknownType</c>: ImageIO does not recognize the data as any format it reads.</summary>
    internal const int ImageStatusUnknownType = -3;

    // CoreFoundation
    [LibraryImport(CoreFoundation)]
    internal static partial void CFRelease(nint cf);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFURLCreateFromFileSystemRepresentation(nint allocator, byte* buffer, nint length, byte isDirectory);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFStringCreateWithCString(nint allocator, byte* text, uint encoding);

    [LibraryImport(CoreFoundation)]
    internal static partial byte CFStringGetCString(nint text, byte* buffer, nint bufferSize, uint encoding);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFGetTypeID(nint cf);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFStringGetTypeID();

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFNumberGetTypeID();

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFBooleanGetTypeID();

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFNumberCreate(nint allocator, nint type, void* value);

    [LibraryImport(CoreFoundation)]
    internal static partial byte CFNumberGetValue(nint number, nint type, void* value);

    [LibraryImport(CoreFoundation)]
    internal static partial byte CFBooleanGetValue(nint boolean);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDataCreateWithBytesNoCopy(nint allocator, byte* bytes, nint length, nint bytesDeallocator);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDataCreateMutable(nint allocator, nint capacity);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDataGetLength(nint data);

    [LibraryImport(CoreFoundation)]
    internal static partial byte* CFDataGetBytePtr(nint data);

    // CoreGraphics
    [LibraryImport(CoreGraphics)]
    internal static partial nuint CGImageGetWidth(nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial nuint CGImageGetHeight(nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGImageCreate(
        nuint width, nuint height, nuint bitsPerComponent, nuint bitsPerPixel, nuint bytesPerRow, nint space, uint bitmapInfo,
        nint provider, double* decode, byte shouldInterpolate, int intent);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGColorSpaceCreateWithName(nint name);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGDataProviderCreateWithData(nint info, void* data, nuint size, nint releaseData);

    // ImageIO
    [LibraryImport(ImageIO)]
    internal static partial nint CGImageSourceCreateWithURL(nint url, nint options);

    [LibraryImport(ImageIO)]
    internal static partial nint CGImageSourceCreateWithData(nint data, nint options);

    [LibraryImport(ImageIO)]
    internal static partial nint CGImageSourceGetType(nint source);

    [LibraryImport(ImageIO)]
    internal static partial int CGImageSourceGetStatus(nint source);

    [LibraryImport(ImageIO)]
    internal static partial nuint CGImageSourceGetCount(nint source);

    [LibraryImport(ImageIO)]
    internal static partial nint CGImageSourceCreateImageAtIndex(nint source, nuint index, nint options);

    [LibraryImport(ImageIO)]
    internal static partial nint CGImageSourceCreateThumbnailAtIndex(nint source, nuint index, nint options);

    [LibraryImport(ImageIO)]
    internal static partial nint CGImageSourceCopyPropertiesAtIndex(nint source, nuint index, nint options);

    [LibraryImport(ImageIO)]
    internal static partial nint CGImageDestinationCreateWithURL(nint url, nint type, nuint count, nint options);

    [LibraryImport(ImageIO)]
    internal static partial nint CGImageDestinationCreateWithData(nint data, nint type, nuint count, nint options);

    [LibraryImport(ImageIO)]
    internal static partial void CGImageDestinationAddImage(nint destination, nint image, nint properties);

    [LibraryImport(ImageIO)]
    internal static partial byte CGImageDestinationFinalize(nint destination);

    // vImage (part of Accelerate)
    [LibraryImport(Accelerate)]
    internal static partial nint vImageBuffer_InitWithCGImage(VImageBuffer* buffer, VImageCGImageFormat* format, double* backgroundColor, nint image, uint flags);

    [LibraryImport(Accelerate)]
    internal static partial nint vImageConverter_CreateWithCGImageFormat(
        VImageCGImageFormat* source, VImageCGImageFormat* destination, double* backgroundColor, uint flags, nint* error);

    [LibraryImport(Accelerate)]
    internal static partial nint vImageConvert_AnyToAny(nint converter, VImageBuffer* sources, VImageBuffer* destinations, void* temporaryBuffer, uint flags);

    [LibraryImport(Accelerate)]
    internal static partial void vImageConverter_Release(nint converter);

    /// <summary>The value of an exported constant such as <c>kCGColorSpaceSRGB</c> (a <c>CFStringRef</c> or <c>CFBooleanRef</c>).</summary>
    internal static nint Constant(string library, string name) => *(nint*)Symbol(library, name);

    /// <summary>The address of an exported variable (the CFDictionary callback structs are passed by pointer).</summary>
    internal static nint Symbol(string library, string name) => NativeLibrary.GetExport(NativeLibrary.Load(library), name);

    /// <summary>A +1 <c>CFURLRef</c> for a file-system path. Short paths are encoded on the stack.</summary>
    internal static nint CreateUrl(string path)
    {
        int length = Encoding.UTF8.GetByteCount(path);
        Span<byte> utf8 = length <= 1024 ? stackalloc byte[length] : new byte[length];
        Encoding.UTF8.GetBytes(path, utf8);

        fixed (byte* p = utf8)
        {
            nint url = CFURLCreateFromFileSystemRepresentation(0, p, length, 0);
            return url != 0 ? url : throw new InvalidOperationException($"CoreFoundation could not make a URL from '{path}'.");
        }
    }

    /// <summary>A +1 <c>CFStringRef</c> from ASCII or UTF-8 text.</summary>
    internal static nint CreateString(string text)
    {
        int length = Encoding.UTF8.GetByteCount(text);
        Span<byte> utf8 = stackalloc byte[length + 1];
        Encoding.UTF8.GetBytes(text, utf8);
        utf8[length] = 0;

        fixed (byte* p = utf8)
        {
            return CFStringCreateWithCString(0, p, CFStringEncodingUTF8);
        }
    }

    /// <summary>The text of a borrowed <c>CFStringRef</c>, or null when <paramref name="text"/> is not a string.</summary>
    internal static string? GetString(nint text)
    {
        if (text == 0 || CFGetTypeID(text) != CFStringGetTypeID())
        {
            return null;
        }

        Span<byte> buffer = stackalloc byte[256];

        fixed (byte* p = buffer)
        {
            return CFStringGetCString(text, p, buffer.Length, CFStringEncodingUTF8) != 0
                ? Marshal.PtrToStringUTF8((nint)p)
                : null;
        }
    }

    /// <summary>The integer value of a borrowed <c>CFNumberRef</c>, or null when <paramref name="number"/> is not a number.</summary>
    internal static int? GetInt(nint number)
    {
        int value = 0;
        return number != 0 && CFGetTypeID(number) == CFNumberGetTypeID() && CFNumberGetValue(number, CFNumberIntType, &value) != 0 ? value : null;
    }

    /// <summary>The value of a borrowed <c>CFBooleanRef</c> (or a non-zero <c>CFNumberRef</c>), or null when it is neither.</summary>
    internal static bool? GetBool(nint value)
    {
        if (value == 0)
        {
            return null;
        }

        if (CFGetTypeID(value) == CFBooleanGetTypeID())
        {
            return CFBooleanGetValue(value) != 0;
        }

        return GetInt(value) is int number ? number != 0 : null;
    }

    /// <summary>A +1 <c>CFNumberRef</c> holding a double.</summary>
    internal static nint CreateNumber(double value) => CFNumberCreate(0, CFNumberFloat64Type, &value);

    /// <summary>A +1 <c>CFNumberRef</c> holding an int.</summary>
    internal static nint CreateNumber(int value) => CFNumberCreate(0, CFNumberIntType, &value);

    /// <summary>A +1 <c>CFDictionaryRef</c> that retains its keys and values (the caller still releases its own references).</summary>
    internal static nint CreateDictionary(ReadOnlySpan<nint> keys, ReadOnlySpan<nint> values)
    {
        fixed (nint* k = keys)
        fixed (nint* v = values)
        {
            return CFDictionaryCreate(0, k, v, keys.Length, Constants.DictionaryKeyCallBacks, Constants.DictionaryValueCallBacks);
        }
    }

    /// <summary><see cref="CFRelease"/>, for a reference that may be 0.</summary>
    internal static void Release(nint cf)
    {
        if (cf != 0)
        {
            CFRelease(cf);
        }
    }
}

/// <summary>
/// The exported constants the loader and saver use, looked up once. The color spaces are created
/// once and kept for the life of the process: they are immutable and thread-safe.
/// </summary>
internal static class Constants
{
    internal static readonly nint DictionaryKeyCallBacks = Native.Symbol(Native.CoreFoundation, "kCFTypeDictionaryKeyCallBacks");
    internal static readonly nint DictionaryValueCallBacks = Native.Symbol(Native.CoreFoundation, "kCFTypeDictionaryValueCallBacks");
    internal static readonly nint AllocatorNull = Native.Constant(Native.CoreFoundation, "kCFAllocatorNull");
    internal static readonly nint True = Native.Constant(Native.CoreFoundation, "kCFBooleanTrue");

    internal static readonly nint PixelWidth = Native.Constant(Native.ImageIO, "kCGImagePropertyPixelWidth");
    internal static readonly nint PixelHeight = Native.Constant(Native.ImageIO, "kCGImagePropertyPixelHeight");
    internal static readonly nint Orientation = Native.Constant(Native.ImageIO, "kCGImagePropertyOrientation");
    internal static readonly nint ColorModel = Native.Constant(Native.ImageIO, "kCGImagePropertyColorModel");
    internal static readonly nint HasAlpha = Native.Constant(Native.ImageIO, "kCGImagePropertyHasAlpha");
    internal static readonly nint Depth = Native.Constant(Native.ImageIO, "kCGImagePropertyDepth");
    internal static readonly nint ThumbnailFromImageAlways = Native.Constant(Native.ImageIO, "kCGImageSourceCreateThumbnailFromImageAlways");
    internal static readonly nint ThumbnailWithTransform = Native.Constant(Native.ImageIO, "kCGImageSourceCreateThumbnailWithTransform");
    internal static readonly nint ThumbnailMaxPixelSize = Native.Constant(Native.ImageIO, "kCGImageSourceThumbnailMaxPixelSize");
    internal static readonly nint LossyCompressionQuality = Native.Constant(Native.ImageIO, "kCGImageDestinationLossyCompressionQuality");

    internal static readonly nint Srgb = CreateColorSpace("kCGColorSpaceSRGB");
    internal static readonly nint ExtendedLinearSrgb = CreateColorSpace("kCGColorSpaceExtendedLinearSRGB");
    internal static readonly nint GrayGamma22 = CreateColorSpace("kCGColorSpaceGenericGrayGamma2_2");

    private static nint CreateColorSpace(string name)
    {
        nint space = Native.CGColorSpaceCreateWithName(Native.Constant(Native.CoreGraphics, name));
        return space != 0 ? space : throw new InvalidOperationException($"CoreGraphics has no color space {name}.");
    }
}

/// <summary><c>vImage_Buffer</c>: rows of pixels at <see cref="Data"/>, <see cref="RowBytes"/> apart.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VImageBuffer
{
    public void* Data;
    public nuint Height;
    public nuint Width;
    public nuint RowBytes;
}

/// <summary><c>vImage_CGImageFormat</c>: how a buffer's pixels are laid out and which color space they are in.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VImageCGImageFormat
{
    public uint BitsPerComponent;
    public uint BitsPerPixel;
    public nint ColorSpace;
    public uint BitmapInfo;
    public uint Version;
    public double* Decode;
    public int RenderingIntent;
}
