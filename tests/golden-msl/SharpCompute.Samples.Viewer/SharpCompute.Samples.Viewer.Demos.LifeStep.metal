// SharpCompute.Samples.Viewer.Demos.LifeStep (SharpCompute.Samples.Viewer): entry point SharpCompute_Samples_Viewer_Demos_LifeStep, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return x | int(metal::reverse_bits(x) == int(0)); }

struct __Constants
{
    int width;
    int height;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");

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

kernel void SharpCompute_Samples_Viewer_Demos_LifeStep(
    device uint* current [[buffer(0)]],
    device uint* next [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    int neighbors = 0;
    for (int dy = -1; dy <= 1; dy = int(uint(dy) + 1u))
    {
        for (int dx = -1; dx <= 1; dx = int(uint(dx) + 1u))
        {
            if (dx == 0 && dy == 0)
            {
                continue;
            }
            int nx = (int(uint(x) + uint(dx) + uint(__c.width))) % __ms_nonzero(__c.width);
            int ny = (int(uint(y) + uint(dy) + uint(__c.height))) % __ms_nonzero(__c.height);
            if (current[int((uint(ny) * uint(__c.width)) + uint(nx))] != 0u)
            {
                neighbors = int(uint(neighbors) + 1u);
            }
        }
    }
    uint age = current[int((uint(y) * uint(__c.width)) + uint(x))];
    uint result = 0u;
    if (age != 0u)
    {
        if (neighbors == 2 || neighbors == 3)
        {
            result = age < 255u ? age + 1u : 255u;
        }
    }
    else if (neighbors == 3)
    {
        result = 1u;
    }
    next[int((uint(y) * uint(__c.width)) + uint(x))] = result;
}
