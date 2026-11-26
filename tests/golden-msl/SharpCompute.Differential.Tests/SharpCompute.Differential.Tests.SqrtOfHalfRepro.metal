// SharpCompute.Differential.Tests.SqrtOfHalfRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_SqrtOfHalfRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_SqrtOfHalfRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    output[0] = long(as_type<uint>(metal::rsqrt(float3(half3(as_type<half>(ushort(input[0]))))).x));
    output[1] = long(as_type<uint>(metal::sqrt(float(as_type<half>(ushort(input[1]))))));
}
