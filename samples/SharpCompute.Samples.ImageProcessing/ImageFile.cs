// Original SharpCompute sample (MIT).
using System;
using System.IO;
using SharpCompute.Imaging;
using SharpCompute.Samples.Imaging;

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
/// Reads any format macOS ImageIO decodes (JPEG, PNG, HEIC, TIFF, ...) and writes PNG, JPEG or HEIC,
/// all through SharpCompute.Imaging, for the CPU side of the sample: the test image and the CPU
/// reference. The GPU pipeline in <see cref="Program"/> loads and saves textures directly.
/// </summary>
public static class ImageFile
{
    /// <summary>The JPEG and HEIC quality the sample writes.</summary>
    public const int Quality = 92;

    /// <summary>
    /// Decodes the first image in <paramref name="path"/> (upright, sRGB, straight alpha) into an
    /// <see cref="Rgba32"/> texture with <see cref="ImageLoader"/>, and premultiplies it into floats.
    /// </summary>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="InvalidDataException">ImageIO can't decode it.</exception>
    public static ImageData Load(string path)
    {
        using ReadOnlyTexture2D<Rgba32> texture = ImageLoader.LoadTexture2D<Rgba32>(GraphicsDevice.GetDefault(), path);
        return new ImageData(texture.Width, texture.Height, PixelConversion.FromStraightRgba32(texture.ToArray()));
    }

    /// <summary>Writes <paramref name="image"/> as PNG, JPEG or HEIC, chosen by the extension; creates the directory if needed.</summary>
    /// <exception cref="NotSupportedException">The extension is not .png, .jpg, .jpeg, .heic or .heif.</exception>
    public static void Save(string path, ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using ReadOnlyTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadOnlyTexture2D<Rgba32>(
            PixelConversion.ToStraightRgba32(image.Pixels), image.Width, image.Height);
        ImageSaver.Save(texture, PrepareOutput(path), jpegQuality: Quality);
    }

    /// <summary>The full path of <paramref name="path"/>, with its directory created.</summary>
    public static string PrepareOutput(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }
}
