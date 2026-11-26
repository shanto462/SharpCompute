// SharpCompute.Samples.Shooter.Rendering.BoundParticleChunks (SharpCompute.Samples.Shooter): entry point SharpCompute_Samples_Shooter_Rendering_BoundParticleChunks, compile options None
#include <metal_stdlib>

using namespace metal;

constant int ChunkSize = 64;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct Particle
{
    packed_float2 Position;
    packed_float2 Velocity;
    packed_float3 Color;
    float Life;
    float MaxLife;
    float Size;
    float Drag;
    float Streak;
};
static_assert(sizeof(Particle) == 48, "SharpCompute: size of 'Particle' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Position) == 0, "SharpCompute: offset of 'Particle.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Velocity) == 8, "SharpCompute: offset of 'Particle.Velocity' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Color) == 16, "SharpCompute: offset of 'Particle.Color' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Life) == 28, "SharpCompute: offset of 'Particle.Life' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, MaxLife) == 32, "SharpCompute: offset of 'Particle.MaxLife' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Size) == 36, "SharpCompute: offset of 'Particle.Size' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Drag) == 40, "SharpCompute: offset of 'Particle.Drag' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Streak) == 44, "SharpCompute: offset of 'Particle.Streak' does not match its C# layout");

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

kernel void SharpCompute_Samples_Shooter_Rendering_BoundParticleChunks(
    device Particle* particles [[buffer(0)]],
    device packed_float4* chunkBounds [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int first = int(__tid.x * uint(::ChunkSize));
    float minX = 1E+09f;
    float minY = 1E+09f;
    float maxX = -1E+09f;
    float maxY = -1E+09f;
    for (int i = 0; i < ::ChunkSize; i = int(uint(i) + 1u))
    {
        Particle p = particles[int(uint(first) + uint(i))];
        if (p.Life > 0.0f)
        {
            float2 tail = p.Position - (p.Velocity * p.Streak);
            float reach = p.Size + 1.5f;
            minX = metal::min(minX, metal::min(p.Position.x, tail.x) - reach);
            minY = metal::min(minY, metal::min(p.Position.y, tail.y) - reach);
            maxX = metal::max(maxX, metal::max(p.Position.x, tail.x) + reach);
            maxY = metal::max(maxY, metal::max(p.Position.y, tail.y) + reach);
        }
    }
    chunkBounds[int(__tid.x)] = float4(minX, minY, maxX, maxY);
}
