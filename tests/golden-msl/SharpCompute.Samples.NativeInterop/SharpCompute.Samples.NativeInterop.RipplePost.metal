// SharpCompute.Samples.NativeInterop.RipplePost (SharpCompute.Samples.NativeInterop): entry point SharpCompute_Samples_NativeInterop_RipplePost, compile options None
#include <metal_stdlib>

using namespace metal;

constexpr sampler __ms_sampler_linear_clamp(coord::normalized, filter::linear, address::clamp_to_edge);

struct __Constants
{
    packed_float2 center;
    float time;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, center) == 0, "SharpCompute: offset of '__Constants.center' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 8, "SharpCompute: offset of '__Constants.time' does not match its C# layout");

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

kernel void SharpCompute_Samples_NativeInterop_RipplePost(
    texture2d<float, access::sample> scene [[texture(0)]],
    texture2d<float, access::read_write> output [[texture(1)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int2 xy = int2(__tid.xy);
    float2 size = float2(float(int(output.get_width())), float(int(output.get_height())));
    float2 uv = (float2(float(xy.x), float(xy.y)) + 0.5f) / size;
    float aspect = size.x / size.y;
    float2 d = (uv - __c.center) * float2(aspect, 1.0f);
    float r = metal::length(d) + 1E-05f;
    float wave = metal::sin((r * 46.0f) - (__c.time * 5.0f)) * metal::exp(-r * 2.2f) * metal::saturate(1.0f - (r / 0.7f)) * 0.014f;
    float2 offset = d / r * wave / float2(aspect, 1.0f);
    float4 middle = scene.sample(__ms_sampler_linear_clamp, uv + offset, metal::level(0));
    float red = scene.sample(__ms_sampler_linear_clamp, uv + (offset * 1.8f), metal::level(0)).x;
    float blue = scene.sample(__ms_sampler_linear_clamp, uv + (offset * 0.4f), metal::level(0)).z;
    float3 color = float3(red, middle.y, blue);
    float3 glow = float3(0.0f, 0.0f, 0.0f);
    float2 step = float2(6.0f, 6.0f) / size;
    for (int k = 0; k < 8; k = int(uint(k) + 1u))
    {
        float angle = float(k) * 0.7853982f;
        float2 tap = uv + offset + (float2(metal::cos(angle), metal::sin(angle)) * step * 1.5f);
        float3 sample = scene.sample(__ms_sampler_linear_clamp, tap, metal::level(0)).xyz;
        glow += metal::max(sample - 0.55f, float3(0.0f, 0.0f, 0.0f));
    }
    color += glow * 0.22f;
    output.write(float4(metal::saturate(color.x), metal::saturate(color.y), metal::saturate(color.z), 1.0f), uint2(xy));
}
