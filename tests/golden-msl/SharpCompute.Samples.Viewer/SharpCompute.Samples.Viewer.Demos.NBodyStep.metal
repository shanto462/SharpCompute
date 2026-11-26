// SharpCompute.Samples.Viewer.Demos.NBodyStep (SharpCompute.Samples.Viewer): entry point SharpCompute_Samples_Viewer_Demos_NBodyStep, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int count;
    float dt;
    float softening2;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 0, "SharpCompute: offset of '__Constants.count' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, dt) == 4, "SharpCompute: offset of '__Constants.dt' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, softening2) == 8, "SharpCompute: offset of '__Constants.softening2' does not match its C# layout");

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

kernel void SharpCompute_Samples_Viewer_Demos_NBodyStep(
    device packed_float4* positions [[buffer(0)]],
    device packed_float4* velocities [[buffer(1)]],
    device packed_float4* nextPositions [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float4 self = positions[i];
    float3 p = self.xyz;
    float3 acceleration = float3(0.0f, 0.0f, 0.0f);
    for (int j = 0; j < __c.count; j = int(uint(j) + 1u))
    {
        float4 other = positions[j];
        float3 d = other.xyz - p;
        float inverse = metal::rsqrt(metal::dot(d, d) + __c.softening2);
        acceleration += d * (other.w * inverse * inverse * inverse);
    }
    float3 velocity = velocities[i].xyz + (acceleration * __c.dt);
    velocities[i] = float4(velocity, 0.0f);
    nextPositions[i] = float4(p + (velocity * __c.dt), self.w);
}
