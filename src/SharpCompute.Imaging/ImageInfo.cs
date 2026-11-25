using System;
using System.IO;

namespace SharpCompute.Imaging;

/// <summary>The file formats <see cref="ImageSaver"/> writes.</summary>
public enum ImageFormat
{
    /// <summary>Chosen from the file extension: <c>.png</c>, <c>.jpg</c>/<c>.jpeg</c> or <c>.heic</c>/<c>.heif</c>.</summary>
    FromExtension,

    /// <summary>PNG: lossless, with alpha; 8 bits per channel, or 16 for the float element types.</summary>
    Png,

    /// <summary>JPEG: lossy, 8 bits per channel, no alpha.</summary>
    Jpeg,

    /// <summary>HEIC (HEIF with HEVC): lossy, with alpha.</summary>
    Heic,
}

/// <summary>
/// How a stored image must be turned to be viewed upright: the EXIF / TIFF orientation tag
/// (<c>kCGImagePropertyOrientation</c>), with the same values. Cameras store a portrait photo as a
/// landscape picture plus <see cref="Right"/>, say. <see cref="ImageLoader"/> applies it.
/// </summary>
public enum ImageOrientation
{
    /// <summary>Stored upright (also what an image without the tag is).</summary>
    Up = 1,

    /// <summary>Mirrored left to right.</summary>
    UpMirrored = 2,

    /// <summary>Rotated 180°.</summary>
    Down = 3,

    /// <summary>Mirrored top to bottom.</summary>
    DownMirrored = 4,

    /// <summary>Transposed: mirrored across the diagonal from the top-left corner to the bottom-right one.</summary>
    LeftMirrored = 5,

    /// <summary>Rotated 90° clockwise to view: the stored picture's top is on the viewer's right.</summary>
    Right = 6,

    /// <summary>Transversed: mirrored across the diagonal from the top-right corner to the bottom-left one.</summary>
    RightMirrored = 7,

    /// <summary>Rotated 90° counterclockwise to view.</summary>
    Left = 8,
}

/// <summary>The color model a file stores its pixels in (<c>kCGImagePropertyColorModel</c>).</summary>
public enum ImageColorModel
{
    /// <summary>Not reported by the file, or one ImageIO names differently.</summary>
    Unknown,

    /// <summary>Red, green and blue.</summary>
    Rgb,

    /// <summary>One gray channel.</summary>
    Gray,

    /// <summary>Cyan, magenta, yellow and black (print JPEGs, say). Loading converts it.</summary>
    Cmyk,

    /// <summary>CIE L*a*b*. Loading converts it.</summary>
    Lab,
}

/// <summary>
/// What <see cref="ImageLoader.GetInfo(string)"/> reads from a file's header, without decoding its pixels.
/// </summary>
/// <param name="Width">
/// Width of the texture <see cref="ImageLoader"/> makes: the stored width, or the stored height when
/// <paramref name="Orientation"/> turns the picture by 90°.
/// </param>
/// <param name="Height">Height of that texture; see <paramref name="Width"/>.</param>
/// <param name="ColorModel">The color model of the stored pixels.</param>
/// <param name="HasAlpha">True when the file stores an alpha channel (it may still be fully opaque).</param>
/// <param name="Orientation">The stored orientation, which loading applies.</param>
/// <param name="BitsPerComponent">Bits per stored channel, as the file reports it: 8 for most files, 16 for a 16-bit PNG or TIFF; 0 when not reported.</param>
/// <param name="TypeIdentifier">The format as a uniform type identifier, such as <c>public.png</c>, <c>public.jpeg</c> or <c>public.heic</c>.</param>
public readonly record struct ImageInfo(
    int Width, int Height, ImageColorModel ColorModel, bool HasAlpha, ImageOrientation Orientation, int BitsPerComponent, string TypeIdentifier)
{
    /// <summary>True when <see cref="Orientation"/> turns the stored picture by 90°, so its stored width is <see cref="Height"/>.</summary>
    public bool IsTransposed => Orientation is ImageOrientation.LeftMirrored or ImageOrientation.Right or ImageOrientation.RightMirrored or ImageOrientation.Left;
}

/// <summary>Maps <see cref="ImageFormat"/> to ImageIO's type identifiers.</summary>
internal static class ImageFormats
{
    /// <summary>The format <paramref name="path"/>'s extension names, for <see cref="ImageFormat.FromExtension"/>.</summary>
    /// <exception cref="NotSupportedException">The extension is not one of the formats <see cref="ImageSaver"/> writes.</exception>
    internal static ImageFormat Resolve(ImageFormat format, string path)
    {
        if (format != ImageFormat.FromExtension)
        {
            return Validate(format);
        }

        string extension = Path.GetExtension(path);

        return extension.ToLowerInvariant() switch
        {
            ".png" => ImageFormat.Png,
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".heic" or ".heif" => ImageFormat.Heic,
            _ => throw new NotSupportedException(
                $"Can't tell the image format from the extension '{extension}' of '{path}': use .png, .jpg, .jpeg, .heic or .heif, or pass an ImageFormat."),
        };
    }

    internal static ImageFormat Validate(ImageFormat format) => format switch
    {
        ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Heic => format,
        ImageFormat.FromExtension => throw new ArgumentException("A stream has no extension: pass ImageFormat.Png, Jpeg or Heic.", nameof(format)),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not an ImageFormat."),
    };

    internal static string TypeIdentifier(ImageFormat format) => format switch
    {
        ImageFormat.Png => "public.png",
        ImageFormat.Jpeg => "public.jpeg",
        _ => "public.heic",
    };

    internal static ImageColorModel ParseColorModel(string? model) => model switch
    {
        "RGB" => ImageColorModel.Rgb,
        "Gray" => ImageColorModel.Gray,
        "CMYK" => ImageColorModel.Cmyk,
        "Lab" => ImageColorModel.Lab,
        _ => ImageColorModel.Unknown,
    };
}
