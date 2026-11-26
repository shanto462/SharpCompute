// SharpCompute.Samples.Imaging.LensDistortion (SharpCompute.Samples.Paint): entry point SharpCompute_Samples_Imaging_LensDistortion, compile options None
#include <metal_stdlib>

using namespace metal;

constexpr sampler __ms_sampler_linear_clamp(coord::normalized, filter::linear, address::clamp_to_edge);

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int width;
    int height;
    float strength;
    float fringe;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, strength) == 8, "SharpCompute: offset of '__Constants.strength' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, fringe) == 12, "SharpCompute: offset of '__Constants.fringe' does not match its C# layout");

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

static float2 SourceUv(float2 pixel, float2 size, float strength, float scale);

static float2 SourceUv(float2 pixel, float2 size, float strength, float scale)
{
    float2 center = size * 0.5f;
    float2 offset = pixel - center;
    float r2 = metal::dot(offset, offset) / metal::dot(center, center);
    float factor = (1.0f + (strength * r2)) / (1.0f + metal::max(strength, 0.0f)) * scale;
    return (center + (offset * factor)) / size;
}

kernel void SharpCompute_Samples_Imaging_LensDistortion(
    texture2d<float, access::sample> source [[texture(0)]],
    device packed_float4* destination [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    float2 size = float2(float(__c.width), float(__c.height));
    float2 pixel = float2(float(int(__tid.x)), float(int(__tid.y))) + 0.5f;
    float4 red = source.sample(__ms_sampler_linear_clamp, ::SourceUv(pixel, size, __c.strength, 1.0f + __c.fringe), metal::level(0));
    float4 green = source.sample(__ms_sampler_linear_clamp, ::SourceUv(pixel, size, __c.strength, 1.0f), metal::level(0));
    float4 blue = source.sample(__ms_sampler_linear_clamp, ::SourceUv(pixel, size, __c.strength, 1.0f - __c.fringe), metal::level(0));
    destination[int((__tid.y * uint(__c.width)) + __tid.x)] = float4(metal::min(red.x, green.w), green.y, metal::min(blue.z, green.w), green.w);
}
