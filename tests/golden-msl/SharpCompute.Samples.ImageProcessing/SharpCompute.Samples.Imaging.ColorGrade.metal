// SharpCompute.Samples.Imaging.ColorGrade (SharpCompute.Samples.ImageProcessing): entry point SharpCompute_Samples_Imaging_ColorGrade, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    float gain;
    float contrast;
    float saturation;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, gain) == 0, "SharpCompute: offset of '__Constants.gain' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, contrast) == 4, "SharpCompute: offset of '__Constants.contrast' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, saturation) == 8, "SharpCompute: offset of '__Constants.saturation' does not match its C# layout");

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

static float4 Grade(float4 color, float gain, float contrast, float saturation);

static float4 Grade(float4 color, float gain, float contrast, float saturation)
{
    if (color.w <= 0.0f)
    {
        return color;
    }
    float3 rgb = (color.xyz / color.w) * gain;
    rgb = ((rgb - 0.5f) * contrast) + 0.5f;
    float luma = (0.2126f * rgb.x) + (0.7152f * rgb.y) + (0.0722f * rgb.z);
    rgb = luma + ((rgb - luma) * saturation);
    return float4(metal::saturate(rgb) * color.w, color.w);
}

kernel void SharpCompute_Samples_Imaging_ColorGrade(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    destination[int(__tid.x)] = ::Grade(source[int(__tid.x)], __c.gain, __c.contrast, __c.saturation);
}
