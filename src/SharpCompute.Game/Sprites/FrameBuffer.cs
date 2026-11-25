using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpCompute.Game;

/// <summary>
/// The image a game draws into at its native resolution: <see cref="Width"/> × <see cref="Height"/>
/// pixels of packed RGBA8 (R in the low byte, the layout of <see cref="Rgba32"/>), row by row from
/// the top, in a shared GPU buffer. Sprite batches and post-processing passes write it on the GPU;
/// the CPU reads the very same memory for screenshots and tests, with no copy in between.
/// </summary>
/// <remarks>
/// It is a buffer, not a texture, so any compute shader can read and write it in place with plain
/// indexing; <see cref="PixelUpscale"/> copies it into a window's texture. Do not touch
/// <see cref="Packed"/>, <see cref="Pixels"/> or <see cref="AsSpan"/> between recording a pass that
/// uses the frame and executing its <see cref="ComputeContext"/>. Not thread-safe.
/// </remarks>
public sealed class FrameBuffer : IDisposable
{
    /// <summary>Allocates a frame, cleared to transparent black.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    public FrameBuffer(GraphicsDevice device, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Buffer = device.AllocateReadWriteBuffer<uint>(checked(width * height));
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The device the frame lives on.</summary>
    public GraphicsDevice Device => Buffer.Device;

    /// <summary>The GPU buffer, for binding to your own shaders: pixel (x, y) is element <c>y * Width + x</c>.</summary>
    public ReadWriteBuffer<uint> Buffer { get; }

    /// <summary>The pixels as packed RGBA (R in the low byte).</summary>
    public ReadOnlySpan<uint> Packed => Buffer.AsReadOnlySpan();

    /// <summary>The pixels as <see cref="Rgba32"/>, e.g. for a PNG encoder.</summary>
    public ReadOnlySpan<Rgba32> Pixels => MemoryMarshal.Cast<uint, Rgba32>(Buffer.AsReadOnlySpan());

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>), read from the shared memory.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the frame.</exception>
    public Rgba32 this[int x, int y]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)x, (uint)Width, nameof(x));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)y, (uint)Height, nameof(y));
            return Pixels[(y * Width) + x];
        }
    }

    /// <summary>A writable view of the packed pixels, to fill the frame from the CPU.</summary>
    public Span<uint> AsSpan() => Buffer.AsSpan();

    /// <summary>Fills the frame from the CPU. (A <see cref="SpriteBatch"/> can clear as part of its own pass instead.)</summary>
    public void Clear(Rgba32 color = default) => Buffer.AsSpan().Fill(Pack(color));

    public void Dispose() => Buffer.Dispose();

    /// <summary>The packed form of a color: R in the low byte, A in the high byte.</summary>
    public static uint Pack(Rgba32 color) => Unsafe.BitCast<Rgba32, uint>(color);

    /// <summary>The color of a packed pixel.</summary>
    public static Rgba32 Unpack(uint packed) => Unsafe.BitCast<uint, Rgba32>(packed);
}
