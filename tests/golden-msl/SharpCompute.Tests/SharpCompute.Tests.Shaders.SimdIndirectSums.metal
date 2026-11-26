// SharpCompute.Tests.Shaders.SimdIndirectSums (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdIndirectSums, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 4;

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

kernel void SharpCompute_Tests_Shaders_SimdIndirectSums(
    const device int* input [[buffer(0)]],
    const device int* count [[buffer(1)]],
    device int* output [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]],
    uint __lane [[thread_index_in_simdgroup]],
    uint __sgid [[simdgroup_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int i = int(__tid.x);
    if (i >= count[0])
    {
        return;
    }
    int v = input[i];
    output[int((uint(i) * uint(::Stride)) + 0u)] = metal::simd_sum(v);
    output[int((uint(i) * uint(::Stride)) + 1u)] = metal::simd_prefix_exclusive_sum(v);
    output[int((uint(i) * uint(::Stride)) + 2u)] = int(uint(metal::simd_vote::vote_t(metal::simd_ballot(true))));
    output[int((uint(i) * uint(::Stride)) + 3u)] = int((__grid.x * 1000u) + (uint(int(__sgid)) * 100u) + uint(int(__lane)));
}
