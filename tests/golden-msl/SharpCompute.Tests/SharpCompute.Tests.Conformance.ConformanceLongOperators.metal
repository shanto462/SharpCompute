// SharpCompute.Tests.Conformance.ConformanceLongOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceLongOperators, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer division (MslPrelude.IntegerDivision).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }
inline int2 __ms_nonzero(int2 x) { uint2 u = uint2(x); return int2(u | (((u - 1u) & ~u) >> 31)); }
inline int3 __ms_nonzero(int3 x) { uint3 u = uint3(x); return int3(u | (((u - 1u) & ~u) >> 31)); }
inline int4 __ms_nonzero(int4 x) { uint4 u = uint4(x); return int4(u | (((u - 1u) & ~u) >> 31)); }
inline uint __ms_nonzero(uint x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint2 __ms_nonzero(uint2 x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint3 __ms_nonzero(uint3 x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint4 __ms_nonzero(uint4 x) { return x | (((x - 1u) & ~x) >> 31); }
inline long __ms_nonzero(long x) { ulong u = ulong(x); return long(u | (((u - 1UL) & ~u) >> 63)); }
inline long2 __ms_nonzero(long2 x) { ulong2 u = ulong2(x); return long2(u | (((u - 1UL) & ~u) >> 63)); }
inline long3 __ms_nonzero(long3 x) { ulong3 u = ulong3(x); return long3(u | (((u - 1UL) & ~u) >> 63)); }
inline long4 __ms_nonzero(long4 x) { ulong4 u = ulong4(x); return long4(u | (((u - 1UL) & ~u) >> 63)); }
inline ulong __ms_nonzero(ulong x) { return x | (((x - 1UL) & ~x) >> 63); }
inline ulong2 __ms_nonzero(ulong2 x) { return x | (((x - 1UL) & ~x) >> 63); }
inline ulong3 __ms_nonzero(ulong3 x) { return x | (((x - 1UL) & ~x) >> 63); }
inline ulong4 __ms_nonzero(ulong4 x) { return x | (((x - 1UL) & ~x) >> 63); }
inline char __ms_nonzero(char x) { return char(__ms_nonzero(int(x))); }
inline char2 __ms_nonzero(char2 x) { return char2(__ms_nonzero(int2(x))); }
inline char3 __ms_nonzero(char3 x) { return char3(__ms_nonzero(int3(x))); }
inline char4 __ms_nonzero(char4 x) { return char4(__ms_nonzero(int4(x))); }
inline uchar __ms_nonzero(uchar x) { return uchar(__ms_nonzero(int(x))); }
inline uchar2 __ms_nonzero(uchar2 x) { return uchar2(__ms_nonzero(int2(x))); }
inline uchar3 __ms_nonzero(uchar3 x) { return uchar3(__ms_nonzero(int3(x))); }
inline uchar4 __ms_nonzero(uchar4 x) { return uchar4(__ms_nonzero(int4(x))); }
inline short __ms_nonzero(short x) { return short(__ms_nonzero(int(x))); }
inline short2 __ms_nonzero(short2 x) { return short2(__ms_nonzero(int2(x))); }
inline short3 __ms_nonzero(short3 x) { return short3(__ms_nonzero(int3(x))); }
inline short4 __ms_nonzero(short4 x) { return short4(__ms_nonzero(int4(x))); }
inline ushort __ms_nonzero(ushort x) { return ushort(__ms_nonzero(int(x))); }
inline ushort2 __ms_nonzero(ushort2 x) { return ushort2(__ms_nonzero(int2(x))); }
inline ushort3 __ms_nonzero(ushort3 x) { return ushort3(__ms_nonzero(int3(x))); }
inline ushort4 __ms_nonzero(ushort4 x) { return ushort4(__ms_nonzero(int4(x))); }
inline uint __ms_div_near_max(uint a, uint b) { return b > a ? 0u : (a - b) / __ms_nonzero(b) + 1u; }
inline uint __ms_rem_near_max(uint a, uint b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline ulong __ms_div_near_max(ulong a, ulong b) { return b > a ? 0UL : (a - b) / __ms_nonzero(b) + 1UL; }
inline ulong __ms_rem_near_max(ulong a, ulong b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline uint2 __ms_div_near_max(uint2 a, uint2 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint2(0u), b > a); }
inline uint2 __ms_rem_near_max(uint2 a, uint2 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline uint3 __ms_div_near_max(uint3 a, uint3 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint3(0u), b > a); }
inline uint3 __ms_rem_near_max(uint3 a, uint3 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline uint4 __ms_div_near_max(uint4 a, uint4 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint4(0u), b > a); }
inline uint4 __ms_rem_near_max(uint4 a, uint4 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline long __ms_div_near_max(long a, long b) { ulong q = __ms_div_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long((a < 0L) != (b < 0L) ? 0UL - q : q); }
inline long __ms_rem_near_max(long a, long b) { ulong r = __ms_rem_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long(a < 0L ? 0UL - r : r); }

constant int Slots = 39;

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

kernel void SharpCompute_Tests_Conformance_ConformanceLongOperators(
    const device long* a [[buffer(0)]],
    const device long* b [[buffer(1)]],
    const device long* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    long x = a[i];
    long y = b[i];
    long z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = (+x);
    output[int(uint(o) + 1u)] = (long(-ulong(x)));
    output[int(uint(o) + 2u)] = (~x);
    output[int(uint(o) + 3u)] = (long(ulong(x) + ulong(y)));
    output[int(uint(o) + 4u)] = (long(ulong(x) - ulong(y)));
    output[int(uint(o) + 5u)] = (long(ulong(x) * ulong(y)));
    output[int(uint(o) + 6u)] = ((y == 0L || (x == -9223372036854775807L - 1L && y == -1L)) ? 0L : x / __ms_nonzero(y));
    output[int(uint(o) + 7u)] = ((y == 0L || (x == -9223372036854775807L - 1L && y == -1L)) ? 0L : x % __ms_nonzero(y));
    output[int(uint(o) + 8u)] = (x & y);
    output[int(uint(o) + 9u)] = (x | y);
    output[int(uint(o) + 10u)] = (x ^ y);
    output[int(uint(o) + 11u)] = (x << n);
    output[int(uint(o) + 12u)] = (x >> n);
    output[int(uint(o) + 13u)] = (long(ulong(x) >> n));
    output[int(uint(o) + 14u)] = (x == y) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (x != y) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (x < y) ? 1L : 0L;
    output[int(uint(o) + 17u)] = (x <= y) ? 1L : 0L;
    output[int(uint(o) + 18u)] = (x > y) ? 1L : 0L;
    output[int(uint(o) + 19u)] = (x >= y) ? 1L : 0L;
    long t0 = x;
    t0 = long(ulong(t0) + ulong(y));
    output[int(uint(o) + 20u)] = (t0);
    long t1 = x;
    t1 = long(ulong(t1) - ulong(y));
    output[int(uint(o) + 21u)] = (t1);
    long t2 = x;
    t2 = long(ulong(t2) * ulong(y));
    output[int(uint(o) + 22u)] = (t2);
    long t3 = x;
    t3 &= y;
    output[int(uint(o) + 23u)] = (t3);
    long t4 = x;
    t4 |= y;
    output[int(uint(o) + 24u)] = (t4);
    long t5 = x;
    t5 ^= y;
    output[int(uint(o) + 25u)] = (t5);
    long t6 = x;
    if (!(y == 0L || (x == -9223372036854775807L - 1L && y == -1L)))
    {
        t6 /= __ms_nonzero(y);
    }
    output[int(uint(o) + 26u)] = (t6);
    long t7 = x;
    if (!(y == 0L || (x == -9223372036854775807L - 1L && y == -1L)))
    {
        t7 %= __ms_nonzero(y);
    }
    output[int(uint(o) + 27u)] = (t7);
    long t8 = x;
    t8 <<= n;
    output[int(uint(o) + 28u)] = (t8);
    long t9 = x;
    t9 >>= n;
    output[int(uint(o) + 29u)] = (t9);
    long t10 = x;
    t10 = long(ulong(t10) >> n);
    output[int(uint(o) + 30u)] = (t10);
    long t11 = x;
    long u11 = long(reinterpret_cast<thread ulong&>(t11)++);
    output[int(uint(o) + 31u)] = (u11);
    output[int(uint(o) + 32u)] = (t11);
    long t12 = x;
    long u12 = long(reinterpret_cast<thread ulong&>(t12)--);
    output[int(uint(o) + 33u)] = (u12);
    output[int(uint(o) + 34u)] = (t12);
    long t13 = x;
    long u13 = long(++reinterpret_cast<thread ulong&>(t13));
    output[int(uint(o) + 35u)] = (u13);
    output[int(uint(o) + 36u)] = (t13);
    long t14 = x;
    long u14 = long(--reinterpret_cast<thread ulong&>(t14));
    output[int(uint(o) + 37u)] = (u14);
    output[int(uint(o) + 38u)] = (t14);
}
