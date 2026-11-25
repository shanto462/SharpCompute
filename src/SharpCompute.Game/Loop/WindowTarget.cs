using System;
using SharpCompute.Presentation;

namespace SharpCompute.Game;

/// <summary>
/// The texture a game presents to its <see cref="MetalWindow"/>: always the window's drawable size,
/// reallocated when the window is resized (never per frame). Render or upscale into
/// <see cref="Texture"/>, then <see cref="Present"/>.
/// </summary>
/// <remarks>Main thread only, like the window. Dispose it before the window's device.</remarks>
public sealed class WindowTarget : IDisposable
{
    private readonly MetalWindow _window;
    private readonly EventHandler _resized;

    /// <summary>Allocates a texture of <paramref name="window"/>'s size and follows its resizes.</summary>
    public WindowTarget(MetalWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        Texture = Allocate();
        _resized = (_, _) =>
        {
            Texture.Dispose();
            Texture = Allocate();
        };
        window.Resized += _resized;
    }

    /// <summary>The texture to render into. A resize replaces it, so read the property each frame instead of caching it.</summary>
    public ReadWriteTexture2D<Rgba32> Texture { get; private set; }

    /// <summary>Shows <see cref="Texture"/> in the window; see <see cref="MetalWindow.Present"/>.</summary>
    public bool Present() => _window.Present(Texture);

    public void Dispose()
    {
        _window.Resized -= _resized;
        Texture.Dispose();
    }

    private ReadWriteTexture2D<Rgba32> Allocate() => _window.Device.AllocateReadWriteTexture2D<Rgba32>(_window.Width, _window.Height);
}
