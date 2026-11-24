// Original SharpCompute sample (MIT).
using System;
using System.Numerics;

namespace SharpCompute.Samples.Platformer.Game;

/// <summary>
/// Follows the player smoothly, looking a little ahead in the direction of travel, and never shows
/// anything outside the level. Positions are the view's top-left corner, in pixels.
/// </summary>
public sealed class Camera(int viewWidth, int viewHeight)
{
    public int ViewWidth { get; } = viewWidth;

    public int ViewHeight { get; } = viewHeight;

    public Vector2 Position { get; private set; }

    /// <summary>The view's top-left corner rounded to whole pixels, so pixel art never shimmers.</summary>
    public (int X, int Y) Snapped => ((int)MathF.Round(Position.X), (int)MathF.Round(Position.Y));

    /// <summary>Jumps straight to the target (level start, respawn).</summary>
    public void Reset(World world) => Position = Clamp(world, Target(world));

    /// <summary>Eases towards the target; frame-rate independent.</summary>
    public void Update(World world, float dt)
    {
        Vector2 target = Target(world);
        float follow = 1f - MathF.Exp(-dt * 6f);
        Position = Clamp(world, Vector2.Lerp(Position, target, follow));
    }

    private Vector2 Target(World world)
    {
        Player p = world.Player;
        float lookAhead = Math.Clamp(p.Velocity.X * 0.35f, -40f, 40f);
        return new Vector2(p.Body.Center.X + lookAhead - (ViewWidth / 2f), p.Body.Center.Y - (ViewHeight * 0.55f));
    }

    private Vector2 Clamp(World world, Vector2 position) => new(
        world.Level.PixelWidth <= ViewWidth ? (world.Level.PixelWidth - ViewWidth) / 2f : Math.Clamp(position.X, 0, world.Level.PixelWidth - ViewWidth),
        world.Level.PixelHeight <= ViewHeight ? (world.Level.PixelHeight - ViewHeight) / 2f : Math.Clamp(position.Y, 0, world.Level.PixelHeight - ViewHeight));
}
