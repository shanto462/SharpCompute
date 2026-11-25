using System;

namespace SharpCompute.Game;

/// <summary>
/// Bloom: bright areas spill light over their surroundings. The bright part of the image is
/// extracted at half resolution and blurred, then halved and blurred again, giving a tight glow
/// (<see cref="Half"/>) and a wide one (<see cref="Quarter"/>) for a fraction of the cost of
/// blurring at full size. Every pass is recorded into the caller's <see cref="ComputeContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>HDR.</b> <see cref="Record(ComputeContext, ReadWriteBuffer{Float4})"/> takes a linear
/// <see cref="Float4"/> image and leaves the two levels for a composite pass to add, normally
/// <see cref="HdrComposite"/>.
/// </para>
/// <para>
/// <b>8-bit frames.</b> <see cref="Record(ComputeContext, FrameBuffer, float)"/> does the whole
/// effect on a <see cref="FrameBuffer"/> in place: extract, blur, and add the glow back.
/// </para>
/// <para>
/// Six dispatches for the levels, plus one to add them to a frame. The buffers are allocated once.
/// Not thread-safe.
/// </para>
/// </remarks>
public sealed class Bloom : IDisposable
{
    private readonly ReadWriteBuffer<Float4> _halfScratch;
    private readonly ReadWriteBuffer<Float4> _quarterScratch;

    /// <summary>Bloom for images of <paramref name="width"/> × <paramref name="height"/> pixels.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A size is below 4 (the quarter level would be empty).</exception>
    public Bloom(GraphicsDevice device, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 4);
        Width = width;
        Height = height;
        Half = device.AllocateReadWriteBuffer<Float4>(checked(width / 2 * (height / 2)));
        _halfScratch = device.AllocateReadWriteBuffer<Float4>(Half.Length);
        Quarter = device.AllocateReadWriteBuffer<Float4>(width / 4 * (height / 4));
        _quarterScratch = device.AllocateReadWriteBuffer<Float4>(Quarter.Length);
    }

    /// <summary>Width of the images this bloom works on.</summary>
    public int Width { get; }

    /// <summary>Height of the images this bloom works on.</summary>
    public int Height { get; }

    /// <summary>
    /// Brightness (of the strongest channel) above which a pixel blooms. The excess is kept, hue
    /// intact, so the glow fades in smoothly instead of switching on.
    /// </summary>
    public float Threshold { get; set; } = 0.3f;

    /// <summary>The tight glow: <c>Width / 2</c> × <c>Height / 2</c>, row by row. Valid after the recorded passes have executed.</summary>
    public ReadWriteBuffer<Float4> Half { get; }

    /// <summary>The wide glow: <c>Width / 4</c> × <c>Height / 4</c>, row by row.</summary>
    public ReadWriteBuffer<Float4> Quarter { get; }

    /// <summary>Records the bloom levels of a linear HDR image (<see cref="Width"/> × <see cref="Height"/>, row by row).</summary>
    /// <exception cref="ArgumentException"><paramref name="source"/> does not hold <see cref="Width"/> × <see cref="Height"/> pixels.</exception>
    public void Record(ComputeContext context, ReadWriteBuffer<Float4> source)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);

        if (source.Length != Width * Height)
        {
            throw new ArgumentException($"This bloom works on {Width} × {Height} images ({Width * Height} pixels); the source holds {source.Length}.", nameof(source));
        }

        context.For(Width / 2, Height / 2, new BloomPrefilterPass(source, Half, Width, Width / 2, Threshold));
        RecordBlurs(context);
    }

    /// <summary>
    /// Records the whole effect on an 8-bit frame, in place: the bloom levels of
    /// <paramref name="frame"/>, then their sum added back onto it, scaled by <paramref name="strength"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="frame"/> is not <see cref="Width"/> × <see cref="Height"/>.</exception>
    public void Record(ComputeContext context, FrameBuffer frame, float strength = 1f)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Width != Width || frame.Height != Height)
        {
            throw new ArgumentException($"This bloom works on {Width} × {Height} images; the frame is {frame.Width} × {frame.Height}.", nameof(frame));
        }

        context.For(Width / 2, Height / 2, new BloomPrefilterPackedPass(frame.Buffer, Half, Width, Width / 2, Threshold));
        RecordBlurs(context);
        context.For(Width, Height, new BloomAddPass(frame.Buffer, Half, Quarter, Width, Height, strength));
    }

    public void Dispose()
    {
        Half.Dispose();
        _halfScratch.Dispose();
        Quarter.Dispose();
        _quarterScratch.Dispose();
    }

    /// <summary>Blur the half level, halve it into the quarter level, blur that: each blur is a horizontal then a vertical pass.</summary>
    private void RecordBlurs(ComputeContext context)
    {
        int halfWidth = Width / 2, halfHeight = Height / 2;
        int quarterWidth = Width / 4, quarterHeight = Height / 4;
        context.For(halfWidth, halfHeight, new BloomBlurPass(Half, _halfScratch, halfWidth, halfHeight, 1, 0));
        context.For(halfWidth, halfHeight, new BloomBlurPass(_halfScratch, Half, halfWidth, halfHeight, 0, 1));
        context.For(quarterWidth, quarterHeight, new BloomDownsamplePass(Half, Quarter, halfWidth, quarterWidth));
        context.For(quarterWidth, quarterHeight, new BloomBlurPass(Quarter, _quarterScratch, quarterWidth, quarterHeight, 1, 0));
        context.For(quarterWidth, quarterHeight, new BloomBlurPass(_quarterScratch, Quarter, quarterWidth, quarterHeight, 0, 1));
    }
}
