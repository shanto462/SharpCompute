using System;
using System.Numerics;

namespace SharpCompute.Game;

/// <summary>
/// Shockwave distortion: expanding rings that bend the image as they pass, as an explosion's
/// pressure wave would. Each frame, <see cref="Clear"/> the list and <see cref="Add"/> the rings
/// that are alive (they go straight into a shared GPU buffer); then either
/// <see cref="Record"/> distorts one <see cref="FrameBuffer"/> into another, or an
/// <see cref="HdrComposite"/> applies the same rings while it resolves an HDR image.
/// </summary>
/// <remarks>
/// A ring displaces pixels along its radius, most strongly just inside and just outside
/// <c>radius</c> and not at all on the ring itself or far from it; red and blue are looked up
/// slightly apart (<see cref="ColorSplit"/>) for a chromatic fringe. As with any pass, execute the
/// context before changing the rings for the next frame. Not thread-safe.
/// </remarks>
public sealed class ShockwaveDistortion : IDisposable
{
    /// <param name="device">The device to allocate on.</param>
    /// <param name="capacity">The most rings per frame; further ones are dropped.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public ShockwaveDistortion(GraphicsDevice device, int capacity = 16)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Rings = device.AllocateReadOnlyBuffer<DistortionRing>(capacity);
    }

    /// <summary>The most rings per frame.</summary>
    public int Capacity => Rings.Length;

    /// <summary>Rings added since <see cref="Clear"/>.</summary>
    public int Count { get; private set; }

    /// <summary>How far red and blue are looked up apart, as a fraction of the displacement. 0 turns the fringe off.</summary>
    public float ColorSplit { get; set; } = 0.3f;

    internal ReadOnlyBuffer<DistortionRing> Rings { get; }

    /// <summary>Removes every ring.</summary>
    public void Clear() => Count = 0;

    /// <summary>Adds a ring for this frame.</summary>
    /// <param name="center">The ring's center, in frame pixels.</param>
    /// <param name="radius">The ring's current radius.</param>
    /// <param name="width">How wide the disturbed band is; must be positive.</param>
    /// <param name="strength">The largest displacement, roughly, in pixels. Fade it out as the ring grows.</param>
    /// <returns>False when the list is full and the ring was dropped.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not positive.</exception>
    public bool Add(Vector2 center, float radius, float width, float strength)
    {
        if (!(width > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A ring's width must be positive.");
        }

        if (Count == Capacity)
        {
            return false;
        }

        Rings.AsSpan()[Count++] = new DistortionRing
        {
            Center = new Float2(center.X, center.Y),
            Radius = radius,
            Width = width,
            Strength = strength,
        };

        return true;
    }

    /// <summary>
    /// Records the distortion of <paramref name="source"/> into <paramref name="destination"/> (two
    /// different frames of the same size: every pixel reads its neighbours, so it can't run in
    /// place). Pixels out of every ring's reach are copied exactly.
    /// </summary>
    /// <exception cref="ArgumentException">The frames are the same object, or differ in size.</exception>
    public void Record(ComputeContext context, FrameBuffer source, FrameBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (ReferenceEquals(source, destination))
        {
            throw new ArgumentException("The distortion reads neighbouring pixels, so the destination must be a different frame than the source.", nameof(destination));
        }

        if (source.Width != destination.Width || source.Height != destination.Height)
        {
            throw new ArgumentException($"The source is {source.Width} × {source.Height} but the destination is {destination.Width} × {destination.Height}.", nameof(destination));
        }

        context.For(source.Width, source.Height, new DistortPass(source.Buffer, destination.Buffer, Rings, Count, source.Width, source.Height, ColorSplit));
    }

    public void Dispose() => Rings.Dispose();
}
