// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SharpCompute.Samples.Shooter.Game;

/// <summary>
/// The whole game outside rendering and sound: the ship, its bullets, the enemies and their shots,
/// waves, score, lives and bombs. Plain C# stepped at a fixed 120 Hz.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic: all randomness comes from one seeded <see cref="Rng"/> and nothing reads the clock,
/// so the same seed and the same inputs give the same game, step for step (see <see cref="Checksum"/>).
/// The renderer only reads the arena; particle bursts, events and shockwaves are outputs it consumes.
/// </para>
/// <para>Not thread-safe: step and read it from one thread.</para>
/// </remarks>
public sealed partial class Arena
{
    private const float Dt = Tuning.StepSeconds;

    private readonly List<Enemy> _enemies = [];
    private readonly List<Projectile> _bullets = [];
    private readonly List<Projectile> _pulses = [];
    private readonly List<Ring> _rings = [];
    private readonly List<SpawnMarker> _markers = [];
    private readonly List<Shockwave> _shockwaves = [];
    private readonly List<ParticleBurst> _bursts = [];
    private readonly List<GameEventAt> _events = [];
    private readonly uint _seed;
    private Rng _rng;
    private bool _bombHeld;
    private bool _fireHeld;
    private int _killStreak;

    public Arena(uint seed = 1)
    {
        _seed = seed;
        _rng = new Rng(seed);
        Reset();
    }

    public Player Player { get; private set; } = new();

    public IReadOnlyList<Enemy> Enemies => _enemies;

    public IReadOnlyList<Projectile> Bullets => _bullets;

    /// <summary>Weaver shots.</summary>
    public IReadOnlyList<Projectile> Pulses => _pulses;

    public IReadOnlyList<Ring> Rings => _rings;

    public IReadOnlyList<SpawnMarker> Markers => _markers;

    public IReadOnlyList<Shockwave> Shockwaves => _shockwaves;

    /// <summary>Particle bursts since the last <see cref="EndFrame"/>.</summary>
    public IReadOnlyList<ParticleBurst> Bursts => _bursts;

    /// <summary>Events since the last <see cref="EndFrame"/>.</summary>
    public IReadOnlyList<GameEventAt> Events => _events;

    public int Score { get; private set; }

    public int Lives { get; private set; }

    public int Bombs { get; private set; }

    /// <summary>Score multiplier: one step up per <see cref="Tuning.KillsPerMultiplier"/> kills without dying.</summary>
    public int Multiplier { get; private set; }

    public int Kills { get; private set; }

    /// <summary>The current wave, from 1 (0 before the first one starts).</summary>
    public int Wave { get; private set; }

    public bool GameOver { get; private set; }

    public Banner? Banner { get; private set; }

    /// <summary>Seconds the current banner has been showing.</summary>
    public float BannerTime { get; private set; }

    public int Steps { get; private set; }

    /// <summary>
    /// When false, no waves start (a sandbox: enemies only come from <see cref="AddEnemy"/>). For
    /// tests and tools; the game leaves it on.
    /// </summary>
    public bool WavesEnabled { get; set; } = true;

    /// <summary>Simulated seconds since the game (re)started.</summary>
    public float Time => Steps * Dt;

    /// <summary>Starts over with the same seed: a restarted game is the same game.</summary>
    public void Reset()
    {
        _rng = new Rng(_seed);
        _enemies.Clear();
        _bullets.Clear();
        _pulses.Clear();
        _rings.Clear();
        _markers.Clear();
        _shockwaves.Clear();
        _waveQueue.Clear();
        Player = new Player();
        Score = 0;
        Lives = Tuning.StartLives;
        Bombs = Tuning.StartBombs;
        Multiplier = 1;
        Kills = 0;
        _killStreak = 0;
        Wave = 0;
        _intermission = 1.2f;
        GameOver = false;
        Steps = 0;
        ShowBanner(new Banner("PULSEWAKE", "WASD MOVE   ARROWS OR MOUSE FIRE   SPACE BOMB", 3.5f));
    }

    /// <summary>One fixed step of <see cref="Tuning.StepSeconds"/>.</summary>
    public void Step(in InputState input)
    {
        Steps++;
        BannerTime += Dt;

        if (Banner is { } banner && BannerTime > banner.Seconds)
        {
            Banner = null;
        }

        bool firePressed = input.Fire && !_fireHeld;
        _fireHeld = input.Fire;

        if (GameOver)
        {
            UpdateShockwaves();
            UpdateRings();

            if (firePressed && BannerTime > 1.5f)
            {
                Reset();
            }

            return;
        }

        UpdatePlayer(input);
        UpdateProjectiles(_bullets);
        UpdateProjectiles(_pulses);
        Trails();
        UpdateEnemies();
        UpdateRings();
        Collide();
        UpdateShockwaves();
        UpdateMarkers();

        if (WavesEnabled)
        {
            UpdateWaves();
        }

        _enemies.RemoveAll(static e => !e.Alive);
    }

    /// <summary>Clears <see cref="Bursts"/> and <see cref="Events"/> once the renderer and the sound have used them.</summary>
    public void EndFrame()
    {
        _bursts.Clear();
        _events.Clear();
    }

    /// <summary>
    /// Adds an enemy right away, without a spawn marker. For tests and tools; the game itself spawns
    /// through markers.
    /// </summary>
    public Enemy AddEnemy(EnemyKind kind, Vector2 position, int tier = 2)
    {
        Enemy e = CreateEnemy(kind, position, tier);
        _enemies.Add(e);
        return e;
    }

    /// <summary>
    /// A hash of the whole simulation state (ship, enemies, shots, rings, score, the generator), to
    /// check that two runs are the same game.
    /// </summary>
    public ulong Checksum()
    {
        ulong h = 14695981039346656037ul;

        void Mix(uint v)
        {
            h = (h ^ v) * 1099511628211ul;
        }

        void MixVector(Vector2 v)
        {
            Mix(BitConverter.SingleToUInt32Bits(v.X));
            Mix(BitConverter.SingleToUInt32Bits(v.Y));
        }

        Mix((uint)Steps);
        Mix((uint)Score);
        Mix((uint)Lives);
        Mix((uint)Bombs);
        Mix((uint)Multiplier);
        Mix((uint)Wave);
        Mix(_rng.State);
        MixVector(Player.Position);
        MixVector(Player.Velocity);

        foreach (Enemy e in _enemies)
        {
            Mix((uint)e.Kind);
            Mix((uint)e.Health);
            MixVector(e.Position);
        }

        foreach (Projectile b in _bullets)
        {
            MixVector(b.Position);
        }

        foreach (Projectile p in _pulses)
        {
            MixVector(p.Position);
        }

        foreach (Ring r in _rings)
        {
            Mix(BitConverter.SingleToUInt32Bits(r.Radius));
        }

        return h;
    }

    private void UpdatePlayer(in InputState input)
    {
        Player p = Player;

        if (!p.Alive)
        {
            p.RespawnTimer -= Dt;

            if (p.RespawnTimer <= 0f && Lives > 0)
            {
                Respawn();
            }

            _bombHeld = input.Bomb;
            return;
        }

        p.Invulnerable = MathF.Max(0f, p.Invulnerable - Dt);

        Vector2 move = input.Move;

        if (move.LengthSquared() > 1f)
        {
            move = Vector2.Normalize(move);
        }

        float blend = 1f - MathF.Exp(-Tuning.PlayerResponse * Dt);
        p.Velocity = Vector2.Lerp(p.Velocity, move * Tuning.PlayerSpeed, blend);
        p.Position = ClampToArena(p.Position + (p.Velocity * Dt), Tuning.PlayerRadius);

        if (input.Aim.LengthSquared() > 1e-6f)
        {
            p.Aim = Vector2.Normalize(input.Aim);
        }

        if (input.Fire)
        {
            p.Heading = p.Aim;
        }
        else if (p.Velocity.LengthSquared() > 100f)
        {
            p.Heading = Vector2.Normalize(p.Velocity);
        }

        p.FireCooldown -= Dt;

        if (input.Fire && p.FireCooldown <= 0f)
        {
            // Alternate a hair either side of the aim, so a stream reads as a stream.
            float spread = (p.ShotsFired & 1) == 0 ? 0.03f : -0.03f;
            Vector2 direction = Rotate(p.Aim, spread);
            _bullets.Add(new Projectile
            {
                Position = p.Position + (p.Aim * 9f),
                Velocity = direction * Tuning.BulletSpeed,
                Life = Tuning.BulletLife,
                Radius = Tuning.BulletRadius,
            });
            p.ShotsFired++;
            p.FireCooldown = MathF.Max(p.FireCooldown + Tuning.FireInterval, 0f);
            _events.Add(new GameEventAt(GameEvent.Shot, p.Position));
            _bursts.Add(new ParticleBurst(p.Position + (p.Aim * 9f), 2, Palette.Bullet, 90f, 0.12f, 0.7f, p.Aim, 0.5f));
        }

        if (input.Bomb && !_bombHeld && Bombs > 0)
        {
            Bombs--;
            p.Invulnerable = MathF.Max(p.Invulnerable, 0.5f);
            _shockwaves.Add(new Shockwave(p.Position, Tuning.BombSpeed, Tuning.BombMaxRadius, 9f, lethal: true));
            _events.Add(new GameEventAt(GameEvent.Bomb, p.Position));
            _bursts.Add(new ParticleBurst(p.Position, 3000, Palette.Bomb * 0.45f, 430f, 1.4f, 0.8f, SpeedJitter: 0.25f, Streak: 0.045f, Drag: 1.2f));
            _bursts.Add(new ParticleBurst(p.Position, 1500, Palette.Ship * 0.6f, 220f, 1.1f, 0.9f, SpeedJitter: 1f, Drag: 2f));
        }

        _bombHeld = input.Bomb;

        // The engine's wake.
        if ((Steps & 1) == 0 && p.Velocity.LengthSquared() > 400f)
        {
            Vector2 back = -Vector2.Normalize(p.Velocity);
            _bursts.Add(new ParticleBurst(p.Position + (back * 6f), 5, Palette.Ship * 0.6f, 70f, 0.6f, 0.8f, back, 0.35f, Streak: 0.015f));
        }
    }

    private static void UpdateProjectiles(List<Projectile> list)
    {
        Span<Projectile> span = CollectionsMarshal.AsSpan(list);
        int count = span.Length;

        for (int i = count - 1; i >= 0; i--)
        {
            ref Projectile b = ref span[i];
            b.Position += b.Velocity * Dt;
            b.Life -= Dt;

            if (b.Life <= 0f || !InsideArena(b.Position, 0f))
            {
                span[i] = span[count - 1];
                count--;
            }
        }

        list.RemoveRange(count, list.Count - count);
    }

    /// <summary>Faint sparks left behind by bullets, pulses and Drifters.</summary>
    private void Trails()
    {
        if ((Steps & 1) != 0)
        {
            return;
        }

        foreach (Projectile b in _bullets)
        {
            _bursts.Add(new ParticleBurst(b.Position, 2, Palette.Bullet * 0.5f, 25f, 0.3f, 0.6f, -b.Velocity, 0.6f, Streak: 0.01f));
        }

        foreach (Projectile p in _pulses)
        {
            _bursts.Add(new ParticleBurst(p.Position, 2, Palette.Pulse * 0.6f, 20f, 0.6f, 0.7f));
        }

        if ((Steps & 7) == 0)
        {
            foreach (Enemy e in _enemies)
            {
                if (e.Kind == EnemyKind.Drifter)
                {
                    _bursts.Add(new ParticleBurst(e.Position, 3, Palette.Drifter * 0.5f, 25f, 0.9f, 0.7f, Drag: 1f));
                }
            }
        }
    }

    private void UpdateRings()
    {
        Span<Ring> span = CollectionsMarshal.AsSpan(_rings);
        int count = span.Length;

        for (int i = count - 1; i >= 0; i--)
        {
            span[i].Radius += span[i].Speed * Dt;

            if (span[i].Radius >= span[i].MaxRadius)
            {
                span[i] = span[count - 1];
                count--;
            }
        }

        _rings.RemoveRange(count, _rings.Count - count);
    }

    private void Collide()
    {
        // Bullets against enemies and Weaver pulses.
        Span<Projectile> bullets = CollectionsMarshal.AsSpan(_bullets);
        int bulletCount = bullets.Length;

        for (int i = bulletCount - 1; i >= 0; i--)
        {
            ref Projectile b = ref bullets[i];
            bool spent = false;

            for (int k = 0; k < _enemies.Count && !spent; k++)
            {
                Enemy e = _enemies[k];

                if (e.Alive && Touching(b.Position, b.Radius, e.Position, e.Radius))
                {
                    spent = true;
                    HitEnemy(e, b.Velocity);
                }
            }

            Span<Projectile> pulses = CollectionsMarshal.AsSpan(_pulses);

            for (int k = 0; k < pulses.Length && !spent; k++)
            {
                if (Touching(b.Position, b.Radius, pulses[k].Position, pulses[k].Radius))
                {
                    spent = true;
                    _bursts.Add(new ParticleBurst(pulses[k].Position, 14, Palette.Pulse, 90f, 0.35f, 0.8f));
                    pulses[k].Life = 0f;
                }
            }

            if (spent)
            {
                bullets[i] = bullets[bulletCount - 1];
                bulletCount--;
            }
        }

        _bullets.RemoveRange(bulletCount, _bullets.Count - bulletCount);
        _pulses.RemoveAll(static p => p.Life <= 0f);

        // Everything hostile against the ship.
        Player p = Player;

        if (!p.Alive || p.Invulnerable > 0f)
        {
            return;
        }

        foreach (Enemy e in _enemies)
        {
            if (e.Alive && Touching(p.Position, Tuning.PlayerRadius * 0.8f, e.Position, e.Radius))
            {
                Die();
                return;
            }
        }

        foreach (Projectile pulse in _pulses)
        {
            if (Touching(p.Position, Tuning.PlayerRadius * 0.7f, pulse.Position, pulse.Radius))
            {
                Die();
                return;
            }
        }

        foreach (Ring r in _rings)
        {
            if (r.Harmful && MathF.Abs(Vector2.Distance(p.Position, r.Center) - r.Radius) < 2.5f + (Tuning.PlayerRadius * 0.6f))
            {
                Die();
                return;
            }
        }
    }

    private void HitEnemy(Enemy e, Vector2 bulletVelocity)
    {
        e.Health--;
        e.Flash = 0.08f;
        Vector2 back = bulletVelocity.LengthSquared() > 0f ? -Vector2.Normalize(bulletVelocity) : Vector2.Zero;
        Vector2 contact = e.Position + (back * e.Radius);
        _events.Add(new GameEventAt(GameEvent.Hit, contact));
        _bursts.Add(new ParticleBurst(contact, 24, Palette.Of(e.Kind), 220f, 0.3f, 0.8f, back, 1.1f, Streak: 0.03f));

        if (e.Health <= 0)
        {
            Kill(e, scored: true);
        }
    }

    /// <summary>
    /// Destroys an enemy: points (when <paramref name="scored"/>), multiplier progress, splitting,
    /// debris. Enemies <paramref name="swept"/> away by a bomb or a respawn go in crowds, so each
    /// leaves less debris; twenty full explosions at once would only wash the screen out.
    /// </summary>
    private void Kill(Enemy e, bool scored, bool swept = false)
    {
        if (!e.Alive)
        {
            return;
        }

        e.Alive = false;
        Vector3 color = Palette.Of(e.Kind);
        float size = e.Radius / 10f * (swept ? 0.4f : 1f);

        if (scored)
        {
            Score += e.Points * Multiplier;
            Kills++;
            _killStreak++;

            if (_killStreak % Tuning.KillsPerMultiplier == 0 && Multiplier < Tuning.MaxMultiplier)
            {
                Multiplier++;
            }
        }

        _events.Add(new GameEventAt(GameEvent.Kill, e.Position));
        _bursts.Add(new ParticleBurst(e.Position, (int)(700 * size), color, 280f * MathF.Sqrt(size), 1.0f, 1.0f, Streak: 0.03f));
        _bursts.Add(new ParticleBurst(e.Position, (int)(260 * size), color, 160f * MathF.Sqrt(size), 1.1f, 0.9f, SpeedJitter: 0.1f, Streak: 0.02f, Drag: 3f));
        _bursts.Add(new ParticleBurst(e.Position, (int)(160 * size), Palette.White, 130f, 0.5f, 0.8f, SpeedJitter: 1f));
        _shockwaves.Add(new Shockwave(e.Position, 260f, 40f + (e.Radius * 4f), 1.5f + (size * 1.5f), lethal: false));

        if (e.Kind == EnemyKind.Drifter && e.Tier > 0)
        {
            Vector2 heading = e.Velocity.LengthSquared() > 1e-3f ? Vector2.Normalize(e.Velocity) : _rng.Direction();
            Vector2 side = new(-heading.Y, heading.X);

            for (int s = -1; s <= 1; s += 2)
            {
                Enemy child = CreateEnemy(EnemyKind.Drifter, ClampToArena(e.Position + (side * (s * e.Radius * 0.6f)), 6f), e.Tier - 1);
                child.Velocity = Rotate(heading, s * 0.9f) * DrifterSpeed(child.Tier) * 1.3f;
                child.Flash = 0.1f;
                _enemies.Add(child);
            }

            _events.Add(new GameEventAt(GameEvent.Split, e.Position));
        }
    }

    private void Die()
    {
        Player p = Player;
        p.Alive = false;
        p.RespawnTimer = Tuning.RespawnDelay;
        Lives--;
        Multiplier = 1;
        _killStreak = 0;
        _pulses.Clear();

        _events.Add(new GameEventAt(GameEvent.Death, p.Position));
        _bursts.Add(new ParticleBurst(p.Position, 3500, Palette.Ship, 340f, 1.7f, 1.1f, Streak: 0.03f, Drag: 1.8f));
        _bursts.Add(new ParticleBurst(p.Position, 1200, Palette.White, 170f, 1.1f, 1.0f, SpeedJitter: 0.05f, Drag: 1.5f));
        _shockwaves.Add(new Shockwave(p.Position, 380f, 300f, 6f, lethal: false));

        if (Lives <= 0)
        {
            GameOver = true;
            _events.Add(new GameEventAt(GameEvent.GameOver, p.Position));
            ShowBanner(new Banner("GAME OVER", $"SCORE {Score}   WAVE {Wave}   FIRE TO PLAY AGAIN", float.PositiveInfinity));
        }
    }

    private void Respawn()
    {
        Player p = Player;
        p.Alive = true;
        p.Position = Tuning.Center;
        p.Velocity = Vector2.Zero;
        p.Invulnerable = Tuning.InvulnerableSeconds;
        _pulses.Clear();
        _rings.Clear();

        // Clear a little room around the center, unscored.
        for (int k = 0; k < _enemies.Count; k++)
        {
            Enemy e = _enemies[k];

            if (Vector2.Distance(e.Position, p.Position) < Tuning.RespawnClearRadius)
            {
                Kill(e, scored: false, swept: true);
            }
        }

        _events.Add(new GameEventAt(GameEvent.Respawn, p.Position));
        _bursts.Add(new ParticleBurst(p.Position, 900, Palette.Ship, 170f, 0.9f, 0.9f, SpeedJitter: 0.1f, Drag: 3f));
        _shockwaves.Add(new Shockwave(p.Position, 420f, Tuning.RespawnClearRadius, 4f, lethal: false));
    }

    private void UpdateShockwaves()
    {
        for (int i = _shockwaves.Count - 1; i >= 0; i--)
        {
            Shockwave w = _shockwaves[i];
            w.Radius += w.Speed * Dt;

            if (w.Lethal)
            {
                float r2 = w.Radius * w.Radius;

                // By index: splitting Drifters append to the list, and their halves are caught too.
                for (int k = 0; k < _enemies.Count; k++)
                {
                    Enemy e = _enemies[k];

                    if (e.Alive && Vector2.DistanceSquared(e.Position, w.Center) < r2)
                    {
                        Kill(e, scored: true, swept: true);
                    }
                }

                _pulses.RemoveAll(p => Vector2.DistanceSquared(p.Position, w.Center) < r2);
                _rings.RemoveAll(r => Vector2.DistanceSquared(r.Center, w.Center) < r2);
            }

            if (w.Done)
            {
                _shockwaves.RemoveAt(i);
            }
        }
    }

    private void ShowBanner(Banner banner)
    {
        Banner = banner;
        BannerTime = 0f;
    }

    private static bool Touching(Vector2 a, float ra, Vector2 b, float rb) =>
        Vector2.DistanceSquared(a, b) < (ra + rb) * (ra + rb);

    internal static bool InsideArena(Vector2 p, float margin) =>
        p.X >= Tuning.Wall + margin && p.X <= Tuning.Width - Tuning.Wall - margin &&
        p.Y >= Tuning.Wall + margin && p.Y <= Tuning.Height - Tuning.Wall - margin;

    internal static Vector2 ClampToArena(Vector2 p, float margin) => new(
        Math.Clamp(p.X, Tuning.Wall + margin, Tuning.Width - Tuning.Wall - margin),
        Math.Clamp(p.Y, Tuning.Wall + margin, Tuning.Height - Tuning.Wall - margin));

    internal static Vector2 Rotate(Vector2 v, float radians)
    {
        (float s, float c) = MathF.SinCos(radians);
        return new Vector2((v.X * c) - (v.Y * s), (v.X * s) + (v.Y * c));
    }
}
