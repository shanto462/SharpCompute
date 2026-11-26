// SharpCompute.Tests.LocalArrays.Initializers (SharpCompute.Tests): entry point SharpCompute_Tests_LocalArrays_Initializers, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }

constant int Slots = 7;

struct __GridWindow
{
    packed_uint3 offset;
    packed_uint3 size;
    packed_uint3 groupOffset;
};
static_assert(sizeof(__GridWindow) == 36, "SharpCompute: size of '__GridWindow' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, offset) == 0, "SharpCompute: offset of '__GridWindow.offset' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, size) == 12, "SharpCompute: offset of '__GridWindow.size' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, groupOffset) == 24, "SharpCompute: offset of '__GridWindow.groupOffset' does not match its C# layout");

constant bool __windowed [[function_constant(0)]];

kernel void SharpCompute_Tests_LocalArrays_Initializers(
    const device int* input [[buffer(0)]],
    device float* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    const float weights[4] = { 0.5f, 0.25f, 0.125f, 0.0625f };
    int primes[5] = { 2, 3, 5, 7, 11 };
    int built[3] = { i, int(uint(i) * 2u), input[i] };
    int bins[4] = {};
    float weighted = 0.0f;
    for (int k = 0; k < 4; k = int(uint(k) + 1u))
    {
        weighted += weights[k] * float(int(uint(i) + uint(k)));
    }
    int primeSum = 0;
    for (int __i_p = 0; __i_p < 5; __i_p++)
    {
        int p = primes[__i_p];
        primeSum = int(uint(primeSum) + uint(p) * uint(i % __ms_nonzero(p)));
    }
    int builtSum = 0;
    for (int __i_b = 0; __i_b < 3; __i_b++)
    {
        int b = built[__i_b];
        builtSum = int(uint(builtSum) + uint(b));
    }
    for (int k = 0; k < 16; k = int(uint(k) + 1u))
    {
        bins[(input[i] >> k) & 3] = int(uint(bins[(input[i] >> k) & 3]) + 1u);
    }
    int o = int(uint(i) * uint(::Slots));
    output[o] = weighted;
    output[int(uint(o) + 1u)] = float(primeSum);
    output[int(uint(o) + 2u)] = float(builtSum);
    for (int k = 0; k < 4; k = int(uint(k) + 1u))
    {
        output[int(uint(o) + 3u + uint(k))] = float(bins[k]);
    }
}
