// SharpCompute.Differential.Tests.RsqrtOfZeroRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_RsqrtOfZeroRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_RsqrtOfZeroRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    float zero = as_type<float>(uint(input[0]));
    half2 x = half2(as_type<half>(ushort(input[1])));
    half2 halfZero = half2(as_type<half>(ushort(input[2])));
    output[0] = long(as_type<uint>(metal::abs(metal::rsqrt(zero))));
    output[1] = long(as_type<uint>(metal::sign(metal::rsqrt(zero))));
    output[2] = long(as_type<ushort>(metal::fmod(x, metal::rsqrt(halfZero)).x));
    output[3] = long(as_type<ushort>(metal::tanh(metal::rsqrt(halfZero)).x));
}
