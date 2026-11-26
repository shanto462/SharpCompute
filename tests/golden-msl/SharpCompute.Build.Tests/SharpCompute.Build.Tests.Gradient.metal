// SharpCompute.Build.Tests.Gradient (SharpCompute.Build.Tests): entry point SharpCompute_Build_Tests_Gradient, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    float time;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 0, "SharpCompute: offset of '__Constants.time' does not match its C# layout");

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

kernel void SharpCompute_Build_Tests_Gradient(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 uv = float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1));
    __output.write(float4(uv.x, uv.y, metal::sin(__c.time), 1.0f), __tid.xy);
    return;
}
