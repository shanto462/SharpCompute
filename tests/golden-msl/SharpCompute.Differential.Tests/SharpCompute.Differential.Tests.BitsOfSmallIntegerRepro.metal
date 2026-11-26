// SharpCompute.Differential.Tests.BitsOfSmallIntegerRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_BitsOfSmallIntegerRepro, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return __builtin_constant_p(x) ? x : x | int(metal::reverse_bits(x) == int(0)); }
inline short __ms_popcount(short x) { return short(metal::popcount(metal::reverse_bits(x))); }
inline int __ms_clz(int x) { return x < 0 ? 0 : metal::clz(x); }
inline uint __ms_clz(uint x) { return int(x) < 0 ? 0u : metal::clz(x); }

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
    output[0] = long(__ms_popcount(b ? s : t));
    output[1] = long(__ms_clz(uint(short(i))));
    output[2] = long(__ms_clz(y == 0 ? 0 : x % __ms_nonzero(y)));
}
