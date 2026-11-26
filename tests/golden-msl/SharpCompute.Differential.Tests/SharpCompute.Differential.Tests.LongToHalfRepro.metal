// SharpCompute.Differential.Tests.LongToHalfRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_LongToHalfRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_LongToHalfRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    output[0] = long(as_type<ushort>(half(long(input[0]))));
    output[1] = long(int(half(input[1])));
}
