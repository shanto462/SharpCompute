// SharpCompute.Differential.Tests.PowNearOneRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_PowNearOneRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_PowNearOneRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    output[0] = long(metal::isinf(metal::pow(as_type<float>(uint(input[0])), as_type<float>(uint(input[1])))) ? 1 : 0);
}
