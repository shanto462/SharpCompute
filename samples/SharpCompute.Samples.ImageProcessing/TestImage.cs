// Original SharpCompute sample (MIT).
using System;
using System.Threading.Tasks;

namespace SharpCompute.Samples.ImageProcessing;

/// <summary>
/// A deterministic synthetic photo for demos and tests, at any size: a dusk gradient, a soft moon,
/// a hue ring (for grading), a row of small bright lights (bokeh turns them into discs), a stripe
/// chart that gets finer to the right (blur, sharpen and edges) and a color checker. Opaque.
/// </summary>
public static class TestImage
{
    public static ImageData Create(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Float4[] pixels = new Float4[width * height];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = Pixel(x + 0.5f, y + 0.5f, width, height);
            }
        });

        return new ImageData(width, height, pixels);
    }

    private static Float4 Pixel(float x, float y, int width, int height)
    {
        float h = height;
        float u = x / width;
        float v = y / h;

        // Dusk: navy at the top to amber at the horizon, a little lighter toward the moon.
        Float3 color = Mix(new Float3(0.04f, 0.07f, 0.2f), new Float3(0.95f, 0.52f, 0.22f), MathF.Pow(v, 1.6f));
        color += new Float3(0.08f, 0.07f, 0.1f) * MathF.Max(0f, 1f - Distance(x, y, 0.75f * width, 0.3f * h) / (0.5f * h));

        // Moon with a soft halo.
        float moon = Distance(x, y, 0.75f * width, 0.3f * h);
        color = Mix(color, new Float3(1f, 0.96f, 0.82f), Coverage(moon, 0.11f * h));

        // Hue ring.
        float ringX = 0.2f * width;
        float ringY = 0.32f * h;
        float ring = Distance(x, y, ringX, ringY);
        float ringCoverage = Coverage(ring, 0.15f * h) * (1f - Coverage(ring, 0.09f * h));
        float hue = (MathF.Atan2(y - ringY, x - ringX) / (2f * MathF.PI)) + 0.5f;
        color = Mix(color, Hue(hue), ringCoverage);

        // City lights: one jittered dot per cell in a band below the horizon line.
        float bandTop = 0.52f * h;
        float cell = MathF.Max(4f, h / 18f);

        if (y >= bandTop && y < bandTop + (3 * cell))
        {
            int cx = (int)(x / cell);
            int cy = (int)((y - bandTop) / cell);
            uint seed = Hash((uint)(cx * 73856093) ^ (uint)(cy * 19349663));
            float jx = (cx + 0.25f + (0.5f * Unit(seed))) * cell;
            float jy = bandTop + ((cy + 0.25f + (0.5f * Unit(Hash(seed)))) * cell);
            Float3 light = (Hash(seed ^ 0xA5A5u) % 4) switch
            {
                0 => new Float3(1f, 0.95f, 0.85f),
                1 => new Float3(1f, 0.7f, 0.3f),
                2 => new Float3(0.45f, 0.9f, 1f),
                _ => new Float3(1f, 0.45f, 0.85f),
            };
            color = Mix(color, light, Coverage(Distance(x, y, jx, jy), MathF.Max(0.8f, 0.007f * h)));
        }

        // Ground.
        float ground = 0.68f * h;

        if (y >= ground)
        {
            color = Mix(color, new Float3(0.07f, 0.06f, 0.08f), 0.85f);
        }

        // Stripe chart: black/white bars whose period halves every fifth of the panel.
        if (u is >= 0.05f and < 0.45f && v is >= 0.74f and < 0.94f)
        {
            float t = (u - 0.05f) / 0.4f;
            float period = MathF.Max(2f, h / 40f / MathF.Pow(2f, MathF.Floor(t * 5f)));
            bool white = ((int)(x / (period / 2f)) & 1) == 0;
            color = white ? new Float3(0.92f, 0.92f, 0.92f) : new Float3(0.05f, 0.05f, 0.05f);
        }

        // Color checker: 2 × 4 patches.
        if (u is >= 0.55f and < 0.95f && v is >= 0.74f and < 0.94f)
        {
            int px = (int)((u - 0.55f) / 0.1f);
            int py = (int)((v - 0.74f) / 0.1f);
            color = Math.Clamp((py * 4) + px, 0, 7) switch
            {
                0 => new Float3(0.45f, 0.32f, 0.26f),
                1 => new Float3(0.77f, 0.58f, 0.5f),
                2 => new Float3(0.36f, 0.48f, 0.61f),
                3 => new Float3(0.35f, 0.42f, 0.26f),
                4 => new Float3(0.85f, 0.2f, 0.18f),
                5 => new Float3(0.2f, 0.6f, 0.25f),
                6 => new Float3(0.15f, 0.25f, 0.7f),
                _ => new Float3(0.95f, 0.8f, 0.1f),
            };
        }

        return new Float4(Saturate(color), 1f);
    }

    private static float Distance(float x0, float y0, float x1, float y1) => MathF.Sqrt(((x0 - x1) * (x0 - x1)) + ((y0 - y1) * (y0 - y1)));

    /// <summary>Anti-aliased coverage of a disc of <paramref name="radius"/> at distance <paramref name="d"/> from its center.</summary>
    private static float Coverage(float d, float radius) => Math.Clamp(radius - d + 0.5f, 0f, 1f);

    private static Float3 Mix(Float3 a, Float3 b, float t) => a + ((b - a) * t);

    private static Float3 Saturate(Float3 c) => new(Math.Clamp(c.X, 0f, 1f), Math.Clamp(c.Y, 0f, 1f), Math.Clamp(c.Z, 0f, 1f));

    /// <summary>Fully saturated color for <paramref name="hue"/> in [0, 1).</summary>
    private static Float3 Hue(float hue)
    {
        float r = Math.Abs((hue * 6f) - 3f) - 1f;
        float g = 2f - Math.Abs((hue * 6f) - 2f);
        float b = 2f - Math.Abs((hue * 6f) - 4f);
        return Saturate(new Float3(r, g, b));
    }

    private static uint Hash(uint input)
    {
        uint state = (input * 747796405u) + 2891336453u;
        uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
        return (word >> 22) ^ word;
    }

    private static float Unit(uint hash) => (hash & 0xFFFFFF) / 16777216f;
}
