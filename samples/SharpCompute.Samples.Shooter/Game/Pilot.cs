// Original SharpCompute sample (MIT).
using System;
using System.Numerics;

namespace SharpCompute.Samples.Shooter.Game;

/// <summary>
/// A simple autopilot for attract mode and headless runs: steer away from threats, keep moving,
/// shoot at the nearest enemy, bomb when crowded. Deterministic (it only reads the arena), so a
/// headless run renders the same frames every time.
/// </summary>
public sealed class Pilot
{
    private int _steps;

    public InputState Next(Arena arena)
    {
        ArgumentNullException.ThrowIfNull(arena);
        _steps++;

        if (arena.GameOver)
        {
            // Tap fire to start over.
            return new InputState(Vector2.Zero, Vector2.Zero, (_steps & 16) != 0, false);
        }

        Player p = arena.Player;
        Vector2 push = Vector2.Zero;
        Enemy? target = null;
        float targetDistance = float.MaxValue;
        int crowd = 0;

        foreach (Enemy e in arena.Enemies)
        {
            Vector2 away = p.Position - e.Position;
            float d = MathF.Max(away.Length(), 1f);
            float reach = e.Kind == EnemyKind.Lancer && e.Phase is LancerPhase.Aim or LancerPhase.Dash ? 170f : 110f;

            if (d < reach)
            {
                push += away / d * ((reach - d) / reach) * 2.5f;
            }

            // Side-step a Lancer's sight line.
            if (e.Kind == EnemyKind.Lancer && e.Phase is LancerPhase.Aim or LancerPhase.Dash)
            {
                Vector2 side = new(-e.Heading.Y, e.Heading.X);
                float across = Vector2.Dot(away, side);
                float along = Vector2.Dot(away, e.Heading);

                if (along > 0f && MathF.Abs(across) < 30f)
                {
                    push += side * (across >= 0f ? 1.5f : -1.5f);
                }
            }

            crowd += d < 100f ? 1 : 0;

            if (d < targetDistance)
            {
                targetDistance = d;
                target = e;
            }
        }

        foreach (Projectile pulse in arena.Pulses)
        {
            Vector2 away = p.Position - pulse.Position;
            float d = MathF.Max(away.Length(), 1f);

            if (d < 70f)
            {
                push += away / d * ((70f - d) / 70f) * 2f;
            }
        }

        foreach (Ring r in arena.Rings)
        {
            // Keep clear of an approaching ring's edge: stay inside it, or get well outside.
            float d = Vector2.Distance(p.Position, r.Center);
            float gap = d - r.Radius;

            if (r.Harmful && gap > 0f && gap < 35f && d > 1f)
            {
                push += (p.Position - r.Center) / d * 1.5f;
            }
        }

        // Drift around the middle when nothing pushes.
        Vector2 fromCenter = p.Position - Tuning.Center;
        Vector2 orbit = new(-fromCenter.Y, fromCenter.X);
        Vector2 move = push + (orbit.LengthSquared() > 1f ? Vector2.Normalize(orbit) * 0.35f : Vector2.UnitX * 0.35f) - (fromCenter / 260f);

        if (move.LengthSquared() > 1f)
        {
            move = Vector2.Normalize(move);
        }

        Vector2 aim = Vector2.Zero;

        if (target is not null)
        {
            // Lead the target a little.
            float flight = targetDistance / Tuning.BulletSpeed;
            aim = target.Position + (target.Velocity * flight) - p.Position;
        }

        // Bomb when crowded, and sooner when the next spare bomb would be wasted.
        bool crowded = crowd >= 3 || (crowd >= 2 && (arena.Bombs >= Tuning.MaxBombs - 1 || arena.Enemies.Count >= 12));
        bool bomb = arena.Bombs > 0 && crowded && p.Alive && !BombActive(arena) && (_steps & 1) == 0;
        return new InputState(move, aim, target is not null, bomb);
    }

    /// <summary>A bomb's wave is still spreading: give it time before judging the crowd again.</summary>
    private static bool BombActive(Arena arena)
    {
        foreach (Shockwave w in arena.Shockwaves)
        {
            if (w.Lethal)
            {
                return true;
            }
        }

        return false;
    }
}
