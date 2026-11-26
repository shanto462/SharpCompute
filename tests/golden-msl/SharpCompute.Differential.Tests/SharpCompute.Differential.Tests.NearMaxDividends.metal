// SharpCompute.Differential.Tests.NearMaxDividends (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_NearMaxDividends, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline uint __ms_nonzero(uint x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint3 __ms_nonzero(uint3 x) { return x | (((x - 1u) & ~x) >> 31); }
inline long __ms_nonzero(long x) { ulong u = ulong(x); return long(u | (((u - 1UL) & ~u) >> 63)); }
inline ulong __ms_nonzero(ulong x) { return x | (((x - 1UL) & ~x) >> 63); }
inline uint __ms_div_near_max(uint a, uint b) { return b > a ? 0u : (a - b) / __ms_nonzero(b) + 1u; }
inline uint __ms_rem_near_max(uint a, uint b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline ulong __ms_div_near_max(ulong a, ulong b) { return b > a ? 0UL : (a - b) / __ms_nonzero(b) + 1UL; }
inline ulong __ms_rem_near_max(ulong a, ulong b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline uint3 __ms_div_near_max(uint3 a, uint3 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint3(0u), b > a); }
inline uint3 __ms_rem_near_max(uint3 a, uint3 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline long __ms_div_near_max(long a, long b) { ulong q = __ms_div_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long((a < 0L) != (b < 0L) ? 0UL - q : q); }
inline long __ms_rem_near_max(long a, long b) { ulong r = __ms_rem_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long(a < 0L ? 0UL - r : r); }

constant int Slots = 24;
constant uint Constant = 4294967280u;
constant uint ReadOnlyMax = 4294967295u;

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

kernel void SharpCompute_Differential_Tests_NearMaxDividends(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    uint x = uint(input[t]);
    long y = long(x);
    uint3 v = uint3(x, x | 1u, (x >> 1) | 1u);
    int o = int(uint(t) * uint(::Slots));
    output[int(uint(o) + 0u)] = long(__ms_div_near_max(4294967295u, x));
    output[int(uint(o) + 1u)] = long(__ms_rem_near_max(4294967295u, x));
    output[int(uint(o) + 2u)] = long(__ms_div_near_max(4294967168u, x));
    output[int(uint(o) + 3u)] = long(__ms_rem_near_max(4294967168u, x));
    output[int(uint(o) + 4u)] = long(__ms_div_near_max(4294967294u, metal::max(x, 1u)));
    output[int(uint(o) + 5u)] = long(__ms_rem_near_max(4294967295u - 5u, metal::max(x, 1u)));
    output[int(uint(o) + 6u)] = long(__ms_div_near_max(::Constant, x));
    output[int(uint(o) + 7u)] = long(__ms_rem_near_max(::ReadOnlyMax, x));
    output[int(uint(o) + 8u)] = __ms_rem_near_max(4294967295L, y);
    output[int(uint(o) + 9u)] = __ms_div_near_max(4294967295L, y);
    output[int(uint(o) + 10u)] = __ms_div_near_max(-4294967295L, y);
    output[int(uint(o) + 11u)] = __ms_rem_near_max(-4294967200L, y);
    output[int(uint(o) + 12u)] = __ms_div_near_max(4294967295L, long(-ulong(y)));
    output[int(uint(o) + 13u)] = __ms_rem_near_max(4294967295L, long(-ulong(y)));
    output[int(uint(o) + 14u)] = long(__ms_div_near_max(4294967295UL, ulong(y)));
    output[int(uint(o) + 15u)] = long(__ms_rem_near_max(4294967200UL, ulong(y)));
    uint3 q = __ms_div_near_max(uint3(4294967295u, 4294967280u, 7u), v);
    uint3 r = __ms_rem_near_max(uint3(4294967294u), v);
    output[int(uint(o) + 16u)] = long(q.x);
    output[int(uint(o) + 17u)] = long(q.y);
    output[int(uint(o) + 18u)] = long(q.z);
    output[int(uint(o) + 19u)] = long(r.x);
    output[int(uint(o) + 20u)] = long(r.y);
    output[int(uint(o) + 21u)] = long(r.z);
    output[int(uint(o) + 22u)] = long(2147483647u / __ms_nonzero(x));
    output[int(uint(o) + 23u)] = 4294967296L / __ms_nonzero(y);
}
