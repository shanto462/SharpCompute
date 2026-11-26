// SharpCompute.Differential.Tests.ConstantDividendRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_ConstantDividendRepro, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline uint __ms_nonzero(uint x) { return x | uint(metal::reverse_bits(x) == uint(0)); }
inline ulong __ms_nonzero(ulong x) { return x | ulong(metal::reverse_bits(x) == ulong(0)); }
inline uint __ms_div_near_max(uint a, uint b) { return b > a ? 0u : (a - b) / __ms_nonzero(b) + 1u; }
inline uint __ms_rem_near_max(uint a, uint b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline ulong __ms_rem_near_max(ulong a, ulong b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline long __ms_rem_near_max(long a, long b) { ulong r = __ms_rem_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long(a < 0L ? 0UL - r : r); }

kernel void SharpCompute_Differential_Tests_ConstantDividendRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    uint x = metal::max(uint(input[0]), 1u);
    long y = long(x);
    output[0] = long(__ms_div_near_max(4294967295u, x));
    output[1] = long(__ms_rem_near_max(4294967295u, x));
    output[2] = __ms_rem_near_max(4294967295L, y);
}
