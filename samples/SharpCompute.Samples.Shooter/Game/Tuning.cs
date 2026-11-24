// Original SharpCompute sample (MIT).
using System.Numerics;

namespace SharpCompute.Samples.Shooter.Game;

/// <summary>
/// Every gameplay number in one place. Distances are arena pixels (the arena is the 640 × 360 frame),
/// times are seconds, speeds are pixels per second.
/// </summary>
public static class Tuning
{
    /// <summary>The simulation runs at a fixed 120 Hz, independent of the display.</summary>
    public const float StepSeconds = 1f / 120f;

    public const int Width = 640;
    public const int Height = 360;

    /// <summary>The playfield's walls sit this far inside the frame edge.</summary>
    public const float Wall = 10f;

    public const float PlayerRadius = 5f;
    public const float PlayerSpeed = 170f;

    /// <summary>How quickly the ship reaches the stick's velocity (1/s, exponential).</summary>
    public const float PlayerResponse = 14f;

    public const float FireInterval = 0.08f;
    public const float BulletSpeed = 470f;
    public const float BulletRadius = 2f;
    public const float BulletLife = 1.4f;

    public const int StartLives = 3;
    public const int StartBombs = 3;
    public const int MaxBombs = 5;
    public const float RespawnDelay = 1.25f;
    public const float InvulnerableSeconds = 2.5f;

    /// <summary>Enemies this close to the center are cleared when the ship respawns there.</summary>
    public const float RespawnClearRadius = 120f;

    public const float BombSpeed = 560f;

    /// <summary>Larger than the arena's diagonal, so a bomb reaches every corner from anywhere.</summary>
    public const float BombMaxRadius = 760f;

    public const float SpawnTelegraphSeconds = 0.9f;

    /// <summary>Spawn markers are placed at least this far from the ship...</summary>
    public const float MinSpawnDistance = 110f;

    /// <summary>...and an enemy whose marker the ship is standing on waits until it moves away.</summary>
    public const float SpawnBlockRadius = 40f;

    public const int KillsPerMultiplier = 10;
    public const int MaxMultiplier = 8;

    public const float WavePause = 1.6f;

    /// <summary>The middle of the arena, where the ship starts and respawns.</summary>
    public static Vector2 Center => new(Width / 2f, Height / 2f);
}

/// <summary>Linear HDR colors shared by the simulation (particle bursts) and the renderer (shapes).</summary>
public static class Palette
{
    public static readonly Vector3 Ship = new(0.45f, 0.95f, 1.0f);
    public static readonly Vector3 Bullet = new(1.0f, 0.88f, 0.5f);
    public static readonly Vector3 Drifter = new(1.0f, 0.5f, 0.1f);
    public static readonly Vector3 Lancer = new(1.0f, 0.2f, 0.6f);
    public static readonly Vector3 Weaver = new(0.3f, 1.0f, 0.5f);
    public static readonly Vector3 Pulse = new(0.75f, 1.0f, 0.25f);
    public static readonly Vector3 Pylon = new(0.55f, 0.4f, 1.0f);
    public static readonly Vector3 Bomb = new(0.7f, 0.9f, 1.0f);
    public static readonly Vector3 White = new(1f, 1f, 1f);

    public static Vector3 Of(EnemyKind kind) => kind switch
    {
        EnemyKind.Drifter => Drifter,
        EnemyKind.Lancer => Lancer,
        EnemyKind.Weaver => Weaver,
        _ => Pylon,
    };
}
