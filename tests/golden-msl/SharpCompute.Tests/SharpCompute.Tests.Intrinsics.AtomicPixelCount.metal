// SharpCompute.Tests.Intrinsics.AtomicPixelCount (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_AtomicPixelCount, compile options None
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

kernel void SharpCompute_Tests_Intrinsics_AtomicPixelCount(
    texture2d<float, access::write> __output [[texture(0)]],
    device int* counts [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[int(__tid.x) & 1]), 1, metal::memory_order_relaxed);
    __output.write(float4(float(__tid.x) / float(metal::max(1, int(__grid.x) - 1)), float(__tid.y) / float(metal::max(1, int(__grid.y) - 1)), 0.5f, 1.0f), __tid.xy);
    return;
}
