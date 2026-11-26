// SharpCompute.Tests.CpuExecution.Gravity (SharpCompute.Tests): entry point SharpCompute_Tests_CpuExecution_Gravity, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int count;
    float softeningSquared;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 0, "SharpCompute: offset of '__Constants.count' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, softeningSquared) == 4, "SharpCompute: offset of '__Constants.softeningSquared' does not match its C# layout");

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

kernel void SharpCompute_Tests_CpuExecution_Gravity(
    const device packed_float4* positions [[buffer(0)]],
    device packed_float4* accelerations [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float3 p = positions[i].xyz;
    float3 acc = float3(0.0f);
    for (int j = 0; j < __c.count; j = int(uint(j) + 1u))
    {
        float4 other = positions[j];
        float3 d = other.xyz - p;
        float inv = 1.0f / metal::sqrt(metal::dot(d, d) + __c.softeningSquared);
        acc += d * (other.w * inv * inv * inv);
    }
    accelerations[i] = float4(acc, 0.0f);
}
