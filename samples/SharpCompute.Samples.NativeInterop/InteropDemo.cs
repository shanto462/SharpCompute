// Original SharpCompute sample (MIT).
using System;
using SharpCompute.Samples.NativeInterop.Host;

namespace SharpCompute.Samples.NativeInterop;

/// <summary>
/// SharpCompute on top of a renderer it did not create. The <see cref="HostRenderer"/> owns the Metal
/// device, queue, particle buffer and frame textures; this class wraps them
/// (<see cref="GraphicsDevice.FromNative"/>, <see cref="GraphicsDevice.WrapNativeReadWriteBuffer{T}"/>,
/// <see cref="GraphicsDevice.WrapNativeReadWriteTexture2D{T}"/>) and, every frame, encodes two compute
/// passes into the host's own command buffer with <see cref="ComputeContext.EncodeInto"/>:
/// <list type="number">
/// <item><description>SharpCompute: <see cref="SimulateParticles"/> on the host's particle buffer,</description></item>
/// <item><description>host: its scene pass (the picture, then the particles drawn from that buffer),</description></item>
/// <item><description>SharpCompute: <see cref="RipplePost"/>, from the host's scene texture into its output texture,</description></item>
/// <item><description>host: its help overlay, then it commits the one command buffer.</description></item>
/// </list>
/// Either SharpCompute pass can be switched off; the host's frame is then what it would be without it.
/// </summary>
/// <remarks>Not thread-safe; use it from one thread.</remarks>
public sealed class InteropDemo : IDisposable
{
    /// <summary>The number of particles in the host's buffer.</summary>
    public const int ParticleCount = 32_768;

    /// <summary>The simulation's time step in the headless mode and the tests: every run computes the same frames.</summary>
    public const double FixedStep = 1.0 / 60.0;

    private const float ParticleSize = 0.0075f;

    private readonly HostRenderer _host;
    private readonly ComputeContext _context;
    private readonly ReadWriteBuffer<Particle> _particles;
    private ReadWriteTexture2D<Rgba32> _scene;
    private ReadOnlyTexture2D<Rgba32> _sceneView;
    private ReadWriteTexture2D<Rgba32> _output;
    private ReadWriteBuffer<Rgba32>? _readback;
    private bool _simulate = true;
    private bool _post = true;
    private bool _overlay = true;

    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    public InteropDemo(int width, int height)
    {
        _host = new HostRenderer(ParticleCount, width, height);

        try
        {
            // The host's device and queue: SharpCompute's work is committed (and ordered) with the host's.
            Device = GraphicsDevice.FromNative(_host.Device, _host.Queue);
            _particles = Device.WrapNativeReadWriteBuffer<Particle>(_host.ParticleBuffer);
            _context = Device.CreateComputeContext();
            (_scene, _sceneView, _output) = WrapFrame();
            UpdateOverlay();
        }
        catch
        {
            _host.Dispose();
            throw;
        }
    }

    /// <summary>The SharpCompute device over the host's <c>MTLDevice</c> and queue.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>The host's particle buffer (<c>id&lt;MTLBuffer&gt;</c>, shared storage), which SharpCompute simulates in place.</summary>
    public nint HostParticleBuffer => _host.ParticleBuffer;

    public int Width => _host.Width;

    public int Height => _host.Height;

    /// <summary>Seconds of simulated time so far.</summary>
    public double Time { get; private set; }

    /// <summary>How long the GPU spent on the last finished frame (both SharpCompute passes and the host's), in milliseconds.</summary>
    public double LastGpuMilliseconds { get; private set; }

    /// <summary>Whether SharpCompute moves the host's particles. Off, they stay where they are.</summary>
    public bool Simulate
    {
        get => _simulate;
        set
        {
            _simulate = value;
            UpdateOverlay();
        }
    }

    /// <summary>Whether SharpCompute post-processes the host's frame. Off, the host shows its scene as rendered.</summary>
    public bool PostProcess
    {
        get => _post;
        set
        {
            _post = value;
            UpdateOverlay();
        }
    }

    /// <summary>Whether the host draws its help overlay.</summary>
    public bool ShowOverlay
    {
        get => _overlay;
        set => _overlay = value;
    }

    /// <summary>Where the post-process rings start, in frame uv (0..1, y down).</summary>
    public Float2 RippleCenter { get; set; } = new(0.3f, 0.56f);

    /// <summary>The help lines the host draws.</summary>
    public string[] OverlayLines =>
    [
        "HOST: RAW METAL RENDER PIPELINE · SHARPCOMPUTE: PARTICLE SIM + POST ON THE HOST'S OWN RESOURCES",
        $"[1] PARTICLE SIM: {(_simulate ? "ON" : "OFF")} · [2] POST (RIPPLES + BLOOM): {(_post ? "ON" : "OFF")} · CLICK MOVES THE RIPPLES · [S] SAVE PNG · [ESC] QUIT",
    ];

    /// <summary>Makes the frame textures <paramref name="width"/> × <paramref name="height"/>.</summary>
    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        // SharpCompute's wrappers go first: they hold references to the textures the host replaces.
        DisposeFrameWrappers();
        _host.Resize(width, height);
        (_scene, _sceneView, _output) = WrapFrame();
        UpdateOverlay();
    }

    /// <summary>
    /// Encodes and commits one frame of <paramref name="dt"/> seconds, in one host command buffer, and
    /// returns it; the GPU may still be running it. Pass it to <see cref="Finish"/> once it has been shown.
    /// </summary>
    public Frame Render(double dt)
    {
        Time += dt;
        float aspect = Width / (float)Height;
        nint commandBuffer = _host.BeginFrame();

        try
        {
            if (_simulate)
            {
                _context.For(ParticleCount, new SimulateParticles(_particles, (float)Time, (float)dt, aspect));

                // Into the host's command buffer, ahead of the scene pass that draws the particles.
                _ = _context.EncodeInto(commandBuffer);
            }

            _host.EncodeScene(commandBuffer, (float)Time, ParticleSize);

            ReadWriteTexture2D<Rgba32> final = _scene;

            if (_post)
            {
                _context.For(Width, Height, new RipplePost(_sceneView, _output, RippleCenter, (float)Time));

                // After the scene pass, which it reads, and before the overlay pass and the present.
                _ = _context.EncodeInto(commandBuffer);
                final = _output;
            }

            if (_overlay)
            {
                _host.EncodeOverlay(commandBuffer, final.NativeHandle);
            }

            HostRenderer.Commit(commandBuffer);
            return new Frame(commandBuffer, final);
        }
        catch
        {
            HostRenderer.EndFrame(commandBuffer);
            throw;
        }
    }

    /// <summary>Waits for a frame from <see cref="Render"/>, records its GPU time and releases it.</summary>
    public void Finish(Frame frame)
    {
        try
        {
            LastGpuMilliseconds = HostRenderer.WaitForFrame(frame.CommandBuffer);
        }
        finally
        {
            HostRenderer.EndFrame(frame.CommandBuffer);
        }
    }

    /// <summary>
    /// The pixels of a finished frame. The host's textures are GPU-only (private storage), so they are
    /// copied on the GPU into a buffer SharpCompute allocated, which the CPU can read.
    /// </summary>
    public Rgba32[] Capture(ReadWriteTexture2D<Rgba32> final)
    {
        if (_readback is null || _readback.Length != Width * Height)
        {
            _readback?.Dispose();
            _readback = Device.AllocateReadWriteBuffer<Rgba32>(Width * Height);
        }

        Device.Copy(final, _readback);
        return _readback.ToArray();
    }

    /// <summary>Renders <paramref name="frames"/> frames of <see cref="FixedStep"/> and returns the pixels of the last one.</summary>
    /// <param name="frames">How many frames to render.</param>
    /// <param name="gpuMilliseconds">When given, receives each frame's GPU time (as many as it has room for).</param>
    public Rgba32[] RenderFixed(int frames, Span<double> gpuMilliseconds = default)
    {
        ReadWriteTexture2D<Rgba32>? final = null;

        for (int i = 0; i < frames; i++)
        {
            Frame frame = Render(FixedStep);
            Finish(frame);
            final = frame.Final;

            if (i < gpuMilliseconds.Length)
            {
                gpuMilliseconds[i] = LastGpuMilliseconds;
            }
        }

        return final is null ? [] : Capture(final);
    }

    public void Dispose()
    {
        _context.Dispose();
        DisposeFrameWrappers();
        _particles.Dispose();
        _readback?.Dispose();
        Device.Dispose();
        _host.Dispose();
    }

    private (ReadWriteTexture2D<Rgba32> Scene, ReadOnlyTexture2D<Rgba32> SceneView, ReadWriteTexture2D<Rgba32> Output) WrapFrame()
    {
        ReadWriteTexture2D<Rgba32> scene = Device.WrapNativeReadWriteTexture2D<Rgba32>(_host.SceneTexture);
        return (scene, scene.AsReadOnly(), Device.WrapNativeReadWriteTexture2D<Rgba32>(_host.OutputTexture));
    }

    private void DisposeFrameWrappers()
    {
        _scene.Dispose();
        _output.Dispose();
    }

    private void UpdateOverlay() => _host.SetOverlay(OverlayLines);

    /// <summary>One committed frame: the host's command buffer (retained) and the texture the frame ended in.</summary>
    public readonly record struct Frame(nint CommandBuffer, ReadWriteTexture2D<Rgba32> Final);
}
