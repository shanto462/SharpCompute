// SharpCompute.Tests.Shaders.CountAbove (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_CountAbove, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int cutoff;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, cutoff) == 0, "SharpCompute: offset of '__Constants.cutoff' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_CountAbove(
    const device int* values [[buffer(0)]],
    device int* counter [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    if (values[int(__tid.x)] >= __c.cutoff)
    {
        metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counter[0]), 1, metal::memory_order_relaxed);
    }
}
