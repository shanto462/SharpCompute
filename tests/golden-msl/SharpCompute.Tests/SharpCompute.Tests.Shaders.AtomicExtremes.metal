// SharpCompute.Tests.Shaders.AtomicExtremes (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_AtomicExtremes, compile options None
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

kernel void SharpCompute_Tests_Shaders_AtomicExtremes(
    const device ulong* keys [[buffer(0)]],
    device ulong* extremes [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    ulong key = keys[int(__tid.x)];
    metal::atomic_min_explicit((device metal::atomic_ulong*)&(extremes[0]), key, metal::memory_order_relaxed);
    metal::atomic_max_explicit((device metal::atomic_ulong*)&(extremes[1]), key, metal::memory_order_relaxed);
}
