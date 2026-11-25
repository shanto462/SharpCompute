namespace SharpCompute;

/// <summary>How <see cref="ReadOnlyTexture2D{T}.Sample"/> combines texels.</summary>
public enum TextureFilter
{
    /// <summary>The nearest texel (<c>filter::nearest</c>).</summary>
    Point,

    /// <summary>Bilinear interpolation of the four nearest texels (<c>filter::linear</c>).</summary>
    Linear,
}

/// <summary>What <see cref="ReadOnlyTexture2D{T}.Sample"/> reads outside <c>[0, 1]</c>.</summary>
public enum TextureAddressMode
{
    /// <summary>The edge texel (<c>address::clamp_to_edge</c>).</summary>
    Clamp,

    /// <summary>The texture tiles (<c>address::repeat</c>).</summary>
    Repeat,

    /// <summary>The texture tiles, every other tile mirrored (<c>address::mirrored_repeat</c>).</summary>
    Mirror,
}

/// <summary>
/// A texture sampler: a filter and an address mode, with normalized coordinates (<c>(0, 0)</c> is the
/// top-left corner of the texture, <c>(1, 1)</c> the bottom-right one, and texel centers are at
/// <c>(i + 0.5) / size</c>).
/// </summary>
/// <remarks>
/// <para>
/// Shaders pass one of the static properties straight to <see cref="ReadOnlyTexture2D{T}.Sample"/>, e.g.
/// <c>texture.Sample(Sampler.LinearClamp, uv)</c>. Each becomes a <c>constexpr sampler</c> in the
/// generated MSL, so there is nothing to allocate or bind; any other expression is a build error.
/// </para>
/// <para>
/// On the CPU (<c>ForCpu</c>) the same filtering is done in C#, and follows the GPU closely but not
/// bit for bit (measured on an Apple M4 Pro; see <see cref="ReadOnlyTexture2D{T}.Sample"/>). Point
/// sampling picks the same texel with <see cref="TextureAddressMode.Clamp"/>, for coordinates in
/// <c>[0, 1)</c>, and for sizes that are a power of two; with <see cref="TextureAddressMode.Repeat"/>
/// or <see cref="TextureAddressMode.Mirror"/> outside <c>[0, 1)</c> on other sizes, a coordinate within
/// float rounding of a texel boundary can pick the neighbouring texel on the GPU. Linear filtering is
/// done in full float precision, while the GPU rounds the two interpolation weights to 1/256 (and
/// rounds the result for a half-float texture to a half), so the results can differ by up to 1/512 of
/// the difference between neighbouring texels per axis: one step, when the result is written to an
/// 8-bit format.
/// </para>
/// </remarks>
public readonly struct Sampler
{
    private Sampler(TextureFilter filter, TextureAddressMode addressMode)
    {
        Filter = filter;
        AddressMode = addressMode;
    }

    /// <summary>Nearest texel, clamped to the edge.</summary>
    public static Sampler PointClamp => new(TextureFilter.Point, TextureAddressMode.Clamp);

    /// <summary>Nearest texel, tiled.</summary>
    public static Sampler PointRepeat => new(TextureFilter.Point, TextureAddressMode.Repeat);

    /// <summary>Nearest texel, tiled with mirroring.</summary>
    public static Sampler PointMirror => new(TextureFilter.Point, TextureAddressMode.Mirror);

    /// <summary>Bilinear, clamped to the edge.</summary>
    public static Sampler LinearClamp => new(TextureFilter.Linear, TextureAddressMode.Clamp);

    /// <summary>Bilinear, tiled.</summary>
    public static Sampler LinearRepeat => new(TextureFilter.Linear, TextureAddressMode.Repeat);

    /// <summary>Bilinear, tiled with mirroring.</summary>
    public static Sampler LinearMirror => new(TextureFilter.Linear, TextureAddressMode.Mirror);

    /// <summary>How texels are combined.</summary>
    public TextureFilter Filter { get; }

    /// <summary>What is read outside <c>[0, 1]</c>.</summary>
    public TextureAddressMode AddressMode { get; }

    public override string ToString() => $"Sampler({Filter}, {AddressMode})";
}
