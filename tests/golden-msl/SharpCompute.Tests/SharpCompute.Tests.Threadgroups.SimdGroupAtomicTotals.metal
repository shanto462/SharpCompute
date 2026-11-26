// SharpCompute.Tests.Threadgroups.SimdGroupAtomicTotals (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_SimdGroupAtomicTotals, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 4;

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

kernel void SharpCompute_Tests_Threadgroups_SimdGroupAtomicTotals(
    const device int* input [[buffer(0)]],
    device int* totals [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gid_in [[threadgroup_position_in_grid]],
    uint __gindex [[thread_index_in_threadgroup]],
    uint __sgroups [[simdgroups_per_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    threadgroup int shared[3];
    int local = int(__gindex);
    if (local == 0)
    {
        shared[0] = 0;
        shared[1] = 0;
        shared[2] = -2147483647 - 1;
    }
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    int sum = metal::simd_sum(input[int(__tid.x)]);
    if (metal::simd_is_first())
    {
        metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(shared[0]), sum, metal::memory_order_relaxed);
        metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(shared[1]), 1, metal::memory_order_relaxed);
        metal::atomic_fetch_max_explicit((threadgroup metal::atomic_int*)&(shared[2]), sum, metal::memory_order_relaxed);
    }
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    if (local == 0)
    {
        int row = int(__gid.x * uint(::Stride));
        totals[int(uint(row) + 0u)] = shared[0];
        totals[int(uint(row) + 1u)] = shared[1];
        totals[int(uint(row) + 2u)] = shared[2];
        totals[int(uint(row) + 3u)] = int(__sgroups);
    }
}
