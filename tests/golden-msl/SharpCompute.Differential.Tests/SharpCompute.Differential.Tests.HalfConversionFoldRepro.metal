// SharpCompute.Differential.Tests.HalfConversionFoldRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_HalfConversionFoldRepro, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

kernel void SharpCompute_Differential_Tests_HalfConversionFoldRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    float4 v = float4(as_type<float>(uint(input[0])), as_type<float>(uint(input[1])), as_type<float>(uint(input[2])), as_type<float>(uint(input[3])));
    half4 h = half4(metal::max(v, float4(1E+30f, INFINITY, -3.0f, 1.5f)));
    float f3e9 = as_type<float>(uint(input[4]));
    output[0] = long(as_type<ushort>(h.x));
    output[1] = long(as_type<ushort>(h.y));
    output[2] = long(as_type<ushort>(h.z));
    output[3] = long(as_type<ushort>(h.w));
    output[4] = long(as_type<ushort>(half(float(__ms_sat_int(f3e9)))));
}
