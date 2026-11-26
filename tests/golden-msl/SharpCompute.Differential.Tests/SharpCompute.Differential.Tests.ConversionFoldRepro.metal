// SharpCompute.Differential.Tests.ConversionFoldRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_ConversionFoldRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_ConversionFoldRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    bool b = input[0] != 0UL;
    half h = as_type<half>(ushort(input[1]));
    int t = int(h);
    output[0] = long(int(b ? NAN : 1E+10f));
    output[1] = long(t == -2147483647 - 1 ? 0 : 1);
    output[2] = long(-ulong(int(h)));
}
