// SharpCompute.Tests.Shaders.Composite (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_Composite, compile options None
#include <metal_stdlib>

using namespace metal;

constexpr sampler __ms_sampler_point_mirror(coord::normalized, filter::nearest, address::mirrored_repeat);
constexpr sampler __ms_sampler_linear_repeat(coord::normalized, filter::linear, address::repeat);

struct __Constants
{
    float gain;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, gain) == 0, "SharpCompute: offset of '__Constants.gain' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_Composite(
    texture2d<float, access::write> __output [[texture(0)]],
    texture2d<float, access::sample> image [[texture(1)]],
    texture2d<float, access::sample> mask [[texture(2)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 uv = (float2(float(int(__tid.x)), float(int(__tid.y))) + 0.5f) / float2(float(int(__grid.x)), float(int(__grid.y)));
    float4 color = image.sample(__ms_sampler_linear_repeat, uv * 2.0f, metal::level(0));
    float coverage = mask.sample(__ms_sampler_point_mirror, uv * 3.0f, metal::level(0)).x;
    __output.write(float4(color.x * coverage * __c.gain, color.y * coverage * __c.gain, color.z * coverage * __c.gain, 1.0f), __tid.xy);
    return;
}
