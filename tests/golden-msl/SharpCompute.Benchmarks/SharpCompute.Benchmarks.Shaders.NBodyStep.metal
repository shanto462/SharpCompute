// SharpCompute.Benchmarks.Shaders.NBodyStep (SharpCompute.Benchmarks): entry point SharpCompute_Benchmarks_Shaders_NBodyStep, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int count;
    float dt;
    float softeningSquared;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 0, "SharpCompute: offset of '__Constants.count' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, dt) == 4, "SharpCompute: offset of '__Constants.dt' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, softeningSquared) == 8, "SharpCompute: offset of '__Constants.softeningSquared' does not match its C# layout");

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

kernel void SharpCompute_Benchmarks_Shaders_NBodyStep(
    const device packed_float4* positions [[buffer(0)]],
    const device packed_float4* velocities [[buffer(1)]],
    device packed_float4* nextPositions [[buffer(2)]],
    device packed_float4* nextVelocities [[buffer(3)]],
    constant __Constants& __c [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float4 self = positions[i];
    float3 p = self.xyz;
    float3 acc = float3(0.0f);
    for (int j = 0; j < __c.count; j = int(uint(j) + 1u))
    {
        float4 other = positions[j];
        float3 d = other.xyz - p;
        float distSquared = metal::dot(d, d) + __c.softeningSquared;
        float inv = 1.0f / metal::sqrt(distSquared);
        float s = other.w * inv * inv * inv;
        acc += d * s;
    }
    float4 v = velocities[i];
    float3 nv = v.xyz + (acc * __c.dt);
    float3 np = p + (nv * __c.dt);
    nextVelocities[i] = float4(nv, v.w);
    nextPositions[i] = float4(np, self.w);
}
