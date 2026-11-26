// SharpCompute.Tests.Threadgroups.PrivatisedHistogram (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_PrivatisedHistogram, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline uint __ms_nonzero(uint x) { return x | uint(metal::reverse_bits(x) == uint(0)); }

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
