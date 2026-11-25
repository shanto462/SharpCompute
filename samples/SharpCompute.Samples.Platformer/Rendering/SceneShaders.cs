// Original SharpCompute sample (MIT).
using SharpCompute.Samples.Shared;

namespace SharpCompute.Samples.Platformer.Rendering;

/// <summary>A sprite to draw this frame: screen position of its 16 × 16 cell, atlas frame and flags.</summary>
public struct SpriteInstance
{
    public int X;
    public int Y;
    public int Frame;

    /// <summary>Bit 0: mirror horizontally.</summary>
    public int Flags;
}

/// <summary>A point light in screen pixels. <c>Shadows</c> 1 casts tile shadows, 0 doesn't.</summary>
public struct LightSource
{
    public Float2 Position;
    public float Radius;
    public float Intensity;
    public Float3 Color;
    public float Shadows;
}

/// <summary>One particle, in world pixels. Spawned by the CPU into a ring buffer; moved by <see cref="StepParticles"/>.</summary>
public struct Particle
{
    public Float2 Position;
    public Float2 Velocity;
    public Float3 Color;
    public float Life;
    public float MaxLife;
    public float Size;
    public float Gravity;
    public float Drag;
}

/// <summary>One HUD character: screen position, glyph index (<see cref="PixelFont"/>), packed RGBA color and pixel scale.</summary>
public struct Glyph
{
    public int X;
    public int Y;
    public int Index;
    public uint Color;
    public int Scale;
}

/// <summary>Moves every live particle one frame: gravity, drag, and aging.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct StepParticles(ReadWriteBuffer<Particle> particles, float dt) : IComputeShader
{
    public void Execute()
    {
        Particle p = particles[ThreadIds.X];

        if (p.Life <= 0f)
        {
            return;
        }

        p.Velocity.Y += p.Gravity * dt;
        p.Velocity *= Gpu.Max(0f, 1f - (p.Drag * dt));
        p.Position += p.Velocity * dt;
        p.Life -= dt;
        particles[ThreadIds.X] = p;
    }
}

/// <summary>
/// Renders one frame of Glowfall at the game's native resolution into a packed RGBA buffer, one
/// thread per pixel: background, tiles, sprites, lighting with tile shadows, particles, HUD.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct RenderScene(
    ReadWriteBuffer<uint> output,
    ReadOnlyBuffer<uint> tiles,
    ReadOnlyBuffer<uint> atlas,
    ReadOnlyBuffer<SpriteInstance> sprites,
    ReadOnlyBuffer<LightSource> lights,
    ReadWriteBuffer<Particle> particles,
    ReadOnlyBuffer<Glyph> glyphs,
    ReadOnlyBuffer<uint> font,
    int width,
    int cameraX,
    int cameraY,
    int levelWidth,
    int levelHeight,
    int spriteCount,
    int lightCount,
    int glyphCount,
    float time,
    float ambient,
    float fade) : IComputeShader
{
    private const int AtlasWidth = 128;

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int wx = x + cameraX;
        int wy = y + cameraY;

        // 1. What is at this pixel: albedo, and whether it glows by itself.
        Float3 albedo = Background(x, y);
        float emissive = 0f;
        float surface = 0f;

        uint tile = Tile(wx >> 4, wy >> 4);
        int lx = wx & 15;
        int ly = wy & 15;

        if (tile == 1u)
        {
            albedo = Rock(wx, wy, lx, ly);
            surface = 1f;
        }
        else if (tile == 2u && ly < 4)
        {
            albedo = Ledge(wx, ly);
            surface = 1f;
        }
        else if (tile == 3u)
        {
            float spike = Spike(lx, ly);

            if (spike > 0f)
            {
                albedo = Gpu.Lerp(new Float3(0.42f, 0.16f, 0.55f), new Float3(0.95f, 0.55f, 0.95f), spike);
                emissive = 0.45f;
                surface = 1f;
            }
        }

        for (int i = 0; i < spriteCount; i++)
        {
            SpriteInstance s = sprites[i];
            int sx = x - s.X;
            int sy = y - s.Y;

            if (sx >= 0 && sx < 16 && sy >= 0 && sy < 16)
            {
                if ((s.Flags & 1) != 0)
                {
                    sx = 15 - sx;
                }

                uint texel = atlas[((((s.Frame / 8) * 16) + sy) * AtlasWidth) + ((s.Frame % 8) * 16) + sx];
                uint alpha = texel >> 24;

                if (alpha != 0u)
                {
                    albedo = PixelShading.UnpackRgb(texel);
                    emissive = alpha == 128u ? 1f : 0f;
                    surface = 1f;
                }
            }
        }

        // 2. Light: ambient plus every point light, shadowed by rock.
        Float2 p = new((float)x + 0.5f, (float)y + 0.5f);
        Float3 light = new(ambient, ambient, ambient * 1.4f);
        Float3 haze = new(0f, 0f, 0f);

        for (int i = 0; i < lightCount; i++)
        {
            LightSource l = lights[i];
            float d = Gpu.Distance(p, l.Position);

            if (d >= l.Radius)
            {
                continue;
            }

            float falloff = 1f - (d / l.Radius);
            falloff *= falloff * l.Intensity;

            if (l.Shadows > 0f && Occluded(p, l.Position, d))
            {
                falloff *= 0.12f;
            }

            light += l.Color * falloff;
            haze += l.Color * (falloff * falloff * 0.35f);
        }

        Float3 color = (albedo * (light * (1f - emissive))) + (albedo * emissive * 1.15f) + (haze * (1f - (surface * 0.6f)));

        // 3. Particles add light where they are (little squares, as pixel art).
        int particleCount = particles.Length;

        for (int i = 0; i < particleCount; i++)
        {
            Particle q = particles[i];

            if (q.Life > 0f)
            {
                float dx = Gpu.Abs((q.Position.X - (float)cameraX) - p.X);
                float dy = Gpu.Abs((q.Position.Y - (float)cameraY) - p.Y);
                float size = q.Size * Gpu.Saturate((q.Life / q.MaxLife) + 0.3f);

                if (dx < size && dy < size)
                {
                    color += q.Color * Gpu.Saturate(q.Life / q.MaxLife) * 1.4f;
                }
            }
        }

        // 4. Vignette and the transition fade, then the HUD on top.
        float u = ((float)x / (float)width) - 0.5f;
        float v = ((float)y / 270f) - 0.5f;
        color *= (1f - (((u * u) + (v * v)) * 0.9f)) * fade;

        for (int i = 0; i < glyphCount; i++)
        {
            Glyph g = glyphs[i];
            int gx = x - g.X;
            int gy = y - g.Y;

            if (PixelShading.GlyphCovers(font, g.Index, gx, gy, g.Scale))
            {
                color = PixelShading.UnpackRgb(g.Color);
            }
        }

        output[(y * width) + x] = PixelShading.PackRgb(color);
    }

    /// <summary>Tile kind at a tile coordinate; outside the level is rock, above it is open air.</summary>
    private uint Tile(int tx, int ty)
    {
        if (tx < 0 || tx >= levelWidth || ty >= levelHeight)
        {
            return 1u;
        }

        return ty < 0 ? 0u : tiles[(ty * levelWidth) + tx];
    }

    /// <summary>True when rock lies between the pixel and the light (ignoring the first few pixels, so lit faces of rock stay lit).</summary>
    private bool Occluded(Float2 p, Float2 lightPosition, float distance)
    {
        Float2 direction = (lightPosition - p) / Gpu.Max(distance, 0.001f);
        int steps = (int)(distance / 3f);

        for (int k = 2; k < steps; k++)
        {
            Float2 q = p + (direction * ((float)k * 3f));
            int tx = ((int)Gpu.Floor(q.X) + cameraX) >> 4;
            int ty = ((int)Gpu.Floor(q.Y) + cameraY) >> 4;

            if (Tile(tx, ty) == 1u)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Deep cave background with two parallax layers of hanging crystal spires.</summary>
    private Float3 Background(int x, int y)
    {
        float depth = (float)y / 270f;
        Float3 color = Gpu.Lerp(new Float3(0.035f, 0.025f, 0.075f), new Float3(0.07f, 0.045f, 0.13f), depth);

        color = Spires(color, x + (cameraX * 3 / 10), y + (cameraY * 3 / 10), 20, 150, new Float3(0.075f, 0.055f, 0.15f), 3u);
        color = Spires(color, x + (cameraX * 6 / 10), y + (cameraY * 6 / 10), 28, 190, new Float3(0.12f, 0.08f, 0.21f), 11u);

        // Sparse far-off glimmers, twinkling slowly: one pixel in the middle of a rare 6 × 6 cell.
        int fx = x + (cameraX / 5);
        int fy = y + (cameraY / 5);
        int gx = fx / 6;
        int gy = fy / 6;
        float h = PixelShading.Hash(gx, gy, 29u);

        if (h > 0.996f && fx - (gx * 6) == 3 && fy - (gy * 6) == 3)
        {
            float twinkle = 0.5f + (0.5f * Gpu.Sin((time * 1.7f) + (h * 300f)));
            color += new Float3(0.25f, 0.3f, 0.5f) * twinkle;
        }

        return color;
    }

    /// <summary>A row of downward spires every <paramref name="spacing"/> pixels, repeating vertically every <paramref name="band"/>.</summary>
    private static Float3 Spires(Float3 color, int px, int py, int spacing, int band, Float3 spireColor, uint seed)
    {
        int column = px / spacing;
        int localX = px - (column * spacing);
        int bandIndex = py / band;
        int localY = py - (bandIndex * band);
        float h = PixelShading.Hash(column, bandIndex, seed);
        float length = 20f + (h * (float)band * 0.45f);
        float half = (float)spacing * (0.25f + (PixelShading.Hash(column, bandIndex, seed + 1u) * 0.2f));
        float dx = Gpu.Abs((float)localX - ((float)spacing * 0.5f));

        return (float)localY < length * (1f - (dx / half)) ? spireColor : color;
    }

    /// <summary>Stone in 2 × 2 pixel grains, with a pale rim and a little moss on open tops.</summary>
    private Float3 Rock(int wx, int wy, int lx, int ly)
    {
        float n = PixelShading.Hash(wx >> 1, wy >> 1, 5u);
        float cell = PixelShading.Hash(wx >> 4, wy >> 4, 17u);
        Float3 color = Gpu.Lerp(new Float3(0.22f, 0.18f, 0.29f), new Float3(0.33f, 0.27f, 0.41f), (n * 0.6f) + (cell * 0.4f));

        bool openAbove = Tile((wx >> 4), (wy >> 4) - 1) != 1u;
        bool openBelow = Tile((wx >> 4), (wy >> 4) + 1) != 1u;
        bool openLeft = Tile((wx >> 4) - 1, (wy >> 4)) != 1u;
        bool openRight = Tile((wx >> 4) + 1, (wy >> 4)) != 1u;

        if (openAbove && ly < 2)
        {
            float moss = PixelShading.Hash(wx, 3, 41u);
            color = moss > 0.35f ? new Float3(0.2f, 0.42f, 0.36f) : new Float3(0.36f, 0.31f, 0.43f);
        }
        else if (openAbove && ly < 4 && PixelShading.Hash(wx, wy, 43u) > 0.6f)
        {
            color = new Float3(0.17f, 0.33f, 0.29f);
        }
        else if ((openLeft && lx < 1) || (openRight && lx > 14))
        {
            color *= 1.35f;
        }
        else if (openBelow && ly > 13)
        {
            color *= 0.6f;
        }

        return color;
    }

    /// <summary>A plank ledge: a light top edge, grain and joints every eight pixels.</summary>
    private static Float3 Ledge(int wx, int ly)
    {
        if (ly == 0)
        {
            return new Float3(0.62f, 0.44f, 0.3f);
        }

        if ((wx & 7) == 0)
        {
            return new Float3(0.2f, 0.12f, 0.1f);
        }

        float grain = PixelShading.Hash(wx >> 2, ly, 59u);
        return Gpu.Lerp(new Float3(0.36f, 0.23f, 0.16f), new Float3(0.45f, 0.29f, 0.19f), grain) * (ly == 3 ? 0.7f : 1f);
    }

    /// <summary>Four crystal spikes per tile; returns 0 outside them, and how bright the crystal is inside.</summary>
    private static float Spike(int lx, int ly)
    {
        int index = lx / 4;
        float center = ((float)index * 4f) + 1.5f;
        float offset = Gpu.Abs((float)lx - center);
        float height = 10f - (offset * 4f);
        float fromBottom = 15f - (float)ly;

        if (fromBottom >= height)
        {
            return 0f;
        }

        return 0.4f + (0.6f * (fromBottom / Gpu.Max(height, 1f))) * (offset < 0.75f ? 1f : 0.7f);
    }

}

/// <summary>
/// Scales the native-resolution frame up by a whole number into the window's texture, with black
/// borders, so pixel art stays crisp (one source pixel becomes an exact <c>scale × scale</c> block).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct UpscaleFrame(ReadWriteBuffer<uint> source, int sourceWidth, int sourceHeight, int scale, int originX, int originY) : IComputeShader<Float4>
{
    public Float4 Execute()
    {
        int x = ThreadIds.X - originX;
        int y = ThreadIds.Y - originY;

        if (x < 0 || y < 0)
        {
            return new Float4(0f, 0f, 0f, 1f);
        }

        int sx = x / scale;
        int sy = y / scale;

        if (sx >= sourceWidth || sy >= sourceHeight)
        {
            return new Float4(0f, 0f, 0f, 1f);
        }

        return new Float4(PixelShading.UnpackRgb(source[(sy * sourceWidth) + sx]), 1f);
    }
}
