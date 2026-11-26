// SharpCompute.Differential.Tests.GuardedDivisionRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_GuardedDivisionRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_GuardedDivisionRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    long x = long(input[0]);
    long l = long(input[1]);
    long t = l == -9223372036854775807L - 1L ? 0L : long(metal::abs(l));
    output[0] = t == 0L ? 0L : x % t;
    uint d = uint(input[2]);
    uint a = uint(input[3]);
    uint y = uint(metal::max(as_type<half>(ushort(input[4])), 0.0h));
    uint q = d == 0u ? 0u : a / d;
    output[1] = long(q == 0u ? 0u : y % q);
}
