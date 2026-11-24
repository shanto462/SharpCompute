using System;
using System.Linq;
using SharpCompute.Samples.Audio;
using SharpCompute.Samples.Platformer;
using SharpCompute.Samples.Platformer.Game;
using SharpCompute.Samples.Platformer.Rendering;

namespace SharpCompute.Tests.Platformer;

/// <summary>Glowfall on the GPU: the scene, the HUD, the upscale, and the generated audio.</summary>
public sealed class GlowfallRenderTests
{
    [Fact]
    public void ArtAndFont_Build()
    {
        uint[] atlas = Art.BuildAtlas();

        Assert.Equal(Art.Width * Art.Height, atlas.Length);
        Assert.Contains(atlas, static p => p >> 24 == 128u);   // emissive pixels
        Assert.Contains(atlas, static p => p >> 24 == 255u);   // lit pixels
        Assert.Equal(64, PixelFont.Masks.Length);
        Assert.NotEqual(0u, PixelFont.Masks[PixelFont.Index('G')]);
        Assert.Equal(PixelFont.Index('g'), PixelFont.Index('G'));
    }

    [MetalFact]
    public void Scene_IsLitAroundTheSpirit_AndDarkFarFromLight()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Renderer renderer = new(device);
        GameSession session = new();

        for (int i = 0; i < 120; i++)
        {
            session.Step(InputState.None);
        }

        renderer.Render(session, Tuning.StepSeconds);
        (int cx, int cy) = session.Camera.Snapped;
        int px = (int)session.World.Player.Body.Center.X - cx;
        int py = (int)session.World.Player.Body.Center.Y - cy;

        ReadOnlySpan<uint> frame = renderer.PackedFrame;
        float near = Luma(frame[(py * Renderer.Width) + px]);
        float corner = Luma(frame[((Renderer.Height - 2) * Renderer.Width) + Renderer.Width - 2]);

        Assert.True(near > 0.5f, $"The spirit glows (luma {near}).");
        Assert.True(corner < near * 0.5f, $"Far corners are darker (corner {corner}, near {near}).");
        Assert.All(frame.ToArray(), static p => Assert.Equal(0xFFu, p >> 24));
    }

    [MetalFact]
    public void Hud_DrawsTheCounters()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Renderer renderer = new(device);
        GameSession session = new();
        session.Step(InputState.None);
        renderer.Render(session, Tuning.StepSeconds);

        // "LANTERNS 0/n" starts at (6, 6): the L's vertical stroke is the brightest thing there.
        ReadOnlySpan<uint> frame = renderer.PackedFrame;
        int lit = 0;

        for (int y = 6; y < 11; y++)
        {
            lit += Luma(frame[(y * Renderer.Width) + 6]) > 0.7f ? 1 : 0;
        }

        Assert.Equal(5, lit);
    }

    [MetalFact]
    public void Upscale_WritesExactWholePixelBlocks_WithBlackBorders()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Renderer renderer = new(device);
        GameSession session = new();
        session.Step(InputState.None);

        // 1000 × 600: scale 2 (960 × 540), centered with 20- and 30-pixel borders.
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(1000, 600);
        renderer.Render(session, Tuning.StepSeconds, target);

        Rgba32[] window = target.ToArray();
        ReadOnlySpan<uint> frame = renderer.PackedFrame;

        Assert.Equal(new Rgba32(0, 0, 0, 255), window[(10 * 1000) + 10]);

        foreach ((int sx, int sy) in new[] { (0, 0), (100, 50), (479, 269), (240, 135) })
        {
            uint expected = frame[(sy * Renderer.Width) + sx];

            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    Rgba32 p = window[((30 + (sy * 2) + dy) * 1000) + 20 + (sx * 2) + dx];
                    Assert.Equal(expected & 0xFFFFFFu, p.R | ((uint)p.G << 8) | ((uint)p.B << 16));
                }
            }
        }
    }

    [MetalFact]
    public void Effects_AndTheme_RenderAsAudibleClips()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ChipSynth synth = new(device);

        foreach ((GameEvent e, ToneSpec tone) in GameAudio.Effects)
        {
            using AudioClip clip = synth.Render(tone);
            Assert.True(clip.Samples.ToArray().Max(MathF.Abs) > 0.05f, $"{e} is silent.");
        }

        using AudioClip theme = synth.Render(GameAudio.Theme());
        Assert.Equal(32 * 60.0 / 92.0, theme.Duration, 0.01);
        Assert.True(theme.Samples.ToArray().Max(MathF.Abs) <= 1f);

        using AudioClip finale = synth.Render(GameAudio.Finale());
        Assert.True(finale.Duration > 5);
    }

    [MetalFact]
    public void Audio_IsASilentNoOp_WhenMuted()
    {
        using GameAudio audio = new(GraphicsDevice.GetDefault(), mute: true);
        audio.Play([new GameEventAt(GameEvent.Jump, default)], 0, finished: false);
        audio.Muted = true;
        Assert.False(audio.Muted);
    }

    private static float Luma(uint c) =>
        ((0.2126f * (c & 0xFF)) + (0.7152f * ((c >> 8) & 0xFF)) + (0.0722f * ((c >> 16) & 0xFF))) / 255f;
}
