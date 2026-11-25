// Original SharpCompute shader (MIT).
using SharpCompute;

namespace SharpCompute.Benchmarks.Shaders;

/// <summary>
/// A raymarched signed-distance scene, written for the first-frame measurement: a noise-displaced
/// ground, a grid of rounded pillars, three orbiting spheres blended into a spinning torus, with soft
/// shadows, ambient occlusion and fog. It is a large kernel on purpose (a dozen helpers, each inlined
/// into the march, the normal, the shadow and the occlusion loops), so that compiling it takes long
/// enough to be worth warming up, and it is heavy to run for the same reason a real scene is.
/// </summary>
/// <param name="time">Seconds; the spheres, the torus and the camera move with it.</param>
[GeneratedComputeShaderDescriptor]
public readonly partial struct RaymarchedGarden(float time) : IComputeShader<Float4>
{
    private const int MarchSteps = 96;
    private const int ShadowSteps = 32;
    private const float FarPlane = 60f;
    private const float PillarPitch = 5f;

    private static float Hash(Float3 p)
    {
        p = Gpu.Fract((p * 0.3183099f) + new Float3(0.1f, 0.17f, 0.23f));
        p *= 17f;

        return Gpu.Fract(p.X * p.Y * p.Z * (p.X + p.Y + p.Z));
    }

    /// <summary>Value noise: the eight corners of the cell, blended with a smooth weight.</summary>
    private static float Noise(Float3 x)
    {
        Float3 cell = Gpu.Floor(x);
        Float3 f = Gpu.Fract(x);
        Float3 w = f * f * (new Float3(3f) - (f * 2f));

        float c000 = Hash(cell);
        float c100 = Hash(cell + new Float3(1f, 0f, 0f));
        float c010 = Hash(cell + new Float3(0f, 1f, 0f));
        float c110 = Hash(cell + new Float3(1f, 1f, 0f));
        float c001 = Hash(cell + new Float3(0f, 0f, 1f));
        float c101 = Hash(cell + new Float3(1f, 0f, 1f));
        float c011 = Hash(cell + new Float3(0f, 1f, 1f));
        float c111 = Hash(cell + new Float3(1f, 1f, 1f));

        float near = Gpu.Lerp(Gpu.Lerp(c000, c100, w.X), Gpu.Lerp(c010, c110, w.X), w.Y);
        float far = Gpu.Lerp(Gpu.Lerp(c001, c101, w.X), Gpu.Lerp(c011, c111, w.X), w.Y);

        return Gpu.Lerp(near, far, w.Z);
    }

    private static float Fbm(Float3 p, int octaves)
    {
        float sum = 0f;
        float amplitude = 0.5f;

        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * Noise(p);
            p = (p * 2.03f) + new Float3(11.3f, 5.7f, 3.1f);
            amplitude *= 0.5f;
        }

        return sum;
    }

    private static float SmoothMin(float a, float b, float k)
    {
        float h = Gpu.Saturate(0.5f + (0.5f * (b - a) / k));

        return Gpu.Lerp(b, a, h) - (k * h * (1f - h));
    }

    private static float Sphere(Float3 p, float radius) => Gpu.Length(p) - radius;

    private static float RoundBox(Float3 p, Float3 halfSize, float radius)
    {
        Float3 q = Gpu.Abs(p) - halfSize;

        return Gpu.Length(Gpu.Max(q, 0f)) + Gpu.Min(Gpu.Max(q.X, Gpu.Max(q.Y, q.Z)), 0f) - radius;
    }

    private static float Torus(Float3 p, float major, float minor)
    {
        float ring = Gpu.Length(new Float2(p.X, p.Z)) - major;

        return Gpu.Length(new Float2(ring, p.Y)) - minor;
    }

    private static Float3 RotateY(Float3 p, float angle)
    {
        float c = Gpu.Cos(angle);
        float s = Gpu.Sin(angle);

        return new Float3((c * p.X) + (s * p.Z), p.Y, (c * p.Z) - (s * p.X));
    }

    private static Float3 RotateX(Float3 p, float angle)
    {
        float c = Gpu.Cos(angle);
        float s = Gpu.Sin(angle);

        return new Float3(p.X, (c * p.Y) - (s * p.Z), (s * p.Y) + (c * p.Z));
    }

    /// <summary>The ground: a plane pushed up and down by three octaves of noise.</summary>
    private static float Ground(Float3 p) => p.Y + 1.2f - (1.6f * Fbm(new Float3(p.X * 0.35f, 0f, p.Z * 0.35f), 3));

    /// <summary>One rounded pillar per cell of a grid in x and z, each a different height.</summary>
    private static float Pillars(Float3 p)
    {
        Float2 cell = new Float2(Gpu.Round(p.X / PillarPitch), Gpu.Round(p.Z / PillarPitch));
        float height = 1.2f + (2.4f * Hash(new Float3(cell.X, 7f, cell.Y)));
        Float3 local = new Float3(p.X - (cell.X * PillarPitch), p.Y - height + 1f, p.Z - (cell.Y * PillarPitch));

        // Only the pillar of this cell is measured, so the distance is capped below the gap to the
        // next cell: a ray then never steps over a neighbouring pillar it has not looked at.
        return Gpu.Min(RoundBox(local, new Float3(0.45f, height, 0.45f), 0.12f), PillarPitch * 0.4f);
    }

    /// <summary>Three spheres on different orbits, melted into a torus that tumbles above the ground.</summary>
    private float Sculpture(Float3 p)
    {
        Float3 center = new Float3(0f, 3.4f, 0f);
        Float3 q = RotateX(RotateY(p - center, time * 0.4f), time * 0.27f);
        float shape = Torus(q, 1.7f, 0.38f);

        for (int i = 0; i < 3; i++)
        {
            float phase = (time * (0.7f + (0.2f * i))) + (i * 2.0944f);
            Float3 orbit = new Float3(Gpu.Cos(phase) * 2.3f, Gpu.Sin(phase * 1.3f) * 0.9f, Gpu.Sin(phase) * 2.3f);

            shape = SmoothMin(shape, Sphere(p - center - orbit, 0.62f), 0.7f);
        }

        return shape;
    }

    private float Scene(Float3 p) => Gpu.Min(SmoothMin(Ground(p), Pillars(p), 0.35f), Sculpture(p));

    /// <summary>0 for the ground, 1 for a pillar, 2 for the sculpture: whichever is nearest.</summary>
    private float Material(Float3 p)
    {
        float ground = Ground(p);
        float pillars = Pillars(p);
        float sculpture = Sculpture(p);

        if (sculpture < ground && sculpture < pillars)
        {
            return 2f;
        }

        return pillars < ground ? 1f : 0f;
    }

    /// <summary>The surface normal from four samples of the scene (the tetrahedron technique).</summary>
    private Float3 Normal(Float3 p)
    {
        const float e = 0.0015f;
        Float3 a = new Float3(1f, -1f, -1f);
        Float3 b = new Float3(-1f, -1f, 1f);
        Float3 c = new Float3(-1f, 1f, -1f);
        Float3 d = new Float3(1f, 1f, 1f);

        return Gpu.Normalize((a * Scene(p + (a * e))) + (b * Scene(p + (b * e))) + (c * Scene(p + (c * e))) + (d * Scene(p + (d * e))));
    }

    private float March(Float3 origin, Float3 direction)
    {
        float t = 0.05f;

        for (int i = 0; i < MarchSteps; i++)
        {
            float distance = Scene(origin + (direction * t));

            if (distance < 0.0012f * t || t > FarPlane)
            {
                break;
            }

            t += distance * 0.85f;
        }

        return t;
    }

    private float SoftShadow(Float3 origin, Float3 direction)
    {
        float light = 1f;
        float t = 0.06f;

        for (int i = 0; i < ShadowSteps; i++)
        {
            float distance = Scene(origin + (direction * t));

            light = Gpu.Min(light, 10f * distance / t);
            t += Gpu.Clamp(distance, 0.04f, 0.6f);

            if (light < 0.005f || t > 18f)
            {
                break;
            }
        }

        return Gpu.Saturate(light);
    }

    private float Occlusion(Float3 p, Float3 normal)
    {
        float occlusion = 0f;
        float weight = 1f;

        for (int i = 1; i <= 5; i++)
        {
            float reach = 0.09f * i;

            occlusion += weight * (reach - Scene(p + (normal * reach)));
            weight *= 0.6f;
        }

        return Gpu.Saturate(1f - (2.2f * occlusion));
    }

    private static Float3 Sky(Float3 direction, Float3 sun)
    {
        float height = Gpu.Saturate(direction.Y);
        Float3 color = Gpu.Lerp(new Float3(0.74f, 0.80f, 0.88f), new Float3(0.20f, 0.38f, 0.72f), Gpu.Pow(height, 0.6f));
        float glow = Gpu.Saturate(Gpu.Dot(direction, sun));

        return color + (new Float3(1f, 0.75f, 0.45f) * ((0.35f * Gpu.Pow(glow, 8f)) + Gpu.Pow(glow, 240f)));
    }

    public Float4 Execute()
    {
        Float2 size = new Float2(DispatchSize.X, DispatchSize.Y);
        Float2 pixel = new Float2(ThreadIds.X + 0.5f, ThreadIds.Y + 0.5f);
        Float2 uv = new Float2(((2f * pixel.X) - size.X) / size.Y, (size.Y - (2f * pixel.Y)) / size.Y);

        // A camera circling the sculpture, looking slightly down at it.
        float orbit = time * 0.18f;
        Float3 eye = new Float3(Gpu.Sin(orbit) * 11f, 4.6f + (0.8f * Gpu.Sin(time * 0.31f)), Gpu.Cos(orbit) * 11f);
        Float3 forward = Gpu.Normalize(new Float3(0f, 2.6f, 0f) - eye);
        Float3 right = Gpu.Normalize(Gpu.Cross(forward, new Float3(0f, 1f, 0f)));
        Float3 up = Gpu.Cross(right, forward);
        Float3 direction = Gpu.Normalize((right * uv.X) + (up * uv.Y) + (forward * 1.6f));

        Float3 sun = Gpu.Normalize(new Float3(0.55f, 0.48f, -0.4f));
        Float3 color = Sky(direction, sun);
        float t = March(eye, direction);

        if (t < FarPlane)
        {
            Float3 p = eye + (direction * t);
            Float3 normal = Normal(p);
            float material = Material(p);

            // The surface color: mossy ground with noise, pale stone pillars, a warm metal sculpture.
            float grain = Fbm(p * 2.7f, 5);
            Float3 albedo = Gpu.Lerp(new Float3(0.20f, 0.27f, 0.12f), new Float3(0.42f, 0.36f, 0.24f), grain);

            if (material > 1.5f)
            {
                albedo = new Float3(0.85f, 0.42f, 0.22f) * (0.8f + (0.4f * grain));
            }
            else if (material > 0.5f)
            {
                albedo = new Float3(0.62f, 0.60f, 0.56f) * (0.7f + (0.5f * Noise(p * 6f)));
            }

            float diffuse = Gpu.Saturate(Gpu.Dot(normal, sun));
            float shadow = diffuse > 0f ? SoftShadow(p + (normal * 0.02f), sun) : 0f;
            float occlusion = Occlusion(p, normal);
            float skyLight = 0.5f + (0.5f * normal.Y);
            Float3 half = Gpu.Normalize(sun - direction);
            float specular = Gpu.Pow(Gpu.Saturate(Gpu.Dot(normal, half)), material > 1.5f ? 90f : 18f) * (material > 1.5f ? 1.4f : 0.15f);

            Float3 lit = albedo * ((new Float3(1.25f, 1.1f, 0.9f) * (diffuse * shadow)) + (new Float3(0.22f, 0.3f, 0.45f) * (skyLight * occlusion)));
            lit += new Float3(1f, 0.9f, 0.75f) * (specular * shadow);

            // Fog toward the sky color, thicker with distance.
            float fog = 1f - Gpu.Exp(-0.0009f * t * t);
            color = Gpu.Lerp(lit, Sky(direction, sun), fog);
        }

        // Tone map, gamma, and a slight vignette.
        color = color / (color + new Float3(0.85f));
        color = Gpu.Pow(color, new Float3(0.4545f));
        color *= 1f - (0.18f * Gpu.Dot(uv, uv) * 0.5f);

        return new Float4(Gpu.Saturate(color), 1f);
    }
}
