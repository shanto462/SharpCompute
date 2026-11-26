// SharpCompute.Differential.Tests.StepTimesConstantRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_StepTimesConstantRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_StepTimesConstantRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    output[0] = long(as_type<uint>(metal::step(as_type<float>(uint(input[0])), as_type<float>(uint(input[1]))) * -2.5f));
}
