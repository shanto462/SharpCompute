// SharpCompute.Tests.Conformance.Semantics.SaturatingFloatVectors (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_SaturatingFloatVectors, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
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
// SharpCompute integer division (MslPrelude.IntegerDivision).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }
inline int2 __ms_nonzero(int2 x) { uint2 u = uint2(x); return int2(u | (((u - 1u) & ~u) >> 31)); }
inline int3 __ms_nonzero(int3 x) { uint3 u = uint3(x); return int3(u | (((u - 1u) & ~u) >> 31)); }
inline int4 __ms_nonzero(int4 x) { uint4 u = uint4(x); return int4(u | (((u - 1u) & ~u) >> 31)); }
inline uint __ms_nonzero(uint x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint2 __ms_nonzero(uint2 x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint3 __ms_nonzero(uint3 x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint4 __ms_nonzero(uint4 x) { return x | (((x - 1u) & ~x) >> 31); }
inline long __ms_nonzero(long x) { ulong u = ulong(x); return long(u | (((u - 1UL) & ~u) >> 63)); }
inline long2 __ms_nonzero(long2 x) { ulong2 u = ulong2(x); return long2(u | (((u - 1UL) & ~u) >> 63)); }
inline long3 __ms_nonzero(long3 x) { ulong3 u = ulong3(x); return long3(u | (((u - 1UL) & ~u) >> 63)); }
inline long4 __ms_nonzero(long4 x) { ulong4 u = ulong4(x); return long4(u | (((u - 1UL) & ~u) >> 63)); }
inline ulong __ms_nonzero(ulong x) { return x | (((x - 1UL) & ~x) >> 63); }
inline ulong2 __ms_nonzero(ulong2 x) { return x | (((x - 1UL) & ~x) >> 63); }
inline ulong3 __ms_nonzero(ulong3 x) { return x | (((x - 1UL) & ~x) >> 63); }
inline ulong4 __ms_nonzero(ulong4 x) { return x | (((x - 1UL) & ~x) >> 63); }
inline char __ms_nonzero(char x) { return char(__ms_nonzero(int(x))); }
inline char2 __ms_nonzero(char2 x) { return char2(__ms_nonzero(int2(x))); }
inline char3 __ms_nonzero(char3 x) { return char3(__ms_nonzero(int3(x))); }
inline char4 __ms_nonzero(char4 x) { return char4(__ms_nonzero(int4(x))); }
inline uchar __ms_nonzero(uchar x) { return uchar(__ms_nonzero(int(x))); }
inline uchar2 __ms_nonzero(uchar2 x) { return uchar2(__ms_nonzero(int2(x))); }
inline uchar3 __ms_nonzero(uchar3 x) { return uchar3(__ms_nonzero(int3(x))); }
inline uchar4 __ms_nonzero(uchar4 x) { return uchar4(__ms_nonzero(int4(x))); }
inline short __ms_nonzero(short x) { return short(__ms_nonzero(int(x))); }
inline short2 __ms_nonzero(short2 x) { return short2(__ms_nonzero(int2(x))); }
inline short3 __ms_nonzero(short3 x) { return short3(__ms_nonzero(int3(x))); }
inline short4 __ms_nonzero(short4 x) { return short4(__ms_nonzero(int4(x))); }
inline ushort __ms_nonzero(ushort x) { return ushort(__ms_nonzero(int(x))); }
inline ushort2 __ms_nonzero(ushort2 x) { return ushort2(__ms_nonzero(int2(x))); }
inline ushort3 __ms_nonzero(ushort3 x) { return ushort3(__ms_nonzero(int3(x))); }
inline ushort4 __ms_nonzero(ushort4 x) { return ushort4(__ms_nonzero(int4(x))); }
inline uint __ms_div_near_max(uint a, uint b) { return b > a ? 0u : (a - b) / __ms_nonzero(b) + 1u; }
inline uint __ms_rem_near_max(uint a, uint b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline ulong __ms_div_near_max(ulong a, ulong b) { return b > a ? 0UL : (a - b) / __ms_nonzero(b) + 1UL; }
inline ulong __ms_rem_near_max(ulong a, ulong b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline uint2 __ms_div_near_max(uint2 a, uint2 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint2(0u), b > a); }
inline uint2 __ms_rem_near_max(uint2 a, uint2 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline uint3 __ms_div_near_max(uint3 a, uint3 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint3(0u), b > a); }
inline uint3 __ms_rem_near_max(uint3 a, uint3 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline uint4 __ms_div_near_max(uint4 a, uint4 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint4(0u), b > a); }
inline uint4 __ms_rem_near_max(uint4 a, uint4 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline long __ms_div_near_max(long a, long b) { ulong q = __ms_div_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long((a < 0L) != (b < 0L) ? 0UL - q : q); }
inline long __ms_rem_near_max(long a, long b) { ulong r = __ms_rem_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long(a < 0L ? 0UL - r : r); }

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

kernel void SharpCompute_Tests_Conformance_Semantics_SaturatingFloatVectors(
    const device float* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    float4 v = float4(input[i], input[(int(uint(i) + 1u)) % __ms_nonzero(__c.count)], input[(int(uint(i) + 2u)) % __ms_nonzero(__c.count)], input[(int(uint(i) + 3u)) % __ms_nonzero(__c.count)]);
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
