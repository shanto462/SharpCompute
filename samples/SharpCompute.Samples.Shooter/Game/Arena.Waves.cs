// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SharpCompute.Samples.Shooter.Game;

/// <summary>Waves: what each one contains, telegraphed spawns, and the pause between waves.</summary>
public sealed partial class Arena
{
    private readonly List<(EnemyKind Kind, int Tier)> _waveQueue = [];
    private float _spawnTimer;
    private float _spawnInterval;
    private float _intermission;

    /// <summary>Enemies of the current wave that have not been telegraphed yet.</summary>
    public int PendingSpawns => _waveQueue.Count;

    /// <summary>
    /// The enemies of wave <paramref name="wave"/> (from 1), in spawn order. Wave 1 is Drifters only;
    /// Lancers join at 2, Weavers at 3, Pylons at 4. Every fifth wave is a surge that arrives all at once.
    /// </summary>
    public static List<(EnemyKind Kind, int Tier)> Compose(int wave, Rng rng)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(wave, 1);
        ArgumentNullException.ThrowIfNull(rng);

        int count = 3 + (2 * wave);
        int maxPylons = wave >= 4 ? 1 + ((wave - 4) / 3) : 0;
        int pylons = 0;
        List<(EnemyKind, int)> list = new(count);

        for (int i = 0; i < count; i++)
        {
            float drifter = 4f, lancer = wave >= 2 ? 3f : 0f, weaver = wave >= 3 ? 2f : 0f, pylon = pylons < maxPylons ? 1f : 0f;
            float pick = rng.NextFloat() * (drifter + lancer + weaver + pylon);

            if ((pick -= drifter) < 0f)
            {
                list.Add((EnemyKind.Drifter, wave >= 2 && rng.NextFloat() < 0.6f ? 2 : 1));
            }
            else if ((pick -= lancer) < 0f)
            {
                list.Add((EnemyKind.Lancer, 0));
            }
            else if ((pick -= weaver) < 0f)
            {
                list.Add((EnemyKind.Weaver, 0));
            }
            else
            {
                list.Add((EnemyKind.Pylon, 0));
                pylons++;
            }
        }

        return list;
    }

    private void StartWave(int wave)
    {
        Wave = wave;
        _waveQueue.Clear();
        _waveQueue.AddRange(Compose(wave, _rng));
        bool surge = wave % 5 == 0;
        _spawnInterval = surge ? 0.08f : MathF.Max(0.25f, 0.9f - (0.05f * wave));
        _spawnTimer = 0.3f;

        // A spare bomb every third wave.
        if (wave > 1 && wave % 3 == 1)
        {
            Bombs = Math.Min(Bombs + 1, Tuning.MaxBombs);
        }

        ShowBanner(new Banner($"WAVE {wave}", surge ? "SURGE" : wave == 1 ? "CLEAR THE ARENA" : $"{_waveQueue.Count} INCOMING", 2.2f));
        _events.Add(new GameEventAt(GameEvent.WaveStart, Tuning.Center));
    }

    private void UpdateWaves()
    {
        if (_waveQueue.Count > 0)
        {
            _spawnTimer -= Dt;

            if (_spawnTimer <= 0f)
            {
                (EnemyKind kind, int tier) = _waveQueue[0];
                _waveQueue.RemoveAt(0);
                _markers.Add(new SpawnMarker { Kind = kind, Tier = tier, Position = PickSpawnPoint(), Timer = Tuning.SpawnTelegraphSeconds });
                _spawnTimer = _spawnInterval;
            }

            return;
        }

        if (_markers.Count > 0 || _enemies.Count > 0 || !Player.Alive)
        {
            return;
        }

        _intermission -= Dt;

        if (_intermission <= 0f)
        {
            _intermission = Tuning.WavePause;
            StartWave(Wave + 1);
        }
    }

    /// <summary>Counts markers down; an enemy appears only if the ship is not standing on its marker.</summary>
    private void UpdateMarkers()
    {
        Span<SpawnMarker> markers = CollectionsMarshal.AsSpan(_markers);
        int count = markers.Length;

        for (int i = count - 1; i >= 0; i--)
        {
            ref SpawnMarker m = ref markers[i];
            m.Timer -= Dt;

            if (m.Timer > 0f)
            {
                continue;
            }

            if (Player.Alive && Vector2.Distance(Player.Position, m.Position) < Tuning.SpawnBlockRadius)
            {
                // Hold at "about to appear" until the ship moves off.
                m.Timer = 0f;
                continue;
            }

            Enemy e = CreateEnemy(m.Kind, m.Position, m.Tier);
            _enemies.Add(e);
            _bursts.Add(new ParticleBurst(m.Position, 200, Palette.Of(m.Kind), 110f, 0.5f, 0.9f, SpeedJitter: 0.1f, Drag: 3f));
            markers[i] = markers[count - 1];
            count--;
        }

        _markers.RemoveRange(count, _markers.Count - count);
    }

    /// <summary>A random point inside the arena, at least <see cref="Tuning.MinSpawnDistance"/> from the ship.</summary>
    private Vector2 PickSpawnPoint()
    {
        const float margin = 30f;
        Vector2 best = Vector2.Zero;
        float bestDistance = -1f;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector2 p = new(
                _rng.Range(Tuning.Wall + margin, Tuning.Width - Tuning.Wall - margin),
                _rng.Range(Tuning.Wall + margin, Tuning.Height - Tuning.Wall - margin));
            float d = Vector2.Distance(p, Player.Position);

            if (d >= Tuning.MinSpawnDistance)
            {
                return p;
            }

            if (d > bestDistance)
            {
                best = p;
                bestDistance = d;
            }
        }

        // Unlucky draws: the corner farthest from the ship is always far enough in a 640 × 360 arena.
        Vector2 corner = new(
            Player.Position.X < Tuning.Width / 2f ? Tuning.Width - Tuning.Wall - margin : Tuning.Wall + margin,
            Player.Position.Y < Tuning.Height / 2f ? Tuning.Height - Tuning.Wall - margin : Tuning.Wall + margin);
        return Vector2.Distance(corner, Player.Position) > bestDistance ? corner : best;
    }
}
