// SharpCompute.Tests.LocalArrays.SortThenReduceGroups (SharpCompute.Tests): entry point SharpCompute_Tests_LocalArrays_SortThenReduceGroups, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Width = 4;

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

static void InsertionSort(thread int* values, int __len_values);
static int WeighByRank(thread const int* values, int __len_values);

static void InsertionSort(thread int* values, int __len_values)
{
    for (int i = 1; i < __len_values; i = int(uint(i) + 1u))
    {
        int value = values[i];
        int j = int(uint(i) - 1u);
        while (j >= 0 && values[j] > value)
        {
            values[int(uint(j) + 1u)] = values[j];
            j = int(uint(j) - 1u);
        }
        values[int(uint(j) + 1u)] = value;
    }
}

static int WeighByRank(thread const int* values, int __len_values)
{
    int weighted = 0;
    int rank = 1;
    for (int __i_value = 0; __i_value < __len_values; __i_value++)
    {
        int value = values[__i_value];
        weighted = int(uint(weighted) + uint(value) * uint(rank));
        rank = int(uint(rank) + 1u);
    }
    return weighted;
}

kernel void SharpCompute_Tests_LocalArrays_SortThenReduceGroups(
    const device int* input [[buffer(0)]],
    device int* sorted [[buffer(1)]],
    device int* totals [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gid_in [[threadgroup_position_in_grid]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    threadgroup int total[1];
    int i = int(__tid.x);
    int values[4] = {};
    for (int k = 0; k < 4; k = int(uint(k) + 1u))
    {
        values[k] = input[int((uint(i) * uint(::Width)) + uint(k))];
    }
    ::InsertionSort(values, 4);
    for (int k = 0; k < 4; k = int(uint(k) + 1u))
    {
        sorted[int((uint(i) * uint(::Width)) + uint(k))] = values[k];
    }
    if (int(__gindex) == 0)
    {
        total[0] = 0;
    }
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    int simdSum = metal::simd_sum(::WeighByRank(values, 4));
    if (metal::simd_is_first())
    {
        metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(total[0]), simdSum, metal::memory_order_relaxed);
    }
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    if (int(__gindex) == 0)
    {
        totals[int(__gid.x)] = total[0];
    }
}
