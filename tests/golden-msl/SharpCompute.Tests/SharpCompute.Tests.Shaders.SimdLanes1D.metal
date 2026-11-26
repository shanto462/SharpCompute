// SharpCompute.Tests.Shaders.SimdLanes1D (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdLanes1D, compile options None
#include <metal_stdlib>

using namespace metal;

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

kernel void SharpCompute_Tests_Shaders_SimdLanes1D(
    device int* output [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint __lane [[thread_index_in_simdgroup]],
    uint __lanes [[threads_per_simdgroup]],
    uint __sgid [[simdgroup_index_in_threadgroup]],
    uint __sgroups [[simdgroups_per_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    output[int((uint(i) * 4u) + 0u)] = int(__lane);
    output[int((uint(i) * 4u) + 1u)] = int(__lanes);
    output[int((uint(i) * 4u) + 2u)] = int(__sgid);
    output[int((uint(i) * 4u) + 3u)] = int(__sgroups);
}
