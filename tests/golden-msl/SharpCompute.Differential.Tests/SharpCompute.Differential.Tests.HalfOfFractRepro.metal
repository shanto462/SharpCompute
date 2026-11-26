// SharpCompute.Differential.Tests.HalfOfFractRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_HalfOfFractRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_HalfOfFractRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    output[0] = long(as_type<ushort>(half(metal::fract(as_type<float>(uint(input[0]))))));
}
