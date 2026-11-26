// SharpCompute.Differential.Tests.ConversionFoldRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_ConversionFoldRepro, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

kernel void SharpCompute_Differential_Tests_ConversionFoldRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    bool b = input[0] != 0UL;
    half h = as_type<half>(ushort(input[1]));
    int t = __ms_sat_int(h);
    output[0] = long(__ms_sat_int(b ? NAN : 1E+10f));
    output[1] = long(t == -2147483647 - 1 ? 0 : 1);
    output[2] = long(-ulong(__ms_sat_int(h)));
}
