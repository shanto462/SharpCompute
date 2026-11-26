// SharpCompute.Differential.Tests.GuardChains (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_GuardChains, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return x | int(metal::reverse_bits(x) == int(0)); }
inline uint __ms_nonzero(uint x) { return x | uint(metal::reverse_bits(x) == uint(0)); }
inline long __ms_nonzero(long x) { return x | long(metal::reverse_bits(x) == long(0)); }
inline ulong __ms_nonzero(ulong x) { return x | ulong(metal::reverse_bits(x) == ulong(0)); }

constant int Slots = 5;

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

kernel void SharpCompute_Differential_Tests_GuardChains(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    ulong a = input[int(3u * uint(t))];
    ushort b = ushort(input[int(3u * uint(t) + 1u)]);
    long c = long(input[int(3u * uint(t) + 2u)]);
    int o = int(uint(t) * uint(::Slots));
    ulong r0 = a;
    ulong d0 = ulong(b);
    ulong q0 = d0 == 0UL ? 0UL : a % __ms_nonzero(d0);
    if (q0 != 0UL)
    {
        r0 %= __ms_nonzero(q0);
    }
    output[int(uint(o) + 0u)] = long(r0);
    uint d1 = uint(b);
    uint q1 = d1 == 0u ? 0u : 65536u / __ms_nonzero(d1);
    output[int(uint(o) + 1u)] = long(q1 == 0u ? 0u : uint(a) % __ms_nonzero(q1));
    ulong q2 = int(b) == 0 ? 0UL : ulong(ushort(a)) / __ms_nonzero(ulong(b));
    output[int(uint(o) + 2u)] = long(q2 == 0UL ? 0UL : ulong(c) % __ms_nonzero(q2));
    long d3 = long(int(b));
    long q3 = c == 0L || (c == -9223372036854775807L - 1L && c == -1L) ? 0L : c / __ms_nonzero(c);
    long q4 = d3 == 0L ? 0L : long(a) / __ms_nonzero(d3);
    output[int(uint(o) + 3u)] = q4 == 0L || (q3 == -9223372036854775807L - 1L && q4 == -1L) ? 0L : q3 / __ms_nonzero(q4);
    ushort u = ushort(a);
    if (int(b) != 0)
    {
        u /= __ms_nonzero(int(b));
    }
    output[int(uint(o) + 4u)] = long(u);
}
