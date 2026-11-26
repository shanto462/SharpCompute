// SharpCompute.Tests.Conformance.Semantics.SaturatingFolds (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_SaturatingFolds, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline char __ms_sat_char(float x) { float y = x == x ? x : 0.0f; return char(metal::clamp(y, -128.0f, 127.0f)); }
inline char __ms_sat_char(half x) { return __ms_sat_char(float(x)); }
inline uchar __ms_sat_uchar(float x) { float y = x > 0.0f ? x : 0.0f; return uchar(metal::min(y, 255.0f)); }
inline uchar __ms_sat_uchar(half x) { return __ms_sat_uchar(float(x)); }
inline short __ms_sat_short(float x) { float y = x == x ? x : 0.0f; return short(metal::clamp(y, -32768.0f, 32767.0f)); }
inline short __ms_sat_short(half x) { return __ms_sat_short(float(x)); }
inline ushort __ms_sat_ushort(float x) { float y = x > 0.0f ? x : 0.0f; return ushort(metal::min(y, 65535.0f)); }
inline ushort __ms_sat_ushort(half x) { return __ms_sat_ushort(float(x)); }
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline int3 __ms_sat_int3(float3 x) { float3 y = metal::select(float3(0.0f), x, x == x); int3 r = int3(metal::clamp(y, float3(-2147483648.0f), float3(2147483520.0f))); return metal::select(r, int3(2147483647), x >= 2147483648.0f); }
inline int3 __ms_sat_int3(half3 x) { return __ms_sat_int3(float3(x)); }
inline uint __ms_sat_uint(float x) { float y = x > 0.0f ? x : 0.0f; uint r = uint(metal::min(y, 4294967040.0f)); return x >= 4294967296.0f ? 4294967295u : r; }
inline uint __ms_sat_uint(half x) { return __ms_sat_uint(float(x)); }
inline uint2 __ms_sat_uint2(float2 x) { float2 y = metal::select(float2(0.0f), x, x > 0.0f); uint2 r = uint2(metal::min(y, float2(4294967040.0f))); return metal::select(r, uint2(4294967295u), x >= 4294967296.0f); }
inline uint2 __ms_sat_uint2(half2 x) { return __ms_sat_uint2(float2(x)); }
inline long __ms_sat_long(float x) { float y = x == x ? x : 0.0f; long r = long(metal::clamp(y, -9223372036854775808.0f, 9223371487098961920.0f)); return x >= 9223372036854775808.0f ? 9223372036854775807L : r; }
inline long __ms_sat_long(half x) { return __ms_sat_long(float(x)); }
inline ulong __ms_sat_ulong(float x) { float y = x > 0.0f ? x : 0.0f; ulong r = ulong(metal::min(y, 18446742974197923840.0f)); return x >= 18446744073709551616.0f ? 18446744073709551615UL : r; }
inline ulong __ms_sat_ulong(half x) { return __ms_sat_ulong(float(x)); }

constant float Big = 1E+10f;
constant int BigInt = int(::Big >= 2147483648.0f ? 2147483647 : (::Big >= -2147483648.0f ? int(::Big) : (::Big < 0.0f ? (-2147483647 - 1) : 0)));
constant int BigNegated = int((-::Big) >= 2147483648.0f ? 2147483647 : ((-::Big) >= -2147483648.0f ? int(-::Big) : ((-::Big) < 0.0f ? (-2147483647 - 1) : 0)));
constant int Scaled = int((::Big * 0.1f) >= 2147483648.0f ? 2147483647 : ((::Big * 0.1f) >= -2147483648.0f ? int(::Big * 0.1f) : ((::Big * 0.1f) < 0.0f ? (-2147483647 - 1) : 0)));
constant half Small = half(-300.0f);
constant uint SmallUInt = uint(float(::Small) >= 4294967296.0f ? 4294967295u : (float(::Small) > 0.0f ? uint(float(::Small)) : 0u)) + uint(::Big >= 4294967296.0f ? 4294967295u : (::Big > 0.0f ? uint(::Big) : 0u));

kernel void SharpCompute_Tests_Conformance_Semantics_SaturatingFolds(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    bool b = input[0] != 0UL;
    half negative = as_type<half>(ushort(input[1]));
    half positive = as_type<half>(ushort(input[2]));
    uint u = uint(input[3]);
    long l = long(input[4]);
    long minusFive = long(input[5]);
    ulong ul = input[6];
    int i70000 = int(input[7]);
    int minus70000 = int(input[8]);
    float f3e9 = as_type<float>(uint(input[9]));
    output[0] = long(__ms_sat_int(b ? NAN : 1E+10f));
    output[1] = long(__ms_sat_int(b ? 1E+10f : NAN));
    output[2] = long(__ms_sat_uchar(b ? NAN : 1E+10f));
    output[3] = long(__ms_sat_uchar(b ? 300.0f : -INFINITY));
    output[4] = long(__ms_sat_char(b ? NAN : -1E+10f));
    output[5] = long(__ms_sat_char(b ? NAN : INFINITY));
    output[6] = long(__ms_sat_short(b ? 1E+10f : NAN));
    output[7] = long(__ms_sat_short(b ? NAN : -INFINITY));
    output[8] = long(__ms_sat_ushort(b ? NAN : 65536.0f));
    output[9] = long(__ms_sat_ushort(b ? 65536.0f : -1.0f));
    output[10] = long(__ms_sat_uint(b ? NAN : 4.2949673E+09f));
    output[11] = long(__ms_sat_uint(b ? 4.2949673E+09f : NAN));
    output[12] = __ms_sat_long(b ? NAN : 1E+30f);
    output[13] = __ms_sat_long(b ? NAN : -INFINITY);
    output[14] = long(__ms_sat_ulong(b ? NAN : 1E+30f));
    output[15] = long(__ms_sat_ulong(b ? 1E+30f : -1E+30f));
    int3 vi = __ms_sat_int3(b ? float3(1.0f, 2.0f, 3.0f) : float3(NAN, 1E+10f, -1E+10f));
    uint2 vu = __ms_sat_uint2(b ? float2(1.0f, 2.0f) : float2(INFINITY, -INFINITY));
    output[16] = long(vi.x);
    output[17] = long(vi.y);
    output[18] = long(vi.z);
    output[19] = long(vu.x);
    output[20] = long(vu.y);
    int t = __ms_sat_int(negative);
    output[21] = long(t == -2147483647 - 1 ? 1 : 0);
    output[22] = long(-ulong(__ms_sat_int(negative)));
    output[23] = long(__ms_sat_uint(negative));
    output[24] = __ms_sat_long(negative);
    output[25] = long(__ms_sat_uchar(positive));
    output[26] = long(__ms_sat_char(negative));
    output[27] = long(__ms_sat_int(positive) > 65504 ? 1 : 0);
    output[28] = long(__ms_sat_int(float(u)));
    output[29] = long(__ms_sat_int(float(l)));
    output[30] = long(__ms_sat_uint(float(minusFive)));
    output[31] = __ms_sat_long(float(ul));
    output[32] = long(__ms_sat_ushort(half(i70000)));
    output[33] = long(__ms_sat_char(half(minus70000)));
    output[34] = long(__ms_sat_uchar(float(i70000)));
    int big = __ms_sat_int(f3e9);
    output[35] = long(big == 2147483647 ? 1 : 0);
    output[36] = long(__ms_sat_uint(f3e9) > 2999999000u ? 1 : 0);
    output[37] = long(__ms_sat_int(f3e9 * 0.0f));
    output[38] = long(::BigInt);
    output[39] = long(::BigNegated);
    output[40] = long(::Scaled);
    output[41] = long(::SmallUInt);
}
