// SharpCompute.Tests.Shaders.IndirectProbeDefault (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_IndirectProbeDefault, compile options None
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

kernel void SharpCompute_Tests_Shaders_IndirectProbeDefault(
    device int* count [[buffer(0)]],
    device int* touched [[buffer(1)]],
    device int* stats [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int i = int(__tid.x);
    metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(stats[0]), 1, metal::memory_order_relaxed);
    if (i == 0)
    {
        stats[1] = int(__grid.x);
    }
    if (i < count[0])
    {
        touched[i] = int(uint(touched[i]) + 1u);
    }
}
