// SharpCompute.Differential.Tests.NarrowedBitCounts (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_NarrowedBitCounts, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }
inline char __ms_popcount(char x) { return char(metal::popcount(uint(uchar(x)))); }
inline short __ms_popcount(short x) { return short(metal::popcount(uint(ushort(x)))); }
inline ushort __ms_popcount(ushort x) { return ushort(metal::popcount(uint(ushort(x)))); }
inline int __ms_popcount(int x) { return metal::popcount(metal::reverse_bits(x)); }
inline uint __ms_popcount(uint x) { return metal::popcount(metal::reverse_bits(x)); }
inline long __ms_popcount(long x) { return metal::popcount(metal::reverse_bits(x)); }
inline ulong __ms_popcount(ulong x) { return metal::popcount(metal::reverse_bits(x)); }
inline int3 __ms_popcount(int3 x) { return metal::popcount(metal::reverse_bits(x)); }
inline uint3 __ms_popcount(uint3 x) { return metal::popcount(metal::reverse_bits(x)); }
inline int __ms_clz(int x) { return x < 0 ? 0 : metal::clz(x); }
inline uint __ms_clz(uint x) { return int(x) < 0 ? 0u : metal::clz(x); }
inline long __ms_clz(long x) { return x < 0L ? 0L : metal::clz(x); }
inline int3 __ms_clz(int3 x) { return metal::select(metal::clz(x), int3(0), x < 0); }
inline uint3 __ms_clz(uint3 x) { return metal::select(metal::clz(x), uint3(0u), int3(x) < 0); }

constant int Slots = 24;

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

kernel void SharpCompute_Differential_Tests_NarrowedBitCounts(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    ulong a = input[int(3u * uint(t))];
    ulong b = input[int(3u * uint(t) + 1u)];
    bool c = input[int(3u * uint(t) + 2u)] != 0UL;
    int o = int(uint(t) * uint(::Slots));
    short sa = short(a);
    short sb = short(b);
    int i = int(a);
    int y = int(char(b));
    int z = int(char(a));
    output[int(uint(o) + 0u)] = long(__ms_popcount(c ? sa : sb));
    output[int(uint(o) + 1u)] = long(__ms_popcount(c ? ushort(a) : ushort(b)));
    output[int(uint(o) + 2u)] = long(__ms_popcount(c ? char(a) : char(b)));
    output[int(uint(o) + 3u)] = long(metal::clz(c ? sa : sb));
    output[int(uint(o) + 4u)] = long(__ms_popcount(int(sa)));
    output[int(uint(o) + 5u)] = long(__ms_clz(int(sa)));
    output[int(uint(o) + 6u)] = long(__ms_popcount(uint(short(i))));
    output[int(uint(o) + 7u)] = long(__ms_clz(uint(short(i))));
    output[int(uint(o) + 8u)] = long(__ms_popcount((i << 16) >> 16));
    output[int(uint(o) + 9u)] = long(__ms_clz((i << 16) >> 16));
    output[int(uint(o) + 10u)] = long(__ms_popcount(c ? int(sa) : int(sb)));
    output[int(uint(o) + 11u)] = long(__ms_clz(c ? int(sa) : int(sb)));
    output[int(uint(o) + 12u)] = long(__ms_popcount(z % __ms_nonzero(y == 0 ? 1 : y)));
    output[int(uint(o) + 13u)] = long(__ms_clz(y == 0 ? 0 : z % __ms_nonzero(y)));
    output[int(uint(o) + 14u)] = long(int(__ms_popcount(uint(sa))));
    output[int(uint(o) + 15u)] = long(int(__ms_clz(uint(sa))));
    output[int(uint(o) + 16u)] = __ms_popcount(long(sa));
    output[int(uint(o) + 17u)] = long(__ms_popcount(ulong(long(sb))));
    output[int(uint(o) + 18u)] = __ms_clz(long(sa));
    output[int(uint(o) + 19u)] = long(int(__ms_popcount(ulong(long(sa)))));
    int3 v = int3(int(sa), int(sb), int(short(a ^ b)));
    output[int(uint(o) + 20u)] = long(__ms_popcount(v).x);
    output[int(uint(o) + 21u)] = long(__ms_clz(v).y);
    uint3 w = uint3(uint(sa), uint(sb), uint(short(a ^ b)));
    output[int(uint(o) + 22u)] = long(__ms_popcount(w).z);
    output[int(uint(o) + 23u)] = long(__ms_clz(w).x);
}
