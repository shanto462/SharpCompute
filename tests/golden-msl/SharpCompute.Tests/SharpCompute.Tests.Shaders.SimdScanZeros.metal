// SharpCompute.Tests.Shaders.SimdScanZeros (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdScanZeros, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }

constant int Stride = 22;

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

kernel void SharpCompute_Tests_Shaders_SimdScanZeros(
    const device float* floats [[buffer(0)]],
    const device half* halves [[buffer(1)]],
    device uint* output [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint __lane [[thread_index_in_simdgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Stride));
    if (__c.skip > 0 && int(__lane) % __ms_nonzero(__c.skip) == 0)
    {
        return;
    }
    float f = floats[i];
    half h = halves[i];
    output[int(uint(o) + 0u)] = 1u;
    output[int(uint(o) + 1u)] = uint(metal::simd_prefix_exclusive_sum(int(true)));
    output[int(uint(o) + 2u)] = uint(metal::quad_prefix_exclusive_sum(1));
    output[int(uint(o) + 3u)] = as_type<uint>(metal::simd_prefix_exclusive_sum(f));
    output[int(uint(o) + 4u)] = as_type<uint>(metal::simd_prefix_inclusive_sum(f));
    output[int(uint(o) + 5u)] = as_type<uint>(metal::simd_prefix_exclusive_product(f));
    output[int(uint(o) + 6u)] = as_type<uint>(metal::quad_prefix_exclusive_sum(f));
    output[int(uint(o) + 7u)] = as_type<uint>(metal::quad_prefix_inclusive_sum(f));
    output[int(uint(o) + 8u)] = as_type<uint>(metal::quad_prefix_exclusive_product(f));
    output[int(uint(o) + 9u)] = as_type<uint>(metal::simd_prefix_exclusive_sum(float3(1.0f, f, 2.0f)).y);
    output[int(uint(o) + 10u)] = uint(as_type<ushort>(metal::simd_prefix_exclusive_sum(h)));
    output[int(uint(o) + 11u)] = uint(as_type<ushort>(metal::simd_prefix_inclusive_sum(h)));
    output[int(uint(o) + 12u)] = uint(as_type<ushort>(metal::simd_prefix_exclusive_product(h)));
    output[int(uint(o) + 13u)] = uint(as_type<ushort>(metal::quad_prefix_exclusive_sum(h)));
    output[int(uint(o) + 14u)] = uint(as_type<ushort>(metal::quad_prefix_inclusive_sum(h)));
    output[int(uint(o) + 15u)] = uint(as_type<ushort>(metal::quad_prefix_exclusive_product(h)));
    output[int(uint(o) + 16u)] = uint(as_type<ushort>(metal::simd_prefix_exclusive_sum(half2(h, h)).y));
    output[int(uint(o) + 17u)] = as_type<uint>(metal::simd_sum(f));
    output[int(uint(o) + 18u)] = uint(as_type<ushort>(metal::simd_sum(h)));
    output[int(uint(o) + 19u)] = as_type<uint>(metal::quad_sum(f));
    output[int(uint(o) + 20u)] = uint(as_type<ushort>(metal::quad_sum(h)));
    output[int(uint(o) + 21u)] = uint(int(__lane));
}
