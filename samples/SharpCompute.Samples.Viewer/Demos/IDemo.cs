using System;
using SharpCompute.Presentation;
using SharpCompute.Samples.Shaders;

namespace SharpCompute.Samples.Viewer.Demos;

/// <summary>The viewer's clock for one frame. <see cref="Time"/> stops while paused.</summary>
/// <param name="Time">Seconds since the demo was selected, excluding paused time.</param>
/// <param name="DeltaTime">Seconds since the previous frame (0 while paused).</param>
/// <param name="Paused">True while the viewer is paused; simulations should not advance.</param>
public readonly record struct DemoFrame(float Time, float DeltaTime, bool Paused);

/// <summary>One entry in the viewer's ←/→ rotation.</summary>
public interface IDemo : IDisposable
{
    string Name { get; }

    string Author { get; }

    /// <summary>Extra controls shown in the console when the demo is selected, or null.</summary>
    string? Controls => null;

    /// <summary>Renders one frame into <paramref name="target"/>, synchronously, advancing any simulation first.</summary>
    void Render(GraphicsDevice device, ReadWriteTexture2D<Rgba32> target, DemoFrame frame);

    /// <summary>A key the viewer itself doesn't use.</summary>
    void OnKeyDown(Key key)
    {
    }

    /// <summary>A mouse event, in the pixel space of the most recently rendered target (may lie outside it while dragging).</summary>
    void OnMouse(MouseAction action, MouseButton button, double x, double y, int targetWidth, int targetHeight)
    {
    }
}

public enum MouseAction
{
    Down,
    Drag,
    Up,
}

/// <summary>Adapts a <see cref="GalleryShader"/> (a pure function of time) to <see cref="IDemo"/>.</summary>
public sealed class GalleryDemo(GalleryShader shader) : IDemo
{
    public string Name => shader.Name;

    public string Author => shader.Author;

    public void Render(GraphicsDevice device, ReadWriteTexture2D<Rgba32> target, DemoFrame frame) => shader.Render(device, target, frame.Time);

    public void Dispose()
    {
    }
}
