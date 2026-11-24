using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpCompute.Samples.Shooter.Game;

namespace SharpCompute.Tests.Shooter;

/// <summary>Pulsewake's simulation: plain C#, no GPU needed.</summary>
public sealed class PulsewakeSimulationTests
{
    private static readonly InputState Idle = InputState.None;

    [Fact]
    public void Ship_AcceleratesToTopSpeed_AndStopsAtTheWall()
    {
        Arena arena = Sandbox();
        InputState right = new(Vector2.UnitX, default, false, false);

        Run(arena, right, 120);
        Assert.Equal(Tuning.PlayerSpeed, arena.Player.Velocity.X, Tuning.PlayerSpeed * 0.01f);
        Assert.True(arena.Player.Position.X > Tuning.Center.X + 100f);

        Run(arena, right, 600);
        Assert.Equal(Tuning.Width - Tuning.Wall - Tuning.PlayerRadius, arena.Player.Position.X);
        Assert.Equal(Tuning.Center.Y, arena.Player.Position.Y);
    }

    [Fact]
    public void DiagonalInput_IsNoFasterThanStraight()
    {
        Arena arena = Sandbox();
        Run(arena, new InputState(new Vector2(1f, 1f), default, false, false), 240);
        Assert.True(arena.Player.Velocity.Length() <= Tuning.PlayerSpeed * 1.0001f);
    }

    [Theory]
    [InlineData(false, false, false, false, 0f, 0f)]
    [InlineData(true, false, false, false, -1f, 0f)]
    [InlineData(false, true, true, false, 0.70710677f, -0.70710677f)]
    [InlineData(true, true, false, true, 0f, 1f)]
    public void Direction_IsOneOfEightUnitVectors(bool left, bool right, bool up, bool down, float x, float y)
    {
        Vector2 d = InputState.Direction(left, right, up, down);
        Assert.Equal(x, d.X, 1e-6f);
        Assert.Equal(y, d.Y, 1e-6f);
    }

    [Fact]
    public void Firing_FollowsTheCadence_AndKeepsTheLastAim()
    {
        Arena arena = Sandbox();
        Run(arena, new InputState(default, -Vector2.UnitY, true, false), 120);

        // One second at one shot per 0.08 s.
        Assert.InRange(arena.Player.ShotsFired, 12, 13);
        Assert.Equal(arena.Player.ShotsFired, arena.Events.Count(e => e.Event == GameEvent.Shot));
        Assert.All(arena.Bullets, b => Assert.True(b.Velocity.Y < -Tuning.BulletSpeed * 0.99f));

        // A zero aim keeps the last direction.
        Run(arena, new InputState(default, default, true, false), 30);
        Assert.Equal(-Vector2.UnitY, arena.Player.Aim);

        // Bullets leave through the walls.
        Run(arena, Idle, 240);
        Assert.Empty(arena.Bullets);
    }

    [Fact]
    public void Bullet_HitsAnEnemy_AndScores()
    {
        Arena arena = Sandbox();
        Enemy target = arena.AddEnemy(EnemyKind.Lancer, Tuning.Center + new Vector2(120f, 0f));

        Assert.True(ShootUntil(arena, target, () => target.Health < 2), "The Lancer was hit.");
        Assert.Contains(arena.Events, e => e.Event == GameEvent.Hit);
        Assert.Equal(0, arena.Score);

        Assert.True(ShootUntil(arena, target, () => !target.Alive), "The Lancer was destroyed.");
        Assert.Equal(100, arena.Score);
        Assert.Equal(1, arena.Kills);
        Assert.Contains(arena.Events, e => e.Event == GameEvent.Kill);
    }

    [Fact]
    public void Drifter_SplitsIntoTwoSmallerOnes_ThenTwoSmallerStill()
    {
        Arena arena = Sandbox();
        Enemy large = arena.AddEnemy(EnemyKind.Drifter, Tuning.Center + new Vector2(110f, 0f), tier: 2);

        Assert.True(ShootUntil(arena, large, () => !large.Alive));
        Enemy[] medium = arena.Enemies.Where(e => e.Kind == EnemyKind.Drifter && e.Alive).ToArray();
        Assert.Equal(2, medium.Length);
        Assert.All(medium, e => Assert.Equal(1, e.Tier));
        Assert.All(medium, e => Assert.True(e.Radius < large.Radius));
        Assert.Contains(arena.Events, e => e.Event == GameEvent.Split);

        Assert.True(ShootUntil(arena, medium[0], () => !medium[0].Alive));
        Assert.Equal(2, arena.Enemies.Count(e => e.Alive && e.Tier == 0));

        // Small ones don't split.
        Enemy small = arena.Enemies.First(e => e.Alive && e.Tier == 0);
        int before = arena.Enemies.Count(e => e.Alive);
        Assert.True(ShootUntil(arena, small, () => !small.Alive));
        Assert.Equal(before - 1, arena.Enemies.Count(e => e.Alive));
    }

    [Fact]
    public void Multiplier_RisesEveryTenKills_AndScalesPoints()
    {
        Arena arena = Sandbox();
        arena.Player.Invulnerable = 1000f;

        for (int i = 0; i < Tuning.KillsPerMultiplier; i++)
        {
            arena.AddEnemy(EnemyKind.Drifter, new Vector2(60f + (i * 50f), 60f), tier: 0);
        }

        Bomb(arena);
        Run(arena, Idle, 240);

        Assert.Empty(arena.Enemies);
        Assert.Equal(Tuning.KillsPerMultiplier * 10, arena.Score);
        Assert.Equal(2, arena.Multiplier);

        // The next kill is worth double.
        Enemy next = arena.AddEnemy(EnemyKind.Drifter, Tuning.Center + new Vector2(80f, 0f), tier: 0);
        Assert.True(ShootUntil(arena, next, () => !next.Alive));
        Assert.Equal((Tuning.KillsPerMultiplier * 10) + 20, arena.Score);
    }

    [Fact]
    public void Bomb_ClearsEveryEnemyAndShot_OncePerPress()
    {
        Arena arena = Sandbox();
        arena.Player.Invulnerable = 1000f;
        arena.AddEnemy(EnemyKind.Drifter, new Vector2(40f, 40f));
        arena.AddEnemy(EnemyKind.Drifter, new Vector2(600f, 320f));
        arena.AddEnemy(EnemyKind.Lancer, new Vector2(600f, 40f));
        arena.AddEnemy(EnemyKind.Weaver, new Vector2(40f, 320f));
        arena.AddEnemy(EnemyKind.Pylon, new Vector2(500f, 200f));
        Run(arena, Idle, 300);
        Assert.NotEmpty(arena.Pulses);
        Assert.NotEmpty(arena.Rings);

        // Held for two seconds: one bomb.
        Run(arena, new InputState(default, default, false, true), 240);
        Assert.Equal(Tuning.StartBombs - 1, arena.Bombs);
        Assert.Single(arena.Events, e => e.Event == GameEvent.Bomb);
        Assert.Empty(arena.Enemies);
        Assert.Empty(arena.Pulses);
        Assert.Empty(arena.Rings);
        Assert.True(arena.Score > 0);

        // Out of bombs: nothing happens.
        Bomb(arena);
        Bomb(arena);
        Assert.Equal(0, arena.Bombs);
        arena.EndFrame();
        Bomb(arena);
        Assert.Equal(0, arena.Bombs);
        Assert.DoesNotContain(arena.Events, e => e.Event == GameEvent.Bomb);
    }

    [Fact]
    public void Respawn_IsInvulnerable_ThenVulnerableAgain()
    {
        Arena arena = Sandbox();
        arena.AddEnemy(EnemyKind.Pylon, arena.Player.Position);
        arena.Step(Idle);

        Assert.False(arena.Player.Alive);
        Assert.Equal(Tuning.StartLives - 1, arena.Lives);
        Assert.Equal(1, arena.Multiplier);
        Assert.Contains(arena.Events, e => e.Event == GameEvent.Death);

        Run(arena, Idle, (int)(Tuning.RespawnDelay / Tuning.StepSeconds) + 2);
        Assert.True(arena.Player.Alive);
        Assert.Equal(Tuning.Center, arena.Player.Position);
        Assert.True(arena.Player.Invulnerable > Tuning.InvulnerableSeconds - 0.1f);
        Assert.Empty(arena.Enemies);   // the respawn clears the middle
        Assert.Equal(0, arena.Score);  // ...without scoring

        // Something sitting on the ship can't hurt it yet...
        arena.AddEnemy(EnemyKind.Pylon, arena.Player.Position);
        Run(arena, Idle, (int)((Tuning.InvulnerableSeconds - 0.3f) / Tuning.StepSeconds));
        Assert.True(arena.Player.Alive);
        Assert.Equal(Tuning.StartLives - 1, arena.Lives);

        // ...until the invulnerability runs out.
        Run(arena, Idle, (int)(0.4f / Tuning.StepSeconds));
        Assert.False(arena.Player.Alive);
        Assert.Equal(Tuning.StartLives - 2, arena.Lives);
    }

    [Fact]
    public void LastLife_EndsTheGame_AndFireStartsOver()
    {
        Arena arena = Sandbox();

        for (int i = 0; i < 4000 && !arena.GameOver; i++)
        {
            if (arena.Player.Alive && arena.Player.Invulnerable <= 0f)
            {
                arena.AddEnemy(EnemyKind.Drifter, arena.Player.Position, tier: 0);
            }

            arena.Step(Idle);
        }

        Assert.True(arena.GameOver);
        Assert.Equal(0, arena.Lives);
        Assert.Contains(arena.Events, e => e.Event == GameEvent.GameOver);
        Assert.Equal("GAME OVER", arena.Banner?.Title);

        // A press right away is ignored (so a held fire button doesn't skip the screen)...
        arena.Step(new InputState(default, default, true, false));
        arena.Step(Idle);
        Assert.True(arena.GameOver);

        // ...a press after a moment starts over.
        Run(arena, Idle, 200);
        arena.Step(new InputState(default, default, true, false));
        Assert.False(arena.GameOver);
        Assert.Equal(Tuning.StartLives, arena.Lives);
        Assert.Equal(Tuning.StartBombs, arena.Bombs);
        Assert.Equal(0, arena.Score);
        Assert.Equal(0, arena.Wave);
    }

    [Fact]
    public void Lancer_TelegraphsBeforeItDashes()
    {
        Arena arena = Sandbox();
        arena.Player.Invulnerable = 1000f;
        Enemy lancer = arena.AddEnemy(EnemyKind.Lancer, Tuning.Center + new Vector2(150f, 60f));
        LancerPhase previous = lancer.Phase;
        float aimTime = 0f;
        bool dashed = false;

        for (int i = 0; i < 600 && !dashed; i++)
        {
            arena.Step(Idle);

            if (lancer.Phase == LancerPhase.Aim)
            {
                aimTime += Tuning.StepSeconds;
            }

            if (lancer.Phase == LancerPhase.Dash && previous == LancerPhase.Aim)
            {
                dashed = true;
                Assert.True(aimTime >= 0.75f, $"Aimed for {aimTime} s before dashing.");
                Assert.True(lancer.Velocity.Length() > 300f);
                Assert.Contains(arena.Events, e => e.Event == GameEvent.Dash);
            }

            Assert.False(previous == LancerPhase.Approach && lancer.Phase == LancerPhase.Dash, "A Lancer never dashes without aiming first.");
            previous = lancer.Phase;
        }

        Assert.True(dashed);
    }

    [Fact]
    public void Weaver_FiresPulsesAtTheShip_ThatBulletsCanShootDown()
    {
        Arena arena = Sandbox();
        arena.Player.Invulnerable = 1000f;
        arena.AddEnemy(EnemyKind.Weaver, Tuning.Center + new Vector2(200f, 0f));
        Run(arena, Idle, 300);

        Assert.NotEmpty(arena.Pulses);
        Assert.Contains(arena.Events, e => e.Event == GameEvent.Pulse);
        Projectile pulse = arena.Pulses[0];
        Assert.True(Vector2.Dot(Vector2.Normalize(pulse.Velocity), Vector2.Normalize(arena.Player.Position - pulse.Position)) > 0.9f);

        int pulses = arena.Pulses.Count;

        for (int i = 0; i < 120 && arena.Pulses.Count >= pulses; i++)
        {
            Projectile nearest = arena.Pulses.OrderBy(p => Vector2.Distance(p.Position, arena.Player.Position)).First();
            arena.Step(new InputState(default, nearest.Position - arena.Player.Position, true, false));
        }

        Assert.True(arena.Pulses.Count < pulses, "A pulse was shot down.");
    }

    [Fact]
    public void PylonRing_ExpandsAndHurts()
    {
        Arena arena = Sandbox();
        arena.AddEnemy(EnemyKind.Pylon, Tuning.Center + new Vector2(70f, 0f));

        for (int i = 0; i < 600 && arena.Player.Alive; i++)
        {
            arena.Step(Idle);
        }

        Assert.Contains(arena.Events, e => e.Event == GameEvent.Ring);
        Assert.False(arena.Player.Alive);
        Ring ring = Assert.Single(arena.Rings);
        Assert.InRange(ring.Radius, 70f - 8f, 70f + 8f);
    }

    [Fact]
    public void Waves_AreTelegraphed_AwayFromTheShip()
    {
        Arena arena = new(3);
        arena.Player.Invulnerable = 1000f;
        int steps = 0;

        while (arena.Markers.Count == 0 && steps++ < 600)
        {
            arena.Step(Idle);
        }

        Assert.Equal(1, arena.Wave);
        Assert.Contains(arena.Events, e => e.Event == GameEvent.WaveStart);
        Assert.Empty(arena.Enemies);

        // The ship idles (nothing is destroyed, so nothing splits): every enemy must appear exactly
        // where a marker of its kind was showing the step before, and every marker away from the ship.
        HashSet<Enemy> seen = [];
        SpawnMarker[] previous = [.. arena.Markers];

        for (int i = 0; i < 1200 && arena.PendingSpawns + arena.Markers.Count > 0; i++)
        {
            Assert.All(arena.Markers, m => Assert.True(Vector2.Distance(m.Position, arena.Player.Position) >= Tuning.MinSpawnDistance));
            arena.Step(Idle);

            foreach (Enemy e in arena.Enemies.Where(seen.Add))
            {
                Assert.Contains(previous, m => m.Kind == e.Kind && Vector2.Distance(m.Position, e.Position) < 0.01f);
            }

            previous = [.. arena.Markers];
        }

        Assert.Equal(5, seen.Count);   // wave 1: 3 + 2 × 1 enemies
        Assert.Equal(0, arena.PendingSpawns);
    }

    [Fact]
    public void Spawn_WaitsWhileTheShipStandsOnItsMarker()
    {
        Arena arena = new(5);

        while (arena.Markers.Count == 0)
        {
            arena.Step(Idle);
        }

        SpawnMarker marker = arena.Markers[0];
        arena.Player.Invulnerable = 1000f;

        for (int i = 0; i < 360; i++)
        {
            arena.Player.Position = marker.Position;
            arena.Player.Velocity = Vector2.Zero;
            arena.Step(Idle);
            Assert.Contains(arena.Markers, m => m.Position == marker.Position);
            Assert.DoesNotContain(arena.Enemies, e => Vector2.Distance(e.Position, marker.Position) < 1f);
        }

        arena.Player.Position = new Vector2(marker.Position.X < Tuning.Center.X ? 600f : 40f, marker.Position.Y < Tuning.Center.Y ? 320f : 40f);
        arena.Step(Idle);
        Assert.DoesNotContain(arena.Markers, m => m.Position == marker.Position);
        Assert.Contains(arena.Enemies, e => e.Kind == marker.Kind && Vector2.Distance(e.Position, marker.Position) < 1f);
    }

    [Fact]
    public void Waves_GrowAndUnlockKinds()
    {
        Assert.All(Arena.Compose(1, new Rng(1)), e => Assert.Equal(EnemyKind.Drifter, e.Kind));
        Assert.DoesNotContain(Arena.Compose(2, new Rng(1)), e => e.Kind is EnemyKind.Weaver or EnemyKind.Pylon);

        for (int wave = 1; wave < 20; wave++)
        {
            Assert.True(Arena.Compose(wave + 1, new Rng(9)).Count > Arena.Compose(wave, new Rng(9)).Count);
            Assert.True(Arena.Compose(wave, new Rng(9)).Count(e => e.Kind == EnemyKind.Pylon) <= Math.Max(0, 1 + ((wave - 4) / 3)));
        }

        // Over many waves every kind shows up.
        var late = Enumerable.Range(6, 10).SelectMany(w => Arena.Compose(w, new Rng((uint)w))).ToArray();
        Assert.All(Enum.GetValues<EnemyKind>(), k => Assert.Contains(late, e => e.Kind == k));
        Assert.Equal(Arena.Compose(7, new Rng(3)), Arena.Compose(7, new Rng(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Arena.Compose(0, new Rng(1)));
    }

    [Fact]
    public void SameSeedAndInput_GiveTheSameGame()
    {
        Arena a = new(42), b = new(42), c = new(43);

        for (int i = 0; i < 6000; i++)
        {
            InputState input = Script(i);
            a.Step(input);
            b.Step(input);
            c.Step(input);
            a.EndFrame();
            b.EndFrame();
            c.EndFrame();

            if (i % 500 == 0)
            {
                Assert.Equal(a.Checksum(), b.Checksum());
            }
        }

        Assert.Equal(a.Checksum(), b.Checksum());
        Assert.Equal(a.Score, b.Score);
        Assert.NotEqual(a.Checksum(), c.Checksum());
        Assert.True(a.Wave >= 1 && a.Kills > 0, $"Something happened (wave {a.Wave}, kills {a.Kills}).");

        // A restart replays the same game.
        a.Reset();
        Arena fresh = new(42);

        for (int i = 0; i < 1000; i++)
        {
            a.Step(Script(i));
            fresh.Step(Script(i));
        }

        Assert.Equal(fresh.Checksum(), a.Checksum());
    }

    [Fact]
    public void Pilot_ClearsTheFirstWaves()
    {
        Arena arena = new(1);
        Pilot pilot = new();

        for (int i = 0; i < 120 * 90 && arena.Wave < 4; i++)
        {
            arena.Step(pilot.Next(arena));
            arena.EndFrame();
        }

        Assert.True(arena.Wave >= 4, $"Reached wave {arena.Wave}.");
        Assert.True(arena.Score > 0);
    }

    [Fact]
    public void Rng_IsSeededAndInRange()
    {
        Rng a = new(7), b = new(7), zero = new(0);

        for (int i = 0; i < 1000; i++)
        {
            float f = a.NextFloat();
            Assert.Equal(f, b.NextFloat());
            Assert.InRange(f, 0f, 0.99999994f);
            Assert.InRange(a.Next(5), 0, 4);
            b.Next(5);
            Assert.Equal(1f, zero.Direction().Length(), 1e-5f);
        }
    }

    private static Arena Sandbox() => new(7) { WavesEnabled = false };

    private static void Run(Arena arena, InputState input, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            arena.Step(input);
        }
    }

    /// <summary>Releases the button, presses it for one step, releases it again.</summary>
    private static void Bomb(Arena arena)
    {
        arena.Step(Idle);
        arena.Step(new InputState(default, default, false, true));
        arena.Step(Idle);
    }

    /// <summary>Fires at <paramref name="target"/> each step until <paramref name="done"/> (at most five seconds).</summary>
    private static bool ShootUntil(Arena arena, Enemy target, Func<bool> done)
    {
        arena.Player.Invulnerable = 1000f;

        for (int i = 0; i < 600; i++)
        {
            if (done())
            {
                return true;
            }

            arena.Step(new InputState(default, target.Position - arena.Player.Position, true, false));
        }

        return done();
    }

    private static InputState Script(int i)
    {
        float t = i * Tuning.StepSeconds;
        Vector2 move = new(MathF.Cos(t * 0.9f) * 0.8f, MathF.Sin(t * 1.3f) * 0.8f);
        Vector2 aim = new(MathF.Cos(t * 2.1f), MathF.Sin(t * 2.1f));
        return new InputState(move, aim, true, i % 1500 == 1400);
    }
}
