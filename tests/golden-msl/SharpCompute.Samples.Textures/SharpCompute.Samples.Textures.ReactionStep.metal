// SharpCompute.Samples.Textures.ReactionStep (SharpCompute.Samples.Textures): entry point SharpCompute_Samples_Textures_ReactionStep, compile options None
#include <metal_stdlib>

using namespace metal;

constexpr sampler __ms_sampler_point_repeat(coord::normalized, filter::nearest, address::repeat);

struct __Constants
{
    float feed;
    float kill;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, feed) == 0, "SharpCompute: offset of '__Constants.feed' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, kill) == 4, "SharpCompute: offset of '__Constants.kill' does not match its C# layout");

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

static float4 Tap(float2 uv, float2 texel, float dx, float dy, texture2d<float, access::sample> previous);

static float4 Tap(float2 uv, float2 texel, float dx, float dy, texture2d<float, access::sample> previous)
{
    return previous.sample(__ms_sampler_point_repeat, uv + (float2(dx, dy) * texel), metal::level(0));
}

kernel void SharpCompute_Samples_Textures_ReactionStep(
    texture2d<float, access::sample> previous [[texture(0)]],
    texture2d<float, access::read_write> next [[texture(1)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    float2 texel = float2(1.0f / float(int(previous.get_width())), 1.0f / float(int(previous.get_height())));
    float2 uv = (float2(float(int(__tid.x)), float(int(__tid.y))) + 0.5f) * texel;
    float4 center = previous.sample(__ms_sampler_point_repeat, uv, metal::level(0));
    float4 laplacian = ((::Tap(uv, texel, -1.0f, 0.0f, previous) + ::Tap(uv, texel, 1.0f, 0.0f, previous) + ::Tap(uv, texel, 0.0f, -1.0f, previous) + ::Tap(uv, texel, 0.0f, 1.0f, previous)) * 0.2f) + ((::Tap(uv, texel, -1.0f, -1.0f, previous) + ::Tap(uv, texel, 1.0f, -1.0f, previous) + ::Tap(uv, texel, -1.0f, 1.0f, previous) + ::Tap(uv, texel, 1.0f, 1.0f, previous)) * 0.05f) - center;
    float a = center.x;
    float b = center.y;
    float reaction = a * b * b;
    float nextA = a + laplacian.x - reaction + (__c.feed * (1.0f - a));
    float nextB = b + (0.5f * laplacian.y) + reaction - ((__c.kill + __c.feed) * b);
    next.write(float4(metal::saturate(nextA), metal::saturate(nextB), 0.0f, 1.0f), uint2(int2(__tid.xy)));
}
