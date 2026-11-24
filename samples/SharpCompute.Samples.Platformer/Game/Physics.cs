// Original SharpCompute sample (MIT).
using System;
using System.Numerics;

namespace SharpCompute.Samples.Platformer.Game;

/// <summary>The buttons the game reads, sampled once per simulation step.</summary>
public readonly record struct InputState(bool Left, bool Right, bool Jump, bool Down)
{
    public static InputState None => default;
}

/// <summary>Tuning for movement, in pixels and seconds. One place, so the feel can be adjusted.</summary>
public static class Tuning
{
    /// <summary>The simulation runs at a fixed rate, independent of the display's refresh rate.</summary>
    public const float StepSeconds = 1f / 120f;

    public const float Gravity = 1500f;
    public const float MaxFallSpeed = 430f;
    public const float RunSpeed = 140f;
    public const float GroundAcceleration = 2200f;
    public const float AirAcceleration = 1400f;
    public const float GroundFriction = 2600f;
    public const float JumpSpeed = 400f;

    /// <summary>Releasing jump while rising keeps this share of the upward speed: short taps give short hops.</summary>
    public const float JumpCut = 0.45f;

    /// <summary>Jumping still works this long after walking off a ledge.</summary>
    public const float CoyoteTime = 0.09f;

    /// <summary>A jump pressed this long before landing still happens on landing.</summary>
    public const float JumpBuffer = 0.12f;

    public const float StompBounce = 300f;

    /// <summary>The player's light, 0..1, fades this much per second and each mote adds <see cref="MoteLight"/>.</summary>
    public const float LightFade = 0.025f;
    public const float MoteLight = 0.12f;
    public const float MinLightRadius = 64f;
    public const float MaxLightRadius = 140f;
}

/// <summary>An axis-aligned box in pixels: top-left corner and size.</summary>
public readonly record struct Box(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public Vector2 Center => new(X + (Width / 2f), Y + (Height / 2f));

    public bool Overlaps(Box other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public Box Shrink(float amount) => new(X + amount, Y + amount, Width - (2 * amount), Height - (2 * amount));
}

/// <summary>Moves boxes through a level's tiles: rock blocks every side, ledges only a fall from above.</summary>
public static class Collision
{
    /// <summary>
    /// Moves <paramref name="box"/> by <paramref name="velocity"/> × <paramref name="dt"/>, one axis at
    /// a time (x first), stopping at tiles. Returns the new box; the flags say which sides hit, and a
    /// velocity component into a hit side is zeroed.
    /// </summary>
    public static Box Move(Level level, Box box, ref Vector2 velocity, float dt, bool dropThroughLedges, out bool hitFloor, out bool hitCeiling, out bool hitWall)
    {
        hitFloor = hitCeiling = hitWall = false;

        // X: rock only.
        float dx = velocity.X * dt;

        if (dx != 0)
        {
            float x = box.X + dx;
            int top = TileOf(box.Y), bottom = TileOf(box.Bottom - 0.01f);

            if (dx > 0)
            {
                int column = TileOf(x + box.Width - 0.01f);

                for (int ty = top; ty <= bottom; ty++)
                {
                    if (level.IsSolid(column, ty))
                    {
                        x = (column * Level.TileSize) - box.Width;
                        hitWall = true;
                        break;
                    }
                }
            }
            else
            {
                int column = TileOf(x);

                for (int ty = top; ty <= bottom; ty++)
                {
                    if (level.IsSolid(column, ty))
                    {
                        x = (column + 1) * Level.TileSize;
                        hitWall = true;
                        break;
                    }
                }
            }

            if (hitWall)
            {
                velocity.X = 0;
            }

            box = box with { X = x };
        }

        // Y: rock, and ledges when falling onto them from above.
        float dy = velocity.Y * dt;

        if (dy != 0)
        {
            float y = box.Y + dy;
            int left = TileOf(box.X), right = TileOf(box.Right - 0.01f);

            if (dy > 0)
            {
                int row = TileOf(y + box.Height - 0.01f);
                float previousBottom = box.Bottom;

                for (int tx = left; tx <= right; tx++)
                {
                    TileKind tile = level[tx, row];
                    bool ledgeBelow = tile == TileKind.Ledge && !dropThroughLedges && previousBottom <= (row * Level.TileSize) + 0.01f;

                    if (tile == TileKind.Rock || ledgeBelow)
                    {
                        y = (row * Level.TileSize) - box.Height;
                        hitFloor = true;
                        break;
                    }
                }
            }
            else
            {
                int row = TileOf(y);

                for (int tx = left; tx <= right; tx++)
                {
                    if (level.IsSolid(tx, row))
                    {
                        y = (row + 1) * Level.TileSize;
                        hitCeiling = true;
                        break;
                    }
                }
            }

            if (hitFloor || hitCeiling)
            {
                velocity.Y = 0;
            }

            box = box with { Y = y };
        }

        return box;
    }

    /// <summary>True when any part of <paramref name="box"/> overlaps a tile of <paramref name="kind"/>.</summary>
    public static bool Touches(Level level, Box box, TileKind kind)
    {
        for (int ty = TileOf(box.Y); ty <= TileOf(box.Bottom - 0.01f); ty++)
        {
            for (int tx = TileOf(box.X); tx <= TileOf(box.Right - 0.01f); tx++)
            {
                if (level[tx, ty] == kind)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>True when the box stands on rock or a ledge (one pixel below its feet).</summary>
    public static bool Grounded(Level level, Box box)
    {
        int row = TileOf(box.Bottom + 0.5f);

        if (MathF.Abs((row * Level.TileSize) - box.Bottom) > 0.5f)
        {
            return false;
        }

        for (int tx = TileOf(box.X); tx <= TileOf(box.Right - 0.01f); tx++)
        {
            if (level[tx, row] is TileKind.Rock or TileKind.Ledge)
            {
                return true;
            }
        }

        return false;
    }

    public static int TileOf(float pixel) => (int)MathF.Floor(pixel / Level.TileSize);
}
