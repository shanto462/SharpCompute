// SharpCompute.Differential.Tests.GuardedDivisions (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_GuardedDivisions, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return __builtin_constant_p(x) ? x : x | int(metal::reverse_bits(x) == int(0)); }
inline uint __ms_nonzero(uint x) { return __builtin_constant_p(x) ? x : x | uint(metal::reverse_bits(x) == uint(0)); }
inline long __ms_nonzero(long x) { return __builtin_constant_p(x) ? x : x | long(metal::reverse_bits(x) == long(0)); }
inline ulong __ms_nonzero(ulong x) { return __builtin_constant_p(x) ? x : x | ulong(metal::reverse_bits(x) == ulong(0)); }
inline int __ms_nonzero_c(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }
inline uint __ms_nonzero_c(uint x) { uint u = uint(x); return uint(u | (((u - 1u) & ~u) >> 31)); }

constant int Slots = 16;

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

kernel void SharpCompute_Differential_Tests_GuardedDivisions(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    long x = long(input[int(3u * uint(t))]);
    long l = long(input[int(3u * uint(t) + 1u)]);
    int d = int(input[int(3u * uint(t) + 2u)]);
    int o = int(uint(t) * uint(::Slots));
    long a = l == -9223372036854775807L - 1L ? 0L : long(metal::abs(l));
    output[int(uint(o) + 0u)] = a;
    output[int(uint(o) + 1u)] = a == 0L ? 0L : x % __ms_nonzero(a);
    output[int(uint(o) + 2u)] = a == 0L ? 0L : x / __ms_nonzero(a);
    long b = l == 5L ? 0L : long(metal::abs(l == -9223372036854775807L - 1L ? 1L : l));
    output[int(uint(o) + 3u)] = b;
    output[int(uint(o) + 4u)] = b == 0L ? 0L : x % __ms_nonzero(b);
    output[int(uint(o) + 5u)] = b != 0L ? x / __ms_nonzero(b) : 0L;
    long r = 0L;
    if (b != 0L)
    {
        r = x % __ms_nonzero(b);
    }
    output[int(uint(o) + 6u)] = r;
    long s = -1L;
    if (a != 0L)
    {
        s = x;
        s %= __ms_nonzero(a);
    }
    output[int(uint(o) + 7u)] = s;
    int xi = int(x);
    int li = int(l >> 32);
    int c = li == -2147483647 - 1 ? 0 : int(metal::abs(li));
    output[int(uint(o) + 8u)] = long(c);
    output[int(uint(o) + 9u)] = long(c == 0 ? 0 : xi % __ms_nonzero(c));
    ulong u = l == 5L ? 0UL : ulong(long(metal::abs(l == -9223372036854775807L - 1L ? 1L : l)));
    output[int(uint(o) + 10u)] = long(u);
    output[int(uint(o) + 11u)] = long(u == 0UL ? 0UL : ulong(x) % __ms_nonzero(u));
    int e = d == 0 ? 0 : xi / __ms_nonzero(d);
    output[int(uint(o) + 12u)] = long(e);
    output[int(uint(o) + 13u)] = long(e == 0 ? 0 : 100 % __ms_nonzero_c(e));
    uint f = d == 0 ? 0u : uint(xi) / __ms_nonzero(uint(d));
    output[int(uint(o) + 14u)] = long(f);
    output[int(uint(o) + 15u)] = long(f == 0u ? 0u : 1000u / __ms_nonzero_c(f));
}
