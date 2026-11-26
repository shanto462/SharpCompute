// SharpCompute.Tests.Threadgroups.PartialGroupSum (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_PartialGroupSum, compile options None
#include <metal_stdlib>

using namespace metal;

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

static void Fold(int local, int __ms_half, int live, threadgroup int* tile);

static void Fold(int local, int __ms_half, int live, threadgroup int* tile)
{
    if (local < __ms_half && int(uint(local) + uint(__ms_half)) < live)
    {
        tile[local] = int(uint(tile[local]) + uint(tile[int(uint(local) + uint(__ms_half))]));
    }
}

kernel void SharpCompute_Tests_Threadgroups_PartialGroupSum(
    const device int* input [[buffer(0)]],
    device int* sums [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gid_in [[threadgroup_position_in_grid]],
    uint3 __gsize [[threads_per_threadgroup]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    threadgroup int tile[64];
    int local = int(__gindex);
    int live = int(__gsize.x * __gsize.y * __gsize.z);
    tile[local] = input[int(__tid.x)];
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    for (int __ms_half = 64 / 2; __ms_half > 0; __ms_half >>= 1)
    {
        ::Fold(local, __ms_half, live, tile);
        metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    }
    if (local == 0)
    {
        sums[int(__gid.x)] = tile[0];
    }
}
