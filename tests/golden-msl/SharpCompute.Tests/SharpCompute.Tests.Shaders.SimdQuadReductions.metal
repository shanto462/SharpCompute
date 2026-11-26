// SharpCompute.Tests.Shaders.SimdQuadReductions (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdQuadReductions, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 14;

struct __Constants
{
    int skip;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, skip) == 0, "SharpCompute: offset of '__Constants.skip' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_SimdQuadReductions(
    const device int* input [[buffer(0)]],
    device int* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint __lane [[thread_index_in_simdgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int v = input[i];
    if ((int(__lane) & 3) == __c.skip)
    {
        return;
    }
    output[int((uint(i) * uint(::Stride)) + 0u)] = metal::quad_sum(v);
    output[int((uint(i) * uint(::Stride)) + 1u)] = metal::quad_product(v);
    output[int((uint(i) * uint(::Stride)) + 2u)] = metal::quad_min(v);
    output[int((uint(i) * uint(::Stride)) + 3u)] = metal::quad_max(v);
    output[int((uint(i) * uint(::Stride)) + 4u)] = metal::quad_and(v);
    output[int((uint(i) * uint(::Stride)) + 5u)] = metal::quad_or(v);
    output[int((uint(i) * uint(::Stride)) + 6u)] = metal::quad_xor(v);
    output[int((uint(i) * uint(::Stride)) + 7u)] = metal::quad_prefix_exclusive_sum(v);
    output[int((uint(i) * uint(::Stride)) + 8u)] = metal::quad_prefix_exclusive_product(v);
    output[int((uint(i) * uint(::Stride)) + 9u)] = metal::quad_prefix_inclusive_sum(v);
    output[int((uint(i) * uint(::Stride)) + 10u)] = metal::quad_prefix_inclusive_product(v);
    output[int((uint(i) * uint(::Stride)) + 11u)] = int(uint(metal::quad_vote::vote_t(metal::quad_ballot((v & 1) == 0))));
    output[int((uint(i) * uint(::Stride)) + 12u)] = metal::quad_any(v > 9) ? 1 : 0;
    output[int((uint(i) * uint(::Stride)) + 13u)] = metal::quad_all(v < 9) ? 1 : 0;
}
