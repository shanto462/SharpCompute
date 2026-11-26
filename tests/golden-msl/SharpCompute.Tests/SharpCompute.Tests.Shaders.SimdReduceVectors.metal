// SharpCompute.Tests.Shaders.SimdReduceVectors (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdReduceVectors, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");
static_assert(sizeof(packed_int2) == 8, "SharpCompute: size of 'packed_int2' does not match its C# layout");
static_assert(sizeof(packed_uint4) == 16, "SharpCompute: size of 'packed_uint4' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_SimdReduceVectors(
    const device packed_float3* floats [[buffer(0)]],
    const device packed_int2* ints [[buffer(1)]],
    device packed_float3* floatSums [[buffer(2)]],
    device packed_float3* floatMax [[buffer(3)]],
    device packed_int2* intSums [[buffer(4)]],
    device packed_int2* intOr [[buffer(5)]],
    device packed_uint4* prefix [[buffer(6)]],
    constant __GridWindow& __window [[buffer(7), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    floatSums[i] = metal::simd_sum(float3(floats[i]));
    floatMax[i] = metal::simd_max(float3(floats[i]));
    intSums[i] = metal::simd_sum(int2(ints[i]));
    intOr[i] = metal::simd_or(int2(ints[i]));
    prefix[i] = metal::simd_prefix_exclusive_sum(uint4(1u, 2u, uint(i), 0u));
}
