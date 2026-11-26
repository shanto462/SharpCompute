// SharpCompute.Tests.Shaders.WindowedField (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_WindowedField, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    float phase;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, phase) == 0, "SharpCompute: offset of '__Constants.phase' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_WindowedField(
    device float* field [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 uv = float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1));
    int index = int((__tid.y * __grid.x) + __tid.x);
    field[index] = (metal::sin((uv.x * 12.0f) + __c.phase) * metal::cos(uv.y * 7.0f)) + (float(int(__tid.x)) * 0.001f) + (float(int(__grid.y)) * 0.5f);
}
