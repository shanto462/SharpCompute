// Original SharpCompute sample (MIT).
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpCompute.Samples.ImageProcessing.Interop;

/// <summary>
/// The few CoreFoundation, CoreGraphics and ImageIO functions the CLI needs to read JPEG/PNG/HEIC
/// and write JPEG/HEIC, called directly through <see cref="LibraryImportAttribute"/> (no bindings).
/// </summary>
/// <remarks>
/// CoreFoundation's ownership rule applies: a function with <c>Create</c> or <c>Copy</c> in its name
/// returns a +1 reference the caller must release (<see cref="CFRelease"/>, or the type-specific
/// <c>CG*Release</c>); <c>Get</c> functions return borrowed references that must not be released.
/// </remarks>
internal static unsafe partial class Native
{
    internal const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    internal const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    internal const string ImageIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    internal const uint CFStringEncodingUTF8 = 0x08000100;
    internal const nint CFNumberIntType = 9;
    internal const nint CFNumberFloat64Type = 6;

    // CGImageAlphaInfo.premultipliedLast | CGBitmapInfo.byteOrder32Big: bytes R, G, B, A in memory.
    internal const uint BitmapInfoPremultipliedRgba = 1 | (4 << 12);

    private static readonly ConcurrentDictionary<string, nint> s_libraries = new();

    // CoreFoundation
    [LibraryImport(CoreFoundation)]
    internal static partial void CFRelease(nint cf);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFURLCreateFromFileSystemRepresentation(nint allocator, byte* buffer, nint length, byte isDirectory);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFStringCreateWithCString(nint allocator, byte* text, uint encoding);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFNumberCreate(nint allocator, nint type, void* value);

    [LibraryImport(CoreFoundation)]
    internal static partial byte CFNumberGetValue(nint number, nint type, void* value);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDictionaryGetValue(nint dictionary, nint key);

    // CoreGraphics
    [LibraryImport(CoreGraphics)]
    internal static partial nuint CGImageGetWidth(nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial nuint CGImageGetHeight(nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial void CGImageRelease(nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGColorSpaceCreateWithName(nint name);

    [LibraryImport(CoreGraphics)]
    internal static partial void CGColorSpaceRelease(nint space);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGBitmapContextCreate(void* data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, nint space, uint bitmapInfo);

    [LibraryImport(CoreGraphics)]
    internal static partial nint CGBitmapContextCreateImage(nint context);

    [LibraryImport(CoreGraphics)]
    internal static partial void CGContextDrawImage(nint context, CGRect rect, nint image);

    [LibraryImport(CoreGraphics)]
    internal static partial void CGContextRelease(nint context);

    // ImageIO
    [LibraryImport(ImageIO)]
    internal static partial nint CGImageSourceCreateWithURL(nint url, nint options);

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
    internal static partial void CGImageDestinationAddImage(nint destination, nint image, nint properties);

    [LibraryImport(ImageIO)]
    internal static partial byte CGImageDestinationFinalize(nint destination);

    /// <summary>The value of an exported <c>CFStringRef</c> / <c>CFBooleanRef</c> constant such as <c>kCGColorSpaceSRGB</c>.</summary>
    internal static nint Constant(string library, string name) => *(nint*)Symbol(library, name);

    /// <summary>The address of an exported variable (for the CFDictionary callback structs, passed by pointer).</summary>
    internal static nint Symbol(string library, string name) =>
        NativeLibrary.GetExport(s_libraries.GetOrAdd(library, static path => NativeLibrary.Load(path)), name);

    /// <summary>A +1 <c>CFURLRef</c> for a file-system path.</summary>
    internal static nint CreateUrl(string path)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(path);

        fixed (byte* p = utf8)
        {
            nint url = CFURLCreateFromFileSystemRepresentation(0, p, utf8.Length, 0);
            return url != 0 ? url : throw new InvalidOperationException($"CoreFoundation could not make a URL from '{path}'.");
        }
    }

    /// <summary>A +1 <c>CFStringRef</c>.</summary>
    internal static nint CreateString(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text + "\0");

        fixed (byte* p = utf8)
        {
            return CFStringCreateWithCString(0, p, CFStringEncodingUTF8);
        }
    }

    /// <summary>A +1 <c>CFNumberRef</c> holding a double.</summary>
    internal static nint CreateNumber(double value) => CFNumberCreate(0, CFNumberFloat64Type, &value);

    /// <summary>A +1 <c>CFDictionaryRef</c> that retains its keys and values (the caller still releases its own references).</summary>
    internal static nint CreateDictionary(ReadOnlySpan<nint> keys, ReadOnlySpan<nint> values)
    {
        fixed (nint* k = keys)
        fixed (nint* v = values)
        {
            return CFDictionaryCreate(
                0, k, v, keys.Length,
                Symbol(CoreFoundation, "kCFTypeDictionaryKeyCallBacks"),
                Symbol(CoreFoundation, "kCFTypeDictionaryValueCallBacks"));
        }
    }

    internal static void Release(nint cf)
    {
        if (cf != 0)
        {
            CFRelease(cf);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct CGRect(double X, double Y, double Width, double Height);
