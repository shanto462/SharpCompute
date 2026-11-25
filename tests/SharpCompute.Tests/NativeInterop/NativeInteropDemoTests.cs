using System;
using System.IO;
using System.Linq;
using SharpCompute.Interop;
using SharpCompute.Samples.NativeInterop;
using Program = SharpCompute.Samples.NativeInterop.Program;

namespace SharpCompute.Tests.NativeInterop;

/// <summary>
/// The native interop demo without its window: a host renderer that owns every Metal object, with
/// SharpCompute's particle simulation and post-process encoded into its command buffer.
/// </summary>
public sealed class NativeInteropDemoTests
{
    private const int Width = 320;
    private const int Height = 200;
    private const int Frames = 45;

    [MetalFact]
    public void Frames_AreDeterministic_AndNotBlank()
    {
        Rgba32[] first = Render(simulate: true, post: true);
        Rgba32[] second = Render(simulate: true, post: true);

        Assert.Equal(first, second);

        // A real picture: many colors, neither black nor washed out.
        Assert.True(first.Distinct().Count() > 2000, "The frame has too few distinct colors to be a picture.");
        double luminance = first.Average(static p => (p.R + p.G + p.B) / 3.0);
        Assert.InRange(luminance, 30, 200);
        Assert.All(first, static p => Assert.Equal((byte)255, p.A));
    }

    [MetalFact]
    public void EachPass_ChangesTheFrame()
    {
        Rgba32[] both = Render(simulate: true, post: true);
        Rgba32[] noSim = Render(simulate: false, post: true);
        Rgba32[] noPost = Render(simulate: true, post: false);
        Rgba32[] hostOnly = Render(simulate: false, post: false);

        // With the overlay off, only the SharpCompute passes differ between these.
        Rgba32[][] frames = [both, noSim, noPost, hostOnly];

        for (int i = 0; i < frames.Length; i++)
        {
            for (int j = i + 1; j < frames.Length; j++)
            {
                Assert.True(ChangedFraction(frames[i], frames[j]) > 0.05, $"Frames {i} and {j} are nearly the same.");
            }
        }
    }

    [MetalFact]
    public void Simulation_MovesTheHostsParticles_InTheHostsOwnBuffer()
    {
        using InteropDemo demo = new(Width, Height) { ShowOverlay = false, PostProcess = false };

        // A second wrapper of the host's particle buffer, read as other code would read it.
        using ReadOnlyBuffer<Particle> particles = demo.Device.WrapNativeReadOnlyBuffer<Particle>(demo.HostParticleBuffer);
        Assert.Equal(InteropDemo.ParticleCount, particles.Length);

        demo.RenderFixed(1);
        Particle[] before = particles.ToArray();

        demo.RenderFixed(10);
        Particle[] after = particles.ToArray();

        int moved = before.Zip(after).Count(static pair => pair.First.Position.X != pair.Second.Position.X || pair.First.Position.Y != pair.Second.Position.Y);
        Assert.True(moved > InteropDemo.ParticleCount * 0.9, $"Only {moved} particles moved.");

        demo.Simulate = false;
        demo.RenderFixed(10);
        Assert.Equal(after, particles.ToArray());
    }

    [MetalFact]
    public void Device_IsTheHostsDeviceAndQueue()
    {
        using InteropDemo demo = new(Width, Height);

        // The host created its device with MTLCreateSystemDefaultDevice, so it is GetDefault's device too.
        Assert.Equal(GraphicsDevice.GetDefault().NativeHandle, demo.Device.NativeHandle);
        Assert.NotEqual(GraphicsDevice.GetDefault().NativeCommandQueueHandle, demo.Device.NativeCommandQueueHandle);
        Assert.Equal(demo.Device.NativeHandle, ObjC.SendIntPtr(demo.Device.NativeCommandQueueHandle, Selectors.Device));
    }

    [MetalFact]
    public void Overlay_NamesBothSides_AndShowsTheToggles()
    {
        using InteropDemo demo = new(Width, Height);

        Assert.Contains("HOST: RAW METAL RENDER PIPELINE", demo.OverlayLines[0]);
        Assert.Contains("SHARPCOMPUTE: PARTICLE SIM + POST", demo.OverlayLines[0]);
        Assert.Contains("[1] PARTICLE SIM: ON", demo.OverlayLines[1]);

        demo.Simulate = false;
        demo.PostProcess = false;
        Assert.Contains("[1] PARTICLE SIM: OFF", demo.OverlayLines[1]);
        Assert.Contains("[2] POST (RIPPLES + BLOOM): OFF", demo.OverlayLines[1]);

        // With the overlay on, the help text is in the frame: the top-left corner differs.
        Rgba32[] withOverlay = demo.RenderFixed(1);
        demo.ShowOverlay = false;
        Rgba32[] without = demo.RenderFixed(1);
        Assert.NotEqual(withOverlay.AsSpan(0, Width * 10).ToArray(), without.AsSpan(0, Width * 10).ToArray());
    }

    [MetalFact]
    public void Resize_RewrapsTheHostsNewTextures()
    {
        using InteropDemo demo = new(Width, Height) { ShowOverlay = false };

        demo.RenderFixed(2);
        demo.Resize(400, 240);
        Rgba32[] pixels = demo.RenderFixed(2);

        Assert.Equal((400, 240), (demo.Width, demo.Height));
        Assert.Equal(400 * 240, pixels.Length);
        Assert.True(pixels.Distinct().Count() > 500);
    }

    [MetalFact]
    public void Headless_WritesTheFrameSet_AndTheScreenshot()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpcompute-nativeinterop-" + Guid.NewGuid().ToString("N"));

        try
        {
            Assert.Equal(0, Program.RunHeadless(directory, Width, Height, frames: 5));

            foreach ((string file, _, _) in Program.HeadlessFrameSet)
            {
                byte[] png = File.ReadAllBytes(Path.Combine(directory, file));
                Assert.Equal(0x89, png[0]);
                Assert.Equal((byte)'P', png[1]);
            }

            string screenshot = Path.Combine(directory, "docs", "shot.png");
            Assert.Equal(0, Program.WriteScreenshot(screenshot, Width, Height));
            Assert.True(new FileInfo(screenshot).Length > 1000);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static Rgba32[] Render(bool simulate, bool post)
    {
        using InteropDemo demo = new(Width, Height) { Simulate = simulate, PostProcess = post, ShowOverlay = false };
        return demo.RenderFixed(Frames);
    }

    private static double ChangedFraction(Rgba32[] a, Rgba32[] b) =>
        a.Zip(b).Count(static pair => !pair.First.Equals(pair.Second)) / (double)a.Length;
}
