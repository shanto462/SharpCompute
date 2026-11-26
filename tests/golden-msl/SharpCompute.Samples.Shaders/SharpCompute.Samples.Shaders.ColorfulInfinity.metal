// SharpCompute.Samples.Shaders.ColorfulInfinity (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_ColorfulInfinity, compile options None
#include <metal_stdlib>

using namespace metal;

constant float NumberOfLayers = 16.0f;
constant int NumberOfIterations = 23;

struct __Constants
{
    float time;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 0, "SharpCompute: offset of '__Constants.time' does not match its C# layout");

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

static float4 Tex(float3 p, constant __Constants& __c);

static float4 Tex(float3 p, constant __Constants& __c)
{
    float t = __c.time + 78.0f;
    float4 o = float4(p.x, p.y, p.z, 3.0f * metal::sin(t * 0.1f));
    float4 dec = float4(1.0f, 0.9f, 0.1f, 0.15f) + float4(0.06f * metal::cos(t * 0.1f), 0.0f, 0.0f, 0.14f * metal::cos(t * 0.23f));
    for (int i = 0; int(reinterpret_cast<thread uint&>(i)++) < ::NumberOfIterations; )
    {
        o.xzyw = metal::abs((o / metal::dot(o, o)) - dec);
    }
    return o;
}

kernel void SharpCompute_Samples_Shaders_ColorfulInfinity(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 uv = (float2(int2(__tid.xy)) - (float2(int2(__grid.xy)) * 0.5f)) / float(int(__grid.y));
    float3 col = float3(0.0f);
    float t = __c.time * 0.3f;
    for (float i = 0.0f; i <= 1.0f; i += 1.0f / ::NumberOfLayers)
    {
        float d = metal::fract(i + t);
        float s = metal::mix(5.0f, 0.5f, d);
        float f = d * metal::smoothstep(1.0f, 0.9f, d);
        col += ::Tex(float3(uv.x * s, uv.y * s, i * 4.0f), __c).xyz * f;
    }
    col /= ::NumberOfLayers;
    col *= float3(2.0f, 1.0f, 2.0f);
    col = metal::pow(col, 0.5f);
    __output.write(float4(col.x, col.y, col.z, 1.0f), __tid.xy);
    return;
}
