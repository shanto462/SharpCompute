// SharpCompute.Benchmarks.Shaders.BlurHorizontal (SharpCompute.Benchmarks): entry point SharpCompute_Benchmarks_Shaders_BlurHorizontal, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int width;
    int radius;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radius) == 4, "SharpCompute: offset of '__Constants.radius' does not match its C# layout");

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

kernel void SharpCompute_Benchmarks_Shaders_BlurHorizontal(
    const device float* source [[buffer(0)]],
    device float* destination [[buffer(1)]],
    const device float* weights [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int row = int(__tid.y * uint(__c.width));
    float sum = 0.0f;
    for (int k = int(-uint(__c.radius)); k <= __c.radius; k = int(uint(k) + 1u))
    {
        int sx = metal::clamp(int(uint(x) + uint(k)), 0, int(uint(__c.width) - 1u));
        sum += weights[int(uint(k) + uint(__c.radius))] * source[int(uint(row) + uint(sx))];
    }
    destination[int(uint(row) + uint(x))] = sum;
}
