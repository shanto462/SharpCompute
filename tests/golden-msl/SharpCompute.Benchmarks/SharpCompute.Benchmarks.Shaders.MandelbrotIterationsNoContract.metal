// SharpCompute.Benchmarks.Shaders.MandelbrotIterationsNoContract (SharpCompute.Benchmarks): entry point SharpCompute_Benchmarks_Shaders_MandelbrotIterationsNoContract, compile options NoFpContract
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int width;
    int height;
    int maxIterations;
    packed_float2 center;
    float step;
};
static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, maxIterations) == 8, "SharpCompute: offset of '__Constants.maxIterations' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, center) == 12, "SharpCompute: offset of '__Constants.center' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, step) == 20, "SharpCompute: offset of '__Constants.step' does not match its C# layout");

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

kernel void SharpCompute_Benchmarks_Shaders_MandelbrotIterationsNoContract(
    device uint* pixels [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int px = int(__tid.x);
    int py = int(__tid.y);
    float cx = __c.center.x + ((float(px) - (float(__c.width) * 0.5f)) * __c.step);
    float cy = __c.center.y + ((float(py) - (float(__c.height) * 0.5f)) * __c.step);
    float x = 0.0f;
    float y = 0.0f;
    float x2 = 0.0f;
    float y2 = 0.0f;
    int n = 0;
    while (n < __c.maxIterations && x2 + y2 <= 4.0f)
    {
        y = (2.0f * x * y) + cy;
        x = x2 - y2 + cx;
        x2 = x * x;
        y2 = y * y;
        n = int(uint(n) + 1u);
    }
    uint count = uint(n);
    uint shade = n < __c.maxIterations ? 255u - ((count * 8u) & 255u) : 0u;
    pixels[int((uint(py) * uint(__c.width)) + uint(px))] = (count & 65535u) | (shade << 16) | (255u << 24);
}
