// SharpCompute.Tests.Shaders.SimdReduceUInt (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdReduceUInt, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 11;

struct __Constants
{
    int count;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 0, "SharpCompute: offset of '__Constants.count' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_SimdReduceUInt(
    const device uint* input [[buffer(0)]],
    device uint* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    if (i >= __c.count)
    {
        return;
    }
    uint v = input[i];
    output[int((uint(i) * uint(::Stride)) + 0u)] = metal::simd_sum(v);
    output[int((uint(i) * uint(::Stride)) + 1u)] = metal::simd_product(v);
    output[int((uint(i) * uint(::Stride)) + 2u)] = metal::simd_min(v);
    output[int((uint(i) * uint(::Stride)) + 3u)] = metal::simd_max(v);
    output[int((uint(i) * uint(::Stride)) + 4u)] = metal::simd_and(v);
    output[int((uint(i) * uint(::Stride)) + 5u)] = metal::simd_or(v);
    output[int((uint(i) * uint(::Stride)) + 6u)] = metal::simd_xor(v);
    output[int((uint(i) * uint(::Stride)) + 7u)] = metal::simd_prefix_exclusive_sum(v);
    output[int((uint(i) * uint(::Stride)) + 8u)] = metal::simd_prefix_exclusive_product(v);
    output[int((uint(i) * uint(::Stride)) + 9u)] = metal::simd_prefix_inclusive_sum(v);
    output[int((uint(i) * uint(::Stride)) + 10u)] = metal::simd_prefix_inclusive_product(v);
}
