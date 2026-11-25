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

    /// <summary>
    /// The scroll wheel or a two-finger scroll over the frame, in wheel notches (fractions for a
    /// trackpad; see <see cref="WheelInput"/>). Positive is a positive
    /// <see cref="MouseWheelEventArgs.DeltaY"/>, which demos treat as "zoom in".
    /// </summary>
    void OnWheel(float steps)
    {
    }
}

public enum MouseAction
{
    Down,
    Drag,
    Up,
}

/// <summary>Turns <see cref="MetalWindow.MouseWheel"/> deltas into the notches <see cref="IDemo.OnWheel"/> takes.</summary>
public static class WheelInput
{
    /// <summary>How far two fingers scroll, in points, for the same effect as one notch of a wheel.</summary>
    public const float PointsPerStep = 40f;

    /// <summary>The most one event may count for, so a fast spin can't jump across a demo's whole range.</summary>
    public const float MaxSteps = 4f;

    /// <summary>
    /// One wheel event as notches. A wheel reports lines, and macOS scales them with how fast it
    /// turns (a slow notch can be a tenth of a line), so each event counts as at least one notch. A
    /// trackpad reports drawable pixels, many events per gesture: those convert proportionally, by
    /// finger travel in points so a Retina display doesn't double the speed. The sign is used as it
    /// comes, because macOS has already applied the user's "Natural scrolling" setting.
    /// </summary>
    public static float ToSteps(float delta, bool precise, double scaleFactor)
    {
        if (delta == 0f || float.IsNaN(delta))
        {
            return 0f;
        }

        float steps = precise
            ? delta / ((float)Math.Max(scaleFactor, 1) * PointsPerStep)
            : MathF.CopySign(MathF.Max(1f, MathF.Abs(delta)), delta);

        return Math.Clamp(steps, -MaxSteps, MaxSteps);
    }
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
