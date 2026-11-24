// Original SharpCompute sample (MIT).
using System.Numerics;

namespace SharpCompute.Samples.Shooter.Game;

/// <summary>The prism ship.</summary>
public sealed class Player
{
    public Vector2 Position { get; set; } = Tuning.Center;

    public Vector2 Velocity { get; set; }

    /// <summary>The unit direction the guns point (the last aim given).</summary>
    public Vector2 Aim { get; set; } = Vector2.UnitX;

    /// <summary>The unit direction the hull points: the aim while firing, otherwise the direction of travel.</summary>
    public Vector2 Heading { get; set; } = Vector2.UnitX;

    public bool Alive { get; set; } = true;

    /// <summary>Seconds of invulnerability left (after a respawn); the ship blinks meanwhile.</summary>
    public float Invulnerable { get; set; }

    /// <summary>Seconds until the ship reappears after being destroyed.</summary>
    public float RespawnTimer { get; set; }

    public float FireCooldown { get; set; }

    public int ShotsFired { get; set; }
}

public enum EnemyKind
{
    /// <summary>A slow orb that drifts and bounces, and splits into two smaller ones when destroyed.</summary>
    Drifter,

    /// <summary>Closes in, locks on with a glowing sight line, then dashes in a straight line.</summary>
    Lancer,

    /// <summary>Circles the ship at range and fires slow pulses at it.</summary>
    Weaver,

    /// <summary>Stationary and tough; sends out expanding rings that have to be dodged.</summary>
    Pylon,
}

/// <summary>A Lancer's attack cycle.</summary>
public enum LancerPhase
{
    Approach,
    Aim,
    Dash,
    Recover,
}

public sealed class Enemy
{
    public required EnemyKind Kind { get; init; }

    public Vector2 Position { get; set; }

    public Vector2 Velocity { get; set; }

    /// <summary>Unit facing (Lancers: the dash direction once locked).</summary>
    public Vector2 Heading { get; set; } = Vector2.UnitX;

    public float Radius { get; set; }

    public int Health { get; set; }

    /// <summary>Drifter size: 2 large, 1 medium, 0 small (does not split).</summary>
    public int Tier { get; init; }

    public bool Alive { get; set; } = true;

    /// <summary>Spin of the drawn shape, radians.</summary>
    public float Angle { get; set; }

    /// <summary>Seconds of the white hit flash left.</summary>
    public float Flash { get; set; }

    public LancerPhase Phase { get; set; }

    /// <summary>Seconds in the current <see cref="Phase"/> (Lancers) or until the next shot or ring (Weavers, Pylons).</summary>
    public float Timer { get; set; }

    /// <summary>Weavers: the angle around the ship they are heading for, and which way they circle (±1).</summary>
    public float Orbit { get; set; }

    public float OrbitDirection { get; set; } = 1f;

    /// <summary>0..1: how far the current wind-up (a Lancer's aim, a Pylon's next ring) has come, for the glow.</summary>
    public float Charge { get; set; }

    public int Points => Kind switch
    {
        EnemyKind.Drifter => Tier switch { 2 => 50, 1 => 25, _ => 10 },
        EnemyKind.Lancer => 100,
        EnemyKind.Weaver => 150,
        _ => 250,
    };
}

/// <summary>A bullet from the ship, or a pulse from a Weaver.</summary>
public struct Projectile
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Life;
    public float Radius;
}

/// <summary>A Pylon's ring: dangerous along its circumference while it expands.</summary>
public struct Ring
{
    public Vector2 Center;
    public float Radius;
    public float Speed;
    public float MaxRadius;

    /// <summary>The last stretch of a ring is a harmless fade-out.</summary>
    public readonly bool Harmful => Radius < MaxRadius - 12f;
}

/// <summary>Where an enemy is about to appear: shown for <see cref="Tuning.SpawnTelegraphSeconds"/> first.</summary>
public struct SpawnMarker
{
    public EnemyKind Kind;
    public int Tier;
    public Vector2 Position;
    public float Timer;

    /// <summary>0 when placed, 1 when the enemy appears.</summary>
    public readonly float Progress => 1f - (System.Math.Max(Timer, 0f) / Tuning.SpawnTelegraphSeconds);
}
