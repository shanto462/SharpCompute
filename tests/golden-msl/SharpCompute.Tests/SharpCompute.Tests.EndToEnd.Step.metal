// SharpCompute.Tests.EndToEnd.Step (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Step, compile options None
#include <metal_stdlib>

using namespace metal;

struct Particle
{
    packed_float3 Position;
    float Mass;
    packed_float2 Uv;
    int Id;
};
static_assert(sizeof(Particle) == 28, "SharpCompute: size of 'Particle' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Position) == 0, "SharpCompute: offset of 'Particle.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Mass) == 12, "SharpCompute: offset of 'Particle.Mass' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Uv) == 16, "SharpCompute: offset of 'Particle.Uv' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Id) == 24, "SharpCompute: offset of 'Particle.Id' does not match its C# layout");

struct Params
{
    float Dt;
    packed_float3 Gravity;
    uint Flags;
};
static_assert(sizeof(Params) == 20, "SharpCompute: size of 'Params' does not match its C# layout");
static_assert(__builtin_offsetof(Params, Dt) == 0, "SharpCompute: offset of 'Params.Dt' does not match its C# layout");
static_assert(__builtin_offsetof(Params, Gravity) == 4, "SharpCompute: offset of 'Params.Gravity' does not match its C# layout");
static_assert(__builtin_offsetof(Params, Flags) == 16, "SharpCompute: offset of 'Params.Flags' does not match its C# layout");

struct __Constants
{
    Params p;
};
static_assert(sizeof(__Constants) == 20, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, p) == 0, "SharpCompute: offset of '__Constants.p' does not match its C# layout");

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

kernel void SharpCompute_Tests_EndToEnd_Step(
    device Particle* particles [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]],
    uint3 __gid_in [[threadgroup_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    int i = int(__tid.y * __grid.x + __tid.x);
    Particle part = particles[i];
    part.Position += __c.p.Gravity * __c.p.Dt * part.Mass;
    part.Uv = part.Uv.yx;
    part.Id = int(uint(part.Id) + (__c.p.Flags + __gtid.x + __gid.y * 100u));
    particles[i] = part;
}
