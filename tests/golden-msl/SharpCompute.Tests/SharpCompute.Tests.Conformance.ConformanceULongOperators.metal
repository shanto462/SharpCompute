// SharpCompute.Tests.Conformance.ConformanceULongOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceULongOperators, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer division (MslPrelude.IntegerDivision).
inline char __ms_nonzero(char x) { return char(x | (metal::clz(x) >> 3)); }
inline char2 __ms_nonzero(char2 x) { return char2(x | (metal::clz(x) >> 3)); }
inline char3 __ms_nonzero(char3 x) { return char3(x | (metal::clz(x) >> 3)); }
inline char4 __ms_nonzero(char4 x) { return char4(x | (metal::clz(x) >> 3)); }
inline uchar __ms_nonzero(uchar x) { return uchar(x | (metal::clz(x) >> 3)); }
inline uchar2 __ms_nonzero(uchar2 x) { return uchar2(x | (metal::clz(x) >> 3)); }
inline uchar3 __ms_nonzero(uchar3 x) { return uchar3(x | (metal::clz(x) >> 3)); }
inline uchar4 __ms_nonzero(uchar4 x) { return uchar4(x | (metal::clz(x) >> 3)); }
inline short __ms_nonzero(short x) { return short(x | (metal::clz(x) >> 4)); }
inline short2 __ms_nonzero(short2 x) { return short2(x | (metal::clz(x) >> 4)); }
inline short3 __ms_nonzero(short3 x) { return short3(x | (metal::clz(x) >> 4)); }
inline short4 __ms_nonzero(short4 x) { return short4(x | (metal::clz(x) >> 4)); }
inline ushort __ms_nonzero(ushort x) { return ushort(x | (metal::clz(x) >> 4)); }
inline ushort2 __ms_nonzero(ushort2 x) { return ushort2(x | (metal::clz(x) >> 4)); }
inline ushort3 __ms_nonzero(ushort3 x) { return ushort3(x | (metal::clz(x) >> 4)); }
inline ushort4 __ms_nonzero(ushort4 x) { return ushort4(x | (metal::clz(x) >> 4)); }
inline int __ms_nonzero(int x) { return int(x | (metal::clz(x) >> 5)); }
inline int2 __ms_nonzero(int2 x) { return int2(x | (metal::clz(x) >> 5)); }
inline int3 __ms_nonzero(int3 x) { return int3(x | (metal::clz(x) >> 5)); }
inline int4 __ms_nonzero(int4 x) { return int4(x | (metal::clz(x) >> 5)); }
inline uint __ms_nonzero(uint x) { return uint(x | (metal::clz(x) >> 5)); }
inline uint2 __ms_nonzero(uint2 x) { return uint2(x | (metal::clz(x) >> 5)); }
inline uint3 __ms_nonzero(uint3 x) { return uint3(x | (metal::clz(x) >> 5)); }
inline uint4 __ms_nonzero(uint4 x) { return uint4(x | (metal::clz(x) >> 5)); }
inline long __ms_nonzero(long x) { return long(x | (metal::clz(x) >> 6)); }
inline long2 __ms_nonzero(long2 x) { return long2(x | (metal::clz(x) >> 6)); }
inline long3 __ms_nonzero(long3 x) { return long3(x | (metal::clz(x) >> 6)); }
inline long4 __ms_nonzero(long4 x) { return long4(x | (metal::clz(x) >> 6)); }
inline ulong __ms_nonzero(ulong x) { return ulong(x | (metal::clz(x) >> 6)); }
inline ulong2 __ms_nonzero(ulong2 x) { return ulong2(x | (metal::clz(x) >> 6)); }
inline ulong3 __ms_nonzero(ulong3 x) { return ulong3(x | (metal::clz(x) >> 6)); }
inline ulong4 __ms_nonzero(ulong4 x) { return ulong4(x | (metal::clz(x) >> 6)); }
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

constant int Slots = 38;

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

kernel void SharpCompute_Tests_Conformance_ConformanceULongOperators(
    const device ulong* a [[buffer(0)]],
    const device ulong* b [[buffer(1)]],
    const device ulong* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    ulong x = a[i];
    ulong y = b[i];
    ulong z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = long(+x);
    output[int(uint(o) + 1u)] = long(~x);
    output[int(uint(o) + 2u)] = long(x + y);
    output[int(uint(o) + 3u)] = long(x - y);
    output[int(uint(o) + 4u)] = long(x * y);
    output[int(uint(o) + 5u)] = long((y == 0UL) ? 0UL : x / __ms_nonzero(y));
    output[int(uint(o) + 6u)] = long((y == 0UL) ? 0UL : x % __ms_nonzero(y));
    output[int(uint(o) + 7u)] = long(x & y);
    output[int(uint(o) + 8u)] = long(x | y);
    output[int(uint(o) + 9u)] = long(x ^ y);
    output[int(uint(o) + 10u)] = long(x << n);
    output[int(uint(o) + 11u)] = long(x >> n);
    output[int(uint(o) + 12u)] = long(x >> n);
    output[int(uint(o) + 13u)] = (x == y) ? 1L : 0L;
    output[int(uint(o) + 14u)] = (x != y) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (x < y) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (x <= y) ? 1L : 0L;
    output[int(uint(o) + 17u)] = (x > y) ? 1L : 0L;
    output[int(uint(o) + 18u)] = (x >= y) ? 1L : 0L;
    ulong t0 = x;
    t0 += y;
    output[int(uint(o) + 19u)] = long(t0);
    ulong t1 = x;
    t1 -= y;
    output[int(uint(o) + 20u)] = long(t1);
    ulong t2 = x;
    t2 *= y;
    output[int(uint(o) + 21u)] = long(t2);
    ulong t3 = x;
    t3 &= y;
    output[int(uint(o) + 22u)] = long(t3);
    ulong t4 = x;
    t4 |= y;
    output[int(uint(o) + 23u)] = long(t4);
    ulong t5 = x;
    t5 ^= y;
    output[int(uint(o) + 24u)] = long(t5);
    ulong t6 = x;
    if (!(y == 0UL))
    {
        t6 /= __ms_nonzero(y);
    }
    output[int(uint(o) + 25u)] = long(t6);
    ulong t7 = x;
    if (!(y == 0UL))
    {
        t7 %= __ms_nonzero(y);
    }
    output[int(uint(o) + 26u)] = long(t7);
    ulong t8 = x;
    t8 <<= n;
    output[int(uint(o) + 27u)] = long(t8);
    ulong t9 = x;
    t9 >>= n;
    output[int(uint(o) + 28u)] = long(t9);
    ulong t10 = x;
    t10 >>= n;
    output[int(uint(o) + 29u)] = long(t10);
    ulong t11 = x;
    ulong u11 = t11++;
    output[int(uint(o) + 30u)] = long(u11);
    output[int(uint(o) + 31u)] = long(t11);
    ulong t12 = x;
    ulong u12 = t12--;
    output[int(uint(o) + 32u)] = long(u12);
    output[int(uint(o) + 33u)] = long(t12);
    ulong t13 = x;
    ulong u13 = ++t13;
    output[int(uint(o) + 34u)] = long(u13);
    output[int(uint(o) + 35u)] = long(t13);
    ulong t14 = x;
    ulong u14 = --t14;
    output[int(uint(o) + 36u)] = long(u14);
    output[int(uint(o) + 37u)] = long(t14);
}
