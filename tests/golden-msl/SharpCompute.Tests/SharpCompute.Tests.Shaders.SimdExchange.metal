// SharpCompute.Tests.Shaders.SimdExchange (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdExchange, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 8;

struct __Constants
{
    int delta;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, delta) == 0, "SharpCompute: offset of '__Constants.delta' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_SimdExchange(
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
    output[int((uint(i) * uint(::Stride)) + 0u)] = metal::simd_shuffle(v, ushort(2));
    output[int((uint(i) * uint(::Stride)) + 1u)] = metal::simd_shuffle(v, ushort(int(uint(metal::simd_sum(int(true))) - 1u - uint(int(__lane)))));
    output[int((uint(i) * uint(::Stride)) + 2u)] = metal::simd_broadcast_first(v);
    output[int((uint(i) * uint(::Stride)) + 3u)] = metal::simd_shuffle_up(v, ushort(__c.delta));
    output[int((uint(i) * uint(::Stride)) + 4u)] = metal::simd_shuffle_down(v, ushort(__c.delta));
    output[int((uint(i) * uint(::Stride)) + 5u)] = metal::simd_shuffle_xor(v, ushort(1));
    output[int((uint(i) * uint(::Stride)) + 6u)] = metal::simd_shuffle_xor(v, ushort(16));
    output[int((uint(i) * uint(::Stride)) + 7u)] = metal::simd_shuffle_up(metal::simd_shuffle_down(v, ushort(1)), ushort(1));
}
