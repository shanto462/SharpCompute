// SharpCompute.Samples.Textures.ShowField (SharpCompute.Samples.Textures): entry point SharpCompute_Samples_Textures_ShowField, compile options None
#include <metal_stdlib>

using namespace metal;

constexpr sampler __ms_sampler_linear_repeat(coord::normalized, filter::linear, address::repeat);

constant float3 Deep = float3(0.02f, 0.03f, 0.09f);
constant float3 Middle = float3(0.1f, 0.62f, 0.78f);
constant float3 Bright = float3(1.0f, 0.93f, 0.62f);

struct __Constants
{
    float zoom;
    packed_float2 pan;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, zoom) == 0, "SharpCompute: offset of '__Constants.zoom' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, pan) == 4, "SharpCompute: offset of '__Constants.pan' does not match its C# layout");

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

static float2 FieldUv(float2 position, float zoom, float2 pan);

static float2 FieldUv(float2 position, float zoom, float2 pan)
{
    return float2(0.5f, 0.5f) + ((position - 0.5f) * zoom) + pan;
}

kernel void SharpCompute_Samples_Textures_ShowField(
    texture2d<float, access::write> __output [[texture(0)]],
    texture2d<float, access::sample> field [[texture(1)]],
    texture2d<float, access::sample> labels [[texture(2)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 frame = float2(float(int(__grid.x)), float(int(__grid.y)));
    float2 uv = ::FieldUv((float2(float(int(__tid.x)), float(int(__tid.y))) + 0.5f) / frame, __c.zoom, __c.pan);
    float amount = metal::saturate(field.sample(__ms_sampler_linear_repeat, uv, metal::level(0)).y * 3.0f);
    float3 color = metal::mix(::Deep, ::Middle, metal::smoothstep(0.0f, 0.45f, amount));
    color = metal::mix(color, ::Bright, metal::smoothstep(0.45f, 1.0f, amount));
    float2 seam = metal::abs(metal::fract(uv) - 0.5f);
    float2 shift = metal::abs(metal::fract(__c.pan + 0.5f) - 0.5f);
    if ((__c.zoom > 1.001f || metal::max(shift.x, shift.y) > 0.0005f) && metal::max(seam.x, seam.y) > 0.5f - (__c.zoom / frame.y))
    {
        color = metal::mix(color, float3(1.0f, 1.0f, 1.0f), 0.3f);
    }
    float ink = labels.read(uint2(int2(__tid.xy))).x;
    if (ink > 0.75f)
    {
        color = float3(1.0f, 1.0f, 1.0f);
    }
    else if (ink > 0.25f)
    {
        color *= 0.22f;
    }
    __output.write(float4(color, 1.0f), __tid.xy);
    return;
}
