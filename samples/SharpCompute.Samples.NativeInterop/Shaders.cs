// Original SharpCompute sample (MIT).
namespace SharpCompute.Samples.NativeInterop;

/// <summary>
/// One particle of the host's particle buffer. The layout is the host's (its MSL <c>Particle</c>,
/// 32 bytes): SharpCompute adopts the app's data as it is, it does not dictate it.
/// </summary>
public struct Particle
{
    /// <summary>World units: x in [-aspect, aspect], y in [-1, 1], y up.</summary>
    public Float2 Position;

    /// <summary>World units per second.</summary>
    public Float2 Velocity;

    /// <summary>The glow's color; alpha scales its brightness.</summary>
    public Float4 Color;
}

/// <summary>
/// The particle simulation, run on the host's own particle buffer: every particle is pulled toward
/// three attractors that wander on slow Lissajous paths and swirls around them, with drag, a speed
/// limit and wrap-around at the edges of the frame. The color follows the speed.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimulateParticles(ReadWriteBuffer<Particle> particles, float time, float dt, float aspect) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Particle p = particles[i];

        Float2 force = Pull(p.Position, Attractor(0f), 1f)
            + Pull(p.Position, Attractor(1f), -0.8f)
            + Pull(p.Position, Attractor(2f), 0.6f);

        p.Velocity = (p.Velocity + (force * dt)) * Gpu.Exp(-0.9f * dt);

        float speed = Gpu.Length(p.Velocity);

        if (speed > 1.6f)
        {
            p.Velocity *= 1.6f / speed;
        }

        p.Position += p.Velocity * dt;

        if (p.Position.X < -aspect)
        {
            p.Position.X += 2f * aspect;
        }
        else if (p.Position.X > aspect)
        {
            p.Position.X -= 2f * aspect;
        }

        if (p.Position.Y < -1f)
        {
            p.Position.Y += 2f;
        }
        else if (p.Position.Y > 1f)
        {
            p.Position.Y -= 2f;
        }

        // Slow: ember orange; fast: cyan.
        float t = Gpu.Saturate(speed / 1.2f);
        p.Color = new Float4(
            Gpu.Lerp(1.0f, 0.25f, t),
            Gpu.Lerp(0.42f, 0.75f, t),
            Gpu.Lerp(0.12f, 1.0f, t),
            Gpu.Lerp(0.22f, 0.5f, t));

        particles[i] = p;
    }

    /// <summary>Where attractor <paramref name="k"/> is now.</summary>
    private Float2 Attractor(float k) => new(
        Gpu.Cos((time * (0.23f + (0.11f * k))) + (k * 2.1f)) * 0.7f * aspect,
        Gpu.Sin((time * (0.31f + (0.07f * k))) + (k * 1.3f)) * 0.65f);

    /// <summary>
    /// A softened pull toward <paramref name="center"/> plus a swirl around it, whose direction is the
    /// sign of <paramref name="spin"/>.
    /// </summary>
    private static Float2 Pull(Float2 position, Float2 center, float spin)
    {
        Float2 d = center - position;
        float r2 = Gpu.Dot(d, d) + 0.03f;
        float inverse = 1f / r2;
        return (d * (0.22f * inverse)) + (new Float2(-d.Y, d.X) * (0.28f * spin * inverse));
    }
}

/// <summary>
/// The post-process pass, run on the host's frame: reads the scene the host rendered and writes a new
/// picture into the host's output texture with water-like rings spreading from <paramref name="center"/>,
/// a slight color split along the distortion and a bloom of the bright parts.
/// </summary>
/// <param name="scene">The host's scene texture, sampled.</param>
/// <param name="output">The host's output texture.</param>
/// <param name="center">The source of the rings, in uv (0..1, y down).</param>
/// <param name="time">Seconds, for the rings' motion.</param>
[GeneratedComputeShaderDescriptor]
public readonly partial struct RipplePost(ReadOnlyTexture2D<Rgba32> scene, ReadWriteTexture2D<Rgba32> output, Float2 center, float time) : IComputeShader
{
    public void Execute()
    {
        Int2 xy = ThreadIds.XY;
        Float2 size = new(output.Width, output.Height);
        Float2 uv = (new Float2(xy.X, xy.Y) + 0.5f) / size;
        float aspect = size.X / size.Y;

        // Rings: a sine of the distance travelling outward, fading out with distance.
        Float2 d = (uv - center) * new Float2(aspect, 1f);
        float r = Gpu.Length(d) + 1e-5f;
        float wave = Gpu.Sin((r * 46f) - (time * 5f)) * Gpu.Exp(-r * 2.2f) * Gpu.Saturate(1f - (r / 0.7f)) * 0.014f;
        Float2 offset = d / r * wave / new Float2(aspect, 1f);

        Float4 middle = scene.Sample(Sampler.LinearClamp, uv + offset);
        float red = scene.Sample(Sampler.LinearClamp, uv + (offset * 1.8f)).X;
        float blue = scene.Sample(Sampler.LinearClamp, uv + (offset * 0.4f)).Z;
        Float3 color = new(red, middle.Y, blue);

        // Bloom: the bright part of a ring of wider taps, added back.
        Float3 glow = new(0f, 0f, 0f);
        Float2 step = new Float2(6f, 6f) / size;

        for (int k = 0; k < 8; k++)
        {
            float angle = k * 0.7853982f;
            Float2 tap = uv + offset + (new Float2(Gpu.Cos(angle), Gpu.Sin(angle)) * step * 1.5f);
            Float3 sample = scene.Sample(Sampler.LinearClamp, tap).XYZ;
            glow += Gpu.Max(sample - 0.55f, new Float3(0f, 0f, 0f));
        }

        color += glow * 0.22f;

        output[xy] = new Float4(Gpu.Saturate(color.X), Gpu.Saturate(color.Y), Gpu.Saturate(color.Z), 1f);
    }
}
