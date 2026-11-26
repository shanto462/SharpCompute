// SharpCompute.Tests.Threadgroups.Rotate (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_Rotate, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Threads = 32;

struct __Constants
{
    int rounds;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, rounds) == 0, "SharpCompute: offset of '__Constants.rounds' does not match its C# layout");

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

kernel void SharpCompute_Tests_Threadgroups_Rotate(
    device int* values [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup int front[32];
    threadgroup int back[32];
    int local = int(__gtid.x);
    front[local] = values[int(__tid.x)];
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    for (int round = 0; round < __c.rounds; round = int(uint(round) + 1u))
    {
        back[local] = int(uint(front[(int(uint(local) + 1u)) % ::Threads]) + 1u);
        metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
        front[local] = back[local];
        metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    }
    values[int(__tid.x)] = front[local];
}
