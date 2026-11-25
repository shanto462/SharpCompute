using System;
using System.Numerics;

namespace SharpCompute.Game;

/// <summary>
/// Screen shake: a strength in pixels that impacts raise and time wears off, and a jittering
/// <see cref="Offset"/> to add to the camera or to a post-processing pass. The jitter is a hash of
/// the update count, not a random generator, so a replay shakes identically and drawing never
/// disturbs the simulation's random numbers.
/// </summary>
/// <remarks>Not thread-safe.</remarks>
public sealed class ScreenShake
{
    private long _updates;

    /// <summary>The current strength: the largest offset on each axis, in pixels.</summary>
    public float Amplitude { get; private set; }

    /// <summary>How quickly the shake dies away, per second (exponential). 8 leaves about 2 % after half a second.</summary>
    public float DecayRate { get; set; } = 8f;

    /// <summary>This update's offset, each axis within ±<see cref="Amplitude"/>.</summary>
    public Vector2 Offset => new((Hash01(_updates, 1) - 0.5f) * 2f * Amplitude, (Hash01(_updates, 2) - 0.5f) * 2f * Amplitude);

    /// <summary>Starts or strengthens the shake. A weaker impact never calms a stronger one already running.</summary>
    public void Add(float amplitude) => Amplitude = MathF.Max(Amplitude, amplitude);

    /// <summary>Wears the shake off by <paramref name="dt"/> seconds and moves to the next offset. Call once per rendered frame.</summary>
    public void Update(float dt)
    {
        _updates++;
        Amplitude *= MathF.Exp(-DecayRate * dt);
    }

    /// <summary>Stops the shake at once.</summary>
    public void Reset()
    {
        Amplitude = 0f;
        _updates = 0;
    }

    private static float Hash01(long update, uint salt)
    {
        uint h = ((uint)update * 2654435761u) ^ (salt * 2246822519u);
        h = (h ^ (h >> 15)) * 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / 16777216f;
    }
}
