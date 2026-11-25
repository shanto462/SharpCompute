using System;
using System.Numerics;
using SharpCompute.Presentation;

namespace SharpCompute.Game;

/// <summary>
/// How a native-resolution frame sits in a larger target: magnified <see cref="Scale"/> times, with
/// its top-left corner at (<see cref="OriginX"/>, <see cref="OriginY"/>). The origin is negative on
/// an axis where the target is smaller than the frame (the frame is then cropped, centered).
/// </summary>
public readonly record struct UpscaleLayout(int Scale, int OriginX, int OriginY)
{
    /// <summary>
    /// Converts a point in target pixels (the mouse position in a window, say) to frame pixels. The
    /// result is outside the frame when the point is in a border.
    /// </summary>
    public Vector2 ToFrame(double x, double y) => new((float)((x - OriginX) / Scale), (float)((y - OriginY) / Scale));
}

/// <summary>
/// Whole-number upscaling of a <see cref="FrameBuffer"/> into a window's texture: every frame pixel
/// becomes an exact <c>scale × scale</c> block, centered, with black borders around. Pixel art
/// stays crisp at any window size, unlike with a filtered stretch.
/// </summary>
public static class PixelUpscale
{
    /// <summary>
    /// The largest whole-number scale (at least 1) at which a <paramref name="frameWidth"/> ×
    /// <paramref name="frameHeight"/> frame fits a <paramref name="targetWidth"/> ×
    /// <paramref name="targetHeight"/> target, and the offset that centers it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    public static UpscaleLayout Layout(int frameWidth, int frameHeight, int targetWidth, int targetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetHeight);
        int scale = Math.Max(1, Math.Min(targetWidth / frameWidth, targetHeight / frameHeight));
        return new UpscaleLayout(scale, (targetWidth - (frameWidth * scale)) / 2, (targetHeight - (frameHeight * scale)) / 2);
    }

    /// <summary>
    /// Records the upscale of <paramref name="source"/> into <paramref name="target"/> (typically
    /// the texture about to be presented to a <see cref="MetalWindow"/>), using <see cref="Layout"/>
    /// for the two sizes. The frame's alpha is ignored; the target is opaque.
    /// </summary>
    public static void Record(ComputeContext context, FrameBuffer source, ReadWriteTexture2D<Rgba32> target)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        UpscaleLayout layout = Layout(source.Width, source.Height, target.Width, target.Height);
        context.ForEach(target, new UpscalePass(source.Buffer, source.Width, source.Height, layout.Scale, layout.OriginX, layout.OriginY));
    }
}
