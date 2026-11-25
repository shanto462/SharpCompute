// Original SharpCompute sample (MIT).
using System;

namespace SharpCompute.Samples.NativeInterop.Host;

/// <summary>
/// The host's picture: a dusk landscape (sky, sun, two ridges of hills and a lake that mirrors them),
/// painted on the CPU once at start-up. It stands in for whatever an app would load from disk, and is
/// the same on every run.
/// </summary>
internal static class HostImage
{
    /// <summary>RGBA8 pixels, row-major and tightly packed, sRGB-encoded.</summary>
    public static byte[] Create(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float v = (y + 0.5f) / height;
                (float r, float g, float b) = Shade(u, v, width / (float)height, 1f / height);
                int i = ((y * width) + x) * 4;
                pixels[i] = ToByte(r);
                pixels[i + 1] = ToByte(g);
                pixels[i + 2] = ToByte(b);
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private const float Horizon = 0.68f;

    /// <param name="u">Horizontal position, 0..1.</param>
    /// <param name="v">Vertical position, 0..1 from the top.</param>
    /// <param name="aspect">Width over height.</param>
    /// <param name="pixel">The height of a pixel in <paramref name="v"/>, for soft edges.</param>
    private static (float R, float G, float B) Shade(float u, float v, float aspect, float pixel)
    {
        if (v > Horizon)
        {
            // The lake: the scene above, mirrored, darker and cooler, broken by a few slow ripples.
            float depth = (v - Horizon) / (1f - Horizon);
            float ripple = 0.003f * MathF.Sin((v * 90f) + (MathF.Sin(u * 7f) * 2f));
            (float r, float g, float b) = Land(u + ripple, Horizon - ((v - Horizon) * 1.3f), aspect, pixel);
            float fade = 0.55f - (0.25f * depth);
            return (r * fade * 0.85f, g * fade * 0.95f, (b * fade) + (0.04f * depth));
        }

        return Land(u, v, aspect, pixel);
    }

    private static (float R, float G, float B) Land(float u, float v, float aspect, float pixel)
    {
        // Sky: deep blue at the top to a warm band at the horizon.
        float t = Math.Clamp(v / Horizon, 0f, 1f);
        float r = Lerp(0.05f, 0.98f, t * t);
        float g = Lerp(0.07f, 0.52f, t * t * t);
        float b = Lerp(0.22f, 0.38f, t);

        // The sun and its glow.
        float dx = (u - 0.64f) * aspect, dy = v - 0.50f;
        float distance = MathF.Sqrt((dx * dx) + (dy * dy));
        float glow = MathF.Exp(-distance * 7f) * 0.55f;
        float disc = Smooth(0.075f + pixel, 0.075f - pixel, distance);
        r += glow + disc;
        g += (glow * 0.75f) + (disc * 0.85f);
        b += (glow * 0.35f) + (disc * 0.6f);

        // Two ridges of hills, the far one hazy.
        float far = 0.52f + (0.05f * MathF.Sin((u * 9f) + 0.5f)) + (0.025f * MathF.Sin((u * 23f) + 1.7f));
        float near = 0.60f + (0.045f * MathF.Sin((u * 5f) + 2.2f)) + (0.018f * MathF.Sin(u * 37f));

        // Each ridge's edge is blended over two pixels, so the outline is not stair-stepped.
        float haze = Math.Clamp((v - far) * 6f, 0f, 1f);
        float onFar = Smooth(far - pixel, far + pixel, v);
        (r, g, b) = (Lerp(r, Lerp(0.42f, 0.20f, haze), onFar), Lerp(g, Lerp(0.24f, 0.13f, haze), onFar), Lerp(b, Lerp(0.42f, 0.30f, haze), onFar));

        float shade = Math.Clamp((v - near) * 5f, 0f, 1f);
        float onNear = Smooth(near - pixel, near + pixel, v);
        (r, g, b) = (Lerp(r, Lerp(0.13f, 0.05f, shade), onNear), Lerp(g, Lerp(0.09f, 0.04f, shade), onNear), Lerp(b, Lerp(0.18f, 0.10f, shade), onNear));

        return (r, g, b);
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float Smooth(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
