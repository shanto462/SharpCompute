// Original SharpCompute sample (MIT).
using System;

namespace SharpCompute.Samples.Platformer.Game;

/// <summary>
/// A very simple automatic player for the attract mode and headless screenshots: it runs right,
/// jumps at walls, gaps, spikes and creatures, and turns around at the level's edge. It is not a
/// solver (that is <see cref="Reachability"/>); it is deterministic, so replays are repeatable.
/// </summary>
public sealed class Pilot
{
    private int _hold;
    private int _direction = 1;
    private int _stuck;
    private float _lastX;

    public InputState Next(World world)
    {
        Player p = world.Player;
        Level level = world.Level;

        if (_hold > 0)
        {
            _hold--;
            return new InputState(_direction < 0, _direction > 0, true, false);
        }

        float ahead = _direction > 0 ? p.Body.Right + 6 : p.Body.X - 6;
        int column = Collision.TileOf(ahead);
        int feetRow = Collision.TileOf(p.Body.Bottom + 1);
        int bodyRow = Collision.TileOf(p.Body.Center.Y);

        bool wall = level.IsSolid(column, bodyRow);
        bool gap = level[column, feetRow] is TileKind.Empty or TileKind.Spikes;
        bool spikes = level[Collision.TileOf(ahead + (_direction * 10)), feetRow - 1] == TileKind.Spikes;
        bool creature = false;

        foreach (Creature c in world.Creatures)
        {
            float dx = (c.Body.Center.X - p.Body.Center.X) * _direction;

            if (c.Alive && dx is > 0 and < 30 && MathF.Abs(c.Body.Center.Y - p.Body.Center.Y) < 24)
            {
                creature = true;
            }
        }

        // Turn around when progress stops (pressed against something it can't jump).
        _stuck = MathF.Abs(p.Body.X - _lastX) < 0.05f && p.OnGround ? _stuck + 1 : 0;
        _lastX = p.Body.X;

        if (_stuck > 60)
        {
            _direction = -_direction;
            _stuck = 0;
        }

        if (p.OnGround && (wall || gap || spikes || creature))
        {
            _hold = 30;
            return new InputState(_direction < 0, _direction > 0, true, false);
        }

        return new InputState(_direction < 0, _direction > 0, false, false);
    }
}
