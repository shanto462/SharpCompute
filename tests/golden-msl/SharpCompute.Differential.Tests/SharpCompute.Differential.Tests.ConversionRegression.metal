// SharpCompute.Differential.Tests.ConversionRegression (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_ConversionRegression, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline char __ms_sat_char(float x) { float y = x == x ? x : 0.0f; return char(metal::clamp(y, -128.0f, 127.0f)); }
inline char __ms_sat_char(half x) { return __ms_sat_char(float(x)); }
inline uchar __ms_sat_uchar(float x) { float y = x > 0.0f ? x : 0.0f; return uchar(metal::min(y, 255.0f)); }
inline uchar __ms_sat_uchar(half x) { return __ms_sat_uchar(float(x)); }
inline short __ms_sat_short(float x) { float y = x == x ? x : 0.0f; return short(metal::clamp(y, -32768.0f, 32767.0f)); }
inline short __ms_sat_short(half x) { return __ms_sat_short(float(x)); }
inline ushort __ms_sat_ushort(float x) { float y = x > 0.0f ? x : 0.0f; return ushort(metal::min(y, 65535.0f)); }
inline ushort __ms_sat_ushort(half x) { return __ms_sat_ushort(float(x)); }
inline uint __ms_sat_uint(float x) { float y = x > 0.0f ? x : 0.0f; uint r = uint(metal::min(y, 4294967040.0f)); return x >= 4294967296.0f ? 4294967295u : r; }
inline uint __ms_sat_uint(half x) { return __ms_sat_uint(float(x)); }
inline ulong __ms_sat_ulong(float x) { float y = x > 0.0f ? x : 0.0f; ulong r = ulong(metal::min(y, 18446742974197923840.0f)); return x >= 18446744073709551616.0f ? 18446744073709551615UL : r; }
inline ulong __ms_sat_ulong(half x) { return __ms_sat_ulong(float(x)); }

kernel void SharpCompute_Differential_Tests_ConversionRegression(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    int i = int(input[0]);
    int i300 = int(input[1]);
    short minusOne = short(input[2]);
    short s16384 = short(input[3]);
    char minus17 = char(input[4]);
    long minus129 = long(input[5]);
    int i70000 = int(input[6]);
    output[0] = long(__ms_sat_uchar(float(i)));
    output[1] = long(__ms_sat_uchar(float(i300)));
    output[2] = long(__ms_sat_uchar(half(minusOne)));
    output[3] = long(__ms_sat_char(float(s16384)));
    output[4] = long(__ms_sat_uint(float(minusOne)));
    output[5] = long(__ms_sat_uint(half(minus17)));
    output[6] = long(__ms_sat_ulong(float(minus17)));
    output[7] = long(__ms_sat_ushort(float(i70000)));
    output[8] = long(__ms_sat_short(float(minus129) * 256.0f));
    output[9] = long(__ms_sat_uchar(half(i300)));
    output[10] = long(__ms_sat_ushort(half(minusOne)));
}
