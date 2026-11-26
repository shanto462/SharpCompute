// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.BlurNaive (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_BlurNaive, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int width;
    int height;
    int radius;
    int stepX;
    int stepY;
};
static_assert(sizeof(__Constants) == 20, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radius) == 8, "SharpCompute: offset of '__Constants.radius' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, stepX) == 12, "SharpCompute: offset of '__Constants.stepX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, stepY) == 16, "SharpCompute: offset of '__Constants.stepY' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_BlurNaive(
    device float* source [[buffer(0)]],
    device float* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float sum = 0.0f;
    for (int k = int(-uint(__c.radius)); k <= __c.radius; k = int(uint(k) + 1u))
    {
        int sx = metal::clamp(int(uint(x) + (uint(k) * uint(__c.stepX))), 0, int(uint(__c.width) - 1u));
        int sy = metal::clamp(int(uint(y) + (uint(k) * uint(__c.stepY))), 0, int(uint(__c.height) - 1u));
        sum += float(int(uint(__c.radius) + 1u - uint(metal::abs(k)))) * source[int((uint(sy) * uint(__c.width)) + uint(sx))];
    }
    destination[int((uint(y) * uint(__c.width)) + uint(x))] = sum / float(int((uint(__c.radius) + 1u) * (uint(__c.radius) + 1u)));
}
