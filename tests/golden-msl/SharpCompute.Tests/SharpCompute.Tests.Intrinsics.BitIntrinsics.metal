// SharpCompute.Tests.Intrinsics.BitIntrinsics (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_BitIntrinsics, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 24;

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

kernel void SharpCompute_Tests_Intrinsics_BitIntrinsics(
    const device uint* input [[buffer(0)]],
    device uint* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    uint u = input[t];
    int i = int(u);
    int o = int(uint(t) * uint(::Stride));
    float f = as_type<float>(u);
    output[int(uint(o) + 0u)] = as_type<uint>(f);
    output[int(uint(o) + 1u)] = uint(as_type<int>(as_type<float>(i)));
    output[int(uint(o) + 2u)] = as_type<uint>(as_type<float>(u)) ^ uint(as_type<int>(as_type<float>(i)));
    uint3 roundTrip = as_type<uint3>(as_type<float3>(uint3(u, ~u, u ^ 1431655765u)));
    int2 signedRoundTrip = as_type<int2>(as_type<float2>(int2(i, ~i)));
    output[int(uint(o) + 3u)] = roundTrip.x ^ (roundTrip.y << 1) ^ (roundTrip.z << 2) ^ uint(signedRoundTrip.x) ^ (uint(signedRoundTrip.y) << 3);
    output[int(uint(o) + 4u)] = metal::popcount(u);
    output[int(uint(o) + 5u)] = uint(metal::popcount(i));
    output[int(uint(o) + 6u)] = metal::clz(u);
    output[int(uint(o) + 7u)] = uint(metal::clz(i));
    output[int(uint(o) + 8u)] = metal::ctz(u);
    output[int(uint(o) + 9u)] = uint(metal::ctz(i));
    output[int(uint(o) + 10u)] = metal::reverse_bits(u);
    output[int(uint(o) + 11u)] = uint(metal::reverse_bits(i));
    uint4 v = uint4(u, ~u, u >> 7, u << 9);
    uint4 counted = metal::popcount(v) + (metal::clz(v) << 8) + (metal::ctz(v) << 16);
    output[int(uint(o) + 12u)] = counted.x ^ (counted.y << 1) ^ (counted.z << 2) ^ (counted.w << 3);
    uint4 reversed = metal::reverse_bits(v);
    output[int(uint(o) + 13u)] = reversed.x ^ reversed.y ^ reversed.z ^ reversed.w;
    int3 s = int3(i, int(-uint(i)), i >> 3);
    int3 signedCounted = int3(uint3(metal::popcount(s)) + uint3(metal::clz(s) << 8) + uint3(metal::ctz(s) << 16) + uint3(metal::reverse_bits(s)));
    output[int(uint(o) + 14u)] = uint(signedCounted.x ^ (signedCounted.y << 1) ^ (signedCounted.z << 2));
    output[int(uint(o) + 15u)] = uint(int(metal::popcount(u)));
    output[int(uint(o) + 16u)] = uint(int(metal::clz(u)));
    output[int(uint(o) + 17u)] = uint(int(metal::ctz(u))) + (uint(int(metal::ctz(i))) << 8);
    output[int(uint(o) + 18u)] = metal::rotate(u, uint(t % 67) & 31u);
    output[int(uint(o) + 19u)] = metal::rotate(u, (0u - uint(t % 67)) & 31u);
    output[int(uint(o) + 20u)] = metal::rotate(u, uint(int(13u - uint(t))) & 31u);
    output[int(uint(o) + 21u)] = metal::rotate(u, (0u - uint(i)) & 31u);
    output[int(uint(o) + 22u)] = (metal::isnan(f) ? 1u : 0u) | (metal::isinf(f) ? 2u : 0u) | (metal::isfinite(f) ? 4u : 0u) | (metal::isnan(f) ? 8u : 0u) | (metal::isinf(f) ? 16u : 0u) | (metal::isfinite(f) ? 32u : 0u);
    output[int(uint(o) + 23u)] = as_type<uint>(metal::copysign(f, -1.0f)) ^ (as_type<uint>(metal::copysign(2.0f, f)) >> 1) ^ (as_type<uint>(metal::copysign(f, 1.0f)) >> 2);
}
