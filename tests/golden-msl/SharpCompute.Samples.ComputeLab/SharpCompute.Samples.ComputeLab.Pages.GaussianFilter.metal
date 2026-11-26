// SharpCompute.Samples.ComputeLab.Pages.GaussianFilter (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_GaussianFilter, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int size;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 0, "SharpCompute: offset of '__Constants.size' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_GaussianFilter(
    device uint* source [[buffer(0)]],
    device uint* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    const int weights[25] = { 1, 4, 6, 4, 1, 4, 16, 24, 16, 4, 6, 24, 36, 24, 6, 4, 16, 24, 16, 4, 1, 4, 6, 4, 1 };
    int r = 0;
    int g = 0;
    int b = 0;
    for (int dy = -2; dy <= 2; dy = int(uint(dy) + 1u))
    {
        for (int dx = -2; dx <= 2; dx = int(uint(dx) + 1u))
        {
            int sx = metal::clamp(int(uint(x) + uint(dx)), 0, int(uint(__c.size) - 1u));
            int sy = metal::clamp(int(uint(y) + uint(dy)), 0, int(uint(__c.size) - 1u));
            uint pixel = source[int((uint(sy) * uint(__c.size)) + uint(sx))];
            int w = weights[int(((uint(dy) + 2u) * 5u) + uint(dx) + 2u)];
            r = int(uint(r) + uint(w) * (pixel & 255u));
            g = int(uint(g) + uint(w) * ((pixel >> 8) & 255u));
            b = int(uint(b) + uint(w) * ((pixel >> 16) & 255u));
        }
    }
    uint red = uint((int(uint(r) + 128u)) >> 8);
    uint green = uint((int(uint(g) + 128u)) >> 8);
    uint blue = uint((int(uint(b) + 128u)) >> 8);
    destination[int((uint(y) * uint(__c.size)) + uint(x))] = 4278190080u | (blue << 16) | (green << 8) | red;
}
