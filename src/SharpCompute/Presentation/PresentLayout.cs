using System;

namespace SharpCompute.Presentation;

/// <summary>A rectangle in drawable pixels, origin top-left.</summary>
internal readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// The pure geometry behind <see cref="MetalWindow"/>: where a frame lands in the drawable, and how
/// AppKit view coordinates map to drawable and frame pixels. Kept free of native calls so it can be
/// unit-tested without a window.
/// </summary>
internal static class PresentLayout
{
    /// <summary>
    /// The destination rectangle of a <paramref name="sourceWidth"/> × <paramref name="sourceHeight"/>
    /// frame inside a <paramref name="targetWidth"/> × <paramref name="targetHeight"/> drawable.
    /// For <see cref="PresentScaling.Fit"/> the result has the source's aspect ratio (to the nearest
    /// pixel), touches two opposite edges, and is centered (any odd leftover pixel goes right/bottom).
    /// </summary>
    internal static PixelRect Destination(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, PresentScaling scaling)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);

        if (scaling == PresentScaling.Stretch)
        {
            return new PixelRect(0, 0, targetWidth, targetHeight);
        }

        // Compare aspect ratios exactly in integers: source is wider than target when sw/sh > tw/th.
        long wide = (long)sourceWidth * targetHeight;
        long tall = (long)sourceHeight * targetWidth;

        if (wide == tall)
        {
            return new PixelRect(0, 0, targetWidth, targetHeight);
        }

        if (wide > tall)
        {
            // Full width, bars top and bottom. Round to nearest: h = tw * sh / sw.
            int height = (int)Math.Clamp((2L * targetWidth * sourceHeight + sourceWidth) / (2L * sourceWidth), 1, targetHeight);
            return new PixelRect(0, (targetHeight - height) / 2, targetWidth, height);
        }
        else
        {
            int width = (int)Math.Clamp((2L * targetHeight * sourceWidth + sourceHeight) / (2L * sourceHeight), 1, targetWidth);
            return new PixelRect((targetWidth - width) / 2, 0, width, targetHeight);
        }
    }

    /// <summary>
    /// True when every source pixel maps to a whole block of destination pixels (1:1 or an integer
    /// upscale on both axes). The present kernel then samples with nearest filtering, which copies
    /// pixels exactly instead of blurring them.
    /// </summary>
    internal static bool IsPixelExact(PixelRect destination, int sourceWidth, int sourceHeight) =>
        destination.Width % sourceWidth == 0 && destination.Height % sourceHeight == 0;

    /// <summary>
    /// Maps a point in drawable pixels to the frame's pixel space. Returns false when the point lies
    /// outside the frame's destination rectangle (in a letterbox bar or outside the window); the
    /// output is still computed, so callers that clamp can use it.
    /// </summary>
    internal static bool DrawableToFrame(double x, double y, PixelRect destination, int sourceWidth, int sourceHeight, out double frameX, out double frameY)
    {
        frameX = (x - destination.X) * sourceWidth / destination.Width;
        frameY = (y - destination.Y) * sourceHeight / destination.Height;

        return frameX >= 0 && frameX < sourceWidth && frameY >= 0 && frameY < sourceHeight;
    }

    /// <summary>
    /// Converts a point in AppKit view coordinates (points, origin bottom-left, y up) to drawable
    /// pixels (origin top-left, y down) for a view of <paramref name="viewWidth"/> × <paramref name="viewHeight"/>
    /// points whose drawable is <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> pixels.
    /// </summary>
    internal static (double X, double Y) ViewToDrawable(double viewX, double viewY, double viewWidth, double viewHeight, int pixelWidth, int pixelHeight)
    {
        if (viewWidth <= 0 || viewHeight <= 0)
        {
            return (0, 0);
        }

        return (viewX * pixelWidth / viewWidth, (viewHeight - viewY) * pixelHeight / viewHeight);
    }

    /// <summary>The drawable size, in whole pixels (at least 1×1), for a view of the given size in points at a backing scale factor.</summary>
    internal static (int Width, int Height) DrawableSize(double viewWidth, double viewHeight, double scale)
    {
        int width = (int)Math.Clamp(Math.Round(viewWidth * scale), 1, Interop.Metal.MaxTexture2DDimension);
        int height = (int)Math.Clamp(Math.Round(viewHeight * scale), 1, Interop.Metal.MaxTexture2DDimension);
        return (width, height);
    }
}
