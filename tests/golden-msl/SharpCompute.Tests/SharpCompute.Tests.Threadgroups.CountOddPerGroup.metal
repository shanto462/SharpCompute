// SharpCompute.Tests.Threadgroups.CountOddPerGroup (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_CountOddPerGroup, compile options None
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

kernel void SharpCompute_Tests_Threadgroups_CountOddPerGroup(
    const device int* input [[buffer(0)]],
    device int* total [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup int tile[64];
    int i = int(__tid.x);
    tile[int(__gtid.x)] = i < __c.count ? input[i] & 1 : 0;
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    if (int(__gtid.x) == 0)
    {
        int sum = 0;
        for (int j = 0; j < 64; j = int(uint(j) + 1u))
        {
            sum = int(uint(sum) + uint(tile[j]));
        }
        metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(total[0]), sum, metal::memory_order_relaxed);
    }
}
