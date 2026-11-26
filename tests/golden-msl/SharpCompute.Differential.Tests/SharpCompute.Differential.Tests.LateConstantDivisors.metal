// SharpCompute.Differential.Tests.LateConstantDivisors (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_LateConstantDivisors, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline uint __ms_nonzero(uint x) { return __builtin_constant_p(x) ? x : x | uint(metal::reverse_bits(x) == uint(0)); }
inline int __ms_nonzero_c(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }
inline uint __ms_nonzero_c(uint x) { uint u = uint(x); return uint(u | (((u - 1u) & ~u) >> 31)); }
inline long __ms_nonzero_c(long x) { ulong u = ulong(x); return long(u | (((u - 1UL) & ~u) >> 63)); }
inline ulong __ms_nonzero_c(ulong x) { ulong u = ulong(x); return ulong(u | (((u - 1UL) & ~u) >> 63)); }
inline uint __ms_div_near_max(uint a, uint b) { return b > a ? 0u : (a - b) / __ms_nonzero_c(b) + 1u; }
inline uint __ms_rem_near_max(uint a, uint b) { return b > a ? a : (a - b) % __ms_nonzero_c(b); }

constant int Slots = 28;

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

kernel void SharpCompute_Differential_Tests_LateConstantDivisors(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    uint x = uint(input[t]);
    int o = int(uint(t) * uint(::Slots));
    uint acc = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        acc = (acc * 31u) + (1000000007u % __ms_nonzero_c(k));
    }
    output[int(uint(o) + 0u)] = long(acc);
    uint f1 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f1 = (f1 * 31u) + (123456789u % __ms_nonzero_c(k)) + (123456789u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 1u)] = long(f1);
    uint f2 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f2 = (f2 * 31u) + (1000000007u % __ms_nonzero_c(k)) + (1000000007u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 2u)] = long(f2);
    uint f3 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f3 = (f3 * 31u) + (2654435769u % __ms_nonzero_c(k)) + (2654435769u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 3u)] = long(f3);
    uint f4 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f4 = (f4 * 31u) + (2863311530u % __ms_nonzero_c(k)) + (2863311530u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 4u)] = long(f4);
    uint f5 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f5 = (f5 * 31u) + (3000000000u % __ms_nonzero_c(k)) + (3000000000u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 5u)] = long(f5);
    uint f6 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f6 = (f6 * 31u) + (4000000000u % __ms_nonzero_c(k)) + (4000000000u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 6u)] = long(f6);
    uint f7 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f7 = (f7 * 31u) + (4294901760u % __ms_nonzero_c(k)) + (4294901760u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 7u)] = long(f7);
    uint f8 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f8 = (f8 * 31u) + (16777216u % __ms_nonzero_c(k)) + (16777216u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 8u)] = long(f8);
    uint f9 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f9 = (f9 * 31u) + (16777217u % __ms_nonzero_c(k)) + (16777217u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 9u)] = long(f9);
    uint f10 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f10 = (f10 * 31u) + (2147483647u % __ms_nonzero_c(k)) + (2147483647u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 10u)] = long(f10);
    uint f11 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f11 = (f11 * 31u) + (2147483648u % __ms_nonzero_c(k)) + (2147483648u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 11u)] = long(f11);
    uint f12 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        f12 = (f12 * 31u) + (7u % __ms_nonzero_c(k)) + (7u / __ms_nonzero_c(k));
    }
    output[int(uint(o) + 12u)] = long(f12);
    int sacc = 0;
    for (int k = 1; k <= 8; k = int(uint(k) + 1u))
    {
        sacc = int((uint(sacc) * 31u) + uint(-1431655766 % __ms_nonzero_c(k)) + uint(-1431655766 / __ms_nonzero_c(k)) + uint(100000007 % __ms_nonzero_c(k)) + uint(-100000007 / __ms_nonzero_c(k)));
    }
    output[int(uint(o) + 13u)] = long(sacc);
    output[int(uint(o) + 14u)] = long(1000000007u % __ms_nonzero_c(metal::clamp(x, 2u, 2u)));
    output[int(uint(o) + 15u)] = long(2863311530u / __ms_nonzero_c(metal::max(x & 0u, 4u)));
    output[int(uint(o) + 16u)] = long(2863311530u % __ms_nonzero_c(metal::ctz(4u)));
    output[int(uint(o) + 17u)] = long(123456789u / __ms_nonzero_c(metal::clamp(x, 6u, 6u)));
    output[int(uint(o) + 18u)] = long(3000000000u % __ms_nonzero_c(metal::max(x & 0u, 3u)));
    output[int(uint(o) + 19u)] = long(-1431655766 % __ms_nonzero_c(metal::clamp(int(x), 2, 2)));
    long lacc = 0L;
    for (long k = 1L; k <= 8L; k = long(ulong(k) + 1UL))
    {
        lacc = long((ulong(lacc) * 31UL) + ulong(-1000000000000007L % __ms_nonzero_c(k)) + (4000000000UL / __ms_nonzero_c(ulong(k))));
    }
    output[int(uint(o) + 20u)] = lacc;
    uint vacc = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        vacc = (vacc * 31u) + ((x | 2147483649u) % __ms_nonzero(k));
    }
    output[int(uint(o) + 21u)] = long(vacc);
    uint d = 1u;
    for (int k = 0; k < 3; k = int(uint(k) + 1u))
    {
        d *= 2u;
    }
    output[int(uint(o) + 22u)] = long(2654435769u % __ms_nonzero_c(d));
    uint e = 6u;
    output[int(uint(o) + 23u)] = long(4000000000u / __ms_nonzero_c(e));
    output[int(uint(o) + 24u)] = long(4294901760u % __ms_nonzero_c(e + 4u));
    output[int(uint(o) + 25u)] = long(2863311530u / __ms_nonzero_c(x % 1u + 2u));
    uint m0 = 0u;
    uint m1 = 0u;
    for (uint k = 1u; k <= 8u; k++)
    {
        m0 = (m0 * 31u) + (__ms_rem_near_max(4294967295u, k));
        m1 = (m1 * 31u) + (__ms_div_near_max(4294967184u, k));
    }
    output[int(uint(o) + 26u)] = long(m0);
    output[int(uint(o) + 27u)] = long(m1);
}
