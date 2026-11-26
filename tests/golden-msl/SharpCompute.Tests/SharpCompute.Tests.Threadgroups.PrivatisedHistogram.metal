// SharpCompute.Tests.Threadgroups.PrivatisedHistogram (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_PrivatisedHistogram, compile options None
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

struct __Constants
{
    int bins;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, bins) == 0, "SharpCompute: offset of '__Constants.bins' does not match its C# layout");

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

static void Count(int bin, threadgroup int* local);

static void Count(int bin, threadgroup int* local)
{
    metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(local[bin]), 1, metal::memory_order_relaxed);
}

kernel void SharpCompute_Tests_Threadgroups_PrivatisedHistogram(
    const device uint* values [[buffer(0)]],
    device int* counts [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gsize [[threads_per_threadgroup]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup int local[256];
    int first = int(__gindex);
    int stride = int(__gsize.x * __gsize.y * __gsize.z);
    for (int bin = first; bin < __c.bins; bin = int(uint(bin) + uint(stride)))
    {
        local[bin] = 0;
    }
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    ::Count(int(values[int(__tid.x)] % __ms_nonzero(uint(__c.bins))), local);
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    for (int bin = first; bin < __c.bins; bin = int(uint(bin) + uint(stride)))
    {
        int count = local[bin];
        if (count != 0)
        {
            metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[bin]), count, metal::memory_order_relaxed);
        }
    }
}
