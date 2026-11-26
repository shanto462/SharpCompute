// SharpCompute.Samples.Imaging.GaussianBlurPass (SharpCompute.Samples.ImageProcessing): entry point SharpCompute_Samples_Imaging_GaussianBlurPass, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

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

kernel void SharpCompute_Samples_Imaging_GaussianBlurPass(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* destination [[buffer(1)]],
    const device float* weights [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float4 sum = float4(0.0f, 0.0f, 0.0f, 0.0f);
    for (int k = int(-uint(__c.radius)); k <= __c.radius; k = int(uint(k) + 1u))
    {
        int sx = metal::clamp(int(uint(x) + (uint(k) * uint(__c.stepX))), 0, int(uint(__c.width) - 1u));
        int sy = metal::clamp(int(uint(y) + (uint(k) * uint(__c.stepY))), 0, int(uint(__c.height) - 1u));
        sum += source[int((uint(sy) * uint(__c.width)) + uint(sx))] * weights[int(uint(k) + uint(__c.radius))];
    }
    destination[int((uint(y) * uint(__c.width)) + uint(x))] = sum;
}
