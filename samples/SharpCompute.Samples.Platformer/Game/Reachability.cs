// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpCompute.Samples.Platformer.Game;

/// <summary>
/// Checks that a level can be finished, using the real movement code: from every place the player
/// can stand, it tries runs and jumps of several lengths in both directions (with
/// <see cref="World.Step"/>, creatures left out) and records everywhere the player passes. A level
/// is finishable when every lantern and the gate are reached; motes are reported too.
/// </summary>
public static class Reachability
{
    private static readonly int[] RunSteps = [0, 8, 16, 32, 56];
    private static readonly int[] JumpHolds = [0, 4, 10, 18, 30, 60];

    public sealed record Result(IReadOnlyList<Vector2> UnreachedLanterns, IReadOnlyList<Vector2> UnreachedMotes, bool GateReached, int StandingSpots, IReadOnlySet<(int X, int Y)> VisitedTiles)
    {
        public bool Finishable => UnreachedLanterns.Count == 0 && GateReached;
    }

    public static Result Check(Level level)
    {
        World probe = new(level, spawnCreatures: false);
        List<Vector2> visited = [];
        HashSet<(int, int)> standing = [];
        Queue<Vector2> frontier = new();

        Vector2 start = new(probe.Player.Body.Center.X, probe.Player.Body.Bottom);
        Settle(level, ref start, visited);
        standing.Add(Key(start));
        frontier.Enqueue(start);

        while (frontier.Count > 0 && standing.Count < 4000)
        {
            Vector2 from = frontier.Dequeue();

            foreach (int direction in (int[])[-1, 1, 0])
            {
                foreach (int run in RunSteps)
                {
                    foreach (int hold in JumpHolds)
                    {
                        if (direction == 0 && (run > 0 || hold == 0))
                        {
                            continue;
                        }

                        if (Simulate(level, from, direction, run, hold, visited) is { } landed && standing.Add(Key(landed)))
                        {
                            frontier.Enqueue(landed);
                        }
                    }
                }
            }
        }

        bool Reached(Vector2 target, float radius)
        {
            foreach (Vector2 v in visited)
            {
                if (Vector2.DistanceSquared(v, target) < radius * radius)
                {
                    return true;
                }
            }

            return false;
        }

        List<Vector2> lanterns = [], motes = [];

        foreach (Lantern lantern in probe.Lanterns)
        {
            if (!Reached(lantern.Position, 16f))
            {
                lanterns.Add(lantern.Position);
            }
        }

        foreach (Mote mote in probe.Motes)
        {
            if (!Reached(mote.Position, 14f))
            {
                motes.Add(mote.Position);
            }
        }

        HashSet<(int X, int Y)> tiles = [];

        foreach (Vector2 v in visited)
        {
            tiles.Add((Collision.TileOf(v.X), Collision.TileOf(v.Y)));
        }

        return new Result(lanterns, motes, Reached(probe.Gate, 14f), standing.Count, tiles);
    }

    private static (int, int) Key(Vector2 feet) => ((int)MathF.Round(feet.X / 3f), (int)MathF.Round(feet.Y));

    private static void Settle(Level level, ref Vector2 feet, List<Vector2> visited)
    {
        World world = new(level, spawnCreatures: false);
        world.Teleport(feet);

        for (int i = 0; i < 240 && !world.Player.OnGround; i++)
        {
            world.Step(InputState.None);
        }

        feet = new Vector2(world.Player.Body.Center.X, world.Player.Body.Bottom);
        visited.Add(world.Player.Body.Center);
    }

    /// <summary>Runs, optionally jumps (holding the button for <paramref name="hold"/> steps), keeps steering until it lands; returns the landing spot, or null if it died or never landed.</summary>
    private static Vector2? Simulate(Level level, Vector2 feet, int direction, int run, int hold, List<Vector2> visited)
    {
        World world = new(level, spawnCreatures: false);
        world.Teleport(feet);
        world.Step(InputState.None);

        bool left = direction < 0, right = direction > 0;

        for (int i = 0; i < run; i++)
        {
            world.Step(new InputState(left, right, false, false));
            visited.Add(world.Player.Body.Center);

            if (world.Player.HurtTimer > 0 || !world.Player.OnGround)
            {
                break;
            }
        }

        bool jumped = hold > 0;

        for (int i = 0; i < 400; i++)
        {
            world.Step(new InputState(left, right, jumped && i < hold, false));

            if (world.Player.HurtTimer > 0)
            {
                return null;
            }

            visited.Add(world.Player.Body.Center);

            if (i > 2 && world.Player.OnGround)
            {
                // Let it come to rest so the landing spot is a real standing spot.
                for (int k = 0; k < 30; k++)
                {
                    world.Step(InputState.None);
                }

                return world.Player.OnGround && world.Player.HurtTimer <= 0
                    ? new Vector2(world.Player.Body.Center.X, world.Player.Body.Bottom)
                    : null;
            }
        }

        return null;
    }
}
