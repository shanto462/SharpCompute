// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpCompute.Samples.Platformer.Game;

/// <summary>A burst of particles to spawn, in world pixels; colors are linear RGB.</summary>
public readonly record struct ParticleBurst(Vector2 Position, int Count, Vector3 Color, float Speed, float Life, float Size, float Gravity, float UpBias);

/// <summary>A line of HUD text and how long it stays.</summary>
public readonly record struct Banner(string Title, string Subtitle, float Seconds);

/// <summary>
/// The whole game outside rendering and sound: the current <see cref="World"/>, moving on to the next
/// level, the camera, fades, banners and the particle bursts that events cause. Deterministic: the
/// same inputs give the same game, which the replay mode and the tests rely on.
/// </summary>
public sealed class GameSession
{
    public const int ViewWidth = 480;
    public const int ViewHeight = 270;

    private const float FadeSeconds = 0.6f;

    private readonly List<ParticleBurst> _bursts = [];
    private readonly List<GameEventAt> _events = [];
    private uint _random = 0x9E3779B9u;
    private float _bannerTime;
    private float _transition;
    private bool _advancing;
    private int _steps;

    public GameSession(int startLevel = 0)
    {
        LevelIndex = Math.Clamp(startLevel, 0, Levels.All.Length - 1);
        World = new World(Levels.All[LevelIndex]);
        Camera = new Camera(ViewWidth, ViewHeight);
        Camera.Reset(World);
        Banner = LevelIndex == 0
            ? new Banner("GLOWFALL", "ARROWS MOVE   Z JUMP   DOWN DROPS", 5f)
            : new Banner(World.Level.Name.ToUpperInvariant(), "LIGHT EVERY LANTERN", 3.5f);
    }

    public int LevelIndex { get; private set; }

    public World World { get; private set; }

    public Camera Camera { get; }

    /// <summary>True after the last level; jump starts over.</summary>
    public bool Finished { get; private set; }

    public Banner? Banner { get; private set; }

    /// <summary>Seconds the current banner has been showing.</summary>
    public float BannerTime => _bannerTime;

    /// <summary>Raised when a new level's world replaces the old one (the renderer reloads its tiles).</summary>
    public event Action<World>? LevelLoaded;

    /// <summary>Particle bursts produced since the last <see cref="TakeBursts"/>.</summary>
    public IReadOnlyList<ParticleBurst> Bursts => _bursts;

    /// <summary>Events since the last <see cref="TakeBursts"/>, for sound.</summary>
    public IReadOnlyList<GameEventAt> Events => _events;

    /// <summary>Simulated seconds since the session started.</summary>
    public float Time => _steps * Tuning.StepSeconds;

    /// <summary>0 black … 1 fully visible: fades around level changes and respawns.</summary>
    public float Fade
    {
        get
        {
            float transition = 1f - Math.Clamp(_transition / FadeSeconds, 0f, 1f);
            float hurt = World.Player.HurtTimer > 0 ? Math.Clamp(World.Player.HurtTimer / 0.7f, 0f, 1f) : 1f;

            // Fade out while hurt, back in on respawn.
            hurt = World.Player.HurtTimer > 0 ? 0.35f + (0.65f * hurt * hurt) : 1f;
            return MathF.Min(_advancing ? transition : 1f - (Math.Clamp(-_transition, 0f, FadeSeconds) / FadeSeconds), hurt);
        }
    }

    /// <summary>One fixed step: simulate, react to what happened, follow with the camera.</summary>
    public void Step(InputState input)
    {
        const float dt = Tuning.StepSeconds;
        _steps++;
        _bannerTime += dt;

        if (Banner is { } banner && _bannerTime > banner.Seconds)
        {
            Banner = null;
        }

        if (Finished)
        {
            if (input.Jump && _bannerTime > 1.5f)
            {
                StartLevel(0, new Banner("GLOWFALL", "ARROWS MOVE   Z JUMP   DOWN DROPS", 4f));
                Finished = false;
            }

            AmbientParticles();
            return;
        }

        if (_advancing)
        {
            _transition += dt;

            if (_transition >= FadeSeconds)
            {
                _advancing = false;
                _transition = -FadeSeconds;

                if (LevelIndex + 1 < Levels.All.Length)
                {
                    StartLevel(LevelIndex + 1, null);
                }
                else
                {
                    Finished = true;
                    ShowBanner(new Banner("THE CAVERNS GLOW AGAIN", "PRESS JUMP TO PLAY AGAIN", float.PositiveInfinity));
                }
            }

            return;
        }

        if (_transition < 0)
        {
            _transition = MathF.Min(0, _transition + dt);
        }

        World.Step(input);

        foreach (GameEventAt e in World.Events)
        {
            _events.Add(e);
            React(e);
        }

        if (World.Complete)
        {
            _advancing = true;
            _transition = 0;
        }

        Camera.Update(World, dt);
        AmbientParticles();
    }

    /// <summary>Restarts the current level from its start (the R key).</summary>
    public void RestartLevel() => StartLevel(LevelIndex, null);

    /// <summary>Clears <see cref="Bursts"/> and <see cref="Events"/> once the renderer and the sound have used them.</summary>
    public void TakeBursts()
    {
        _bursts.Clear();
        _events.Clear();
    }

    /// <summary>A deterministic random number in [0, 1).</summary>
    public float Random()
    {
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return (_random & 0xFFFFFF) / 16777216f;
    }

    private void StartLevel(int index, Banner? banner)
    {
        LevelIndex = index;
        World = new World(Levels.All[index]);
        Camera.Reset(World);
        ShowBanner(banner ?? new Banner(World.Level.Name.ToUpperInvariant(), "LIGHT EVERY LANTERN", 3.5f));
        LevelLoaded?.Invoke(World);
    }

    private void ShowBanner(Banner banner)
    {
        Banner = banner;
        _bannerTime = 0;
    }

    private void React(GameEventAt e)
    {
        Vector3 ember = new(1.0f, 0.55f, 0.2f), gold = new(1.0f, 0.85f, 0.45f), mint = new(0.35f, 1.0f, 0.8f), dust = new(0.35f, 0.3f, 0.45f);

        switch (e.Event)
        {
            case GameEvent.Jump:
                _bursts.Add(new ParticleBurst(e.Position, 4, dust, 40f, 0.3f, 0.6f, 60f, 0.2f));
                break;
            case GameEvent.Land:
                _bursts.Add(new ParticleBurst(e.Position, 6, dust, 55f, 0.35f, 0.6f, 80f, 0.4f));
                break;
            case GameEvent.Mote:
                _bursts.Add(new ParticleBurst(e.Position, 14, gold, 70f, 0.6f, 1.0f, -20f, 0f));
                break;
            case GameEvent.Lantern:
                _bursts.Add(new ParticleBurst(e.Position, 48, ember, 110f, 1.3f, 1.5f, -60f, 0.6f));
                ShowBanner(new Banner(World.LanternsLit == World.Lanterns.Count ? "THE GATE IS OPEN" : "LANTERN LIT", $"{World.LanternsLit} OF {World.Lanterns.Count}", 2.2f));
                break;
            case GameEvent.Stomp:
                _bursts.Add(new ParticleBurst(e.Position, 18, mint, 90f, 0.7f, 1.0f, 120f, 0.5f));
                break;
            case GameEvent.Hurt:
                _bursts.Add(new ParticleBurst(e.Position, 30, ember, 100f, 0.7f, 1.0f, 150f, 0.6f));
                break;
            case GameEvent.GateOpen:
                _bursts.Add(new ParticleBurst(World.Gate, 40, new Vector3(0.45f, 0.75f, 1.0f), 80f, 1.4f, 1.5f, -40f, 0.3f));
                break;
            case GameEvent.LevelComplete:
                _bursts.Add(new ParticleBurst(e.Position, 60, new Vector3(0.7f, 0.9f, 1.0f), 140f, 1.2f, 1.5f, -30f, 0.5f));
                break;
        }
    }

    /// <summary>The spirit's trail and the embers that lit lanterns shed.</summary>
    private void AmbientParticles()
    {
        Player p = World.Player;

        if (p.HurtTimer <= 0 && !Finished && _steps % 4 == 0)
        {
            Vector2 tip = new(p.Body.Center.X + ((Random() - 0.5f) * 4f), p.Body.Y + 1f);
            _bursts.Add(new ParticleBurst(tip, 1, new Vector3(1.0f, 0.6f, 0.25f) * (0.4f + (0.6f * p.Light)), 12f, 0.5f, 0.6f, -50f, 0.9f));
        }

        if (_steps % 9 == 0)
        {
            foreach (Lantern lantern in World.Lanterns)
            {
                if (lantern.Lit)
                {
                    _bursts.Add(new ParticleBurst(lantern.Position + new Vector2(0, -4), 1, new Vector3(1.0f, 0.65f, 0.3f), 14f, 1.2f, 0.6f, -35f, 1f));
                }
            }
        }
    }
}
