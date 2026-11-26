// SharpCompute.Tests.CpuExecution.PixelWithIds (SharpCompute.Tests): entry point SharpCompute_Tests_CpuExecution_PixelWithIds, compile options None
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

kernel void SharpCompute_Tests_CpuExecution_PixelWithIds(
    texture2d<float, access::write> __output [[texture(0)]],
    device int* ids [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int i = int((__tid.y * __grid.x) + __tid.x);
    ids[int((uint(i) * 4u) + 0u)] = int(__tid.x);
    ids[int((uint(i) * 4u) + 1u)] = int(__tid.y);
    ids[int((uint(i) * 4u) + 2u)] = int(__grid.x);
    ids[int((uint(i) * 4u) + 3u)] = int(__grid.y);
    float2 uv = float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1));
    __output.write(float4(uv.x, uv.y, 0.5f, 1.0f), __tid.xy);
    return;
}
