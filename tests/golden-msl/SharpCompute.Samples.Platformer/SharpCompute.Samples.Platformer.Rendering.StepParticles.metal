// SharpCompute.Samples.Platformer.Rendering.StepParticles (SharpCompute.Samples.Platformer): entry point SharpCompute_Samples_Platformer_Rendering_StepParticles, compile options None
#include <metal_stdlib>

using namespace metal;

struct Particle
{
    packed_float2 Position;
    packed_float2 Velocity;
    packed_float3 Color;
    float Life;
    float MaxLife;
    float Size;
    float Gravity;
    float Drag;
};
static_assert(sizeof(Particle) == 48, "SharpCompute: size of 'Particle' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Position) == 0, "SharpCompute: offset of 'Particle.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Velocity) == 8, "SharpCompute: offset of 'Particle.Velocity' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Color) == 16, "SharpCompute: offset of 'Particle.Color' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Life) == 28, "SharpCompute: offset of 'Particle.Life' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, MaxLife) == 32, "SharpCompute: offset of 'Particle.MaxLife' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Size) == 36, "SharpCompute: offset of 'Particle.Size' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Gravity) == 40, "SharpCompute: offset of 'Particle.Gravity' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Drag) == 44, "SharpCompute: offset of 'Particle.Drag' does not match its C# layout");

struct __Constants
{
    float dt;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, dt) == 0, "SharpCompute: offset of '__Constants.dt' does not match its C# layout");

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

kernel void SharpCompute_Samples_Platformer_Rendering_StepParticles(
    device Particle* particles [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    Particle p = particles[int(__tid.x)];
    if (p.Life <= 0.0f)
    {
        return;
    }
    p.Velocity.y += p.Gravity * __c.dt;
    p.Velocity *= metal::max(0.0f, 1.0f - (p.Drag * __c.dt));
    p.Position += p.Velocity * __c.dt;
    p.Life -= __c.dt;
    particles[int(__tid.x)] = p;
}
