// SharpCompute.Tests.Shaders.WideIntegerOps (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_WideIntegerOps, compile options None
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

constant int Stride = 64;
constant long Multiplier = 7109453100751455733L;
constant ulong Mixer = 13787848793156543929UL;
constant long Lowest = -9223372036854775807L - 1L;
constant ulong HighBit = 1UL << 63;

struct __Constants
{
    uint __length_longs;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __length_longs) == 0, "SharpCompute: offset of '__Constants.__length_longs' does not match its C# layout");

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

static long Fold(long x, ulong y);
static void Swap(thread long& x, thread long& y);

static long Fold(long x, ulong y)
{
    ulong z = ulong(x) + y;
    z = (z ^ (z >> 30)) * ::Mixer;
    z = (z ^ (z >> 27)) * 10723151780598845931UL;
    return long(z ^ (z >> 31));
}

static void Swap(thread long& x, thread long& y)
{
    long t = x;
    x = y;
    y = t;
}

kernel void SharpCompute_Tests_Shaders_WideIntegerOps(
    const device long* longs [[buffer(0)]],
    const device ulong* ulongs [[buffer(1)]],
    device long* output [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int j = (int((uint(i) * 5u) + 1u)) % __ms_nonzero(int(__c.__length_longs));
    int o = int(uint(i) * uint(::Stride));
    long a = longs[i];
    long b = longs[j];
    ulong u = ulongs[i];
    ulong v = ulongs[j];
    int count = int(uint(i % 140) - 5u);
    output[int(uint(o) + 0u)] = long(ulong(a) + ulong(b));
    output[int(uint(o) + 1u)] = long(ulong(a) - ulong(b));
    output[int(uint(o) + 2u)] = long(ulong(a) * ulong(b));
    output[int(uint(o) + 3u)] = b != 0L && !(a == -9223372036854775807L - 1L && b == -1L) ? a / __ms_nonzero(b) : 0L;
    output[int(uint(o) + 4u)] = b != 0L && !(a == -9223372036854775807L - 1L && b == -1L) ? a % __ms_nonzero(b) : 0L;
    output[int(uint(o) + 5u)] = long(u + v);
    output[int(uint(o) + 6u)] = long(u - v);
    output[int(uint(o) + 7u)] = long(u * v);
    output[int(uint(o) + 8u)] = v != 0UL ? long(u / __ms_nonzero(v)) : 0L;
    output[int(uint(o) + 9u)] = v != 0UL ? long(u % __ms_nonzero(v)) : 0L;
    output[int(uint(o) + 10u)] = a << count;
    output[int(uint(o) + 11u)] = a >> count;
    output[int(uint(o) + 12u)] = long(ulong(a) >> count);
    output[int(uint(o) + 13u)] = long(u << count);
    output[int(uint(o) + 14u)] = long(u >> count);
    output[int(uint(o) + 15u)] = a & b;
    output[int(uint(o) + 16u)] = a | b;
    output[int(uint(o) + 17u)] = a ^ b;
    output[int(uint(o) + 18u)] = ~a;
    output[int(uint(o) + 19u)] = long(-ulong(a));
    output[int(uint(o) + 20u)] = long((u & v) | (~u ^ v));
    output[int(uint(o) + 21u)] = long((a < b ? 1 : 0) | (a <= b ? 2 : 0) | (a > b ? 4 : 0) | (a >= b ? 8 : 0) | (a == b ? 16 : 0) | (a != b ? 32 : 0) | (u < v ? 64 : 0) | (u >= v ? 128 : 0) | (u == v ? 256 : 0));
    int n = int(a);
    uint m = uint(u);
    output[int(uint(o) + 22u)] = long(ulong(n) + ulong(a));
    output[int(uint(o) + 23u)] = long(ulong(m) - ulong(n));
    output[int(uint(o) + 24u)] = long(-ulong(m));
    output[int(uint(o) + 25u)] = long(ulong(n) * ulong(n));
    output[int(uint(o) + 26u)] = long(ulong(m) * ulong(m));
    output[int(uint(o) + 27u)] = long(long(n) < long(m) ? 1 : 0);
    output[int(uint(o) + 28u)] = long(int(a));
    output[int(uint(o) + 29u)] = long(uint(a));
    output[int(uint(o) + 30u)] = long(short(a));
    output[int(uint(o) + 31u)] = long(ushort(a));
    output[int(uint(o) + 32u)] = long(uchar(a));
    output[int(uint(o) + 33u)] = long(char(a));
    output[int(uint(o) + 34u)] = long(u);
    output[int(uint(o) + 35u)] = long(int(u));
    output[int(uint(o) + 36u)] = long(uint(a));
    output[int(uint(o) + 37u)] = long(ulong(char(a)));
    output[int(uint(o) + 38u)] = long(ulong(short(a)));
    long ca = a;
    ca = long(ulong(ca) + ulong(b));
    ca ^= ca >> 33;
    ca = long(ulong(ca) * ulong(::Multiplier));
    ca = long(ulong(ca) - ulong(n));
    ca <<= 3;
    ca >>= 1;
    ca |= 1L;
    ca &= ~16L;
    ca = long(ulong(ca) + 1UL);
    output[int(uint(o) + 39u)] = ca;
    ulong cu = u;
    cu += v;
    cu ^= cu >> 29;
    cu *= ::Mixer;
    cu >>= 2;
    cu <<= 1;
    cu %= 1000003UL;
    cu--;
    output[int(uint(o) + 40u)] = long(cu);
    long picked = a > b ? a : ::Lowest;
    output[int(uint(o) + 41u)] = picked;
    output[int(uint(o) + 42u)] = long(18446744073709551615UL - u);
    output[int(uint(o) + 43u)] = long(ulong(9223372036854775807L) - ulong(a & 9223372036854775807L));
    output[int(uint(o) + 44u)] = long(u | ::HighBit);
    output[int(uint(o) + 45u)] = ::Fold(a, u);
    ::Swap(a, b);
    output[int(uint(o) + 46u)] = long(ulong(a) - 1UL);
    output[int(uint(o) + 47u)] = long(ulong(b) + 1UL);
    long bucket;
    switch (u & 3UL)
    {
        case 0UL:
        {
            bucket = 1L << 40;
            break;
        }
        case 1UL:
        {
            bucket = -1L;
            break;
        }
        default:
        {
            bucket = 9223372036854775807L;
            break;
        }
    }
    output[int(uint(o) + 48u)] = bucket;
    output[int(uint(o) + 49u)] = long(ulong(long(0)) + ulong(0));
    output[int(uint(o) + 50u)] = long(int(uint(n) * uint(n)));
    output[int(uint(o) + 51u)] = long(uint(n) * m);
}
