// SharpCompute.Differential.Tests.BitsOfSmallIntegerRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_BitsOfSmallIntegerRepro, compile options None
#include <metal_stdlib>

using namespace metal;

kernel void SharpCompute_Differential_Tests_BitsOfSmallIntegerRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    bool b = input[0] != 0UL;
    short s = short(input[1]);
    short t = short(input[2]);
    int i = int(input[3]);
    int x = int(char(input[4]));
    int y = int(char(input[5]));
    output[0] = long(metal::popcount(b ? s : t));
    output[1] = long(metal::clz(uint(short(i))));
    output[2] = long(metal::clz(y == 0 ? 0 : x % y));
}
