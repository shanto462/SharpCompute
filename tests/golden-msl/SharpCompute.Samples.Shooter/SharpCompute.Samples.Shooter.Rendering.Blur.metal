// SharpCompute.Samples.Shooter.Rendering.Blur (SharpCompute.Samples.Shooter): entry point SharpCompute_Samples_Shooter_Rendering_Blur, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int width;
    int height;
    int stepX;
    int stepY;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, stepX) == 8, "SharpCompute: offset of '__Constants.stepX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, stepY) == 12, "SharpCompute: offset of '__Constants.stepY' does not match its C# layout");

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

static float4 Tap(int x, int y, int k, device packed_float4* source, constant __Constants& __c);

static float4 Tap(int x, int y, int k, device packed_float4* source, constant __Constants& __c)
{
    int ax = metal::clamp(int(uint(x) + (uint(__c.stepX) * uint(k))), 0, int(uint(__c.width) - 1u));
    int ay = metal::clamp(int(uint(y) + (uint(__c.stepY) * uint(k))), 0, int(uint(__c.height) - 1u));
    int bx = metal::clamp(int(uint(x) - (uint(__c.stepX) * uint(k))), 0, int(uint(__c.width) - 1u));
    int by = metal::clamp(int(uint(y) - (uint(__c.stepY) * uint(k))), 0, int(uint(__c.height) - 1u));
    return source[int((uint(ay) * uint(__c.width)) + uint(ax))] + source[int((uint(by) * uint(__c.width)) + uint(bx))];
}

kernel void SharpCompute_Samples_Shooter_Rendering_Blur(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float4 sum = source[int((uint(y) * uint(__c.width)) + uint(x))] * 0.227027f;
    sum += ::Tap(x, y, 1, source, __c) * 0.1945946f;
    sum += ::Tap(x, y, 2, source, __c) * 0.1216216f;
    sum += ::Tap(x, y, 3, source, __c) * 0.054054f;
    sum += ::Tap(x, y, 4, source, __c) * 0.0162162f;
    destination[int((uint(y) * uint(__c.width)) + uint(x))] = sum;
}
