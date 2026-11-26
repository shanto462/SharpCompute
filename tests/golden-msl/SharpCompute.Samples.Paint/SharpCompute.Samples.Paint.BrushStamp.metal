// SharpCompute.Samples.Paint.BrushStamp (SharpCompute.Samples.Paint): entry point SharpCompute_Samples_Paint_BrushStamp, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");
static_assert(sizeof(packed_float2) == 8, "SharpCompute: size of 'packed_float2' does not match its C# layout");

struct __Constants
{
    int stampCount;
    int width;
    int originX;
    int originY;
    float radius;
    float hardness;
    float opacity;
    packed_float4 color;
    int erase;
};
static_assert(sizeof(__Constants) == 48, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, stampCount) == 0, "SharpCompute: offset of '__Constants.stampCount' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 4, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, originX) == 8, "SharpCompute: offset of '__Constants.originX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, originY) == 12, "SharpCompute: offset of '__Constants.originY' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radius) == 16, "SharpCompute: offset of '__Constants.radius' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, hardness) == 20, "SharpCompute: offset of '__Constants.hardness' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, opacity) == 24, "SharpCompute: offset of '__Constants.opacity' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, color) == 28, "SharpCompute: offset of '__Constants.color' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, erase) == 44, "SharpCompute: offset of '__Constants.erase' does not match its C# layout");

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

static float Coverage(float distance, float radius, float hardness);
static float SegmentDistance(float2 p, float2 a, float2 b);
static float4 Composite(float4 under, float4 color, float alpha, int erase);

static float Coverage(float distance, float radius, float hardness)
{
    float band = metal::max(1.0f - hardness, 1.0f / radius);
    float f = metal::saturate((1.0f - (distance / radius)) / band);
    return f * f * (3.0f - (2.0f * f));
}

static float SegmentDistance(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float t = metal::saturate(metal::dot(p - a, ab) / metal::max(metal::dot(ab, ab), 1E-12f));
    return metal::length(p - (a + (ab * t)));
}

static float4 Composite(float4 under, float4 color, float alpha, int erase)
{
    return erase != 0 ? under * (1.0f - alpha) : (color * alpha) + (under * (1.0f - alpha));
}

kernel void SharpCompute_Samples_Paint_BrushStamp(
    device packed_float4* canvas [[buffer(0)]],
    device packed_float4* strokeBase [[buffer(1)]],
    device float* mask [[buffer(2)]],
    device packed_float2* stamps [[buffer(3)]],
    constant __Constants& __c [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(uint(__c.originX) + __tid.x);
    int y = int(uint(__c.originY) + __tid.y);
    int index = int((uint(y) * uint(__c.width)) + uint(x));
    float2 pixel = float2(float(x) + 0.5f, float(y) + 0.5f);
    float coverage = mask[index];
    if (__c.stampCount == 1)
    {
        coverage = metal::max(coverage, ::Coverage(metal::length(pixel - stamps[0]), __c.radius, __c.hardness));
    }
    for (int s = 0; int(uint(s) + 1u) < __c.stampCount; s = int(uint(s) + 1u))
    {
        coverage = metal::max(coverage, ::Coverage(::SegmentDistance(pixel, stamps[s], stamps[int(uint(s) + 1u)]), __c.radius, __c.hardness));
    }
    mask[index] = coverage;
    canvas[index] = ::Composite(strokeBase[index], __c.color, coverage * __c.opacity, __c.erase);
}
