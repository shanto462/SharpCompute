// SharpCompute.Differential.Tests.LongToHalfRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_LongToHalfRepro, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

kernel void SharpCompute_Differential_Tests_LongToHalfRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    output[0] = long(as_type<ushort>(half(long(input[0]))));
    output[1] = long(__ms_sat_int(half(input[1])));
}
