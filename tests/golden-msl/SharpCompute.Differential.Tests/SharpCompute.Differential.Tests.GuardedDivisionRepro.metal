// SharpCompute.Differential.Tests.GuardedDivisionRepro (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_GuardedDivisionRepro, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline long __ms_nonzero(long x) { ulong u = ulong(x); return long(u | (((u - 1UL) & ~u) >> 63)); }

kernel void SharpCompute_Differential_Tests_GuardedDivisionRepro(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]])
{
    long x = long(input[0]);
    long l = long(input[1]);
    long t = l == -9223372036854775807L - 1L ? 0L : long(metal::abs(l));
    output[0] = t == 0L ? 0L : x % __ms_nonzero(t);
}
