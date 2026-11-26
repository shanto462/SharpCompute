// SharpCompute.Tests.Threadgroups.ThrowsBeforeBarrier (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_ThrowsBeforeBarrier, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int failingThread;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, failingThread) == 0, "SharpCompute: offset of '__Constants.failingThread' does not match its C# layout");

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

kernel void SharpCompute_Tests_Threadgroups_ThrowsBeforeBarrier(
    device int* output [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup int tile[16];
    int i = int(__tid.x);
    tile[int(__gtid.x)] = i == __c.failingThread ? output[int(uint(-1) - uint(i))] : i;
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    output[i] = tile[int(__gtid.x)];
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
}
