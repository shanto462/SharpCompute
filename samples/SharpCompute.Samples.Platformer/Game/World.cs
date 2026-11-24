// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpCompute.Samples.Platformer.Game;

/// <summary>Something that happened during a step, for sound and particles.</summary>
public enum GameEvent
{
    Jump,
    Land,
    Mote,
    Lantern,
    Stomp,
    Hurt,
    GateOpen,
    LevelComplete,
}

/// <summary>An event with where it happened (pixels).</summary>
public readonly record struct GameEventAt(GameEvent Event, Vector2 Position);

/// <summary>The lantern spirit you play: a little flame that carries its own light.</summary>
public sealed class Player
{
    public const float Width = 10f;
    public const float Height = 13f;

    public Box Body;
    public Vector2 Velocity;
    public bool OnGround;
    public bool FacingLeft;
    public float CoyoteTimer;
    public float JumpBufferTimer;
    public bool JumpHeld;

    /// <summary>0..1: how much light the spirit carries; motes refill it, it fades slowly.</summary>
    public float Light = 0.6f;

    /// <summary>Seconds left of the fade-out after touching spikes or a creature.</summary>
    public float HurtTimer;

    /// <summary>Seconds spent running, for the animation.</summary>
    public float RunTime;

    public float LightRadius => Tuning.MinLightRadius + ((Tuning.MaxLightRadius - Tuning.MinLightRadius) * Light);
}

/// <summary>A floating mote of light; collecting one brightens the spirit.</summary>
public sealed class Mote(Vector2 position, float phase)
{
    public Vector2 Position { get; } = position;

    public float Phase { get; } = phase;

    public bool Collected;
}

/// <summary>An old lantern: touching it relights it, which also makes it the respawn point.</summary>
public sealed class Lantern(Vector2 position)
{
    public Vector2 Position { get; } = position;

    public bool Lit;

    /// <summary>Seconds since it was lit, for the flare.</summary>
    public float LitTime;
}

/// <summary>A creature: crawlers walk ledges and turn at edges; moths drift in a figure eight.</summary>
public sealed class Creature(SpawnKind kind, Box body, Vector2 home)
{
    public SpawnKind Kind { get; } = kind;

    public Box Body = body;

    public Vector2 Home { get; } = home;

    public float Direction = -1f;

    public float Time;

    public bool Alive = true;
}

/// <summary>
/// One level being played: all the game state and the fixed-step simulation. No rendering and no
/// timing here, so tests can drive it step by step.
/// </summary>
public sealed class World
{
    private readonly List<GameEventAt> _events = [];
    private Vector2 _respawn;
    private bool _gateOpen;

    /// <param name="level">The level to play.</param>
    /// <param name="spawnCreatures">False leaves the creatures out (the reachability check uses that).</param>
    public World(Level level, bool spawnCreatures = true)
    {
        Level = level ?? throw new ArgumentNullException(nameof(level));
        Player = new Player();

        foreach (Spawn spawn in level.Spawns)
        {
            Vector2 center = Level.Center(spawn.TileX, spawn.TileY);

            switch (spawn.Kind)
            {
                case SpawnKind.Player:
                    _respawn = new Vector2(center.X, (spawn.TileY + 1) * Level.TileSize);
                    break;
                case SpawnKind.Mote:
                    Motes.Add(new Mote(center, (spawn.TileX * 0.7f) + (spawn.TileY * 1.3f)));
                    break;
                case SpawnKind.Lantern:
                    Lanterns.Add(new Lantern(center));
                    break;
                case SpawnKind.Crawler when spawnCreatures:
                    Creatures.Add(new Creature(SpawnKind.Crawler, new Box(center.X - 6, ((spawn.TileY + 1) * Level.TileSize) - 9, 12, 9), center));
                    break;
                case SpawnKind.Moth when spawnCreatures:
                    Creatures.Add(new Creature(SpawnKind.Moth, new Box(center.X - 6, center.Y - 5, 12, 10), center));
                    break;
                case SpawnKind.Gate:
                    Gate = center;
                    break;
            }
        }

        PlacePlayer(_respawn);
    }

    public Level Level { get; }

    public Player Player { get; }

    public List<Mote> Motes { get; } = [];

    public List<Lantern> Lanterns { get; } = [];

    public List<Creature> Creatures { get; } = [];

    /// <summary>The gate's center; it opens once every lantern is lit.</summary>
    public Vector2 Gate { get; }

    public bool GateOpen => _gateOpen;

    public bool Complete { get; private set; }

    /// <summary>Simulated seconds.</summary>
    public double Time { get; private set; }

    public int LanternsLit { get; private set; }

    public int MotesCollected { get; private set; }

    /// <summary>The events of the last <see cref="Step"/>; the list is reused, so copy what you keep.</summary>
    public IReadOnlyList<GameEventAt> Events => _events;

    /// <summary>Advances the world by one fixed step of <see cref="Tuning.StepSeconds"/>.</summary>
    public void Step(InputState input)
    {
        const float dt = Tuning.StepSeconds;
        _events.Clear();
        Time += dt;

        if (Complete)
        {
            return;
        }

        StepPlayer(input, dt);
        StepCreatures(dt);
        Interact(dt);

        foreach (Lantern lantern in Lanterns)
        {
            lantern.LitTime += lantern.Lit ? dt : 0;
        }
    }

    private void StepPlayer(InputState input, float dt)
    {
        Player p = Player;

        if (p.HurtTimer > 0)
        {
            p.HurtTimer -= dt;

            if (p.HurtTimer <= 0)
            {
                PlacePlayer(_respawn);
            }

            return;
        }

        // Horizontal: accelerate towards the held direction, or brake.
        float wish = (input.Right ? 1f : 0f) - (input.Left ? 1f : 0f);
        float acceleration = p.OnGround ? Tuning.GroundAcceleration : Tuning.AirAcceleration;

        if (wish != 0)
        {
            p.Velocity.X = Approach(p.Velocity.X, wish * Tuning.RunSpeed, acceleration * dt);
            p.FacingLeft = wish < 0;
        }
        else if (p.OnGround)
        {
            p.Velocity.X = Approach(p.Velocity.X, 0, Tuning.GroundFriction * dt);
        }

        // Jump: buffered presses, coyote time and a cut when the button is released early.
        bool pressed = input.Jump && !p.JumpHeld;
        p.JumpHeld = input.Jump;
        p.JumpBufferTimer = pressed ? Tuning.JumpBuffer : MathF.Max(0, p.JumpBufferTimer - dt);
        p.CoyoteTimer = p.OnGround ? Tuning.CoyoteTime : MathF.Max(0, p.CoyoteTimer - dt);

        if (p.JumpBufferTimer > 0 && p.CoyoteTimer > 0)
        {
            p.Velocity.Y = -Tuning.JumpSpeed;
            p.JumpBufferTimer = p.CoyoteTimer = 0;
            p.OnGround = false;
            Emit(GameEvent.Jump, new Vector2(p.Body.Center.X, p.Body.Bottom));
        }

        if (!input.Jump && p.Velocity.Y < 0)
        {
            p.Velocity.Y *= MathF.Pow(Tuning.JumpCut, dt * 30f);
        }

        p.Velocity.Y = MathF.Min(p.Velocity.Y + (Tuning.Gravity * dt), Tuning.MaxFallSpeed);

        bool wasGrounded = p.OnGround;
        float fallSpeed = p.Velocity.Y;
        p.Body = Collision.Move(Level, p.Body, ref p.Velocity, dt, input.Down, out bool hitFloor, out _, out _);
        p.OnGround = hitFloor || (p.Velocity.Y >= 0 && Collision.Grounded(Level, p.Body) && !input.Down);

        if (p.OnGround && !wasGrounded && fallSpeed > 120f)
        {
            Emit(GameEvent.Land, new Vector2(p.Body.Center.X, p.Body.Bottom));
        }

        p.RunTime = p.OnGround && MathF.Abs(p.Velocity.X) > 10 ? p.RunTime + dt : 0;
        p.Light = MathF.Max(0, p.Light - (Tuning.LightFade * dt));

        if (Collision.Touches(Level, p.Body.Shrink(2), TileKind.Spikes) || p.Body.Y > Level.PixelHeight)
        {
            Hurt();
        }
    }

    private void StepCreatures(float dt)
    {
        foreach (Creature c in Creatures)
        {
            if (!c.Alive)
            {
                continue;
            }

            c.Time += dt;

            if (c.Kind == SpawnKind.Crawler)
            {
                // Walk; turn at walls and at ledge edges, so crawlers never fall off.
                Vector2 velocity = new(c.Direction * 28f, 0);
                Box moved = Collision.Move(Level, c.Body, ref velocity, dt, false, out _, out _, out bool hitWall);
                float probeX = c.Direction > 0 ? moved.Right + 1 : moved.X - 1;
                int below = Collision.TileOf(moved.Bottom + 1);
                bool edge = Level[Collision.TileOf(probeX), below] is not (TileKind.Rock or TileKind.Ledge);

                if (hitWall || edge)
                {
                    c.Direction = -c.Direction;
                }
                else
                {
                    c.Body = moved;
                }
            }
            else
            {
                // Moths drift in a slow figure eight around where they started.
                float t = c.Time * 0.9f;
                Vector2 center = c.Home + new Vector2(MathF.Sin(t) * 26f, MathF.Sin(2 * t) * 10f);
                c.Direction = MathF.Cos(t) >= 0 ? 1 : -1;
                c.Body = c.Body with { X = center.X - (c.Body.Width / 2), Y = center.Y - (c.Body.Height / 2) };
            }
        }
    }

    private void Interact(float dt)
    {
        Player p = Player;

        if (p.HurtTimer > 0)
        {
            return;
        }

        Box body = p.Body;

        foreach (Mote mote in Motes)
        {
            if (!mote.Collected && Vector2.DistanceSquared(mote.Position, body.Center) < 14f * 14f)
            {
                mote.Collected = true;
                MotesCollected++;
                p.Light = MathF.Min(1f, p.Light + Tuning.MoteLight);
                Emit(GameEvent.Mote, mote.Position);
            }
        }

        foreach (Lantern lantern in Lanterns)
        {
            if (!lantern.Lit && Vector2.DistanceSquared(lantern.Position, body.Center) < 16f * 16f)
            {
                lantern.Lit = true;
                LanternsLit++;
                p.Light = 1f;
                _respawn = new Vector2(lantern.Position.X, lantern.Position.Y + (Level.TileSize / 2f));
                Emit(GameEvent.Lantern, lantern.Position);

                if (LanternsLit == Lanterns.Count)
                {
                    _gateOpen = true;
                    Emit(GameEvent.GateOpen, Gate);
                }
            }
        }

        foreach (Creature c in Creatures)
        {
            if (!c.Alive || !body.Shrink(1).Overlaps(c.Body))
            {
                continue;
            }

            // A stomp: falling, with the feet in the creature's upper half.
            if (p.Velocity.Y > 0 && body.Bottom - (p.Velocity.Y * dt) <= c.Body.Y + (c.Body.Height / 2))
            {
                c.Alive = false;
                p.Velocity.Y = -Tuning.StompBounce;
                p.OnGround = false;
                Emit(GameEvent.Stomp, c.Body.Center);
            }
            else
            {
                Hurt();
                return;
            }
        }

        if (_gateOpen && Vector2.DistanceSquared(Gate, body.Center) < 14f * 14f)
        {
            Complete = true;
            Emit(GameEvent.LevelComplete, Gate);
        }
    }

    private void Hurt()
    {
        if (Player.HurtTimer > 0)
        {
            return;
        }

        Player.HurtTimer = 0.7f;
        Player.Velocity = Vector2.Zero;
        Emit(GameEvent.Hurt, Player.Body.Center);
    }

    /// <summary>Puts the player's feet at <paramref name="feet"/> (pixels), standing still.</summary>
    public void Teleport(Vector2 feet) => PlacePlayer(feet);

    private void PlacePlayer(Vector2 feet)
    {
        Player.Body = new Box(feet.X - (Player.Width / 2), feet.Y - Player.Height, Player.Width, Player.Height);
        Player.Velocity = Vector2.Zero;
        Player.OnGround = false;
        Player.HurtTimer = 0;
        Player.Light = MathF.Max(Player.Light, 0.5f);
    }

    private void Emit(GameEvent e, Vector2 at) => _events.Add(new GameEventAt(e, at));

    private static float Approach(float value, float target, float step) =>
        value < target ? MathF.Min(value + step, target) : MathF.Max(value - step, target);
}
