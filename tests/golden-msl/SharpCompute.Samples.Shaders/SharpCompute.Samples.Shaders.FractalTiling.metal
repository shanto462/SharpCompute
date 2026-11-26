// SharpCompute.Samples.Shaders.FractalTiling (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_FractalTiling, compile options None
#include <metal_stdlib>

using namespace metal;

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

kernel void SharpCompute_Samples_Shaders_FractalTiling(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 position = ((float2(int2(256u * __tid.xy))) / float(int(__grid.x))) + __c.time;
    float4 color = float4(0.0f);
    for (int i = 0; i < 6; i = int(uint(i) + 1u))
    {
        float2 a = metal::floor(position);
        float2 b = metal::fract(position);
        float4 w = metal::fract((metal::sin((a.x * 7.0f) + (31.0f * a.y) + (0.01f * __c.time)) + float4(0.035f, 0.01f, 0.0f, 0.7f)) * 13.545317f);
        color.xyz += w.xyz * 2.0f * metal::smoothstep(0.45f, 0.55f, w.w) * metal::sqrt(16.0f * b.x * b.y * (1.0f - b.x) * (1.0f - b.y));
        position /= 2.0f;
        color /= 2.0f;
    }
    color.xyz = metal::pow(color.xyz, float3(0.7f, 0.8f, 0.5f));
    color.w = 1.0f;
    __output.write(color, __tid.xy);
    return;
}
