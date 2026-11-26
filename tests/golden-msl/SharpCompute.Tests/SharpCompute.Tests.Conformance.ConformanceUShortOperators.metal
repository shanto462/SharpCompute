// SharpCompute.Tests.Conformance.ConformanceUShortOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceUShortOperators, compile options None
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

constant int Slots = 45;

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

kernel void SharpCompute_Tests_Conformance_ConformanceUShortOperators(
    const device ushort* a [[buffer(0)]],
    const device ushort* b [[buffer(1)]],
    const device ushort* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    ushort x = a[i];
    ushort y = b[i];
    ushort z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = long(+int(x));
    output[int(uint(o) + 1u)] = long(int(-uint(x)));
    output[int(uint(o) + 2u)] = long(~int(x));
    output[int(uint(o) + 3u)] = long(int(uint(x) + uint(y)));
    output[int(uint(o) + 4u)] = long(int(uint(x) - uint(y)));
    output[int(uint(o) + 5u)] = long(int(uint(x) * uint(y)));
    output[int(uint(o) + 6u)] = long((int(y) == 0) ? 0 : int(x) / __ms_nonzero(int(y)));
    output[int(uint(o) + 7u)] = long((int(y) == 0) ? 0 : int(x) % __ms_nonzero(int(y)));
    output[int(uint(o) + 8u)] = long(int(x) & int(y));
    output[int(uint(o) + 9u)] = long(int(x) | int(y));
    output[int(uint(o) + 10u)] = long(int(x) ^ int(y));
    output[int(uint(o) + 11u)] = long(int(x) << n);
    output[int(uint(o) + 12u)] = long(int(x) >> n);
    output[int(uint(o) + 13u)] = long(int(uint(int(x)) >> n));
    output[int(uint(o) + 14u)] = (int(x) == int(y)) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (int(x) != int(y)) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (int(x) < int(y)) ? 1L : 0L;
    output[int(uint(o) + 17u)] = (int(x) <= int(y)) ? 1L : 0L;
    output[int(uint(o) + 18u)] = (int(x) > int(y)) ? 1L : 0L;
    output[int(uint(o) + 19u)] = (int(x) >= int(y)) ? 1L : 0L;
    output[int(uint(o) + 20u)] = long(ushort(int(uint(x) + uint(y))));
    output[int(uint(o) + 21u)] = long(ushort(int(uint(x) - uint(y))));
    output[int(uint(o) + 22u)] = long(ushort(int(uint(x) * uint(y))));
    output[int(uint(o) + 23u)] = long(ushort(int(x) << n));
    output[int(uint(o) + 24u)] = long(ushort(int(-uint(x))));
    output[int(uint(o) + 25u)] = long(ushort(~int(x)));
    ushort t0 = x;
    t0 += int(y);
    output[int(uint(o) + 26u)] = long(t0);
    ushort t1 = x;
    t1 -= int(y);
    output[int(uint(o) + 27u)] = long(t1);
    ushort t2 = x;
    t2 *= uint(y);
    output[int(uint(o) + 28u)] = long(t2);
    ushort t3 = x;
    t3 &= int(y);
    output[int(uint(o) + 29u)] = long(t3);
    ushort t4 = x;
    t4 |= int(y);
    output[int(uint(o) + 30u)] = long(t4);
    ushort t5 = x;
    t5 ^= int(y);
    output[int(uint(o) + 31u)] = long(t5);
    ushort t6 = x;
    if (!(int(y) == 0))
    {
        t6 /= __ms_nonzero(int(y));
    }
    output[int(uint(o) + 32u)] = long(t6);
    ushort t7 = x;
    if (!(int(y) == 0))
    {
        t7 %= __ms_nonzero(int(y));
    }
    output[int(uint(o) + 33u)] = long(t7);
    ushort t8 = x;
    t8 <<= n;
    output[int(uint(o) + 34u)] = long(t8);
    ushort t9 = x;
    t9 >>= n;
    output[int(uint(o) + 35u)] = long(t9);
    ushort t10 = x;
    t10 >>= n;
    output[int(uint(o) + 36u)] = long(t10);
    ushort t11 = x;
    ushort u11 = t11++;
    output[int(uint(o) + 37u)] = long(u11);
    output[int(uint(o) + 38u)] = long(t11);
    ushort t12 = x;
    ushort u12 = t12--;
    output[int(uint(o) + 39u)] = long(u12);
    output[int(uint(o) + 40u)] = long(t12);
    ushort t13 = x;
    ushort u13 = ++t13;
    output[int(uint(o) + 41u)] = long(u13);
    output[int(uint(o) + 42u)] = long(t13);
    ushort t14 = x;
    ushort u14 = --t14;
    output[int(uint(o) + 43u)] = long(u14);
    output[int(uint(o) + 44u)] = long(t14);
}
