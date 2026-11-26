// SharpCompute.Samples.Textures.SamplerGrid (SharpCompute.Samples.Textures): entry point SharpCompute_Samples_Textures_SamplerGrid, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

constexpr sampler __ms_sampler_point_clamp(coord::normalized, filter::nearest, address::clamp_to_edge);
constexpr sampler __ms_sampler_point_repeat(coord::normalized, filter::nearest, address::repeat);
constexpr sampler __ms_sampler_point_mirror(coord::normalized, filter::nearest, address::mirrored_repeat);
constexpr sampler __ms_sampler_linear_clamp(coord::normalized, filter::linear, address::clamp_to_edge);
constexpr sampler __ms_sampler_linear_repeat(coord::normalized, filter::linear, address::repeat);
constexpr sampler __ms_sampler_linear_mirror(coord::normalized, filter::linear, address::mirrored_repeat);

constant float3 Backdrop = float3(0.05f, 0.06f, 0.08f);

struct __Constants
{
    packed_float2 origin;
    packed_float2 cell;
    float gap;
    float zoom;
    packed_float2 pan;
    float angle;
    float aspect;
};
static_assert(sizeof(__Constants) == 40, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, origin) == 0, "SharpCompute: offset of '__Constants.origin' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, cell) == 8, "SharpCompute: offset of '__Constants.cell' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, gap) == 16, "SharpCompute: offset of '__Constants.gap' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, zoom) == 20, "SharpCompute: offset of '__Constants.zoom' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, pan) == 24, "SharpCompute: offset of '__Constants.pan' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, angle) == 32, "SharpCompute: offset of '__Constants.angle' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, aspect) == 36, "SharpCompute: offset of '__Constants.aspect' does not match its C# layout");

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

static float2 ImageUv(float2 local, float2 cell, float zoom, float2 pan, float angle, float aspect);
static float4 Pick(int sampler, float2 uv, texture2d<float, access::sample> image);

static float2 ImageUv(float2 local, float2 cell, float zoom, float2 pan, float angle, float aspect)
{
    float2 p = (local - 0.5f) * float2(cell.x / cell.y, 1.0f);
    float sine = metal::sin(angle);
    float cosine = metal::cos(angle);
    float2 rotated = float2((p.x * cosine) - (p.y * sine), (p.x * sine) + (p.y * cosine));
    return float2(0.5f, 0.5f) + (rotated * zoom * float2(1.0f / aspect, 1.0f)) + pan;
}

static float4 Pick(int sampler, float2 uv, texture2d<float, access::sample> image)
{
    switch (sampler)
    {
        case 0:
        {
            return image.sample(__ms_sampler_point_clamp, uv, metal::level(0));
        }
        case 1:
        {
            return image.sample(__ms_sampler_point_repeat, uv, metal::level(0));
        }
        case 2:
        {
            return image.sample(__ms_sampler_point_mirror, uv, metal::level(0));
        }
        case 3:
        {
            return image.sample(__ms_sampler_linear_clamp, uv, metal::level(0));
        }
        case 4:
        {
            return image.sample(__ms_sampler_linear_repeat, uv, metal::level(0));
        }
        default:
        {
            return image.sample(__ms_sampler_linear_mirror, uv, metal::level(0));
        }
    }
}

kernel void SharpCompute_Samples_Textures_SamplerGrid(
    texture2d<float, access::write> __output [[texture(0)]],
    texture2d<float, access::sample> image [[texture(1)]],
    texture2d<float, access::sample> labels [[texture(2)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 pixel = float2(float(int(__tid.x)), float(int(__tid.y))) + 0.5f;
    float2 index = metal::floor((pixel - __c.origin) / __c.cell);
    float3 color = ::Backdrop;
    if (index.x >= 0.0f && index.x < 3.0f && index.y >= 0.0f && index.y < 2.0f)
    {
        float2 local = (pixel - __c.origin - (index * __c.cell)) / __c.cell;
        float2 margin = metal::min(local, 1.0f - local) * __c.cell;
        if (metal::min(margin.x, margin.y) >= __c.gap)
        {
            float2 uv = ::ImageUv(local, __c.cell, __c.zoom, __c.pan, __c.angle, __c.aspect);
            color = ::Pick(int(uint(__ms_sat_int(index.x)) + (uint(__ms_sat_int(index.y)) * 3u)), uv, image).xyz;
            float2 fromCenter = metal::abs(uv - 0.5f) * float2(__c.aspect, 1.0f);
            if (metal::abs(metal::max(fromCenter.x - (0.5f * __c.aspect), fromCenter.y - 0.5f)) < 1.25f * __c.zoom / __c.cell.y)
            {
                color = metal::mix(color, float3(1.0f, 1.0f, 1.0f), 0.85f);
            }
        }
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
