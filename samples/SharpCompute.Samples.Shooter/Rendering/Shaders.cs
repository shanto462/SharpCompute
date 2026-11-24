// Original SharpCompute sample (MIT).
namespace SharpCompute.Samples.Shooter.Rendering;

/// <summary>
/// One particle, in arena pixels. Written by the CPU into a ring buffer when a burst spawns, then
/// only ever touched by the GPU: <see cref="StepParticles"/> moves it, <see cref="DrawScene"/> draws it.
/// </summary>
public struct Particle
{
    public Float2 Position;
    public Float2 Velocity;
    public Float3 Color;

    /// <summary>Seconds left; at or below zero the slot is free.</summary>
    public float Life;
    public float MaxLife;

    /// <summary>Radius of the glowing core, pixels.</summary>
    public float Size;

    /// <summary>Velocity damping per second (exponential).</summary>
    public float Drag;

    /// <summary>Drawn as a line from the position back along the velocity for this many seconds.</summary>
    public float Streak;
}

/// <summary>
/// A vector primitive for the neon shapes, drawn as a signed distance with a glow.
/// <see cref="Kind"/>: 0 a line segment A→B (half-thickness <see cref="Width"/>), 1 a circle outline
/// around A of <see cref="Radius"/>, 2 a filled disc around A. <see cref="Bounds"/> (min x, min y,
/// max x, max y) is how far the glow reaches; the CPU computes it so the GPU can bin primitives into tiles.
/// </summary>
public struct Prim
{
    public Float2 A;
    public Float2 B;
    public Float3 Color;
    public float Width;
    public float Radius;

    /// <summary>Brightness of the halo around the shape relative to its core.</summary>
    public float Glow;

    /// <summary>How quickly the halo fades, per pixel (exponential).</summary>
    public float Falloff;
    public int Kind;
    public Float4 Bounds;
}

/// <summary>A radial distortion ring (explosions, the bomb): displacement peaks at <see cref="Radius"/>.</summary>
public struct Ripple
{
    public Float2 Center;
    public float Radius;
    public float Width;
    public float Strength;
}

/// <summary>One HUD character: top-left pixel, glyph index (<see cref="PixelFont"/>), pixel scale, color.</summary>
public struct Glyph
{
    public int X;
    public int Y;
    public int Index;
    public int Scale;
    public Float3 Color;
}

/// <summary>Moves every live particle: drag, motion, a soft bounce off the arena walls, and aging.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct StepParticles(ReadWriteBuffer<Particle> particles, float dt, float minX, float minY, float maxX, float maxY) : IComputeShader
{
    public void Execute()
    {
        Particle p = particles[ThreadIds.X];

        if (p.Life <= 0f)
        {
            return;
        }

        p.Velocity *= Gpu.Exp(-p.Drag * dt);
        p.Position += p.Velocity * dt;

        if ((p.Position.X < minX && p.Velocity.X < 0f) || (p.Position.X > maxX && p.Velocity.X > 0f))
        {
            p.Velocity.X = -p.Velocity.X * 0.6f;
        }

        if ((p.Position.Y < minY && p.Velocity.Y < 0f) || (p.Position.Y > maxY && p.Velocity.Y > 0f))
        {
            p.Velocity.Y = -p.Velocity.Y * 0.6f;
        }

        p.Life -= dt;
        particles[ThreadIds.X] = p;
    }
}

/// <summary>
/// The screen-space bounding box of each chunk of 64 consecutive particles (min x, min y, max x,
/// max y; inverted when the whole chunk is dead). A burst is written into consecutive slots, so a
/// chunk is usually one small cluster.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BoundParticleChunks(ReadWriteBuffer<Particle> particles, ReadWriteBuffer<Float4> chunkBounds) : IComputeShader
{
    private const int ChunkSize = 64;

    public void Execute()
    {
        int first = ThreadIds.X * ChunkSize;
        float minX = 1e9f, minY = 1e9f, maxX = -1e9f, maxY = -1e9f;

        for (int i = 0; i < ChunkSize; i++)
        {
            Particle p = particles[first + i];

            if (p.Life > 0f)
            {
                Float2 tail = p.Position - (p.Velocity * p.Streak);
                float reach = p.Size + 1.5f;
                minX = Gpu.Min(minX, Gpu.Min(p.Position.X, tail.X) - reach);
                minY = Gpu.Min(minY, Gpu.Min(p.Position.Y, tail.Y) - reach);
                maxX = Gpu.Max(maxX, Gpu.Max(p.Position.X, tail.X) + reach);
                maxY = Gpu.Max(maxY, Gpu.Max(p.Position.Y, tail.Y) + reach);
            }
        }

        chunkBounds[ThreadIds.X] = new Float4(minX, minY, maxX, maxY);
    }
}

/// <summary>
/// Bins particle chunks and primitives into 16 × 16 pixel tiles: one thread per tile writes the
/// list of chunks and the list of primitives whose bounds overlap it. This replaces the atomics a
/// scatter renderer would need (SharpCompute has none yet): every pixel then gathers from its tile's
/// short lists instead of testing every particle, and the result is deterministic.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BinTiles(
    ReadWriteBuffer<Float4> chunkBounds,
    int chunkCount,
    ReadOnlyBuffer<Prim> prims,
    int primCount,
    ReadWriteBuffer<int> tileChunks,
    ReadWriteBuffer<int> tilePrims,
    ReadWriteBuffer<int> tileCounts,
    int tilesX,
    int maxChunks,
    int maxPrims) : IComputeShader
{
    private const float TileSize = 16f;

    public void Execute()
    {
        int tile = (ThreadIds.Y * tilesX) + ThreadIds.X;
        float x0 = (float)ThreadIds.X * TileSize;
        float y0 = (float)ThreadIds.Y * TileSize;
        float x1 = x0 + TileSize;
        float y1 = y0 + TileSize;

        int n = 0;

        for (int c = 0; c < chunkCount && n < maxChunks; c++)
        {
            Float4 b = chunkBounds[c];

            if (b.X < x1 && b.Z > x0 && b.Y < y1 && b.W > y0)
            {
                tileChunks[(tile * maxChunks) + n] = c;
                n++;
            }
        }

        tileCounts[tile * 2] = n;
        n = 0;

        for (int k = 0; k < primCount && n < maxPrims; k++)
        {
            Float4 b = prims[k].Bounds;

            if (b.X < x1 && b.Z > x0 && b.Y < y1 && b.W > y0)
            {
                tilePrims[(tile * maxPrims) + n] = k;
                n++;
            }
        }

        tileCounts[(tile * 2) + 1] = n;
    }
}

/// <summary>
/// Draws the scene in linear HDR, one thread per pixel: the drifting nebula and parallax dust, the
/// arena walls, the tile's vector primitives (signed distances with exponential glow) and the
/// tile's particles, all added together the way light adds.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawScene(
    ReadWriteBuffer<Float4> hdr,
    ReadOnlyBuffer<Prim> prims,
    ReadWriteBuffer<Particle> particles,
    ReadWriteBuffer<int> tileChunks,
    ReadWriteBuffer<int> tilePrims,
    ReadWriteBuffer<int> tileCounts,
    int width,
    int height,
    int tilesX,
    int maxChunks,
    int maxPrims,
    float time,
    float energy,
    float parallaxX,
    float parallaxY,
    float wall) : IComputeShader
{
    private const int ChunkSize = 64;

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float2 p = new((float)x + 0.5f, (float)y + 0.5f);

        Float3 color = Background(p) + Walls(p);

        int tile = ((y / 16) * tilesX) + (x / 16);
        int primCount = tileCounts[(tile * 2) + 1];

        for (int k = 0; k < primCount; k++)
        {
            color += Shade(prims[tilePrims[(tile * maxPrims) + k]], p);
        }

        int chunkCount = tileCounts[tile * 2];

        for (int k = 0; k < chunkCount; k++)
        {
            int first = tileChunks[(tile * maxChunks) + k] * ChunkSize;

            for (int i = 0; i < ChunkSize; i++)
            {
                Particle q = particles[first + i];

                if (q.Life > 0f)
                {
                    color += Spark(q, p);
                }
            }
        }

        hdr[(y * width) + x] = new Float4(color, 1f);
    }

    private static Float3 Shade(Prim q, Float2 p)
    {
        float d;

        if (q.Kind == 0)
        {
            d = Segment(p, q.A, q.B) - q.Width;
        }
        else if (q.Kind == 1)
        {
            d = Gpu.Abs(Gpu.Length(p - q.A) - q.Radius) - q.Width;
        }
        else
        {
            d = Gpu.Length(p - q.A) - q.Radius;
        }

        // A one-pixel anti-aliased core plus an exponential halo.
        float core = Gpu.Saturate(0.5f - d);
        float glow = q.Glow * Gpu.Exp(-Gpu.Max(d, 0f) * q.Falloff);
        return q.Color * (core + glow);
    }

    private static Float3 Spark(Particle q, Float2 p)
    {
        Float2 tail = q.Position - (q.Velocity * q.Streak);
        float reach = q.Size + 1f;

        if (p.X < Gpu.Min(q.Position.X, tail.X) - reach || p.X > Gpu.Max(q.Position.X, tail.X) + reach ||
            p.Y < Gpu.Min(q.Position.Y, tail.Y) - reach || p.Y > Gpu.Max(q.Position.Y, tail.Y) + reach)
        {
            return new Float3(0f, 0f, 0f);
        }

        float k = Gpu.Saturate(1f - (Segment(p, q.Position, tail) / reach));
        float t = Gpu.Saturate(q.Life / q.MaxLife);
        float intensity = k * k * t;

        // Hot particles burn white before cooling to their color.
        return (q.Color * (intensity * 1.3f)) + (new Float3(1f, 1f, 1f) * (intensity * t * t * 0.6f));
    }

    private static float Segment(Float2 p, Float2 a, Float2 b)
    {
        Float2 pa = p - a;
        Float2 ba = b - a;
        float h = Gpu.Saturate(Gpu.Dot(pa, ba) / Gpu.Max(Gpu.Dot(ba, ba), 1e-6f));
        return Gpu.Length(pa - (ba * h));
    }

    /// <summary>Two layers of slowly drifting value-noise nebula and three layers of parallax dust.</summary>
    private Float3 Background(Float2 p)
    {
        Float2 far = p + new Float2(parallaxX * 0.04f, parallaxY * 0.04f);
        float n1 = Fbm((far * 0.0055f) + new Float2(time * 0.010f, time * 0.004f), 3u);
        float n2 = Fbm((far * 0.009f) + new Float2((n1 * 1.6f) - (time * 0.006f), 40f + (time * 0.008f)), 11u);

        float depth = p.Y / (float)height;
        Float3 color = Gpu.Lerp(new Float3(0.006f, 0.008f, 0.024f), new Float3(0.014f, 0.008f, 0.032f), depth);
        color += new Float3(0.0f, 0.055f, 0.075f) * Gpu.Smoothstep(0.42f, 0.85f, n1);
        color += new Float3(0.075f, 0.012f, 0.07f) * Gpu.Smoothstep(0.5f, 0.92f, n2);
        color += new Float3(0.03f, 0.02f, 0.06f) * (energy * Gpu.Smoothstep(0.35f, 0.8f, n1));

        for (int layer = 0; layer < 3; layer++)
        {
            float l = (float)layer;
            float cell = 11f + (l * 7f);
            Float2 d = p + new Float2((parallaxX * (0.06f + (0.07f * l))) + (time * (2f + (3f * l))), (parallaxY * (0.06f + (0.07f * l))) + (time * (0.6f + l)));
            int cx = (int)Gpu.Floor(d.X / cell);
            int cy = (int)Gpu.Floor(d.Y / cell);
            uint seed = 50u + (uint)layer;
            float h = Hash(cx, cy, seed);

            if (h > 0.9f)
            {
                Float2 star = new(((float)cx + 0.2f + (0.6f * Hash(cx, cy, seed + 10u))) * cell, ((float)cy + 0.2f + (0.6f * Hash(cx, cy, seed + 20u))) * cell);
                float b = Gpu.Saturate(1f - (Gpu.Length(d - star) / (0.8f + (0.4f * l))));
                float twinkle = 0.6f + (0.4f * Gpu.Sin((time * (1f + (h * 3f))) + (h * 50f)));
                color += new Float3(0.35f, 0.45f, 0.75f) * (b * b * twinkle * (0.2f + (0.25f * l)));
            }
        }

        return color;
    }

    /// <summary>The arena's glowing walls, shading from blue to magenta across, and a darker outside.</summary>
    private Float3 Walls(Float2 p)
    {
        float bx = Gpu.Min(p.X - wall, ((float)width - wall) - p.X);
        float by = Gpu.Min(p.Y - wall, ((float)height - wall) - p.Y);
        float inside = Gpu.Min(bx, by);
        float d = Gpu.Abs(inside);
        Float3 tint = Gpu.Lerp(new Float3(0.25f, 0.4f, 1.0f), new Float3(0.95f, 0.25f, 0.8f), p.X / (float)width);
        Float3 light = tint * ((Gpu.Saturate(1.0f - d) * 0.9f) + (0.1f * Gpu.Exp(-d * 0.25f)));
        return inside < 0f ? light - new Float3(0.004f, 0.004f, 0.012f) : light;
    }

    private static float Fbm(Float2 p, uint seed)
    {
        float sum = 0f;
        float amplitude = 0.5f;

        for (int i = 0; i < 4; i++)
        {
            sum += amplitude * ValueNoise(p, seed + (uint)i);
            p = (p * 2.03f) + new Float2(17.1f, 9.3f);
            amplitude *= 0.5f;
        }

        return sum / 0.9375f;
    }

    private static float ValueNoise(Float2 p, uint seed)
    {
        float fx = Gpu.Floor(p.X);
        float fy = Gpu.Floor(p.Y);
        int ix = (int)fx;
        int iy = (int)fy;
        float u = p.X - fx;
        float v = p.Y - fy;
        u = u * u * (3f - (2f * u));
        v = v * v * (3f - (2f * v));
        float a = Hash(ix, iy, seed);
        float b = Hash(ix + 1, iy, seed);
        float c = Hash(ix, iy + 1, seed);
        float d = Hash(ix + 1, iy + 1, seed);
        return Gpu.Lerp(Gpu.Lerp(a, b, u), Gpu.Lerp(c, d, u), v);
    }

    private static float Hash(int x, int y, uint seed)
    {
        uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u) + (seed * 2246822519u);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (float)(h & 0xFFFFFFu) / 16777215f;
    }
}

/// <summary>
/// Bloom, step 1: halves the resolution (a 2 × 2 average) and keeps only what is brighter than
/// <c>threshold</c>, with its hue (the excess over the threshold, not a hard cut).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BloomPrefilter(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int sourceWidth, int destinationWidth, float threshold) : IComputeShader
{
    public void Execute()
    {
        int sx = ThreadIds.X * 2;
        int sy = ThreadIds.Y * 2;
        int i = (sy * sourceWidth) + sx;
        Float3 c = (source[i].XYZ + source[i + 1].XYZ + source[i + sourceWidth].XYZ + source[i + sourceWidth + 1].XYZ) * 0.25f;
        float peak = Gpu.Max(c.X, Gpu.Max(c.Y, c.Z));
        float keep = Gpu.Saturate((peak - threshold) / Gpu.Max(peak, 1e-4f));
        destination[(ThreadIds.Y * destinationWidth) + ThreadIds.X] = new Float4(c * keep, 1f);
    }
}

/// <summary>Bloom, the downsample between levels: a 2 × 2 average.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Downsample(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int sourceWidth, int destinationWidth) : IComputeShader
{
    public void Execute()
    {
        int i = (ThreadIds.Y * 2 * sourceWidth) + (ThreadIds.X * 2);
        destination[(ThreadIds.Y * destinationWidth) + ThreadIds.X] = (source[i] + source[i + 1] + source[i + sourceWidth] + source[i + sourceWidth + 1]) * 0.25f;
    }
}

/// <summary>
/// Bloom, the blur: one direction of a separable 9-tap Gaussian (σ ≈ 2 texels), so a horizontal
/// and a vertical pass cost 18 reads per pixel instead of 81. Edges clamp.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Blur(ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, int width, int height, int stepX, int stepY) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float4 sum = source[(y * width) + x] * 0.2270270f;
        sum += Tap(x, y, 1) * 0.1945946f;
        sum += Tap(x, y, 2) * 0.1216216f;
        sum += Tap(x, y, 3) * 0.0540540f;
        sum += Tap(x, y, 4) * 0.0162162f;
        destination[(y * width) + x] = sum;
    }

    /// <summary>The two samples <paramref name="k"/> texels either side, added.</summary>
    private Float4 Tap(int x, int y, int k)
    {
        int ax = Gpu.Clamp(x + (stepX * k), 0, width - 1);
        int ay = Gpu.Clamp(y + (stepY * k), 0, height - 1);
        int bx = Gpu.Clamp(x - (stepX * k), 0, width - 1);
        int by = Gpu.Clamp(y - (stepY * k), 0, height - 1);
        return source[(ay * width) + ax] + source[(by * width) + bx];
    }
}

/// <summary>
/// The final image: shockwave distortion (each ripple pushes the lookup radially, with a slight
/// color split), screen shake, the scene plus two bloom levels, flash, tone mapping, vignette and
/// the HUD text on top, packed to RGBA8.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Composite(
    ReadWriteBuffer<Float4> hdr,
    ReadWriteBuffer<Float4> bloomHalf,
    ReadWriteBuffer<Float4> bloomQuarter,
    ReadOnlyBuffer<Ripple> ripples,
    int rippleCount,
    ReadOnlyBuffer<Glyph> glyphs,
    int glyphCount,
    ReadOnlyBuffer<uint> font,
    ReadWriteBuffer<uint> output,
    int width,
    int height,
    float bloom,
    float exposure,
    float flashR,
    float flashG,
    float flashB,
    float shakeX,
    float shakeY) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float2 p = new((float)x + 0.5f, (float)y + 0.5f);

        Float2 offset = new(0f, 0f);

        for (int i = 0; i < rippleCount; i++)
        {
            Ripple r = ripples[i];
            Float2 v = p - r.Center;
            float d = Gpu.Length(v);
            float band = (d - r.Radius) / r.Width;
            float push = -band * Gpu.Exp(-band * band) * r.Strength;
            offset += v / Gpu.Max(d, 1f) * push;
        }

        Float2 s = p + offset + new Float2(shakeX, shakeY);
        Float2 split = offset * 0.3f;
        Float3 color = new(SampleHdr(s + split).X, SampleHdr(s).Y, SampleHdr(s - split).Z);
        Float3 glow = (SampleHalf(s * 0.5f) * 0.8f) + (SampleQuarter(s * 0.25f) * 1.1f);
        color += (glow * bloom) + new Float3(flashR, flashG, flashB);

        // Exponential tone mapping keeps saturated neon from clipping to flat white too early.
        color = new Float3(1f, 1f, 1f) - new Float3(Gpu.Exp(-color.X * exposure), Gpu.Exp(-color.Y * exposure), Gpu.Exp(-color.Z * exposure));

        float u = ((float)x / (float)width) - 0.5f;
        float w = ((float)y / (float)height) - 0.5f;
        color *= 1f - (((u * u) + (w * w)) * 0.55f);

        for (int i = 0; i < glyphCount; i++)
        {
            Glyph g = glyphs[i];
            int gx = x - g.X;
            int gy = y - g.Y;

            if (gx >= 0 && gy >= 0 && gx < 3 * g.Scale && gy < 5 * g.Scale && ((font[g.Index] >> (((gy / g.Scale) * 3) + (gx / g.Scale))) & 1u) != 0u)
            {
                color = g.Color;
            }
        }

        color = Gpu.Saturate(color);
        uint red = (uint)((color.X * 255f) + 0.5f);
        uint green = (uint)((color.Y * 255f) + 0.5f);
        uint blue = (uint)((color.Z * 255f) + 0.5f);
        output[(y * width) + x] = red | (green << 8) | (blue << 16) | 0xFF000000u;
    }

    private Float3 SampleHdr(Float2 at)
    {
        Int4 i = Corners(at, width, height);
        Float2 f = Weights(at);
        return Bilinear(hdr[(i.Y * width) + i.X].XYZ, hdr[(i.Y * width) + i.Z].XYZ, hdr[(i.W * width) + i.X].XYZ, hdr[(i.W * width) + i.Z].XYZ, f);
    }

    private Float3 SampleHalf(Float2 at)
    {
        int w = width / 2;
        Int4 i = Corners(at, w, height / 2);
        Float2 f = Weights(at);
        return Bilinear(bloomHalf[(i.Y * w) + i.X].XYZ, bloomHalf[(i.Y * w) + i.Z].XYZ, bloomHalf[(i.W * w) + i.X].XYZ, bloomHalf[(i.W * w) + i.Z].XYZ, f);
    }

    private Float3 SampleQuarter(Float2 at)
    {
        int w = width / 4;
        Int4 i = Corners(at, w, height / 4);
        Float2 f = Weights(at);
        return Bilinear(bloomQuarter[(i.Y * w) + i.X].XYZ, bloomQuarter[(i.Y * w) + i.Z].XYZ, bloomQuarter[(i.W * w) + i.X].XYZ, bloomQuarter[(i.W * w) + i.Z].XYZ, f);
    }

    /// <summary>The clamped texel columns (x, z) and rows (y, w) around a point whose texel centers are at +0.5.</summary>
    private static Int4 Corners(Float2 at, int w, int h)
    {
        int x0 = (int)Gpu.Floor(at.X - 0.5f);
        int y0 = (int)Gpu.Floor(at.Y - 0.5f);
        return new Int4(Gpu.Clamp(x0, 0, w - 1), Gpu.Clamp(y0, 0, h - 1), Gpu.Clamp(x0 + 1, 0, w - 1), Gpu.Clamp(y0 + 1, 0, h - 1));
    }

    private static Float2 Weights(Float2 at) => new(Gpu.Fract(at.X - 0.5f), Gpu.Fract(at.Y - 0.5f));

    private static Float3 Bilinear(Float3 a, Float3 b, Float3 c, Float3 d, Float2 f) =>
        Gpu.Lerp(Gpu.Lerp(a, b, f.X), Gpu.Lerp(c, d, f.X), f.Y);
}

/// <summary>
/// Scales the native 640 × 360 frame up by a whole number into the window's texture, centered with
/// black borders, so every arena pixel becomes an exact <c>scale × scale</c> block.
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

        uint c = source[(sy * sourceWidth) + sx];
        return new Float4((float)(c & 0xFFu) / 255f, (float)((c >> 8) & 0xFFu) / 255f, (float)((c >> 16) & 0xFFu) / 255f, 1f);
    }
}
