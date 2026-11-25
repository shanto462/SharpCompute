// Original SharpCompute sample (MIT).
namespace SharpCompute.Samples.Textures;

// ---- formats ------------------------------------------------------------------------------------
//
// One shader per element type, all with the same body: `decoded[xy] = source[xy]`. Whatever the
// format stores, the read gives a Float4: normalized bytes become c / 255, sRGB is decoded to
// linear, and the channels a format lacks read as (0, 0, 1). The explorer shows that Float4.
// (A shader struct cannot be generic, hence one struct per type.)

[GeneratedComputeShaderDescriptor]
public readonly partial struct DecodeRgba32(ReadOnlyTexture2D<Rgba32> source, ReadWriteTexture2D<Float4> decoded) : IComputeShader
{
    public void Execute() => decoded[ThreadIds.XY] = source[ThreadIds.XY];
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct DecodeRgba32Srgb(ReadOnlyTexture2D<Rgba32Srgb> source, ReadWriteTexture2D<Float4> decoded) : IComputeShader
{
    public void Execute() => decoded[ThreadIds.XY] = source[ThreadIds.XY];
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct DecodeR8(ReadOnlyTexture2D<R8> source, ReadWriteTexture2D<Float4> decoded) : IComputeShader
{
    public void Execute() => decoded[ThreadIds.XY] = source[ThreadIds.XY];
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct DecodeRg16(ReadOnlyTexture2D<Rg16> source, ReadWriteTexture2D<Float4> decoded) : IComputeShader
{
    public void Execute() => decoded[ThreadIds.XY] = source[ThreadIds.XY];
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct DecodeHalf(ReadOnlyTexture2D<System.Half> source, ReadWriteTexture2D<Float4> decoded) : IComputeShader
{
    public void Execute() => decoded[ThreadIds.XY] = source[ThreadIds.XY];
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct DecodeRgba16Float(ReadOnlyTexture2D<Rgba16Float> source, ReadWriteTexture2D<Float4> decoded) : IComputeShader
{
    public void Execute() => decoded[ThreadIds.XY] = source[ThreadIds.XY];
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct DecodeSingle(ReadOnlyTexture2D<float> source, ReadWriteTexture2D<Float4> decoded) : IComputeShader
{
    public void Execute() => decoded[ThreadIds.XY] = source[ThreadIds.XY];
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct DecodeFloat4(ReadOnlyTexture2D<Float4> source, ReadWriteTexture2D<Float4> decoded) : IComputeShader
{
    public void Execute() => decoded[ThreadIds.XY] = source[ThreadIds.XY];
}

// ---- sampler explorer ---------------------------------------------------------------------------

/// <summary>
/// The explorer's frame: a grid of three columns (Clamp, Repeat, Mirror) by two rows (Point,
/// Linear), each panel magnifying <paramref name="image"/> through one of the six samplers. The
/// coordinates run well past [0, 1], which is where the address modes differ; a white outline marks
/// the texture's own square.
/// </summary>
/// <param name="image">The picture, as a shader reads it.</param>
/// <param name="labels">The frame's text (see <see cref="TextLayer"/>).</param>
/// <param name="origin">Top-left corner of the grid, in frame pixels.</param>
/// <param name="cell">Size of one panel, in frame pixels.</param>
/// <param name="gap">Half the width of the dark gap between panels, in pixels.</param>
/// <param name="zoom">How many texture heights fit in a panel's height.</param>
/// <param name="pan">The texture coordinate offset of the view.</param>
/// <param name="angle">The rotation of the view, in radians.</param>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SamplerGrid(
    ReadOnlyTexture2D<Float4> image, ReadOnlyTexture2D<R8> labels, Float2 origin, Float2 cell, float gap, float zoom, Float2 pan, float angle)
    : IComputeShader<Float4>
{
    private static readonly Float3 Backdrop = new(0.05f, 0.06f, 0.08f);

    public Float4 Execute()
    {
        Float2 pixel = new Float2(ThreadIds.X, ThreadIds.Y) + 0.5f;
        Float2 index = Gpu.Floor((pixel - origin) / cell);
        Float3 color = Backdrop;

        if (index.X >= 0f && index.X < 3f && index.Y >= 0f && index.Y < 2f)
        {
            // Position in the panel, 0..1. Computed from the panel's own corner so that the same
            // pixel of every panel gets exactly the same coordinates.
            Float2 local = (pixel - origin - (index * cell)) / cell;
            Float2 margin = Gpu.Min(local, 1f - local) * cell;

            if (Gpu.Min(margin.X, margin.Y) >= gap)
            {
                Float2 uv = PanelUv(local, cell, zoom, pan, angle);
                color = Pick((int)index.X + ((int)index.Y * 3), uv).XYZ;

                // The texture itself covers [0, 1]; everything outside the outline is the address mode.
                Float2 fromCenter = Gpu.Abs(uv - 0.5f);

                if (Gpu.Abs(Gpu.Max(fromCenter.X, fromCenter.Y) - 0.5f) < 1.25f * zoom / cell.Y)
                {
                    color = Gpu.Lerp(color, new Float3(1f, 1f, 1f), 0.85f);
                }
            }
        }

        float ink = labels[ThreadIds.XY].X;

        if (ink > 0.75f)
        {
            color = new Float3(1f, 1f, 1f);
        }
        else if (ink > 0.25f)
        {
            color *= 0.22f;
        }

        return new Float4(color, 1f);
    }

    /// <summary>
    /// The texture coordinate shown at <paramref name="local"/> (0..1 across a panel): the panel's
    /// center looks at the texture's center plus <paramref name="pan"/>, and its height spans
    /// <paramref name="zoom"/> texture heights.
    /// </summary>
    public static Float2 PanelUv(Float2 local, Float2 cell, float zoom, Float2 pan, float angle)
    {
        Float2 p = (local - 0.5f) * new Float2(cell.X / cell.Y, 1f);
        float sine = Gpu.Sin(angle);
        float cosine = Gpu.Cos(angle);
        Float2 rotated = new Float2((p.X * cosine) - (p.Y * sine), (p.X * sine) + (p.Y * cosine));

        return new Float2(0.5f, 0.5f) + (rotated * zoom) + pan;
    }

    /// <summary>The six samplers, in the order of the panels: Point then Linear, each Clamp, Repeat, Mirror.</summary>
    private Float4 Pick(int sampler, Float2 uv)
    {
        switch (sampler)
        {
            case 0:
                return image.Sample(Sampler.PointClamp, uv);
            case 1:
                return image.Sample(Sampler.PointRepeat, uv);
            case 2:
                return image.Sample(Sampler.PointMirror, uv);
            case 3:
                return image.Sample(Sampler.LinearClamp, uv);
            case 4:
                return image.Sample(Sampler.LinearRepeat, uv);
            default:
                return image.Sample(Sampler.LinearMirror, uv);
        }
    }
}

// ---- ping-pong ----------------------------------------------------------------------------------

/// <summary>
/// One step of a Gray-Scott reaction-diffusion: two chemicals, A in the red channel and B in the
/// green one, diffuse, and B grows by consuming A. Reads the whole neighbourhood of
/// <paramref name="previous"/> and writes <paramref name="next"/>; the next step swaps the two.
/// </summary>
/// <remarks>
/// <paramref name="previous"/> is the <c>AsReadOnly()</c> view of the other read-write texture:
/// a read-write texture cannot be sampled, its view can, and <c>PointRepeat</c> makes the field wrap
/// around at the edges with no index arithmetic.
/// </remarks>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ReactionStep(ReadOnlyTexture2D<Float4> previous, ReadWriteTexture2D<Float4> next, float feed, float kill) : IComputeShader
{
    public void Execute()
    {
        Float2 texel = new Float2(1f / previous.Width, 1f / previous.Height);
        Float2 uv = (new Float2(ThreadIds.X, ThreadIds.Y) + 0.5f) * texel;
        Float4 center = previous.Sample(Sampler.PointRepeat, uv);

        // A 3 × 3 Laplacian: 0.2 for the four neighbours, 0.05 for the diagonals.
        Float4 laplacian =
            ((Tap(uv, texel, -1f, 0f) + Tap(uv, texel, 1f, 0f) + Tap(uv, texel, 0f, -1f) + Tap(uv, texel, 0f, 1f)) * 0.2f) +
            ((Tap(uv, texel, -1f, -1f) + Tap(uv, texel, 1f, -1f) + Tap(uv, texel, -1f, 1f) + Tap(uv, texel, 1f, 1f)) * 0.05f) -
            center;

        float a = center.X;
        float b = center.Y;
        float reaction = a * b * b;
        float nextA = a + laplacian.X - reaction + (feed * (1f - a));
        float nextB = b + (0.5f * laplacian.Y) + reaction - ((kill + feed) * b);

        next[ThreadIds.XY] = new Float4(Gpu.Saturate(nextA), Gpu.Saturate(nextB), 0f, 1f);
    }

    private Float4 Tap(Float2 uv, Float2 texel, float dx, float dy) =>
        previous.Sample(Sampler.PointRepeat, uv + (new Float2(dx, dy) * texel));
}

/// <summary>
/// The brush: dispatched over the brush's bounding box only (<paramref name="origin"/> places that
/// box on the texture), it writes the pixels inside the circle with the indexer. Painting sets B,
/// erasing restores the empty state.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct PaintBrush(ReadWriteTexture2D<Float4> field, Int2 origin, Float2 center, float radius, int erase) : IComputeShader
{
    public void Execute()
    {
        Int2 position = origin + ThreadIds.XY;
        Float2 offset = new Float2(position.X + 0.5f, position.Y + 0.5f) - center;

        if (Gpu.Dot(offset, offset) > radius * radius)
        {
            return;
        }

        // The field wraps around, and so does the brush.
        int x = ((position.X % field.Width) + field.Width) % field.Width;
        int y = ((position.Y % field.Height) + field.Height) % field.Height;
        Float4 state = field[x, y];

        field[x, y] = erase != 0 ? new Float4(1f, 0f, 0f, 1f) : new Float4(state.X, 1f, 0f, 1f);
    }
}

/// <summary>
/// The ping-pong mode's frame: the field, magnified to the frame with <c>LinearRepeat</c> and
/// colored by the amount of B. <paramref name="field"/> is the <c>AsReadOnly()</c> view of the
/// texture the last step wrote. With <paramref name="zoom"/> above 1 the frame shows more than one
/// copy of the field, with a faint line where they meet; <paramref name="pan"/> slides the view over
/// the (endlessly repeating) field, which is what zooming around the pointer needs.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ShowField(ReadOnlyTexture2D<Float4> field, ReadOnlyTexture2D<R8> labels, float zoom, Float2 pan) : IComputeShader<Float4>
{
    private static readonly Float3 Deep = new(0.02f, 0.03f, 0.09f);
    private static readonly Float3 Middle = new(0.10f, 0.62f, 0.78f);
    private static readonly Float3 Bright = new(1.00f, 0.93f, 0.62f);

    public Float4 Execute()
    {
        Float2 frame = new Float2(DispatchSize.X, DispatchSize.Y);
        Float2 uv = FieldUv((new Float2(ThreadIds.X, ThreadIds.Y) + 0.5f) / frame, zoom, pan);
        float amount = Gpu.Saturate(field.Sample(Sampler.LinearRepeat, uv).Y * 3f);

        Float3 color = Gpu.Lerp(Deep, Middle, Gpu.Smoothstep(0f, 0.45f, amount));
        color = Gpu.Lerp(color, Bright, Gpu.Smoothstep(0.45f, 1f, amount));

        // Where one copy of the field ends and the next begins. Not in the starting view, where the
        // seams are the frame's own edges: only once the view shows more than one field or has been
        // moved off a whole number of fields (the tolerance absorbs the rounding of a zoom in and
        // back out around the same point).
        Float2 seam = Gpu.Abs(Gpu.Fract(uv) - 0.5f);
        Float2 shift = Gpu.Abs(Gpu.Fract(pan + 0.5f) - 0.5f);

        if ((zoom > 1.001f || Gpu.Max(shift.X, shift.Y) > 0.0005f) && Gpu.Max(seam.X, seam.Y) > 0.5f - (zoom / frame.Y))
        {
            color = Gpu.Lerp(color, new Float3(1f, 1f, 1f), 0.3f);
        }

        float ink = labels[ThreadIds.XY].X;

        if (ink > 0.75f)
        {
            color = new Float3(1f, 1f, 1f);
        }
        else if (ink > 0.25f)
        {
            color *= 0.22f;
        }

        return new Float4(color, 1f);
    }

    /// <summary>
    /// The field coordinate shown at <paramref name="position"/> (0..1 across the frame): the middle
    /// of the frame shows <c>0.5 + pan</c>, and the frame spans <paramref name="zoom"/> fields.
    /// </summary>
    public static Float2 FieldUv(Float2 position, float zoom, Float2 pan) => new Float2(0.5f, 0.5f) + ((position - 0.5f) * zoom) + pan;
}
