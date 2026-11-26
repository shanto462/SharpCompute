// SharpCompute.Differential.Tests.HalfOfFractRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_HalfOfFractRepro, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline half __ms_to_half(float x) { return metal::abs(x) >= 65520.0f ? (x < 0.0f ? half(-INFINITY) : half(INFINITY)) : half(x); }

kernel void SharpCompute_Differential_Tests_HalfOfFractRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    output[0] = long(as_type<ushort>(__ms_to_half(metal::fract(as_type<float>(uint(input[0]))))));
}
