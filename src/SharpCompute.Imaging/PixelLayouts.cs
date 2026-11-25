using System;
using SharpCompute.Imaging.Interop;

namespace SharpCompute.Imaging;

/// <summary>
/// The texture element types images load into and save from, and how each one's pixels look to
/// CoreGraphics and vImage: bits per channel, channel order, alpha and color space.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><see cref="Rgba32"/>, <see cref="Rgba32Srgb"/></term><description>8-bit sRGB, straight alpha. The bytes are the same; a shader reads <see cref="Rgba32Srgb"/> decoded to linear.</description></item>
/// <item><term><see cref="R8"/></term><description>8-bit gray with a 2.2 gamma (<c>kCGColorSpaceGenericGrayGamma2_2</c>), no alpha; color images are converted to their luminance, translucent ones composited over black.</description></item>
/// <item><term><see cref="Rgba16Float"/>, <see cref="Float4"/></term><description>Linear extended sRGB (<c>kCGColorSpaceExtendedLinearSRGB</c>), straight alpha: sRGB primaries, linear light, and values outside [0, 1] for wide-gamut and HDR sources.</description></item>
/// </list>
/// </remarks>
internal static class PixelLayouts
{
    internal const string SupportedTypes = "Rgba32, Rgba32Srgb, R8, Rgba16Float or Float4";

    /// <summary>The layout of <typeparamref name="T"/>, or null when images cannot be loaded into or saved from it.</summary>
    internal static VImageCGImageFormat? Get<T>()
        where T : unmanaged
    {
        if (typeof(T) == typeof(Rgba32) || typeof(T) == typeof(Rgba32Srgb))
        {
            return Format(8, 32, Constants.Srgb, Native.AlphaLast);
        }

        if (typeof(T) == typeof(R8))
        {
            return Format(8, 8, Constants.GrayGamma22, Native.AlphaNone);
        }

        if (typeof(T) == typeof(Rgba16Float))
        {
            return Format(16, 64, Constants.ExtendedLinearSrgb, Native.AlphaLast | Native.FloatComponents | Native.ByteOrder16Little);
        }

        if (typeof(T) == typeof(Float4))
        {
            return Format(32, 128, Constants.ExtendedLinearSrgb, Native.AlphaLast | Native.FloatComponents | Native.ByteOrder32Little);
        }

        return null;
    }

    /// <summary>True for the float element types, which hold linear light.</summary>
    internal static bool IsFloat<T>()
        where T : unmanaged => typeof(T) == typeof(Rgba16Float) || typeof(T) == typeof(Float4);

    /// <summary>16-bit sRGB with straight alpha, in host byte order: what a float texture is saved as.</summary>
    internal static VImageCGImageFormat Srgb16() => Format(16, 64, Constants.Srgb, Native.AlphaLast | Native.ByteOrder16Little);

    internal static NotSupportedException Unsupported<T>() =>
        new($"Images load into and save from textures of {SupportedTypes}, not '{typeof(T).Name}'. " +
            "Load into one of those and convert in a shader for the other element types.");

    private static VImageCGImageFormat Format(uint bitsPerComponent, uint bitsPerPixel, nint space, uint bitmapInfo) => new()
    {
        BitsPerComponent = bitsPerComponent,
        BitsPerPixel = bitsPerPixel,
        ColorSpace = space,
        BitmapInfo = bitmapInfo,
    };
}
