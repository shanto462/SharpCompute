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
/// Shaders pass one of the static properties straight to <see cref="ReadOnlyTexture2D{T}.Sample"/>, e.g.
/// <c>texture.Sample(Sampler.LinearClamp, uv)</c>. Each becomes a <c>constexpr sampler</c> in the
/// generated MSL, so there is nothing to allocate or bind. On the CPU (<c>ForCpu</c>) the same filtering is
/// done in C#; linear filtering of 8-bit formats can differ from the GPU by about one step of 1/255,
/// because GPUs interpolate with limited precision.
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
