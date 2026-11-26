// SharpCompute.Tests.Intrinsics.HalfGeometry (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_HalfGeometry, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 9;

static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");

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

kernel void SharpCompute_Tests_Intrinsics_HalfGeometry(
    const device packed_half4* a [[buffer(0)]],
    const device packed_half4* b [[buffer(1)]],
    device packed_half4* output [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Stride));
    half4 x = a[i];
    half4 y = b[i];
    output[o] = half4(metal::dot(x.xy, y.xy), metal::dot(x.xyz, y.xyz), metal::dot(x, y), metal::length(x.xy));
    output[int(uint(o) + 1u)] = half4(metal::length(x.xyz), metal::length(x), metal::distance(x.xy, y.xy), metal::distance(x.xyz, y.xyz));
    output[int(uint(o) + 2u)] = half4(metal::distance(x, y), metal::dot(y, y), metal::length(y.zw), metal::distance(y.xw, x.zy));
    output[int(uint(o) + 3u)] = half4(metal::normalize(x.xy), metal::normalize(y.zw));
    output[int(uint(o) + 4u)] = half4(metal::normalize(x.xyz), metal::length(y.yzw));
    output[int(uint(o) + 5u)] = metal::normalize(x);
    output[int(uint(o) + 6u)] = half4(metal::reflect(x.xy, y.xy), metal::reflect(y.zw, x.zw));
    output[int(uint(o) + 7u)] = half4(metal::reflect(x.xyz, y.xyz), metal::dot(x.yzw, y.yzw));
    output[int(uint(o) + 8u)] = half4(metal::cross(x.xyz, y.xyz), metal::reflect(x, y).w);
}
