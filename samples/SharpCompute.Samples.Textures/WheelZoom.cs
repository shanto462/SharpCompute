// Original SharpCompute sample (MIT).
using System;
using SharpCompute.Presentation;

namespace SharpCompute.Samples.Textures;

/// <summary>
/// Turns <see cref="MetalWindow.MouseWheel"/> events into the zoom factors
/// <see cref="TextureDemo.ZoomAt"/> takes (below 1 moves closer).
/// </summary>
/// <remarks>
/// A trackpad and a notched wheel report different things, so they zoom differently. A trackpad
/// sends many pixel-precise events per gesture, and the zoom follows the fingers continuously. A
/// wheel sends about one event per notch, in lines that macOS scales by how fast the wheel turns,
/// and the zoom moves a whole number of fixed steps: the same step the + and - keys take, so the
/// wheel and the keys land on the same magnifications. Pure functions: safe from any thread.
/// </remarks>
public static class WheelZoom
{
    /// <summary>The zoom ratio of one notch of a wheel, and of one press of + or -.</summary>
    public const float Step = 1.1f;

    /// <summary>How far two fingers scroll, in points, for the same zoom as one notch of a wheel.</summary>
    public const float PointsPerStep = 40f;

    /// <summary>The most notches one event may count for, so a fast spin can't cross the whole zoom range at once.</summary>
    public const int MaxSteps = 4;

    /// <summary>
    /// The zoom factor for one wheel event: 1 for an event that should not zoom. A positive
    /// <see cref="MouseWheelEventArgs.DeltaY"/> zooms in, as it conventionally does; the sign is used
    /// as it comes, because macOS has already applied the user's "Natural scrolling" setting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Trackpad inertia (<see cref="MouseWheelEventArgs.IsMomentum"/>) is ignored. Zooming around the
    /// pointer is how a detail gets framed, and a view that keeps magnifying after the fingers have
    /// left the trackpad runs past the framing the user stopped at.
    /// </para>
    /// <para>
    /// Only the vertical delta zooms. Shift + a notched wheel, which macOS reports as a horizontal
    /// scroll, therefore does nothing, and neither does a sideways swipe.
    /// </para>
    /// </remarks>
    /// <param name="e">The event.</param>
    /// <param name="scaleFactor">
    /// The window's <see cref="MetalWindow.ScaleFactor"/>: precise deltas are drawable pixels, and
    /// the zoom goes by finger travel in points so that a Retina display doesn't double its speed.
    /// </param>
    public static float Factor(MouseWheelEventArgs e, double scaleFactor)
    {
        ArgumentNullException.ThrowIfNull(e);

        float delta = e.DeltaY;

        if (e.IsMomentum || delta == 0f || !float.IsFinite(delta))
        {
            return 1f;
        }

        if (e.IsPrecise)
        {
            // Continuous: the factors of a gesture's events multiply up to Step^(travel / PointsPerStep),
            // however the travel is cut into events. Clamped like a wheel, against a wild delta. A
            // scale factor that is not one (zero, NaN) counts as 1: the comparison is false for NaN,
            // which Math.Max would pass on.
            float steps = delta / ((float)(scaleFactor >= 1.0 ? scaleFactor : 1.0) * PointsPerStep);

            return MathF.Pow(Step, -Math.Clamp(steps, -MaxSteps, MaxSteps));
        }

        // A slow notch can be as little as a tenth of a line and a fast spin several lines: every
        // event counts for at least one notch, and always for a whole number of them.
        int notches = (int)Math.Min(MathF.Ceiling(MathF.Abs(delta)), MaxSteps);

        return MathF.Pow(Step, delta > 0f ? -notches : notches);
    }
}
