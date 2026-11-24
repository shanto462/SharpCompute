using System;
using System.Linq;
using System.Numerics;
using SharpCompute.Samples.Audio;
using SharpCompute.Samples.Shooter;
using SharpCompute.Samples.Shooter.Game;
using SharpCompute.Samples.Shooter.Rendering;

namespace SharpCompute.Tests.Shooter;

/// <summary>Pulsewake on the GPU: the scene, bloom, particles, the upscale, and the generated audio.</summary>
public sealed class PulsewakeRenderTests
{
    [Fact]
    public void Font_CoversTheHudCharacters()
    {
        Assert.Equal(0u, PixelFont.Masks[PixelFont.Index(' ')]);

        foreach (char c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:-/.!+")
        {
            Assert.NotEqual(0, PixelFont.Index(c));
            Assert.NotEqual(0u, PixelFont.Masks[PixelFont.Index(c)]);
            Assert.True(PixelFont.Masks[PixelFont.Index(c)] < 1u << 15);
        }

        Assert.Equal(PixelFont.Index('X'), PixelFont.Index('x'));
        Assert.Equal(0, PixelFont.Index('§'));
        Assert.Equal(PixelFont.Masks.Length, PixelFont.Masks.Distinct().Count());
    }

    [Theory]
    [InlineData(640, 360, 1, 0, 0)]
    [InlineData(2560, 1440, 4, 0, 0)]
    [InlineData(1300, 740, 2, 10, 10)]
    [InlineData(2000, 1200, 3, 40, 60)]
    [InlineData(320, 200, 1, -160, -80)]
    public void Layout_UsesTheLargestWholeScale_Centered(int width, int height, int scale, int originX, int originY)
    {
        Assert.Equal((scale, originX, originY), Renderer.Layout(width, height));

        // The center of the window maps to the center of the arena, and pixel blocks map back exactly.
        Vector2 center = Renderer.WindowToArena(width / 2.0, height / 2.0, width, height);
        Assert.Equal(Tuning.Width / 2f, center.X, 0.5f + (1f / scale));
        Assert.Equal(new Vector2(10f, 20f), Renderer.WindowToArena(originX + (10 * scale), originY + (20 * scale), width, height));
    }

    [MetalFact]
    public void Frame_IsNotBlack_AndTheShipGlows()
    {
        using Renderer renderer = new(GraphicsDevice.GetDefault());
        Arena arena = new(1);
        Pilot pilot = new();

        for (int i = 0; i < 400; i++)
        {
            arena.Step(pilot.Next(arena));

            if (i % 2 == 1)
            {
                renderer.Render(arena, 2 * Tuning.StepSeconds);
                arena.EndFrame();
            }
        }

        ReadOnlySpan<uint> frame = renderer.PackedFrame;
        double mean = 0;

        foreach (uint p in frame)
        {
            mean += Luma(p);
            Assert.Equal(0xFFu, p >> 24);
        }

        mean /= frame.Length;
        Assert.InRange(mean, 0.02, 0.6);

        Vector2 ship = arena.Player.Position;
        float peak = 0f;

        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                peak = MathF.Max(peak, Luma(frame[(((int)ship.Y + dy) * Renderer.Width) + (int)ship.X + dx]));
            }
        }

        Assert.True(peak > 0.5f, $"The ship glows (peak luma {peak}).");
        Assert.True(renderer.PrimCount > 5);
        Assert.True(renderer.LiveParticles > 0);
    }

    [MetalFact]
    public void Bloom_BrightensAroundBrightShapes_AndLeavesDarkAreasAlone()
    {
        using Renderer renderer = new(GraphicsDevice.GetDefault());
        Arena arena = new(1) { WavesEnabled = false };
        arena.Step(InputState.None);
        arena.EndFrame();

        renderer.BloomStrength = 0f;
        renderer.Render(arena, 0f);
        uint[] plain = renderer.PackedFrame.ToArray();

        renderer.BloomStrength = 1f;
        renderer.Render(arena, 0f);
        uint[] bloomed = renderer.PackedFrame.ToArray();

        // The ship at the center: its halo is brighter with bloom, a quiet patch far away is not.
        int cx = (int)arena.Player.Position.X, cy = (int)arena.Player.Position.Y;
        float gained = 0f;

        foreach ((int dx, int dy) in new[] { (0, 12), (0, -12), (14, 0), (-14, 0), (10, 10) })
        {
            int i = ((cy + dy) * Renderer.Width) + cx + dx;
            gained += Luma(bloomed[i]) - Luma(plain[i]);
        }

        Assert.True(gained / 5f > 0.01f, $"Bloom adds light around the ship (mean gain {gained / 5f}).");

        int far = (300 * Renderer.Width) + 120;
        Assert.InRange(Luma(bloomed[far]) - Luma(plain[far]), -0.002f, 0.01f);
    }

    [MetalFact]
    public void Particles_StepOnTheGpu_AsTheCpuFormulaSays()
    {
        using Renderer renderer = new(GraphicsDevice.GetDefault());
        Arena arena = new(1) { WavesEnabled = false };

        // One particle heading right; one about to cross the left wall, which it bounces off.
        renderer.Spawn(new ParticleBurst(new Vector2(200f, 100f), 1, Vector3.One, 100f, 2f, 1f, Vector2.UnitX, 0f, SpeedJitter: 0f, Drag: 2f));
        renderer.Spawn(new ParticleBurst(new Vector2(Tuning.Wall + 0.5f, 200f), 1, Vector3.One, 100f, 2f, 1f, -Vector2.UnitX, 0f, SpeedJitter: 0f, Drag: 0f));
        Particle before = renderer.Particles[0];

        const float dt = 0.1f;
        renderer.Render(arena, dt);

        Particle after = renderer.Particles[0];
        float damped = 100f * MathF.Exp(-2f * dt);
        Assert.Equal(damped, after.Velocity.X, 0.01f);
        Assert.Equal(before.Position.X + (damped * dt), after.Position.X, 0.01f);
        Assert.Equal(100f, after.Position.Y, 0.001f);
        Assert.Equal(before.Life - dt, after.Life, 1e-4f);

        Particle bounced = renderer.Particles[1];
        Assert.True(bounced.Velocity.X > 0f, "The wall reflects it.");
        Assert.Equal(60f, bounced.Velocity.X, 0.01f);

        // Particles age out.
        for (int i = 0; i < 25; i++)
        {
            renderer.Render(arena, dt);
        }

        Assert.Equal(0, renderer.LiveParticles);
    }

    [MetalFact]
    public void Particles_AreDrawnWhereTheyAre()
    {
        using Renderer renderer = new(GraphicsDevice.GetDefault());
        Arena arena = new(1) { WavesEnabled = false };
        renderer.Render(arena, 0f);
        float dark = Luma(renderer.PackedFrame[(250 * Renderer.Width) + 150]);

        renderer.Spawn(new ParticleBurst(new Vector2(150f, 250f), 400, new Vector3(1f, 0.5f, 0.2f), 30f, 5f, 1.2f));
        renderer.Render(arena, 0.05f);
        float lit = Luma(renderer.PackedFrame[(250 * Renderer.Width) + 150]);
        float elsewhere = Luma(renderer.PackedFrame[(250 * Renderer.Width) + 500]);

        Assert.True(lit > dark + 0.3f, $"The burst lights its spot ({dark} → {lit}).");
        Assert.True(elsewhere < 0.2f, $"...and not the other side of the arena ({elsewhere}).");
    }

    [MetalFact]
    public void FullParticleBuffer_RendersEveryChunk()
    {
        using Renderer renderer = new(GraphicsDevice.GetDefault());
        Arena arena = new(1) { WavesEnabled = false };
        Rng rng = new(11);

        while (renderer.Particles.ToArray().Count(p => p.Life > 0f) < Renderer.MaxParticles)
        {
            renderer.Spawn(new ParticleBurst(new Vector2(rng.Range(40f, 600f), rng.Range(40f, 320f)), 1000, Vector3.One, 200f, 10f, 1f));
        }

        renderer.Render(arena, 0.01f);

        Assert.Equal(Renderer.MaxParticles, renderer.LiveParticles);
        Assert.All(renderer.PackedFrame.ToArray(), static p => Assert.Equal(0xFFu, p >> 24));
        Assert.True(renderer.PackedFrame.ToArray().Average(static p => Luma(p)) > 0.2);
    }

    [MetalFact]
    public void Rendering_IsDeterministic()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Renderer a = new(device), b = new(device);
        Arena arena = new(4);
        Pilot pilot = new();

        for (int i = 0; i < 240; i++)
        {
            arena.Step(pilot.Next(arena));
            a.Render(arena, Tuning.StepSeconds);
            b.Render(arena, Tuning.StepSeconds);
            arena.EndFrame();
        }

        Assert.True(a.PackedFrame.SequenceEqual(b.PackedFrame));
    }

    [MetalFact]
    public void Upscale_WritesExactWholePixelBlocks_WithBlackBorders()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Renderer renderer = new(device);
        Arena arena = new(1);
        arena.Step(InputState.None);

        // 1300 × 740: scale 2 (1280 × 720), centered with 10-pixel borders.
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(1300, 740);
        renderer.Render(arena, Tuning.StepSeconds, target);

        Rgba32[] window = target.ToArray();
        ReadOnlySpan<uint> frame = renderer.PackedFrame;

        Assert.Equal(new Rgba32(0, 0, 0, 255), window[(5 * 1300) + 5]);
        Assert.Equal(new Rgba32(0, 0, 0, 255), window[(735 * 1300) + 1295]);

        foreach ((int sx, int sy) in new[] { (0, 0), (100, 50), (639, 359), (320, 180), (20, 20) })
        {
            uint expected = frame[(sy * Renderer.Width) + sx];

            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    Rgba32 p = window[((10 + (sy * 2) + dy) * 1300) + 10 + (sx * 2) + dx];
                    Assert.Equal(expected & 0xFFFFFFu, p.R | ((uint)p.G << 8) | ((uint)p.B << 16));
                }
            }
        }
    }

    [MetalFact]
    public void Effects_AndTheme_RenderAsAudibleClips()
    {
        using ChipSynth synth = new(GraphicsDevice.GetDefault());

        foreach ((GameEvent e, ToneSpec tone) in GameAudio.Effects)
        {
            using AudioClip clip = synth.Render(tone);
            float peak = clip.Samples.ToArray().Max(MathF.Abs);
            Assert.True(peak > 0.05f, $"{e} is silent.");
            Assert.True(peak <= 1f, $"{e} clips.");
        }

        // Every event the arena raises has a sound.
        Assert.Equal(Enum.GetValues<GameEvent>().Length, GameAudio.Effects.Select(static e => e.Event).Distinct().Count());

        using AudioClip theme = synth.Render(GameAudio.Theme());
        Assert.Equal(32 * 60.0 / 132.0, theme.Duration, 0.01);
        float themePeak = theme.Samples.ToArray().Max(MathF.Abs);
        Assert.InRange(themePeak, 0.1f, 1f);
        Assert.True(GameAudio.MusicVolume <= 0.5f, "The music leaves headroom for the effects.");
    }

    [MetalFact]
    public void Audio_IsASilentNoOp_WhenMuted()
    {
        using GameAudio audio = new(GraphicsDevice.GetDefault(), mute: true);
        audio.Play([new GameEventAt(GameEvent.Bomb, default), new GameEventAt(GameEvent.Shot, default)], gameOver: false);
        audio.Play([], gameOver: true);
        audio.Muted = true;
        Assert.False(audio.Muted);
    }

    private static float Luma(uint c) =>
        ((0.2126f * (c & 0xFF)) + (0.7152f * ((c >> 8) & 0xFF)) + (0.0722f * ((c >> 16) & 0xFF))) / 255f;
}
