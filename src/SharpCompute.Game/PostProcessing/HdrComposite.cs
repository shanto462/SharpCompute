using System;
using System.Numerics;

namespace SharpCompute.Game;

/// <summary>
/// Turns a linear HDR image into the 8-bit frame that is shown, in one pass per pixel: shockwave
/// distortion and a shake offset on the lookup, the scene plus its <see cref="Bloom"/>, a full-screen
/// flash, exponential tone mapping and a vignette. Doing all of it in one pass, before the image is
/// clamped to 8 bits, keeps bright colors bright through every step.
/// </summary>
/// <remarks>
/// The properties are read when <see cref="Record"/> is called. Holds no GPU resources. Not thread-safe.
/// </remarks>
public sealed class HdrComposite
{
    /// <summary>How much of the bloom is added; 0 turns it off.</summary>
    public float BloomStrength { get; set; } = 1f;

    /// <summary>
    /// Tone-mapping exposure: a channel of value <c>c</c> comes out as <c>1 − exp(−c × Exposure)</c>,
    /// so highlights roll off towards white instead of clipping.
    /// </summary>
    public float Exposure { get; set; } = 1f;

    /// <summary>Darkening towards the corners: 0 none; at 1 the corners keep half their brightness.</summary>
    public float Vignette { get; set; }

    /// <summary>A color added to the whole image before tone mapping (an explosion's flash); fade it yourself.</summary>
    public Vector3 Flash { get; set; }

    /// <summary>Shifts the whole image by this many pixels: feed it <see cref="ScreenShake.Offset"/>.</summary>
    public Vector2 Offset { get; set; }

    /// <summary>Records the composite of <paramref name="hdr"/> into <paramref name="output"/>.</summary>
    /// <param name="context">The context to record into.</param>
    /// <param name="hdr">The linear scene, <paramref name="output"/>'s size, row by row.</param>
    /// <param name="bloom">The bloom of <paramref name="hdr"/>, recorded earlier in the same context.</param>
    /// <param name="shockwaves">Rings that distort the image; an empty list distorts nothing.</param>
    /// <param name="output">The 8-bit frame to write; its alpha is set to 255.</param>
    /// <exception cref="ArgumentException"><paramref name="hdr"/> or <paramref name="bloom"/> does not match <paramref name="output"/>'s size.</exception>
    public void Record(ComputeContext context, ReadWriteBuffer<Float4> hdr, Bloom bloom, ShockwaveDistortion shockwaves, FrameBuffer output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hdr);
        ArgumentNullException.ThrowIfNull(bloom);
        ArgumentNullException.ThrowIfNull(shockwaves);
        ArgumentNullException.ThrowIfNull(output);

        if (hdr.Length != output.Width * output.Height)
        {
            throw new ArgumentException($"The output is {output.Width} × {output.Height} ({output.Width * output.Height} pixels); the HDR image holds {hdr.Length}.", nameof(hdr));
        }

        if (bloom.Width != output.Width || bloom.Height != output.Height)
        {
            throw new ArgumentException($"The output is {output.Width} × {output.Height}; the bloom was made for {bloom.Width} × {bloom.Height}.", nameof(bloom));
        }

        context.For(output.Width, output.Height, new HdrCompositePass(
            hdr, bloom.Half, bloom.Quarter, shockwaves.Rings, shockwaves.Count, output.Buffer,
            output.Width, output.Height, BloomStrength, Exposure, Flash.X, Flash.Y, Flash.Z, Offset.X, Offset.Y,
            shockwaves.ColorSplit, Vignette));
    }
}
