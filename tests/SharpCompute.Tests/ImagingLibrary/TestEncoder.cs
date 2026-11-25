using System;
using System.IO;
using SharpCompute.Imaging.Interop;

namespace SharpCompute.Tests.ImagingLibrary;

/// <summary>
/// Writes small test images straight through ImageIO, for what <c>ImageSaver</c> never writes: 16-bit
/// integer pixels, an EXIF orientation, and other color spaces. Nothing is committed as a fixture.
/// </summary>
internal static unsafe class TestEncoder
{
    /// <summary>8-bit RGBA, straight alpha, sRGB.</summary>
    internal static byte[] Rgba8(ReadOnlySpan<Rgba32> pixels, int width, int height, string type = "public.png", int orientation = 1, string space = "kCGColorSpaceSRGB")
    {
        fixed (Rgba32* p = pixels)
        {
            return Encode(p, width, height, 8, 32, (nuint)width * 4, Native.AlphaLast, space, type, orientation);
        }
    }

    /// <summary>16-bit RGBA in host byte order, straight alpha, sRGB.</summary>
    internal static byte[] Rgba16(ReadOnlySpan<ushort> channels, int width, int height, string type = "public.png")
    {
        fixed (ushort* p = channels)
        {
            return Encode(p, width, height, 16, 64, (nuint)width * 8, Native.AlphaLast | Native.ByteOrder16Little, "kCGColorSpaceSRGB", type, 1);
        }
    }

    private static byte[] Encode(
        void* pixels, int width, int height, uint bitsPerComponent, uint bitsPerPixel, nuint bytesPerRow, uint bitmapInfo, string spaceName, string type, int orientation)
    {
        nint space = Native.CGColorSpaceCreateWithName(Native.Constant(Native.CoreGraphics, spaceName));
        nint provider = Native.CGDataProviderCreateWithData(0, pixels, bytesPerRow * (nuint)height, 0);
        nint image = Native.CGImageCreate((nuint)width, (nuint)height, bitsPerComponent, bitsPerPixel, bytesPerRow, space, bitmapInfo, provider, null, 0, 0);
        nint data = Native.CFDataCreateMutable(0, 0);
        nint typeString = Native.CreateString(type);
        nint destination = Native.CGImageDestinationCreateWithData(data, typeString, 1, 0);
        nint number = Native.CreateNumber(orientation);
        nint properties = Native.CreateDictionary([Constants.Orientation], [number]);

        try
        {
            Assert.NotEqual(0, image);
            Assert.NotEqual(0, destination);
            Native.CGImageDestinationAddImage(destination, image, properties);
            Assert.NotEqual(0, Native.CGImageDestinationFinalize(destination));
            return new ReadOnlySpan<byte>(Native.CFDataGetBytePtr(data), (int)Native.CFDataGetLength(data)).ToArray();
        }
        finally
        {
            Native.Release(properties);
            Native.Release(number);
            Native.Release(destination);
            Native.Release(typeString);
            Native.Release(data);
            Native.Release(image);
            Native.Release(provider);
            Native.Release(space);
        }
    }

    /// <summary>Writes <paramref name="bytes"/> to a file in <paramref name="directory"/> and returns its path.</summary>
    internal static string Write(string directory, string name, byte[] bytes)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
