// SharpCompute.Tests.Shaders.SimdAfterReturns (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdAfterReturns, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 8;

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

kernel void SharpCompute_Tests_Shaders_SimdAfterReturns(
    const device int* input [[buffer(0)]],
    device int* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint __lane [[thread_index_in_simdgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int lane = int(__lane);
    int v = input[i];
    output[int((uint(i) * uint(::Stride)) + 0u)] = metal::simd_sum(v);
    if (lane % 3 == 0)
    {
        return;
    }
    output[int((uint(i) * uint(::Stride)) + 1u)] = metal::simd_sum(v);
    output[int((uint(i) * uint(::Stride)) + 2u)] = metal::simd_prefix_exclusive_sum(1);
    output[int((uint(i) * uint(::Stride)) + 3u)] = metal::simd_broadcast_first(v);
    output[int((uint(i) * uint(::Stride)) + 4u)] = metal::simd_is_first() ? 1 : 0;
    output[int((uint(i) * uint(::Stride)) + 5u)] = int(uint(metal::simd_vote::vote_t(metal::simd_ballot(true))));
    output[int((uint(i) * uint(::Stride)) + 6u)] = metal::simd_sum(int(true));
    output[int((uint(i) * uint(::Stride)) + 7u)] = metal::simd_shuffle_up(v, ushort(1));
}
