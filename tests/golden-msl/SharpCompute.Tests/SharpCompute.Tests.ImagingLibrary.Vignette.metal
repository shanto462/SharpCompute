// SharpCompute.Tests.ImagingLibrary.Vignette (SharpCompute.Tests): entry point SharpCompute_Tests_ImagingLibrary_Vignette, compile options None
#include <metal_stdlib>

using namespace metal;

constexpr sampler __ms_sampler_linear_clamp(coord::normalized, filter::linear, address::clamp_to_edge);

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

kernel void SharpCompute_Tests_ImagingLibrary_Vignette(
    texture2d<float, access::write> __output [[texture(0)]],
    texture2d<float, access::sample> photo [[texture(1)]],
    constant __GridWindow& __window [[buffer(0), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 uv = float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1));
    float edge = metal::length(uv - 0.5f);
    __output.write(float4(photo.sample(__ms_sampler_linear_clamp, uv, metal::level(0)).xyz * metal::saturate(1.25f - (2.0f * edge * edge)), 1.0f), __tid.xy);
    return;
}
