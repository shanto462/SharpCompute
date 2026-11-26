// SharpCompute.Differential.Tests.HalfDivisionRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_HalfDivisionRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_HalfDivisionRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    output[0] = long(as_type<ushort>(as_type<half>(ushort(input[0])) / half(3.0f)));
}
