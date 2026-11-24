// Original SharpCompute sample (MIT).
using System;
using System.Numerics;

namespace SharpCompute.Samples.Shooter.Game;

/// <summary>
/// One step's input. <see cref="Move"/> and <see cref="Aim"/> are directions (length ≤ 1 for move;
/// any length for aim, zero meaning "keep the last aim"). <see cref="Bomb"/> is the button's held
/// state: the arena fires a bomb on the step it goes down, not while it stays down.
/// </summary>
public readonly record struct InputState(Vector2 Move, Vector2 Aim, bool Fire, bool Bomb)
{
    public static InputState None => default;

    /// <summary>One of the eight directions (or zero) from four held keys, normalized.</summary>
    public static Vector2 Direction(bool left, bool right, bool up, bool down)
    {
        Vector2 d = new((right ? 1f : 0f) - (left ? 1f : 0f), (down ? 1f : 0f) - (up ? 1f : 0f));
        return d == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(d);
    }
}

/// <summary>
/// A small xorshift generator. The simulation's only source of randomness, so a seed and a sequence of
/// inputs replay the same game exactly (the determinism tests and the headless frames rely on it).
/// </summary>
public sealed class Rng(uint seed)
{
    private uint _state = seed == 0 ? 0x9E3779B9u : seed;

    public uint State => _state;

    public uint NextUInt()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return _state;
    }

    /// <summary>A float in [0, 1).</summary>
    public float NextFloat() => (NextUInt() >> 8) / 16777216f;

    public float Range(float min, float max) => min + ((max - min) * NextFloat());

    /// <summary>An integer in [0, <paramref name="count"/>).</summary>
    public int Next(int count) => (int)(NextUInt() % (uint)Math.Max(1, count));

    /// <summary>A unit vector in a uniformly random direction.</summary>
    public Vector2 Direction()
    {
        float a = NextFloat() * MathF.Tau;
        return new Vector2(MathF.Cos(a), MathF.Sin(a));
    }
}

/// <summary>Things that happen in the arena, for sound (and the renderer's flashes and shakes).</summary>
public enum GameEvent
{
    Shot,
    Hit,
    Split,
    Kill,
    Dash,
    Pulse,
    Ring,
    Bomb,
    Death,
    Respawn,
    WaveStart,
    GameOver,
}

public readonly record struct GameEventAt(GameEvent Event, Vector2 Position);

/// <summary>
/// Particles to spawn, in arena pixels; colors are linear HDR RGB. A zero <see cref="Direction"/>
/// sprays in every direction; otherwise particles leave within ±<see cref="Spread"/> radians of it.
/// <see cref="SpeedJitter"/> 0 gives every particle the same speed (an expanding ring), 1 any speed
/// up to <see cref="Speed"/> (a filled cloud). <see cref="Streak"/> stretches each particle into a
/// line along its velocity (seconds of motion).
/// </summary>
public readonly record struct ParticleBurst(
    Vector2 Position, int Count, Vector3 Color, float Speed, float Life, float Size,
    Vector2 Direction = default, float Spread = MathF.PI, float SpeedJitter = 0.7f, float Streak = 0.02f, float Drag = 2.5f);

/// <summary>A line of banner text and how long it stays (infinity: until replaced).</summary>
public readonly record struct Banner(string Title, string Subtitle, float Seconds);

/// <summary>
/// An expanding ring. Bombs are <see cref="Lethal"/>: everything hostile inside the radius is
/// destroyed. Explosions make harmless ones that only bend the picture.
/// </summary>
public sealed class Shockwave(Vector2 center, float speed, float maxRadius, float strength, bool lethal)
{
    public Vector2 Center { get; } = center;

    public float Radius { get; set; }

    public float Speed { get; } = speed;

    public float MaxRadius { get; } = maxRadius;

    /// <summary>Peak displacement of the distortion, in pixels.</summary>
    public float Strength { get; } = strength;

    public bool Lethal { get; } = lethal;

    /// <summary>0 at the start, 1 at <see cref="MaxRadius"/>.</summary>
    public float Progress => Math.Clamp(Radius / MaxRadius, 0f, 1f);

    public bool Done => Radius >= MaxRadius;
}
