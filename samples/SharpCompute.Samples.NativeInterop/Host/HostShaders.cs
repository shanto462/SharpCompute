// Original SharpCompute sample (MIT).
namespace SharpCompute.Samples.NativeInterop.Host;

/// <summary>
/// The host's render pipeline, in Metal Shading Language, compiled at run time with
/// <c>newLibraryWithSource:</c>: a full-frame textured quad, an instanced particle field drawn
/// straight from the host's particle buffer, and the help overlay. This is ordinary renderer code
/// with no knowledge of SharpCompute; the only contract between the two is the layout of
/// <c>Particle</c>, which the SharpCompute side mirrors as <see cref="NativeInterop.Particle"/>.
/// </summary>
internal static class HostShaders
{
    public const string Source = """
        #include <metal_stdlib>
        using namespace metal;

        // 32 bytes: position and velocity in world units (x in [-aspect, aspect], y in [-1, 1], y up),
        // and a color whose alpha scales the particle's brightness.
        struct Particle
        {
            float2 position;
            float2 velocity;
            float4 color;
        };

        struct Uniforms
        {
            float2 viewScale;     // world to clip space: (1 / aspect, 1)
            float particleSize;   // half the side of a particle's quad, in world units
            float time;           // seconds
        };

        struct QuadOut
        {
            float4 position [[position]];
            float2 uv;
        };

        constant float2 Corners[4] = { float2(-1, -1), float2(1, -1), float2(-1, 1), float2(1, 1) };

        // A quad over the whole target, drawn as a 4-vertex triangle strip; uv has (0, 0) at the top left.
        vertex QuadOut frame_quad(uint vid [[vertex_id]])
        {
            float2 corner = Corners[vid];
            QuadOut out;
            out.position = float4(corner, 0, 1);
            out.uv = float2(corner.x * 0.5 + 0.5, 0.5 - corner.y * 0.5);
            return out;
        }

        // The host's "scene": its own image, covering the frame, with a slow zoom and drift.
        fragment float4 background(QuadOut in [[stage_in]],
                                   texture2d<float> image [[texture(0)]],
                                   constant Uniforms& u [[buffer(0)]])
        {
            constexpr sampler linear(filter::linear, address::mirrored_repeat);
            float imageAspect = float(image.get_width()) / float(image.get_height());
            float frameAspect = 1.0 / u.viewScale.x;
            float2 uv = in.uv - 0.5;
            uv.x *= frameAspect / imageAspect;
            float zoom = 0.92 - 0.04 * sin(u.time * 0.11);
            uv = uv * zoom + 0.5 + float2(0.03 * sin(u.time * 0.07), 0.0);
            return float4(image.sample(linear, uv).rgb, 1);
        }

        struct ParticleOut
        {
            float4 position [[position]];
            float2 local;
            float4 color;
        };

        // One instance per particle, read from the host's buffer: a small quad around its position.
        vertex ParticleOut particle_quad(uint vid [[vertex_id]],
                                         uint iid [[instance_id]],
                                         const device Particle* particles [[buffer(0)]],
                                         constant Uniforms& u [[buffer(1)]])
        {
            Particle p = particles[iid];
            float2 corner = Corners[vid];
            ParticleOut out;
            out.position = float4((p.position + corner * u.particleSize) * u.viewScale, 0, 1);
            out.local = corner;
            out.color = p.color;
            return out;
        }

        // A soft round dot, added to what is there (blending one + one).
        fragment float4 particle_glow(ParticleOut in [[stage_in]])
        {
            float falloff = saturate(1.0 - length(in.local));
            falloff *= falloff;
            return float4(in.color.rgb * (in.color.a * falloff), 0);
        }

        // The help overlay: an R8 texture of text (1) on dark plates (0.5), blended over the frame.
        fragment float4 overlay(QuadOut in [[stage_in]], texture2d<float> labels [[texture(0)]])
        {
            constexpr sampler nearest(filter::nearest);
            float value = labels.sample(nearest, in.uv).r;

            if (value > 0.75)
            {
                return float4(1, 1, 1, 1);
            }

            return value > 0.25 ? float4(0.02, 0.03, 0.05, 0.72) : float4(0);
        }
        """;
}
