// SharpCompute.Tests.Conformance.Semantics.SaturatingHalfVectors (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_SaturatingHalfVectors, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline int2 __ms_sat_int2(float2 x) { float2 y = metal::select(float2(0.0f), x, x == x); int2 r = int2(metal::clamp(y, float2(-2147483648.0f), float2(2147483520.0f))); return metal::select(r, int2(2147483647), x >= 2147483648.0f); }
inline int2 __ms_sat_int2(half2 x) { return __ms_sat_int2(float2(x)); }
inline int3 __ms_sat_int3(float3 x) { float3 y = metal::select(float3(0.0f), x, x == x); int3 r = int3(metal::clamp(y, float3(-2147483648.0f), float3(2147483520.0f))); return metal::select(r, int3(2147483647), x >= 2147483648.0f); }
inline int3 __ms_sat_int3(half3 x) { return __ms_sat_int3(float3(x)); }
inline int4 __ms_sat_int4(float4 x) { float4 y = metal::select(float4(0.0f), x, x == x); int4 r = int4(metal::clamp(y, float4(-2147483648.0f), float4(2147483520.0f))); return metal::select(r, int4(2147483647), x >= 2147483648.0f); }
inline int4 __ms_sat_int4(half4 x) { return __ms_sat_int4(float4(x)); }
inline uint2 __ms_sat_uint2(float2 x) { float2 y = metal::select(float2(0.0f), x, x > 0.0f); uint2 r = uint2(metal::min(y, float2(4294967040.0f))); return metal::select(r, uint2(4294967295u), x >= 4294967296.0f); }
inline uint2 __ms_sat_uint2(half2 x) { return __ms_sat_uint2(float2(x)); }
inline uint3 __ms_sat_uint3(float3 x) { float3 y = metal::select(float3(0.0f), x, x > 0.0f); uint3 r = uint3(metal::min(y, float3(4294967040.0f))); return metal::select(r, uint3(4294967295u), x >= 4294967296.0f); }
inline uint3 __ms_sat_uint3(half3 x) { return __ms_sat_uint3(float3(x)); }
inline uint4 __ms_sat_uint4(float4 x) { float4 y = metal::select(float4(0.0f), x, x > 0.0f); uint4 r = uint4(metal::min(y, float4(4294967040.0f))); return metal::select(r, uint4(4294967295u), x >= 4294967296.0f); }
inline uint4 __ms_sat_uint4(half4 x) { return __ms_sat_uint4(float4(x)); }

constant int Slots = 18;

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

kernel void SharpCompute_Tests_Conformance_Semantics_SaturatingHalfVectors(
    const device half* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    half4 v = half4(input[i], input[(int(uint(i) + 1u)) % __c.count], input[(int(uint(i) + 2u)) % __c.count], input[(int(uint(i) + 3u)) % __c.count]);
    int4 i4 = __ms_sat_int4(v);
    uint4 u4 = __ms_sat_uint4(v);
    int3 i3 = __ms_sat_int3(v.xyz);
    uint3 u3 = __ms_sat_uint3(v.xyz);
    int2 i2 = __ms_sat_int2(v.xy);
    uint2 u2 = __ms_sat_uint2(v.xy);
    output[int(uint(o) + 0u)] = long(i4.x);
    output[int(uint(o) + 1u)] = long(i4.y);
    output[int(uint(o) + 2u)] = long(i4.z);
    output[int(uint(o) + 3u)] = long(i4.w);
    output[int(uint(o) + 4u)] = long(u4.x);
    output[int(uint(o) + 5u)] = long(u4.y);
    output[int(uint(o) + 6u)] = long(u4.z);
    output[int(uint(o) + 7u)] = long(u4.w);
    output[int(uint(o) + 8u)] = long(i3.x);
    output[int(uint(o) + 9u)] = long(i3.y);
    output[int(uint(o) + 10u)] = long(i3.z);
    output[int(uint(o) + 11u)] = long(u3.x);
    output[int(uint(o) + 12u)] = long(u3.y);
    output[int(uint(o) + 13u)] = long(u3.z);
    output[int(uint(o) + 14u)] = long(i2.x);
    output[int(uint(o) + 15u)] = long(i2.y);
    output[int(uint(o) + 16u)] = long(u2.x);
    output[int(uint(o) + 17u)] = long(u2.y);
}
