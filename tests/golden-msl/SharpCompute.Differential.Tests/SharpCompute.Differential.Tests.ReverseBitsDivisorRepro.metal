// SharpCompute.Differential.Tests.ReverseBitsDivisorRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_ReverseBitsDivisorRepro, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline uint __ms_nonzero_c(uint x) { uint u = uint(x); return uint(u | (((u - 1u) & ~u) >> 31)); }

kernel void SharpCompute_Differential_Tests_ReverseBitsDivisorRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    output[0] = long(2863311530u % __ms_nonzero_c(metal::reverse_bits(1073741824u)));
    output[1] = long(1431655765u / __ms_nonzero_c(metal::reverse_bits(536870912u)) + uint(input[0]) * 0u);
}
