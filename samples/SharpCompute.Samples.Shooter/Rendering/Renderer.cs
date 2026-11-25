// Original SharpCompute sample (MIT).
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpCompute.Samples.Shooter.Game;

namespace SharpCompute.Samples.Shooter.Rendering;

/// <summary>
/// Draws an <see cref="Arena"/> with SharpCompute. Every buffer is allocated once; per frame the
/// CPU writes the new particles, the vector primitives, the distortion ripples and the HUD glyphs
/// straight into shared (unified-memory) buffers, then one <see cref="ComputeContext"/> runs the
/// whole frame with a single commit:
/// <list type="number">
/// <item>step the particles, bound each chunk of 64, bin chunks and primitives into 16 × 16 tiles;</item>
/// <item>draw the scene in HDR at 640 × 360 (nebula, walls, primitives, particles);</item>
/// <item>bloom: a thresholded half-resolution copy, blurred, then a quarter-resolution copy, blurred;</item>
/// <item>composite: shockwave distortion, bloom, tone mapping, HUD;</item>
/// <item>optionally, a whole-number upscale into the window's texture.</item>
/// </list>
/// </summary>
/// <remarks>Not thread-safe: render from one thread. <see cref="Render"/> returns when the GPU has finished the frame.</remarks>
public sealed class Renderer : IDisposable
{
    public const int Width = Tuning.Width;
    public const int Height = Tuning.Height;

    /// <summary>Particle capacity: a ring buffer, so the oldest particles are replaced when it is full.</summary>
    public const int MaxParticles = 65536;
    public const int ChunkSize = 64;
    public const int ChunkCount = MaxParticles / ChunkSize;
    public const int TileSize = 16;
    public const int TilesX = (Width + TileSize - 1) / TileSize;
    public const int TilesY = (Height + TileSize - 1) / TileSize;

    /// <summary>Per-tile list capacities. Beyond them a tile drops the extra (newest) chunks or primitives.</summary>
    public const int MaxChunksPerTile = 128;
    public const int MaxPrimsPerTile = 64;

    public const int MaxPrims = 2048;
    public const int MaxGlyphs = 480;
    public const int MaxRipples = 16;

    private const float BloomThreshold = 0.3f;

    private readonly GraphicsDevice _device;
    private readonly ComputeContext _context;
    private readonly ReadWriteBuffer<uint> _frame;
    private readonly ReadWriteBuffer<Float4> _hdr;
    private readonly ReadWriteBuffer<Float4> _halfA;
    private readonly ReadWriteBuffer<Float4> _halfB;
    private readonly ReadWriteBuffer<Float4> _quarterA;
    private readonly ReadWriteBuffer<Float4> _quarterB;
    private readonly ReadWriteBuffer<Particle> _particles;
    private readonly ReadWriteBuffer<Float4> _chunkBounds;
    private readonly ReadWriteBuffer<int> _tileChunks;
    private readonly ReadWriteBuffer<int> _tilePrims;
    private readonly ReadWriteBuffer<int> _tileCounts;
    private readonly ReadOnlyBuffer<Prim> _prims;
    private readonly ReadOnlyBuffer<Ripple> _ripples;
    private readonly ReadOnlyBuffer<Glyph> _glyphs;
    private readonly ReadOnlyBuffer<uint> _font;
    private readonly Rng _rng = new(0x5EED5u);
    private int _nextParticle;
    private int _primCount;
    private int _rippleCount;
    private int _glyphCount;
    private long _frameIndex;
    private Vector3 _flash;
    private float _shake;
    private float _energy;
    private (int Score, int Multiplier, int Wave, int Lives, int Bombs) _hud = (-1, -1, -1, -1, -1);
    private string _scoreText = "";
    private string _multiplierText = "";
    private string _waveText = "";
    private string _livesText = "";
    private string _bombsText = "";

    public Renderer(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _context = device.CreateComputeContext();
        _frame = device.AllocateReadWriteBuffer<uint>(Width * Height);
        _hdr = device.AllocateReadWriteBuffer<Float4>(Width * Height);
        _halfA = device.AllocateReadWriteBuffer<Float4>(Width / 2 * (Height / 2));
        _halfB = device.AllocateReadWriteBuffer<Float4>(Width / 2 * (Height / 2));
        _quarterA = device.AllocateReadWriteBuffer<Float4>(Width / 4 * (Height / 4));
        _quarterB = device.AllocateReadWriteBuffer<Float4>(Width / 4 * (Height / 4));
        _particles = device.AllocateReadWriteBuffer<Particle>(MaxParticles);
        _chunkBounds = device.AllocateReadWriteBuffer<Float4>(ChunkCount);
        _tileChunks = device.AllocateReadWriteBuffer<int>(TilesX * TilesY * MaxChunksPerTile);
        _tilePrims = device.AllocateReadWriteBuffer<int>(TilesX * TilesY * MaxPrimsPerTile);
        _tileCounts = device.AllocateReadWriteBuffer<int>(TilesX * TilesY * 2);
        _prims = device.AllocateReadOnlyBuffer<Prim>(MaxPrims);
        _ripples = device.AllocateReadOnlyBuffer<Ripple>(MaxRipples);
        _glyphs = device.AllocateReadOnlyBuffer<Glyph>(MaxGlyphs);
        _font = device.AllocateReadOnlyBuffer<uint>(PixelFont.Masks);
    }

    /// <summary>Bloom intensity; 0 turns it off (the passes still run).</summary>
    public float BloomStrength { get; set; } = 1f;

    /// <summary>Tone-mapping exposure.</summary>
    public float Exposure { get; set; } = 1.35f;

    /// <summary>The last frame, 640 × 360 packed RGBA (R in the low byte).</summary>
    public ReadOnlySpan<uint> PackedFrame => _frame.AsReadOnlySpan();

    /// <summary>The last frame as <see cref="Rgba32"/> pixels, for PNG screenshots.</summary>
    public ReadOnlySpan<Rgba32> FramePixels => MemoryMarshal.Cast<uint, Rgba32>(_frame.AsReadOnlySpan());

    /// <summary>Primitives drawn in the last frame.</summary>
    public int PrimCount => _primCount;

    /// <summary>Particles alive right now (reads the GPU buffer; for stats and tests).</summary>
    public int LiveParticles
    {
        get
        {
            int live = 0;

            foreach (Particle p in _particles.AsReadOnlySpan())
            {
                live += p.Life > 0f ? 1 : 0;
            }

            return live;
        }
    }

    /// <summary>The particle buffer itself, for tests.</summary>
    public ReadOnlySpan<Particle> Particles => _particles.AsReadOnlySpan();

    /// <summary>
    /// How the native frame sits in a window texture of the given size: the largest whole-number
    /// scale that fits (at least 1) and the top-left offset that centers it.
    /// </summary>
    public static (int Scale, int OriginX, int OriginY) Layout(int targetWidth, int targetHeight)
    {
        int scale = Math.Max(1, Math.Min(targetWidth / Width, targetHeight / Height));
        return (scale, (targetWidth - (Width * scale)) / 2, (targetHeight - (Height * scale)) / 2);
    }

    /// <summary>Converts a point in window-texture pixels (e.g. the mouse) to arena pixels, for the <see cref="Layout"/> of that texture.</summary>
    public static Vector2 WindowToArena(double x, double y, int targetWidth, int targetHeight)
    {
        (int scale, int originX, int originY) = Layout(targetWidth, targetHeight);
        return new Vector2((float)((x - originX) / scale), (float)((y - originY) / scale));
    }

    /// <summary>
    /// Renders <paramref name="arena"/> after <paramref name="dt"/> seconds of simulation (particles
    /// advance by that much). Spawns the arena's pending <see cref="Arena.Bursts"/> but does not clear
    /// them: call <see cref="Arena.EndFrame"/> after the sound has seen the events too. When
    /// <paramref name="target"/> is given, the frame is also scaled into it in the same submission.
    /// </summary>
    public void Render(Arena arena, float dt, ReadWriteTexture2D<Rgba32>? target = null, bool paused = false)
    {
        ArgumentNullException.ThrowIfNull(arena);
        _frameIndex++;

        React(arena, dt);

        foreach (ParticleBurst burst in arena.Bursts)
        {
            Spawn(burst);
        }

        _primCount = 0;
        BuildPrims(arena);
        BuildRipples(arena);
        BuildHud(arena, paused);

        float shakeX = (Hash01(_frameIndex, 1) - 0.5f) * 2f * _shake;
        float shakeY = (Hash01(_frameIndex, 2) - 0.5f) * 2f * _shake;
        Vector2 parallax = arena.Player.Position - Tuning.Center;

        // One context for the renderer's lifetime: it keeps its command lists between frames, so
        // recording a frame allocates nothing once the lists have grown to fit.
        ComputeContext context = _context;

        try
        {
            context.For(MaxParticles, new StepParticles(_particles, paused ? 0f : dt, Tuning.Wall, Tuning.Wall, Width - Tuning.Wall, Height - Tuning.Wall));
            context.For(ChunkCount, new BoundParticleChunks(_particles, _chunkBounds));
            context.For(TilesX, TilesY, new BinTiles(_chunkBounds, ChunkCount, _prims, _primCount, _tileChunks, _tilePrims, _tileCounts, TilesX, MaxChunksPerTile, MaxPrimsPerTile));
            context.For(Width, Height, new DrawScene(
                _hdr, _prims, _particles, _tileChunks, _tilePrims, _tileCounts,
                Width, Height, TilesX, MaxChunksPerTile, MaxPrimsPerTile,
                arena.Time, _energy, -parallax.X, -parallax.Y, Tuning.Wall));

            context.For(Width / 2, Height / 2, new BloomPrefilter(_hdr, _halfA, Width, Width / 2, BloomThreshold));
            context.For(Width / 2, Height / 2, new Blur(_halfA, _halfB, Width / 2, Height / 2, 1, 0));
            context.For(Width / 2, Height / 2, new Blur(_halfB, _halfA, Width / 2, Height / 2, 0, 1));
            context.For(Width / 4, Height / 4, new Downsample(_halfA, _quarterA, Width / 2, Width / 4));
            context.For(Width / 4, Height / 4, new Blur(_quarterA, _quarterB, Width / 4, Height / 4, 1, 0));
            context.For(Width / 4, Height / 4, new Blur(_quarterB, _quarterA, Width / 4, Height / 4, 0, 1));

            context.For(Width, Height, new Composite(
                _hdr, _halfA, _quarterA, _ripples, _rippleCount, _glyphs, _glyphCount, _font, _frame,
                Width, Height, BloomStrength, Exposure, _flash.X, _flash.Y, _flash.Z, shakeX, shakeY));

            if (target is not null)
            {
                (int scale, int originX, int originY) = Layout(target.Width, target.Height);
                context.ForEach(target, new UpscaleFrame(_frame, Width, Height, scale, originX, originY));
            }
        }
        finally
        {
            context.Execute();
        }
    }

    /// <summary>
    /// Writes a burst into the particle ring buffer, in place (the buffer is shared memory, so this
    /// is the upload). Uses the renderer's own generator, so drawing never changes the simulation.
    /// </summary>
    /// <remarks>
    /// The burst is stratified: its particles are written in order of (speed band, angle sector), so
    /// each chunk of 64 consecutive slots flies out as one compact wedge instead of 64 random
    /// directions across the whole explosion. That keeps chunk bounds small, and with them the number
    /// of chunks each tile has to gather (about an eighth of the unsorted cost for a large explosion).
    /// </remarks>
    public void Spawn(in ParticleBurst burst)
    {
        Span<Particle> particles = _particles.AsSpan();
        bool radial = burst.Direction == Vector2.Zero;
        Vector2 direction = radial ? Vector2.UnitX : Vector2.Normalize(burst.Direction);
        int count = Math.Min(burst.Count, MaxParticles);
        int chunks = Math.Max(1, (count + ChunkSize - 1) / ChunkSize);
        int bands = burst.SpeedJitter < 0.2f ? 1 : Math.Max(1, (int)MathF.Round(MathF.Sqrt(chunks / 3f)));
        int sectors = Math.Max(1, (chunks + bands - 1) / bands);
        int cells = bands * sectors;

        for (int i = 0; i < count; i++)
        {
            int cell = (int)((long)i * cells / count);
            float a = ((cell % sectors) + _rng.NextFloat()) / sectors;
            float u = ((cell / sectors) + _rng.NextFloat()) / bands;
            float angle = radial ? a * MathF.Tau : ((a * 2f) - 1f) * burst.Spread;
            Vector2 heading = radial ? new Vector2(MathF.Cos(angle), MathF.Sin(angle)) : Arena.Rotate(direction, angle);
            float speed = burst.Speed * (1f - (burst.SpeedJitter * u));
            float life = burst.Life * (0.6f + (0.4f * _rng.NextFloat()));

            particles[_nextParticle] = new Particle
            {
                Position = new Float2(burst.Position.X, burst.Position.Y),
                Velocity = new Float2(heading.X * speed, heading.Y * speed),
                Color = new Float3(burst.Color.X, burst.Color.Y, burst.Color.Z),
                Life = life,
                MaxLife = life,
                Size = burst.Size * (0.7f + (0.6f * _rng.NextFloat())),
                Drag = burst.Drag,
                Streak = burst.Streak,
            };

            _nextParticle = (_nextParticle + 1) % MaxParticles;
        }
    }

    /// <summary>Kills every particle (a fresh game).</summary>
    public void ClearParticles()
    {
        _particles.AsSpan().Clear();
        _nextParticle = 0;
    }

    public void Dispose()
    {
        _context.Dispose();
        _frame.Dispose();
        _hdr.Dispose();
        _halfA.Dispose();
        _halfB.Dispose();
        _quarterA.Dispose();
        _quarterB.Dispose();
        _particles.Dispose();
        _chunkBounds.Dispose();
        _tileChunks.Dispose();
        _tilePrims.Dispose();
        _tileCounts.Dispose();
        _prims.Dispose();
        _ripples.Dispose();
        _glyphs.Dispose();
        _font.Dispose();
    }

    /// <summary>Screen flash, shake and the background's "energy" react to what just happened.</summary>
    private void React(Arena arena, float dt)
    {
        _flash *= MathF.Exp(-6f * dt);
        _shake *= MathF.Exp(-8f * dt);
        _energy *= MathF.Exp(-1.5f * dt);

        foreach (GameEventAt e in arena.Events)
        {
            switch (e.Event)
            {
                case GameEvent.Kill:
                    _energy = MathF.Min(_energy + 0.12f, 1.5f);
                    _shake = MathF.Max(_shake, 1f);
                    break;
                case GameEvent.Bomb:
                    _flash = new Vector3(0.25f, 0.32f, 0.45f);
                    _shake = MathF.Max(_shake, 5f);
                    _energy = 1.5f;
                    break;
                case GameEvent.Death:
                    _flash = new Vector3(0.4f, 0.12f, 0.2f);
                    _shake = MathF.Max(_shake, 6f);
                    break;
                case GameEvent.WaveStart:
                    _energy = MathF.Max(_energy, 0.8f);
                    break;
            }
        }
    }

    private void BuildPrims(Arena arena)
    {
        float t = arena.Time;

        foreach (Ring r in arena.Rings)
        {
            float fade = r.Harmful ? 1f : Math.Clamp((r.MaxRadius - r.Radius) / 12f, 0f, 1f);
            Circle(r.Center, r.Radius, Palette.Pylon * (1.1f * fade), 1.1f, 0.8f, 0.22f);
        }

        foreach (SpawnMarker m in arena.Markers)
        {
            float k = m.Progress;
            Vector3 c = Palette.Of(m.Kind) * (0.3f + (0.7f * k));
            float radius = 8f + (28f * (1f - k));
            Circle(m.Position, radius, c, 0.5f, 0.5f, 0.3f);

            for (int i = 0; i < 4; i++)
            {
                Vector2 d = Arena.Rotate(Vector2.UnitX, (k * 3f) + (i * MathF.PI / 2f));
                Line(m.Position + (d * (radius + 3f)), m.Position + (d * (radius + 7f)), c, 0.5f, 0.4f, 0.4f);
            }
        }

        foreach (Enemy e in arena.Enemies)
        {
            if (e.Alive)
            {
                DrawEnemy(e, t);
            }
        }

        foreach (Projectile p in arena.Pulses)
        {
            Disc(p.Position, 2.2f, Palette.Pulse * 1.2f, 0.8f, 0.3f);
            Circle(p.Position, 4.5f + (MathF.Sin((t * 12f) + p.Position.X) * 0.6f), Palette.Pulse * 0.7f, 0.4f, 0.3f, 0.4f);
        }

        foreach (Projectile b in arena.Bullets)
        {
            Vector2 back = Vector2.Normalize(b.Velocity) * 7f;
            Line(b.Position, b.Position - back, Palette.Bullet, 0.8f, 0.7f, 0.35f);
        }

        DrawShip(arena.Player, t);

        foreach (Shockwave w in arena.Shockwaves)
        {
            float fade = 1f - w.Progress;

            if (w.Lethal)
            {
                Circle(w.Center, w.Radius, Palette.Bomb * (1.2f * fade), 1.5f, 0.6f, 0.2f);
            }
            else
            {
                Circle(w.Center, w.Radius, Palette.White * (0.35f * fade * fade), 0.6f, 0.4f, 0.3f);
            }
        }
    }

    private void DrawShip(Player p, float t)
    {
        if (!p.Alive || (p.Invulnerable > 0f && ((int)(p.Invulnerable * 12f) & 1) == 1))
        {
            return;
        }

        Vector3 c = Palette.Ship;
        Polygon(Shapes.Prism, p.Position, p.Heading, c, 0.6f, 0.35f, 0.4f, scale: 1.2f);
        Line(Transform(new Vector2(-3f, 0f), p.Position, p.Heading), Transform(new Vector2(12f, 0f), p.Position, p.Heading), c * 0.45f, 0.35f, 0.2f, 0.5f);
        float thrust = Math.Clamp(p.Velocity.Length() / Tuning.PlayerSpeed, 0f, 1f);
        Disc(Transform(new Vector2(-5f, 0f), p.Position, p.Heading), 0.8f + thrust, c * (0.3f + (0.5f * thrust)), 0.4f, 0.4f);
        Disc(p.Position + (p.Aim * 20f), 0.7f, c * 0.6f, 0.4f, 0.5f);

        if (p.Invulnerable > 0f)
        {
            Circle(p.Position, 12f, c * (0.35f + (0.15f * MathF.Sin(t * 20f))), 0.5f, 0.4f, 0.3f);
        }
    }

    private void DrawEnemy(Enemy e, float t)
    {
        Vector3 c = Palette.Of(e.Kind);

        if (e.Flash > 0f)
        {
            c = Vector3.Lerp(c, Palette.White * 1.5f, 0.7f);
        }

        Vector2 spin = new(MathF.Cos(e.Angle), MathF.Sin(e.Angle));

        switch (e.Kind)
        {
            case EnemyKind.Drifter:
                Circle(e.Position, e.Radius, c, 0.8f, 0.6f, 0.25f);
                Circle(e.Position, e.Radius * 0.45f, c * 0.8f, 0.5f, 0.4f, 0.35f);
                Line(e.Position + (spin * (e.Radius * 0.45f)), e.Position + (spin * (e.Radius * 0.9f)), c * 0.9f, 0.5f, 0.3f, 0.4f);
                Line(e.Position - (spin * (e.Radius * 0.45f)), e.Position - (spin * (e.Radius * 0.9f)), c * 0.9f, 0.5f, 0.3f, 0.4f);
                break;

            case EnemyKind.Lancer:
                Polygon(Shapes.Lance, e.Position, e.Heading, c * 1.1f, 0.6f, 0.6f, 0.3f);
                Line(Transform(new Vector2(-9f, 0f), e.Position, e.Heading), Transform(new Vector2(-13f, -5f), e.Position, e.Heading), c, 0.5f, 0.4f, 0.4f);
                Line(Transform(new Vector2(-9f, 0f), e.Position, e.Heading), Transform(new Vector2(-13f, 5f), e.Position, e.Heading), c, 0.5f, 0.4f, 0.4f);
                Disc(e.Position, 1.5f + (e.Charge * 2f), c * (0.6f + e.Charge), 0.5f + e.Charge, 0.3f);

                if (e.Phase == LancerPhase.Aim)
                {
                    // The sight line: dim while tracking, bright and steady once locked.
                    bool locked = e.Charge > 0.68f;
                    float k = e.Charge * e.Charge;
                    Vector3 sight = locked ? Vector3.Lerp(c, Palette.White, 0.4f) : c;
                    Line(e.Position + (e.Heading * 13f), e.Position + (e.Heading * 320f), sight * (0.25f + (0.75f * k)), 0.25f, 0.6f * k, 0.5f);
                }

                break;

            case EnemyKind.Weaver:
                for (int tri = 0; tri < 2; tri++)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float a0 = e.Angle + (tri * MathF.PI / 3f) + (i * MathF.Tau / 3f);
                        float a1 = a0 + (MathF.Tau / 3f);
                        Line(e.Position + (new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * 9f), e.Position + (new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * 9f), c * (tri == 0 ? 1.1f : 0.7f), 0.55f, 0.5f, 0.3f);
                    }
                }

                Disc(e.Position, 1.6f, c, 0.6f, 0.35f);
                break;

            case EnemyKind.Pylon:
                Polygon(Shapes.Pylon, e.Position, Vector2.UnitX, c * 1.1f, 0.8f, 0.6f, 0.25f);
                Polygon(Shapes.Pylon, e.Position, spin, c * 0.5f, 0.4f, 0.3f, 0.4f, scale: 0.45f);
                Disc(e.Position, 1.5f + (e.Charge * 2f), c * (0.4f + (0.6f * e.Charge)), 0.25f + (0.5f * e.Charge), 0.35f);
                Circle(e.Position, e.Radius + 6f + ((1f - e.Charge) * 12f), c * (0.5f * e.Charge * e.Charge), 0.5f, 0.4f, 0.35f);
                break;
        }
    }

    private void BuildRipples(Arena arena)
    {
        Span<Ripple> ripples = _ripples.AsSpan();
        _rippleCount = 0;

        foreach (Shockwave w in arena.Shockwaves)
        {
            if (_rippleCount == MaxRipples)
            {
                break;
            }

            float fade = 1f - w.Progress;
            ripples[_rippleCount++] = new Ripple
            {
                Center = new Float2(w.Center.X, w.Center.Y),
                Radius = w.Radius,
                Width = w.Lethal ? 22f : 10f,
                Strength = w.Strength * fade,
            };
        }
    }

    private void BuildHud(Arena arena, bool paused)
    {
        _glyphCount = 0;
        Vector3 text = new(0.85f, 0.95f, 1f), dim = new(0.45f, 0.55f, 0.7f);

        (int, int, int, int, int) hud = (arena.Score, arena.Multiplier, arena.Wave, arena.Lives, arena.Bombs);

        // The strings are only rebuilt when a number changes, not every frame.
        if (hud != _hud)
        {
            _hud = hud;
            _scoreText = arena.Score.ToString("D7", System.Globalization.CultureInfo.InvariantCulture);
            _multiplierText = $"X{arena.Multiplier}";
            _waveText = $"WAVE {arena.Wave}";
            _livesText = $"SHIPS {arena.Lives}";
            _bombsText = $"BOMBS {arena.Bombs}";
        }

        Text("SCORE", 18, 16, dim, 2);
        Text(_scoreText, 18 + (6 * 8), 16, text, 2);
        Vector3 multiplierColor = Vector3.Lerp(new Vector3(0.6f, 0.8f, 1f), new Vector3(1f, 0.55f, 0.9f), (arena.Multiplier - 1) / (float)(Tuning.MaxMultiplier - 1));
        Text(_multiplierText, 18 + (14 * 8), 16, multiplierColor, 2);
        Text(_waveText, Width - 18 - (_waveText.Length * 8), 16, text, 2);
        Text(_livesText, 18, Height - 28, text, 2);
        Text(_bombsText, Width - 18 - (_bombsText.Length * 8), Height - 28, text, 2);

        if (paused)
        {
            Centered("PAUSED", 140, new Vector3(1f, 1f, 1f), 4);
            Centered("P TO RESUME", 170, dim, 2);
        }
        else if (arena.Banner is { } banner)
        {
            float age = arena.BannerTime;
            bool visible = float.IsInfinity(banner.Seconds) || age < banner.Seconds - 0.4f || ((int)(age * 10f) & 1) == 0;

            if (visible)
            {
                Centered(banner.Title, 130, new Vector3(1f, 0.85f, 0.95f), 4);
                Centered(banner.Subtitle, 162, dim, 2);
            }
        }
    }

    private void Centered(string s, int y, Vector3 color, int scale) => Text(s, (Width - (s.Length * 4 * scale) + scale) / 2, y, color, scale);

    /// <summary>A line of text with a dark drop shadow; each character advances four pixels times <paramref name="scale"/>.</summary>
    private void Text(string s, int x, int y, Vector3 color, int scale)
    {
        Span<Glyph> glyphs = _glyphs.AsSpan();

        for (int pass = 0; pass < 2; pass++)
        {
            Vector3 c = pass == 0 ? new Vector3(0.02f, 0.02f, 0.06f) : color;
            int shift = pass == 0 ? Math.Max(1, scale / 2) : 0;

            for (int i = 0; i < s.Length && _glyphCount < MaxGlyphs; i++)
            {
                int index = PixelFont.Index(s[i]);

                if (index != 0)
                {
                    glyphs[_glyphCount++] = new Glyph { X = x + (i * 4 * scale) + shift, Y = y + shift, Index = index, Scale = scale, Color = new Float3(c.X, c.Y, c.Z) };
                }
            }
        }
    }

    private void Line(Vector2 a, Vector2 b, Vector3 color, float width, float glow, float falloff) =>
        Add(0, a, b, 0f, color, width, glow, falloff);

    private void Circle(Vector2 center, float radius, Vector3 color, float width, float glow, float falloff) =>
        Add(1, center, center, radius, color, width, glow, falloff);

    private void Disc(Vector2 center, float radius, Vector3 color, float glow, float falloff) =>
        Add(2, center, center, radius, color, 0f, glow, falloff);

    /// <summary>A closed outline of <paramref name="points"/> (x forward, y to the right of <paramref name="forward"/>).</summary>
    private void Polygon(ReadOnlySpan<Vector2> points, Vector2 center, Vector2 forward, Vector3 color, float width, float glow, float falloff, float scale = 1f)
    {
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = Transform(points[i] * scale, center, forward);
            Vector2 b = Transform(points[(i + 1) % points.Length] * scale, center, forward);
            Line(a, b, color, width, glow, falloff);
        }
    }

    private static Vector2 Transform(Vector2 local, Vector2 center, Vector2 forward) =>
        center + (forward * local.X) + (new Vector2(-forward.Y, forward.X) * local.Y);

    private void Add(int kind, Vector2 a, Vector2 b, float radius, Vector3 color, float width, float glow, float falloff)
    {
        float peak = MathF.Max(color.X, MathF.Max(color.Y, color.Z));

        if (_primCount == MaxPrims || peak <= 0.002f)
        {
            return;
        }

        // How far the halo stays above 1/256: the primitive's bounds for tile binning.
        float reach = width + 1.5f + (glow * peak > 0.004f ? MathF.Log(glow * peak / 0.004f) / falloff : 0f);
        float extent = (kind == 0 ? 0f : radius) + reach;
        Vector4 bounds = new(MathF.Min(a.X, b.X) - extent, MathF.Min(a.Y, b.Y) - extent, MathF.Max(a.X, b.X) + extent, MathF.Max(a.Y, b.Y) + extent);

        if (bounds.X > Width || bounds.Y > Height || bounds.Z < 0f || bounds.W < 0f)
        {
            return;
        }

        _prims.AsSpan()[_primCount++] = new Prim
        {
            A = new Float2(a.X, a.Y),
            B = new Float2(b.X, b.Y),
            Color = new Float3(color.X, color.Y, color.Z),
            Width = width,
            Radius = radius,
            Glow = glow,
            Falloff = falloff,
            Kind = kind,
            Bounds = new Float4(bounds.X, bounds.Y, bounds.Z, bounds.W),
        };
    }

    private static float Hash01(long frame, uint salt)
    {
        uint h = ((uint)frame * 2654435761u) ^ (salt * 2246822519u);
        h = (h ^ (h >> 15)) * 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / 16777216f;
    }

    /// <summary>Outlines in local coordinates: x forward, y to the right.</summary>
    private static class Shapes
    {
        /// <summary>The ship: a prism with a notched tail.</summary>
        public static readonly Vector2[] Prism = [new(10f, 0f), new(-6f, -6.5f), new(-2.5f, 0f), new(-6f, 6.5f)];

        /// <summary>A Lancer: a long, thin needle.</summary>
        public static readonly Vector2[] Lance = [new(12f, 0f), new(0f, -4.5f), new(-9f, 0f), new(0f, 4.5f)];

        /// <summary>A Pylon: a tall diamond (drawn upright).</summary>
        public static readonly Vector2[] Pylon = [new(0f, -15f), new(8f, 0f), new(0f, 15f), new(-8f, 0f)];
    }
}
