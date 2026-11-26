// SharpCompute.Differential.Tests.ConstantDividendRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_ConstantDividendRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_ConstantDividendRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    uint x = metal::max(uint(input[0]), 1u);
    long y = long(x);
    output[0] = long(4294967295u / x);
    output[1] = long(4294967295u % x);
    output[2] = 4294967295L % y;
}
