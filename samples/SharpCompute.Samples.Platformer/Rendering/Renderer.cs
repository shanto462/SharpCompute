// Original SharpCompute sample (MIT).
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using SharpCompute.Samples.Platformer.Game;

namespace SharpCompute.Samples.Platformer.Rendering;

/// <summary>
/// Draws a <see cref="GameSession"/> with SharpCompute. All per-frame data (sprites, lights, HUD
/// glyphs, new particles) is written straight into shared GPU buffers allocated once: unified
/// memory, no uploads and no per-frame allocations. Each frame is one <see cref="ComputeContext"/>:
/// step particles, render the scene at 480 × 270, and optionally upscale it into the window texture.
/// </summary>
public sealed class Renderer : IDisposable
{
    public const int Width = GameSession.ViewWidth;
    public const int Height = GameSession.ViewHeight;
    public const int MaxSprites = 96;
    public const int MaxLights = 48;
    public const int MaxParticles = 1536;
    public const int MaxGlyphs = 320;

    private readonly GraphicsDevice _device;
    private readonly ComputeContext _context;
    private readonly ReadWriteBuffer<uint> _frame;
    private readonly ReadOnlyBuffer<uint> _atlas;
    private readonly ReadOnlyBuffer<SpriteInstance> _sprites;
    private readonly ReadOnlyBuffer<LightSource> _lights;
    private readonly ReadWriteBuffer<Particle> _particles;
    private readonly ReadOnlyBuffer<Glyph> _glyphs;
    private readonly ReadOnlyBuffer<uint> _font;
    private ReadOnlyBuffer<uint>? _tiles;
    private Level? _tilesLevel;
    private int _spriteCount;
    private int _lightCount;
    private int _glyphCount;
    private int _nextParticle;
    private (int Lit, int Lanterns, int Motes, int Total) _hudCounts = (-1, -1, -1, -1);
    private string _lanternText = "";
    private string _moteText = "";

    public Renderer(GraphicsDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _context = device.CreateComputeContext();
        _frame = device.AllocateReadWriteBuffer<uint>(Width * Height);
        _atlas = device.AllocateReadOnlyBuffer<uint>(Art.BuildAtlas());
        _sprites = device.AllocateReadOnlyBuffer<SpriteInstance>(MaxSprites);
        _lights = device.AllocateReadOnlyBuffer<LightSource>(MaxLights);
        _particles = device.AllocateReadWriteBuffer<Particle>(MaxParticles);
        _glyphs = device.AllocateReadOnlyBuffer<Glyph>(MaxGlyphs);
        _font = device.AllocateReadOnlyBuffer<uint>(PixelFont.Masks);
    }

    /// <summary>The last rendered frame, 480 × 270 packed RGBA (R in the low byte).</summary>
    public ReadOnlySpan<uint> PackedFrame => _frame.AsReadOnlySpan();

    /// <summary>Number of particles alive, for the stats line and tests.</summary>
    public int LiveParticles
    {
        get
        {
            int live = 0;

            foreach (Particle p in _particles.AsReadOnlySpan())
            {
                live += p.Life > 0 ? 1 : 0;
            }

            return live;
        }
    }

    /// <summary>
    /// Renders <paramref name="session"/> after <paramref name="dt"/> seconds of simulation. When
    /// <paramref name="target"/> is given, the frame is also scaled up into it (whole-number scale,
    /// centered, black borders) in the same submission.
    /// </summary>
    public void Render(GameSession session, float dt, ReadWriteTexture2D<Rgba32>? target = null)
    {
        World world = session.World;
        EnsureTiles(world.Level);
        SpawnParticles(session);
        (int cameraX, int cameraY) = session.Camera.Snapped;
        float time = session.Time;

        _spriteCount = _lightCount = _glyphCount = 0;
        Span<SpriteInstance> sprites = _sprites.AsSpan();
        Span<LightSource> lights = _lights.AsSpan();

        // Back to front: gate, lanterns, motes, creatures, the spirit.
        bool gateOpen = world.GateOpen;
        AddSprite(sprites, world.Gate - new Vector2(8, 24), gateOpen ? Frame.GateOpenTop : Frame.GateClosedTop, false, cameraX, cameraY);
        AddSprite(sprites, world.Gate - new Vector2(8, 8), gateOpen ? Frame.GateOpenBottom : Frame.GateClosedBottom, false, cameraX, cameraY);

        if (gateOpen)
        {
            AddLight(lights, world.Gate - new Vector2(0, 8), 110f, 1.1f, new Vector3(0.45f, 0.7f, 1.0f), true, cameraX, cameraY);
        }

        foreach (Lantern lantern in world.Lanterns)
        {
            bool flicker = ((int)(time * 7f) + (int)lantern.Position.X) % 2 == 0;
            AddSprite(sprites, lantern.Position - new Vector2(8, 8), lantern.Lit ? (flicker ? Frame.LanternLit0 : Frame.LanternLit1) : Frame.LanternDark, false, cameraX, cameraY);

            if (lantern.Lit)
            {
                float flare = MathF.Max(0, 1f - lantern.LitTime) * 0.8f;
                float wobble = 1f + (0.05f * MathF.Sin((time * 9f) + lantern.Position.X));
                AddLight(lights, lantern.Position, (125f * wobble) + (flare * 80f), 1.15f + flare, new Vector3(1.0f, 0.62f, 0.28f), true, cameraX, cameraY);
            }
            else
            {
                AddLight(lights, lantern.Position, 22f, 0.25f, new Vector3(0.5f, 0.45f, 0.7f), false, cameraX, cameraY);
            }
        }

        foreach (Mote mote in world.Motes)
        {
            if (!mote.Collected)
            {
                Vector2 bob = new(0, MathF.Sin((time * 2.5f) + mote.Phase) * 2f);
                AddSprite(sprites, mote.Position + bob - new Vector2(8, 8), ((int)(time * 4f + mote.Phase) & 1) == 0 ? Frame.Mote0 : Frame.Mote1, false, cameraX, cameraY);
                AddLight(lights, mote.Position + bob, 26f, 0.7f, new Vector3(1.0f, 0.85f, 0.45f), false, cameraX, cameraY);
            }
        }

        foreach (Creature c in world.Creatures)
        {
            if (!c.Alive)
            {
                continue;
            }

            bool alternate = ((int)(c.Time * (c.Kind == SpawnKind.Moth ? 10f : 6f)) & 1) == 0;

            if (c.Kind == SpawnKind.Crawler)
            {
                AddSprite(sprites, new Vector2(c.Body.Center.X - 8, c.Body.Bottom - 15), alternate ? Frame.Crawler0 : Frame.Crawler1, c.Direction > 0, cameraX, cameraY);
                AddLight(lights, c.Body.Center, 20f, 0.45f, new Vector3(0.35f, 1.0f, 0.8f), false, cameraX, cameraY);
            }
            else
            {
                AddSprite(sprites, c.Body.Center - new Vector2(8, 7), alternate ? Frame.Moth0 : Frame.Moth1, c.Direction < 0, cameraX, cameraY);
                AddLight(lights, c.Body.Center, 18f, 0.4f, new Vector3(0.75f, 0.6f, 1.0f), false, cameraX, cameraY);
            }
        }

        Player p = world.Player;

        if (p.HurtTimer <= 0 && !session.Finished)
        {
            AddSprite(sprites, new Vector2(p.Body.Center.X - 8, p.Body.Bottom - 15), SpiritFrame(p, time), p.FacingLeft, cameraX, cameraY);
            float pulse = 1f + (0.04f * MathF.Sin(time * 5f));
            AddLight(lights, p.Body.Center + new Vector2(0, -2), p.LightRadius * pulse, 0.9f + (0.3f * p.Light), new Vector3(1.0f, 0.68f, 0.36f), true, cameraX, cameraY);
        }

        Hud(session);

        float ambient = world.GateOpen ? 0.14f : 0.1f;
        RenderScene scene = new(
            _frame, _tiles!, _atlas, _sprites, _lights, _particles, _glyphs, _font,
            Width, cameraX, cameraY, world.Level.Width, world.Level.Height,
            _spriteCount, _lightCount, _glyphCount, time, ambient, session.Fade);

        // One context for the renderer's lifetime: it keeps its command lists between frames, so
        // recording a frame allocates nothing once the lists have grown to fit.
        ComputeContext context = _context;

        try
        {
            context.For(MaxParticles, new StepParticles(_particles, dt));
            context.For(Width, Height, scene);

            if (target is not null)
            {
                int scale = Math.Max(1, Math.Min(target.Width / Width, target.Height / Height));
                context.ForEach(target, new UpscaleFrame(_frame, Width, Height, scale, (target.Width - (Width * scale)) / 2, (target.Height - (Height * scale)) / 2));
            }
        }
        finally
        {
            context.Execute();
        }
    }

    /// <summary>The last frame as <see cref="Rgba32"/> pixels, for PNG screenshots.</summary>
    public ReadOnlySpan<Rgba32> FramePixels => MemoryMarshal.Cast<uint, Rgba32>(_frame.AsReadOnlySpan());

    public void Dispose()
    {
        _context.Dispose();
        _frame.Dispose();
        _atlas.Dispose();
        _sprites.Dispose();
        _lights.Dispose();
        _particles.Dispose();
        _glyphs.Dispose();
        _font.Dispose();
        _tiles?.Dispose();
    }

    private static Frame SpiritFrame(Player p, float time)
    {
        if (!p.OnGround)
        {
            return p.Velocity.Y < 0 ? Frame.SpiritJump : Frame.SpiritFall;
        }

        if (p.RunTime > 0)
        {
            return (Frame)((int)Frame.SpiritRun0 + ((int)(p.RunTime * 12f) % 4));
        }

        return ((int)(time * 2.5f) & 1) == 0 ? Frame.SpiritIdle0 : Frame.SpiritIdle1;
    }

    /// <summary>Uploads a level's tiles when it changes: once per level, not per frame.</summary>
    private void EnsureTiles(Level level)
    {
        if (ReferenceEquals(level, _tilesLevel))
        {
            return;
        }

        _tiles?.Dispose();
        _tiles = _device.AllocateReadOnlyBuffer<uint>(level.Width * level.Height);
        Span<uint> tiles = _tiles.AsSpan();

        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = (uint)level.Tiles[i];
        }

        _tilesLevel = level;

        // A new level starts with no particles from the old one.
        _particles.AsSpan().Clear();
    }

    /// <summary>Writes the session's new bursts into the particle ring buffer, in place.</summary>
    private void SpawnParticles(GameSession session)
    {
        Span<Particle> particles = _particles.AsSpan();

        foreach (ParticleBurst burst in session.Bursts)
        {
            for (int i = 0; i < burst.Count; i++)
            {
                float angle = session.Random() * MathF.Tau;
                float speed = burst.Speed * (0.3f + (0.7f * session.Random()));
                Vector2 velocity = new(MathF.Cos(angle) * speed, (MathF.Sin(angle) * speed) - (burst.UpBias * burst.Speed));
                float life = burst.Life * (0.6f + (0.4f * session.Random()));

                particles[_nextParticle] = new Particle
                {
                    Position = new Float2(burst.Position.X, burst.Position.Y),
                    Velocity = new Float2(velocity.X, velocity.Y),
                    Color = new Float3(burst.Color.X, burst.Color.Y, burst.Color.Z),
                    Life = life,
                    MaxLife = life,
                    Size = burst.Size,
                    Gravity = burst.Gravity,
                    Drag = 1.5f,
                };

                _nextParticle = (_nextParticle + 1) % MaxParticles;
            }
        }
    }

    private void AddSprite(Span<SpriteInstance> sprites, Vector2 topLeft, Frame frame, bool flip, int cameraX, int cameraY)
    {
        int x = (int)MathF.Round(topLeft.X) - cameraX;
        int y = (int)MathF.Round(topLeft.Y) - cameraY;

        if (_spriteCount < MaxSprites && x > -16 && y > -16 && x < Width && y < Height)
        {
            sprites[_spriteCount++] = new SpriteInstance { X = x, Y = y, Frame = (int)frame, Flags = flip ? 1 : 0 };
        }
    }

    private void AddLight(Span<LightSource> lights, Vector2 position, float radius, float intensity, Vector3 color, bool shadows, int cameraX, int cameraY)
    {
        float x = position.X - cameraX;
        float y = position.Y - cameraY;

        if (_lightCount < MaxLights && x > -radius && y > -radius && x < Width + radius && y < Height + radius)
        {
            lights[_lightCount++] = new LightSource
            {
                Position = new Float2(x, y),
                Radius = radius,
                Intensity = intensity,
                Color = new Float3(color.X, color.Y, color.Z),
                Shadows = shadows ? 1f : 0f,
            };
        }
    }

    private void Hud(GameSession session)
    {
        Span<Glyph> glyphs = _glyphs.AsSpan();
        World world = session.World;
        const uint text = 0xFFE8DCF0u, dim = 0xFFB0A0C0u, warm = 0xFF6EC8FFu;

        if (!session.Finished)
        {
            // The counter strings are only rebuilt when a count changes, not every frame.
            (int, int, int, int) counts = (world.LanternsLit, world.Lanterns.Count, world.MotesCollected, world.Motes.Count);

            if (counts != _hudCounts)
            {
                _hudCounts = counts;
                _lanternText = $"LANTERNS {world.LanternsLit}/{world.Lanterns.Count}";
                _moteText = $"MOTES {world.MotesCollected}/{world.Motes.Count}";
            }

            Text(glyphs, _lanternText, 6, 6, world.GateOpen ? warm : text);
            Text(glyphs, _moteText, Width - 6 - (_moteText.Length * 4), 6, text);
        }

        if (session.Banner is { } banner)
        {
            float t = session.BannerTime;
            bool visible = float.IsInfinity(banner.Seconds) || t < banner.Seconds - 0.3f || ((int)(t * 10) & 1) == 0;

            if (visible)
            {
                int y = session.Finished ? (Height / 2) - 8 : 54;
                Text(glyphs, banner.Title, (Width - (banner.Title.Length * 8)) / 2, y, warm, scale: 2);
                Text(glyphs, banner.Subtitle, (Width - (banner.Subtitle.Length * 4)) / 2, y + 16, dim);
            }
        }
    }

    /// <summary>Adds a line of text with a drop shadow; each character advances four pixels times <paramref name="scale"/>.</summary>
    private void Text(Span<Glyph> glyphs, string text, int x, int y, uint color, int scale = 1)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            uint c = pass == 0 ? 0xFF1A0E14u : color;
            int offset = pass == 0 ? scale : 0;

            for (int i = 0; i < text.Length && _glyphCount < MaxGlyphs; i++)
            {
                int index = PixelFont.Index(text[i]);

                if (index != 0)
                {
                    glyphs[_glyphCount++] = new Glyph { X = x + (i * 4 * scale) + offset, Y = y + offset, Index = index, Color = c, Scale = scale };
                }
            }
        }
    }
}
