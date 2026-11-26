// SharpCompute.Tests.Shaders.SampleFloat4 (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SampleFloat4, compile options None
#include <metal_stdlib>

using namespace metal;

constexpr sampler __ms_sampler_point_clamp(coord::normalized, filter::nearest, address::clamp_to_edge);
constexpr sampler __ms_sampler_point_repeat(coord::normalized, filter::nearest, address::repeat);
constexpr sampler __ms_sampler_point_mirror(coord::normalized, filter::nearest, address::mirrored_repeat);
constexpr sampler __ms_sampler_linear_clamp(coord::normalized, filter::linear, address::clamp_to_edge);
constexpr sampler __ms_sampler_linear_repeat(coord::normalized, filter::linear, address::repeat);
constexpr sampler __ms_sampler_linear_mirror(coord::normalized, filter::linear, address::mirrored_repeat);

static_assert(sizeof(packed_float2) == 8, "SharpCompute: size of 'packed_float2' does not match its C# layout");
static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int sampler;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, sampler) == 0, "SharpCompute: offset of '__Constants.sampler' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_SampleFloat4(
    texture2d<float, access::sample> source [[texture(0)]],
    const device packed_float2* coordinates [[buffer(0)]],
    device packed_float4* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float2 uv = coordinates[i];
    switch (__c.sampler)
    {
        case 0:
        {
            output[i] = source.sample(__ms_sampler_point_clamp, uv, metal::level(0));
            break;
        }
        case 1:
        {
            output[i] = source.sample(__ms_sampler_point_repeat, uv, metal::level(0));
            break;
        }
        case 2:
        {
            output[i] = source.sample(__ms_sampler_point_mirror, uv, metal::level(0));
            break;
        }
        case 3:
        {
            output[i] = source.sample(__ms_sampler_linear_clamp, uv, metal::level(0));
            break;
        }
        case 4:
        {
            output[i] = source.sample(__ms_sampler_linear_repeat, uv, metal::level(0));
            break;
        }
        default:
        {
            output[i] = source.sample(__ms_sampler_linear_mirror, uv, metal::level(0));
            break;
        }
    }
}
