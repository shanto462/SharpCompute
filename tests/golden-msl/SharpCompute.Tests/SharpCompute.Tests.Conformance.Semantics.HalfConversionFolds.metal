// SharpCompute.Tests.Conformance.Semantics.HalfConversionFolds (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_HalfConversionFolds, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline ushort __ms_sat_ushort(float x) { float y = x > 0.0f ? x : 0.0f; return ushort(metal::min(y, 65535.0f)); }
inline ushort __ms_sat_ushort(half x) { return __ms_sat_ushort(float(x)); }
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline int3 __ms_sat_int3(float3 x) { float3 y = metal::select(float3(0.0f), x, x == x); int3 r = int3(metal::clamp(y, float3(-2147483648.0f), float3(2147483520.0f))); return metal::select(r, int3(2147483647), x >= 2147483648.0f); }
inline int3 __ms_sat_int3(half3 x) { return __ms_sat_int3(float3(x)); }
inline uint __ms_sat_uint(float x) { float y = x > 0.0f ? x : 0.0f; uint r = uint(metal::min(y, 4294967040.0f)); return x >= 4294967296.0f ? 4294967295u : r; }
inline uint __ms_sat_uint(half x) { return __ms_sat_uint(float(x)); }
inline long __ms_sat_long(float x) { float y = x == x ? x : 0.0f; long r = long(metal::clamp(y, -9223372036854775808.0f, 9223371487098961920.0f)); return x >= 9223372036854775808.0f ? 9223372036854775807L : r; }
inline long __ms_sat_long(half x) { return __ms_sat_long(float(x)); }
inline ulong __ms_sat_ulong(float x) { float y = x > 0.0f ? x : 0.0f; ulong r = ulong(metal::min(y, 18446742974197923840.0f)); return x >= 18446744073709551616.0f ? 18446744073709551615UL : r; }
inline ulong __ms_sat_ulong(half x) { return __ms_sat_ulong(float(x)); }
inline half __ms_to_half(float x) { return metal::abs(x) >= 65520.0f ? (x < 0.0f ? half(-INFINITY) : half(INFINITY)) : half(x); }
inline half3 __ms_to_half3(float3 x) { return metal::select(half3(x), metal::select(half3(INFINITY), half3(-INFINITY), x < 0.0f), metal::abs(x) >= 65520.0f); }
inline half4 __ms_to_half4(float4 x) { return metal::select(half4(x), metal::select(half4(INFINITY), half4(-INFINITY), x < 0.0f), metal::abs(x) >= 65520.0f); }

constant half BigHalf = (1E+30f >= 65520.0f ? half(INFINITY) : (1E+30f <= -65520.0f ? half(-INFINITY) : half(1E+30f)));
constant float Big = 1E+30f;
constant half FromBig = (::Big >= 65520.0f ? half(INFINITY) : (::Big <= -65520.0f ? half(-INFINITY) : half(::Big)));
constant half FromNegated = ((-::Big) >= 65520.0f ? half(INFINITY) : ((-::Big) <= -65520.0f ? half(-INFINITY) : half((-::Big))));
constant half3 FromVector = half3(float3(::Big, 1.0f, -::Big).x >= 65520.0f ? half(INFINITY) : (float3(::Big, 1.0f, -::Big).x <= -65520.0f ? half(-INFINITY) : half(float3(::Big, 1.0f, -::Big).x)), float3(::Big, 1.0f, -::Big).y >= 65520.0f ? half(INFINITY) : (float3(::Big, 1.0f, -::Big).y <= -65520.0f ? half(-INFINITY) : half(float3(::Big, 1.0f, -::Big).y)), float3(::Big, 1.0f, -::Big).z >= 65520.0f ? half(INFINITY) : (float3(::Big, 1.0f, -::Big).z <= -65520.0f ? half(-INFINITY) : half(float3(::Big, 1.0f, -::Big).z)));

kernel void SharpCompute_Tests_Conformance_Semantics_HalfConversionFolds(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    bool b = input[0] != 0UL;
    float4 v = float4(as_type<float>(uint(input[1])), as_type<float>(uint(input[2])), as_type<float>(uint(input[3])), as_type<float>(uint(input[4])));
    float f3e9 = as_type<float>(uint(input[5]));
    float minus3e9 = as_type<float>(uint(input[6]));
    float f1e20 = as_type<float>(uint(input[7]));
    float infinity = as_type<float>(uint(input[8]));
    float x = as_type<float>(uint(input[9]));
    half4 h = __ms_to_half4(metal::max(v, float4(1E+30f, INFINITY, -3.0f, 1.5f)));
    output[0] = long(as_type<ushort>(h.x));
    output[1] = long(as_type<ushort>(h.y));
    output[2] = long(as_type<ushort>(h.z));
    output[3] = long(as_type<ushort>(h.w));
    output[4] = long(as_type<ushort>(__ms_to_half(metal::max(x, 1E+30f))));
    output[5] = long(as_type<ushort>(__ms_to_half(metal::min(x, -1E+30f))));
    output[6] = long(as_type<ushort>(__ms_to_half(metal::min(x, 1E+30f))));
    output[7] = long(as_type<ushort>(__ms_to_half(metal::max(x, -65520.0f))));
    output[8] = long(as_type<ushort>(__ms_to_half(b ? x : 1E+10f)));
    output[9] = long(as_type<ushort>(__ms_to_half(b ? 1E+10f : x)));
    output[10] = long(as_type<ushort>(__ms_to_half(b ? x : -65520.0f)));
    output[11] = long(as_type<ushort>(__ms_to_half(b ? NAN : 65520.0f)));
    output[12] = long(as_type<ushort>(__ms_to_half(float(__ms_sat_int(f3e9)))));
    output[13] = long(as_type<ushort>(__ms_to_half(float(__ms_sat_int(minus3e9)))));
    output[14] = long(as_type<ushort>(__ms_to_half(float(__ms_sat_long(f1e20)))));
    output[15] = long(as_type<ushort>(__ms_to_half(float(__ms_sat_long(infinity)))));
    output[16] = long(as_type<ushort>(__ms_to_half(float(__ms_sat_uint(f1e20)))));
    output[17] = long(as_type<ushort>(__ms_to_half(float(__ms_sat_ulong(infinity)))));
    half3 through = __ms_to_half3(float3(__ms_sat_int3(float3(f3e9, minus3e9, NAN))));
    output[18] = long(as_type<ushort>(through.x));
    output[19] = long(as_type<ushort>(through.y));
    output[20] = long(as_type<ushort>(through.z));
    output[21] = long(as_type<ushort>(__ms_to_half(1E+30f)));
    output[22] = long(as_type<ushort>(__ms_to_half(-70000.0f)));
    output[23] = long(as_type<ushort>(half(65519.0f)));
    half3 constants = __ms_to_half3(float3(1E+30f, 2.0f, -65520.0f));
    output[24] = long(as_type<ushort>(constants.x));
    output[25] = long(as_type<ushort>(constants.y));
    output[26] = long(as_type<ushort>(constants.z));
    output[27] = long(as_type<ushort>(::BigHalf));
    output[28] = long(as_type<ushort>(::FromBig));
    output[29] = long(as_type<ushort>(::FromNegated));
    output[30] = long(as_type<ushort>(::FromVector.x));
    output[31] = long(as_type<ushort>(::FromVector.y));
    output[32] = long(as_type<ushort>(::FromVector.z));
    output[33] = long(as_type<ushort>(half(__ms_sat_int(f3e9))));
    output[34] = long(as_type<ushort>(half(__ms_sat_uint(f3e9))));
    output[35] = long(as_type<ushort>(half(__ms_sat_ushort(f3e9))));
}
