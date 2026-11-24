// Original SharpCompute sample (MIT).
using System;
using System.Numerics;

namespace SharpCompute.Samples.Shooter.Game;

/// <summary>Enemy behaviour.</summary>
public sealed partial class Arena
{
    private const float LancerAimSeconds = 0.8f;
    private const float LancerLockSeconds = 0.55f;
    private const float LancerDashSeconds = 0.55f;
    private const float LancerRecoverSeconds = 0.7f;
    private const float WeaverOrbitRadius = 135f;
    private const float PylonRingMaxRadius = 160f;

    /// <summary>Wave speed-up: 7 % per wave, capped at 1.8×.</summary>
    private float SpeedFactor => MathF.Min(1f + (0.07f * Math.Max(0, Wave - 1)), 1.8f);

    private static float DrifterSpeed(int tier) => 30f + (14f * (2 - tier));

    private Enemy CreateEnemy(EnemyKind kind, Vector2 position, int tier)
    {
        tier = Math.Clamp(tier, 0, 2);

        Enemy e = new()
        {
            Kind = kind,
            Tier = kind == EnemyKind.Drifter ? tier : 0,
            Position = position,
            Angle = _rng.Range(0f, MathF.Tau),
        };

        switch (kind)
        {
            case EnemyKind.Drifter:
                e.Radius = tier switch { 2 => 13f, 1 => 9f, _ => 6f };
                e.Health = tier + 1;
                e.Velocity = _rng.Direction() * DrifterSpeed(tier) * SpeedFactor;
                break;
            case EnemyKind.Lancer:
                e.Radius = 7f;
                e.Health = 2;
                e.Phase = LancerPhase.Approach;
                break;
            case EnemyKind.Weaver:
                e.Radius = 8f;
                e.Health = 3;
                e.Timer = 1.2f + _rng.Range(0f, 0.8f);
                e.Orbit = MathF.Atan2(position.Y - Player.Position.Y, position.X - Player.Position.X);
                e.OrbitDirection = _rng.NextFloat() < 0.5f ? -1f : 1f;
                break;
            case EnemyKind.Pylon:
                e.Radius = 11f;
                e.Health = 12;
                e.Timer = 1.5f;
                break;
        }

        return e;
    }

    private void UpdateEnemies()
    {
        float speed = SpeedFactor;

        // By index: nothing here adds enemies, but kills elsewhere in the step can.
        for (int i = 0; i < _enemies.Count; i++)
        {
            Enemy e = _enemies[i];

            if (!e.Alive)
            {
                continue;
            }

            e.Flash = MathF.Max(0f, e.Flash - Dt);

            switch (e.Kind)
            {
                case EnemyKind.Drifter:
                    UpdateDrifter(e, speed);
                    break;
                case EnemyKind.Lancer:
                    UpdateLancer(e, speed);
                    break;
                case EnemyKind.Weaver:
                    UpdateWeaver(e, speed);
                    break;
                case EnemyKind.Pylon:
                    UpdatePylon(e, speed);
                    break;
            }
        }
    }

    /// <summary>Drifts in a straight line, bending gently toward the ship, and bounces off the walls.</summary>
    private void UpdateDrifter(Enemy e, float speedFactor)
    {
        float speed = DrifterSpeed(e.Tier) * speedFactor;
        Vector2 velocity = e.Velocity;

        if (Player.Alive)
        {
            Vector2 toPlayer = Player.Position - e.Position;

            if (toPlayer.LengthSquared() > 1f)
            {
                velocity += Vector2.Normalize(toPlayer) * (22f * Dt);
            }
        }

        float length = velocity.Length();

        if (length > speed * 1.4f)
        {
            velocity *= MathF.Max(speed / length, 1f - (1.5f * Dt));
        }
        else if (length < speed * 0.6f)
        {
            velocity = (length > 1e-3f ? velocity / length : _rng.Direction()) * (speed * 0.6f);
        }

        e.Velocity = velocity;
        Bounce(e);
        e.Angle += Dt * (1.6f - (e.Tier * 0.45f));
    }

    /// <summary>Approach, lock on (the sight line glows), dash straight, recover, repeat.</summary>
    private void UpdateLancer(Enemy e, float speedFactor)
    {
        e.Timer += Dt;
        Vector2 toPlayer = Player.Position - e.Position;
        float distance = toPlayer.Length();
        Vector2 direction = distance > 1e-3f ? toPlayer / distance : e.Heading;

        switch (e.Phase)
        {
            case LancerPhase.Approach:
                e.Heading = Vector2.Normalize(Vector2.Lerp(e.Heading, direction, 1f - MathF.Exp(-6f * Dt)) + new Vector2(1e-6f, 0f));
                e.Velocity = e.Heading * (55f * speedFactor);
                e.Charge = 0f;

                if (Player.Alive && ((distance < 190f && e.Timer > 0.8f) || e.Timer > 3f))
                {
                    e.Phase = LancerPhase.Aim;
                    e.Timer = 0f;
                }

                break;
            case LancerPhase.Aim:
                e.Velocity *= MathF.Exp(-10f * Dt);

                // Tracks the ship for most of the wind-up, then commits: the last moment is the cue to sidestep.
                if (e.Timer < LancerLockSeconds && distance > 1e-3f)
                {
                    e.Heading = direction;
                }

                e.Charge = Math.Clamp(e.Timer / LancerAimSeconds, 0f, 1f);

                if (e.Timer >= LancerAimSeconds)
                {
                    e.Phase = LancerPhase.Dash;
                    e.Timer = 0f;
                    e.Velocity = e.Heading * (390f * MathF.Sqrt(speedFactor));
                    _events.Add(new GameEventAt(GameEvent.Dash, e.Position));
                }

                break;
            case LancerPhase.Dash:
                e.Charge = 1f;

                if ((Steps & 1) == 0)
                {
                    _bursts.Add(new ParticleBurst(e.Position - (e.Heading * 8f), 14, Palette.Lancer, 50f, 0.45f, 0.9f, -e.Heading, 0.5f, Streak: 0.04f));
                }

                if (e.Timer >= LancerDashSeconds || !InsideArena(e.Position + (e.Velocity * Dt), e.Radius))
                {
                    e.Phase = LancerPhase.Recover;
                    e.Timer = 0f;
                }

                break;
            case LancerPhase.Recover:
                e.Velocity *= MathF.Exp(-6f * Dt);
                e.Charge = 1f - Math.Clamp(e.Timer / LancerRecoverSeconds, 0f, 1f);

                if (e.Timer >= LancerRecoverSeconds)
                {
                    e.Phase = LancerPhase.Approach;
                    e.Timer = 0f;
                }

                break;
        }

        e.Position = ClampToArena(e.Position + (e.Velocity * Dt), e.Radius);
        e.Angle = MathF.Atan2(e.Heading.Y, e.Heading.X);
    }

    /// <summary>Circles the ship at range and fires a slow pulse at it every couple of seconds.</summary>
    private void UpdateWeaver(Enemy e, float speedFactor)
    {
        e.Orbit += e.OrbitDirection * 0.8f * speedFactor * Dt;
        Vector2 target = ClampToArena(Player.Position + (new Vector2(MathF.Cos(e.Orbit), MathF.Sin(e.Orbit)) * WeaverOrbitRadius), 20f);
        Vector2 toTarget = target - e.Position;
        float distance = toTarget.Length();
        Vector2 desired = distance > 1e-3f ? toTarget / distance * MathF.Min(110f * speedFactor, distance * 3f) : Vector2.Zero;
        e.Velocity = Vector2.Lerp(e.Velocity, desired, 1f - MathF.Exp(-4f * Dt));
        e.Position = ClampToArena(e.Position + (e.Velocity * Dt), e.Radius);
        e.Angle += 2.2f * Dt;

        e.Timer -= Dt;

        if (e.Timer <= 0f)
        {
            e.Timer = 2.4f / MathF.Sqrt(speedFactor);

            if (Player.Alive)
            {
                Vector2 toPlayer = Player.Position - e.Position;
                Vector2 aim = toPlayer.LengthSquared() > 1f ? Vector2.Normalize(toPlayer) : e.Heading;
                e.Heading = aim;
                _pulses.Add(new Projectile { Position = e.Position + (aim * e.Radius), Velocity = aim * (90f * MathF.Sqrt(speedFactor)), Life = 7f, Radius = 3.5f });
                _events.Add(new GameEventAt(GameEvent.Pulse, e.Position));
                _bursts.Add(new ParticleBurst(e.Position + (aim * e.Radius), 10, Palette.Pulse, 70f, 0.3f, 0.8f, aim, 0.7f));
            }
        }
    }

    /// <summary>Stays put and sends out an expanding ring every few seconds.</summary>
    private void UpdatePylon(Enemy e, float speedFactor)
    {
        float period = 3.4f / MathF.Sqrt(speedFactor);
        e.Timer -= Dt;
        e.Charge = 1f - Math.Clamp(e.Timer / period, 0f, 1f);
        e.Angle += 0.6f * Dt;

        if (e.Timer <= 0f)
        {
            e.Timer = period;
            _rings.Add(new Ring { Center = e.Position, Radius = e.Radius + 2f, Speed = 72f * MathF.Sqrt(speedFactor), MaxRadius = PylonRingMaxRadius });
            _events.Add(new GameEventAt(GameEvent.Ring, e.Position));
            _bursts.Add(new ParticleBurst(e.Position, 240, Palette.Pylon, 80f, 0.7f, 0.9f, SpeedJitter: 0.05f, Drag: 1f));
        }
    }

    private static void Bounce(Enemy e)
    {
        Vector2 p = e.Position + (e.Velocity * Dt);
        Vector2 v = e.Velocity;
        float min = Tuning.Wall + e.Radius;
        float maxX = Tuning.Width - Tuning.Wall - e.Radius;
        float maxY = Tuning.Height - Tuning.Wall - e.Radius;

        if ((p.X < min && v.X < 0f) || (p.X > maxX && v.X > 0f))
        {
            v.X = -v.X;
        }

        if ((p.Y < min && v.Y < 0f) || (p.Y > maxY && v.Y > 0f))
        {
            v.Y = -v.Y;
        }

        e.Velocity = v;
        e.Position = ClampToArena(p, e.Radius);
    }
}
