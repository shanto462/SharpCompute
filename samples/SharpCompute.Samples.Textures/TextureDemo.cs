// Original SharpCompute sample (MIT).
using System;

namespace SharpCompute.Samples.Textures;

/// <summary>The demo's two screens.</summary>
public enum DemoMode
{
    /// <summary>The six samplers side by side, on the selected texture format.</summary>
    Samplers,

    /// <summary>A simulation that goes back and forth between two read-write textures.</summary>
    PingPong,
}

/// <summary>
/// Everything the demo draws, without a window: the <see cref="SamplerExplorer"/>, the
/// <see cref="ReactionDiffusion"/>, the text, and <see cref="Render"/>, which records the current
/// mode's passes into one long-lived <see cref="ComputeContext"/> and executes it, once per frame.
/// </summary>
/// <remarks>Not thread-safe: one thread drives it. The window and the headless mode are both thin layers over this class.</remarks>
public sealed class TextureDemo : IDisposable
{
    /// <summary>Radians per second of the optional rotation.</summary>
    private const float RotationSpeed = 0.2f;

    private readonly GraphicsDevice _device;
    private readonly ComputeContext _context;
    private TextLayer? _labels;
    private (DemoMode Mode, int Format, bool Paused) _drawn;

    public TextureDemo(GraphicsDevice device)
    {
        _device = device;
        _context = device.CreateComputeContext();
        Explorer = new SamplerExplorer(device);
        Reaction = new ReactionDiffusion(device);
    }

    public SamplerExplorer Explorer { get; }

    public ReactionDiffusion Reaction { get; }

    public DemoMode Mode { get; set; }

    /// <summary>Stops the simulation (the brush still paints).</summary>
    public bool Paused { get; set; }

    /// <summary>Slowly turns the explorer's view.</summary>
    public bool Rotating { get; set; }

    /// <summary>The frame's text, as last drawn by <see cref="Render"/>.</summary>
    public TextLayer? Labels => _labels;

    /// <summary>Advances what moves on its own by <paramref name="seconds"/>.</summary>
    public void Advance(double seconds)
    {
        if (Rotating && Mode == DemoMode.Samplers)
        {
            Explorer.Angle += RotationSpeed * (float)seconds;
        }
    }

    /// <summary>Draws the current mode into <paramref name="target"/> and returns when the GPU has finished.</summary>
    public void Render(ReadWriteTexture2D<Rgba32> target)
    {
        TextLayer labels = UpdateLabels(target.Width, target.Height);

        if (Mode == DemoMode.Samplers)
        {
            Explorer.Record(_context, target, labels);
        }
        else
        {
            Reaction.RecordSteps(_context, Paused ? 0 : ReactionDiffusion.StepsPerFrame);
            Reaction.RecordDisplay(_context, target, labels);
        }

        // One commit and one wait for the whole frame.
        _context.Execute();
    }

    public void Dispose()
    {
        _context.Dispose();
        _labels?.Dispose();
        Reaction.Dispose();
        Explorer.Dispose();
    }

    /// <summary>The text for a frame of this size, redrawn and uploaded only when what it says changed.</summary>
    private TextLayer UpdateLabels(int width, int height)
    {
        (DemoMode, int, bool) state = (Mode, Explorer.Format, Paused);

        if (_labels is not null && _labels.Width == width && _labels.Height == height && state == _drawn)
        {
            return _labels;
        }

        if (_labels is null || _labels.Width != width || _labels.Height != height)
        {
            _labels?.Dispose();
            _labels = new TextLayer(_device, width, height);
        }

        _labels.Clear();

        if (Mode == DemoMode.Samplers)
        {
            Explorer.DrawLabels(_labels);
        }
        else
        {
            Reaction.DrawLabels(_labels, Paused);
        }

        _labels.Upload();
        _drawn = state;
        return _labels;
    }
}
