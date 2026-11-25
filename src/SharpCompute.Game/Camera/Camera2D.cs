using System;
using System.Numerics;

namespace SharpCompute.Game;

/// <summary>
/// A 2D camera over a pixel world: it eases after a focus point, leaves the focus alone inside a
/// deadzone, never shows anything outside the world's bounds, and reports a whole-pixel position so
/// pixel art does not shimmer. <see cref="Position"/> is the view's top-left corner in world pixels.
/// </summary>
/// <remarks>
/// Call <see cref="Follow"/> once per simulation step (it is frame-rate independent, but stepping it
/// with the game keeps a replay exact) and draw with <see cref="Snapped"/>. Not thread-safe.
/// </remarks>
public sealed class Camera2D
{
    private Vector4? _bounds;

    /// <param name="viewWidth">Width of the view, in world pixels.</param>
    /// <param name="viewHeight">Height of the view, in world pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    public Camera2D(int viewWidth, int viewHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewHeight);
        ViewWidth = viewWidth;
        ViewHeight = viewHeight;
    }

    public int ViewWidth { get; }

    public int ViewHeight { get; }

    /// <summary>The view's top-left corner, in world pixels (fractional while easing).</summary>
    public Vector2 Position { get; private set; }

    /// <summary>
    /// Where in the view the focus point rests, as a fraction of the view size: (0.5, 0.5) centers
    /// it; a platformer may prefer the focus a little below the middle, e.g. (0.5, 0.55).
    /// </summary>
    public Vector2 Anchor { get; set; } = new(0.5f, 0.5f);

    /// <summary>
    /// How quickly the camera closes the distance to its target, per second (exponential): after
    /// <c>1 / FollowRate</c> seconds about 63 % of the gap is gone. <see cref="float.PositiveInfinity"/>
    /// follows rigidly.
    /// </summary>
    public float FollowRate { get; set; } = 6f;

    /// <summary>
    /// Half the size of the box around the anchor in which the focus may move without the camera
    /// following, in pixels. Zero (the default) follows every movement.
    /// </summary>
    public Vector2 Deadzone { get; set; }

    /// <summary>Shake added to <see cref="Snapped"/>; update and trigger it yourself (see <see cref="ScreenShake"/>).</summary>
    public ScreenShake Shake { get; } = new();

    /// <summary>
    /// The view's top-left corner in whole pixels, shake included: subtract it from world positions
    /// to get screen positions.
    /// </summary>
    public (int X, int Y) Snapped
    {
        get
        {
            Vector2 offset = Shake.Offset;
            return ((int)MathF.Round(Position.X + offset.X), (int)MathF.Round(Position.Y + offset.Y));
        }
    }

    /// <summary>
    /// Keeps the view inside the world rectangle from (<paramref name="minX"/>, <paramref name="minY"/>)
    /// to (<paramref name="maxX"/>, <paramref name="maxY"/>). On an axis where the world is smaller
    /// than the view, the world is centered instead. Takes effect at the next
    /// <see cref="Follow"/> or <see cref="Reset"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A maximum is below its minimum.</exception>
    public void SetBounds(float minX, float minY, float maxX, float maxY)
    {
        if (!(maxX >= minX) || !(maxY >= minY))
        {
            throw new ArgumentException("The world bounds must have a maximum at or above the minimum on both axes.");
        }

        _bounds = new Vector4(minX, minY, maxX, maxY);
    }

    /// <summary>Lets the view go anywhere again.</summary>
    public void ClearBounds() => _bounds = null;

    /// <summary>Jumps straight to <paramref name="focus"/> (a level start, a respawn), ignoring the deadzone.</summary>
    public void Reset(Vector2 focus) => Position = Clamp(Desired(focus));

    /// <summary>Eases towards <paramref name="focus"/> over <paramref name="dt"/> seconds.</summary>
    public void Follow(Vector2 focus, float dt)
    {
        Vector2 target = Desired(focus);

        if (Deadzone != Vector2.Zero)
        {
            // Aim only as far as brings the focus back to the edge of the deadzone.
            Vector2 gap = target - Position;
            target = new Vector2(
                MathF.Abs(gap.X) <= Deadzone.X ? Position.X : target.X - (MathF.Sign(gap.X) * Deadzone.X),
                MathF.Abs(gap.Y) <= Deadzone.Y ? Position.Y : target.Y - (MathF.Sign(gap.Y) * Deadzone.Y));
        }

        float follow = dt > 0f ? 1f - MathF.Exp(-dt * FollowRate) : 0f;
        Position = Clamp(Vector2.Lerp(Position, target, follow));
    }

    /// <summary>Converts a world position to view pixels, using <see cref="Snapped"/>.</summary>
    public Vector2 WorldToScreen(Vector2 world)
    {
        (int x, int y) = Snapped;
        return new Vector2(world.X - x, world.Y - y);
    }

    /// <summary>Converts a position in view pixels to the world, using <see cref="Snapped"/>.</summary>
    public Vector2 ScreenToWorld(Vector2 screen)
    {
        (int x, int y) = Snapped;
        return new Vector2(screen.X + x, screen.Y + y);
    }

    /// <summary>The top-left corner that puts <paramref name="focus"/> on the anchor.</summary>
    private Vector2 Desired(Vector2 focus) => new(focus.X - (ViewWidth * Anchor.X), focus.Y - (ViewHeight * Anchor.Y));

    private Vector2 Clamp(Vector2 position)
    {
        if (_bounds is not { } b)
        {
            return position;
        }

        float width = b.Z - b.X;
        float height = b.W - b.Y;
        return new Vector2(
            width <= ViewWidth ? b.X + ((width - ViewWidth) / 2f) : Math.Clamp(position.X, b.X, b.Z - ViewWidth),
            height <= ViewHeight ? b.Y + ((height - ViewHeight) / 2f) : Math.Clamp(position.Y, b.Y, b.W - ViewHeight));
    }
}
