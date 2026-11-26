// SharpCompute.Tests.Shaders.SimdFastMath (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdFastMath, compile options FastMath
#include <metal_stdlib>

using namespace metal;

constant int Stride = 5;

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

kernel void SharpCompute_Tests_Shaders_SimdFastMath(
    const device float* input [[buffer(0)]],
    device float* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float v = input[i];
    output[int((uint(i) * uint(::Stride)) + 0u)] = metal::simd_sum(v);
    output[int((uint(i) * uint(::Stride)) + 1u)] = metal::simd_max((v * 0.5f) + 1.0f);
    output[int((uint(i) * uint(::Stride)) + 2u)] = metal::simd_prefix_exclusive_sum(v);
    output[int((uint(i) * uint(::Stride)) + 3u)] = float(metal::simd_sum(int(v > 0.0f)));
    output[int((uint(i) * uint(::Stride)) + 4u)] = metal::simd_shuffle_xor(v, ushort(1));
}
