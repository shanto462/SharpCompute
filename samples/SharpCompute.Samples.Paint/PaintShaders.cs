// Original SharpCompute sample (MIT).
namespace SharpCompute.Samples.Paint;

/// <summary>
/// Paints one run of brush stamps. The dispatch covers only the stamps' bounding box, offset by
/// (<c>originX</c>, <c>originY</c>). The brush is swept between consecutive stamps (coverage uses
/// the distance to the segment joining them), so soft brushes stay smooth instead of scalloping
/// at the stamp spacing; a run of one stamp is a single dab. Each pixel keeps the strongest
/// coverage it has seen during the stroke in <c>mask</c> and is recomposited from
/// <c>strokeBase</c> (the canvas when the stroke began), so overlapping stamps never build up past
/// the brush opacity: a stroke looks the same however fast it was drawn.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BrushStamp(
    ReadWriteBuffer<Float4> canvas, ReadWriteBuffer<Float4> strokeBase, ReadWriteBuffer<float> mask, ReadWriteBuffer<Float2> stamps,
    int stampCount, int width, int originX, int originY, float radius, float hardness, float opacity, Float4 color, int erase) : IComputeShader
{
    public void Execute()
    {
        int x = originX + ThreadIds.X;
        int y = originY + ThreadIds.Y;
        int index = (y * width) + x;
        Float2 pixel = new(x + 0.5f, y + 0.5f);
        float coverage = mask[index];

        if (stampCount == 1)
        {
            coverage = Gpu.Max(coverage, Coverage(Gpu.Length(pixel - stamps[0]), radius, hardness));
        }

        for (int s = 0; s + 1 < stampCount; s++)
        {
            coverage = Gpu.Max(coverage, Coverage(SegmentDistance(pixel, stamps[s], stamps[s + 1]), radius, hardness));
        }

        mask[index] = coverage;
        canvas[index] = Composite(strokeBase[index], color, coverage * opacity, erase);
    }

    /// <summary>
    /// A round brush's coverage at <paramref name="distance"/> from its center: 1 inside
    /// <c>hardness × radius</c>, easing (smoothstep) to 0 at the rim. The soft band is at least one
    /// pixel wide, so a fully hard brush is still anti-aliased.
    /// </summary>
    public static float Coverage(float distance, float radius, float hardness)
    {
        float band = Gpu.Max(1f - hardness, 1f / radius);
        float f = Gpu.Saturate((1f - (distance / radius)) / band);
        return f * f * (3f - (2f * f));
    }

    /// <summary>Distance from <paramref name="p"/> to the segment <paramref name="a"/>–<paramref name="b"/>.</summary>
    public static float SegmentDistance(Float2 p, Float2 a, Float2 b)
    {
        Float2 ab = b - a;
        float t = Gpu.Saturate(Gpu.Dot(p - a, ab) / Gpu.Max(Gpu.Dot(ab, ab), 1e-12f));
        return Gpu.Length(p - (a + (ab * t)));
    }

    /// <summary>Paints premultiplied <paramref name="color"/> over <paramref name="under"/> with <paramref name="alpha"/>, or erases toward transparent.</summary>
    public static Float4 Composite(Float4 under, Float4 color, float alpha, int erase) =>
        erase != 0 ? under * (1f - alpha) : (color * alpha) + (under * (1f - alpha));
}

/// <summary>Fills a float buffer (the stroke mask) with one value.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct FillMask(ReadWriteBuffer<float> mask, float value) : IComputeShader
{
    public void Execute() => mask[ThreadIds.X] = value;
}

/// <summary>Packs premultiplied Float4 pixels into RGBA8 (R in the low byte) for an undo snapshot.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct PackSnapshot(ReadWriteBuffer<Float4> source, ReadWriteBuffer<uint> destination) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        UInt4 q = (UInt4)((Gpu.Clamp(source[i], 0f, 1f) * 255f) + new Float4(0.5f, 0.5f, 0.5f, 0.5f));
        destination[i] = q.X | (q.Y << 8) | (q.Z << 16) | (q.W << 24);
    }
}

/// <summary>Unpacks an undo snapshot back into Float4 pixels.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct UnpackSnapshot(ReadWriteBuffer<uint> source, ReadWriteBuffer<Float4> destination) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        uint v = source[i];
        destination[i] = new Float4((float)(v & 255u), (float)((v >> 8) & 255u), (float)((v >> 16) & 255u), (float)(v >> 24)) / 255f;
    }
}

/// <summary>
/// Draws what the window shows: the canvas over a checkerboard (where it's transparent), a HUD in
/// the top-left corner with the brush color and a preview of its size and softness, and, while
/// picking a color, the hue/saturation wheel over a dimmed canvas. Writes packed opaque RGBA8.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct RenderView(
    ReadWriteBuffer<Float4> canvas, ReadWriteBuffer<uint> pixels, int width, Float4 brushColor, float brushRadius, float hardness,
    float opacity, int erase, int picker, Float2 wheelCenter, float wheelRadius) : IComputeShader
{
    public const int HudLeft = 12;
    public const int HudTop = 12;
    public const int HudWidth = 224;
    public const int HudHeight = 76;

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float4 c = canvas[(y * width) + x];
        Float3 rgb = c.XYZ + (Checker(x, y) * (1f - c.W));
        Float2 p = new(x + 0.5f, y + 0.5f);

        if (picker != 0)
        {
            rgb *= 0.35f;
            Float2 d = p - wheelCenter;
            float r = Gpu.Length(d);
            float inside = Gpu.Saturate(wheelRadius - r + 0.5f);

            if (inside > 0f)
            {
                float hue = (Gpu.Atan2(d.Y, d.X) / 6.2831855f) + 0.5f;
                rgb = Gpu.Lerp(rgb, HueSaturation(hue, Gpu.Saturate(r / wheelRadius)), inside);
            }
        }

        if (x >= HudLeft && y >= HudTop && x < HudLeft + HudWidth && y < HudTop + HudHeight)
        {
            rgb = Gpu.Lerp(rgb, new Float3(0.08f, 0.08f, 0.1f), 0.8f);

            // Color swatch (a checkerboard with the eraser).
            float swatchDistance = Gpu.Length(p - new Float2(HudLeft + 38f, HudTop + 38f));
            Float3 swatch = erase != 0 ? Checker(x / 2, y / 2) : brushColor.XYZ;
            rgb = Gpu.Lerp(rgb, new Float3(1f, 1f, 1f), Gpu.Saturate(29f - swatchDistance));
            rgb = Gpu.Lerp(rgb, swatch, Gpu.Saturate(27f - swatchDistance));

            // Brush preview: true size up to the slot, true hardness and opacity.
            float previewRadius = Gpu.Min(brushRadius, 32f);
            float previewDistance = Gpu.Length(p - new Float2(HudLeft + 148f, HudTop + 38f));
            Float3 ink = erase != 0 ? new Float3(1f, 1f, 1f) : brushColor.XYZ;
            rgb = Gpu.Lerp(rgb, ink, Coverage(previewDistance, previewRadius, hardness) * opacity);
        }

        pixels[(y * width) + x] = Pack(rgb);
    }

    /// <summary>The fully bright color of <paramref name="hue"/> (0..1, red at 0) mixed toward white by 1 - <paramref name="saturation"/>.</summary>
    public static Float3 HueSaturation(float hue, float saturation)
    {
        float r = Gpu.Saturate(Gpu.Abs((hue * 6f) - 3f) - 1f);
        float g = Gpu.Saturate(2f - Gpu.Abs((hue * 6f) - 2f));
        float b = Gpu.Saturate(2f - Gpu.Abs((hue * 6f) - 4f));
        return Gpu.Lerp(new Float3(1f, 1f, 1f), new Float3(r, g, b), saturation);
    }

    private static Float3 Checker(int x, int y)
    {
        float shade = (((x / 16) + (y / 16)) & 1) == 0 ? 0.86f : 0.74f;
        return new Float3(shade, shade, shade);
    }

    // Same curve as BrushStamp.Coverage (shaders can only call their own helpers).
    private static float Coverage(float distance, float radius, float hardness)
    {
        float band = Gpu.Max(1f - hardness, 1f / radius);
        float f = Gpu.Saturate((1f - (distance / radius)) / band);
        return f * f * (3f - (2f * f));
    }

    private static uint Pack(Float3 color)
    {
        UInt3 c = (UInt3)((Gpu.Saturate(color) * 255f) + new Float3(0.5f, 0.5f, 0.5f));
        return c.X | (c.Y << 8) | (c.Z << 16) | (255u << 24);
    }
}
