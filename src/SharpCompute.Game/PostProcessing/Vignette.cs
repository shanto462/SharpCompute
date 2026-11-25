using System;

namespace SharpCompute.Game;

/// <summary>A vignette for 8-bit frames: the image darkens towards its corners, drawing the eye to the middle.</summary>
public static class Vignette
{
    /// <summary>
    /// Records a vignette on <paramref name="frame"/>, in place. Brightness falls off with the
    /// squared distance from the center: at <paramref name="strength"/> 1 the corners keep half of
    /// it and the middle of each edge three quarters. Alpha is untouched.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="strength"/> is negative or NaN.</exception>
    public static void Record(ComputeContext context, FrameBuffer frame, float strength = 0.6f)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(frame);

        if (!(strength >= 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(strength), strength, "The vignette strength cannot be negative.");
        }

        context.For(frame.Width, frame.Height, new VignettePass(frame.Buffer, frame.Width, frame.Height, strength));
    }
}
