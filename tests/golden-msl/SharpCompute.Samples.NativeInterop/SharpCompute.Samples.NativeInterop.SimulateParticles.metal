// SharpCompute.Samples.NativeInterop.SimulateParticles (SharpCompute.Samples.NativeInterop): entry point SharpCompute_Samples_NativeInterop_SimulateParticles, compile options None
#include <metal_stdlib>

using namespace metal;

struct Particle
{
    packed_float2 Position;
    packed_float2 Velocity;
    packed_float4 Color;
};
static_assert(sizeof(Particle) == 32, "SharpCompute: size of 'Particle' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Position) == 0, "SharpCompute: offset of 'Particle.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Velocity) == 8, "SharpCompute: offset of 'Particle.Velocity' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Color) == 16, "SharpCompute: offset of 'Particle.Color' does not match its C# layout");

struct __Constants
{
    float time;
    float dt;
    float aspect;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 0, "SharpCompute: offset of '__Constants.time' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, dt) == 4, "SharpCompute: offset of '__Constants.dt' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, aspect) == 8, "SharpCompute: offset of '__Constants.aspect' does not match its C# layout");

struct __GridWindow
{
    packed_uint3 offset;
    packed_uint3 size;
    packed_uint3 groupOffset;
};
static_assert(sizeof(__GridWindow) == 36, "SharpCompute: size of '__GridWindow' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, offset) == 0, "SharpCompute: offset of '__GridWindow.offset' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, size) == 12, "SharpCompute: offset of '__GridWindow.size' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, groupOffset) == 24, "SharpCompute: offset of '__GridWindow.groupOffset' does not match its C# layout");

constant bool __windowed [[function_constant(0)]];

static float2 Pull(float2 position, float2 center, float spin);
static float2 Attractor(float k, constant __Constants& __c);

static float2 Pull(float2 position, float2 center, float spin)
{
    float2 d = center - position;
    float r2 = metal::dot(d, d) + 0.03f;
    float inverse = 1.0f / r2;
    return (d * (0.22f * inverse)) + (float2(-d.y, d.x) * (0.28f * spin * inverse));
}

static float2 Attractor(float k, constant __Constants& __c)
{
    return float2(metal::cos((__c.time * (0.23f + (0.11f * k))) + (k * 2.1f)) * 0.7f * __c.aspect, metal::sin((__c.time * (0.31f + (0.07f * k))) + (k * 1.3f)) * 0.65f);
}

kernel void SharpCompute_Samples_NativeInterop_SimulateParticles(
    device Particle* particles [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    Particle p = particles[i];
    float2 force = ::Pull(p.Position, ::Attractor(0.0f, __c), 1.0f) + ::Pull(p.Position, ::Attractor(1.0f, __c), -0.8f) + ::Pull(p.Position, ::Attractor(2.0f, __c), 0.6f);
    p.Velocity = (p.Velocity + (force * __c.dt)) * metal::exp(-0.9f * __c.dt);
    float speed = metal::length(p.Velocity);
    if (speed > 1.6f)
    {
        p.Velocity *= 1.6f / speed;
    }
    p.Position += p.Velocity * __c.dt;
    if (p.Position.x < -__c.aspect)
    {
        p.Position.x += 2.0f * __c.aspect;
    }
    else if (p.Position.x > __c.aspect)
    {
        p.Position.x -= 2.0f * __c.aspect;
    }
    if (p.Position.y < -1.0f)
    {
        p.Position.y += 2.0f;
    }
    else if (p.Position.y > 1.0f)
    {
        p.Position.y -= 2.0f;
    }
    float t = metal::saturate(speed / 1.2f);
    p.Color = float4(metal::mix(1.0f, 0.25f, t), metal::mix(0.42f, 0.75f, t), metal::mix(0.12f, 1.0f, t), metal::mix(0.22f, 0.5f, t));
    particles[i] = p;
}
