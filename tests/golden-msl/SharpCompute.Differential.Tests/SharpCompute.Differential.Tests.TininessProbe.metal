// SharpCompute.Differential.Tests.TininessProbe (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_TininessProbe, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");

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

kernel void SharpCompute_Differential_Tests_TininessProbe(
    const device packed_float3* input [[buffer(0)]],
    device uint* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    float3 v = input[int(__tid.x)];
    int o = int(__tid.x * 3u);
    output[o] = as_type<uint>(v.x * v.y);
    output[int(uint(o) + 1u)] = as_type<uint>(v.x / v.y);
    output[int(uint(o) + 2u)] = as_type<uint>(metal::fma(v.x, v.y, v.z));
}
