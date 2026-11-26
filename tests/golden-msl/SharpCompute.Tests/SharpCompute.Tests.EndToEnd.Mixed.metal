// SharpCompute.Tests.EndToEnd.Mixed (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Mixed, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    float unusedScalar;
    float extra;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, unusedScalar) == 0, "SharpCompute: offset of '__Constants.unusedScalar' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, extra) == 4, "SharpCompute: offset of '__Constants.extra' does not match its C# layout");

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

kernel void SharpCompute_Tests_EndToEnd_Mixed(
    device float* o [[buffer(0)]],
    const device int* unusedBuffer [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    o[int(__tid.x)] = float(int(__tid.x)) * __c.extra + float(int(__grid.x));
}
