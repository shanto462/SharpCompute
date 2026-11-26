// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.ScanBlocks (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_ScanBlocks, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int count;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 0, "SharpCompute: offset of '__Constants.count' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_ScanBlocks(
    device int* input [[buffer(0)]],
    device int* output [[buffer(1)]],
    device int* totals [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]],
    uint3 __gid_in [[threadgroup_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    threadgroup int tile[256];
    int i = int(__tid.x);
    int local = int(__gtid.x);
    tile[local] = i < __c.count ? input[i] : 0;
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    for (int stride = 1; stride < 256; stride <<= 1)
    {
        if (((int(uint(local) + 1u)) & (int(uint(stride << 1) - 1u))) == 0)
        {
            tile[local] = int(uint(tile[local]) + uint(tile[int(uint(local) - uint(stride))]));
        }
        metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    }
    if (local == int(uint(256) - 1u))
    {
        totals[int(__gid.x)] = tile[local];
        tile[local] = 0;
    }
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    for (int stride = 256 / 2; stride > 0; stride >>= 1)
    {
        if (((int(uint(local) + 1u)) & (int(uint(stride << 1) - 1u))) == 0)
        {
            int left = tile[int(uint(local) - uint(stride))];
            tile[int(uint(local) - uint(stride))] = tile[local];
            tile[local] = int(uint(tile[local]) + uint(left));
        }
        metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    }
    if (i < __c.count)
    {
        output[i] = tile[local];
    }
}
