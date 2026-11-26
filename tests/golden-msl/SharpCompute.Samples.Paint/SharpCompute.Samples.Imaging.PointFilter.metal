// SharpCompute.Samples.Imaging.PointFilter (SharpCompute.Samples.Paint): entry point SharpCompute_Samples_Imaging_PointFilter, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int mode;
    float levels;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, mode) == 0, "SharpCompute: offset of '__Constants.mode' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, levels) == 4, "SharpCompute: offset of '__Constants.levels' does not match its C# layout");

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

static float4 Apply(float4 color, int mode, float levels);

static float4 Apply(float4 color, int mode, float levels)
{
    if (mode == 0)
    {
        return float4(color.w - color.x, color.w - color.y, color.w - color.z, color.w);
    }
    if (mode == 1)
    {
        if (color.w <= 0.0f)
        {
            return color;
        }
        float steps = levels - 1.0f;
        float3 rgb = metal::round((color.xyz / color.w) * steps) / steps;
        return float4(rgb * color.w, color.w);
    }
    float luma = (0.2126f * color.x) + (0.7152f * color.y) + (0.0722f * color.z);
    return float4(luma, luma, luma, color.w);
}

kernel void SharpCompute_Samples_Imaging_PointFilter(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    destination[int(__tid.x)] = ::Apply(source[int(__tid.x)], __c.mode, __c.levels);
}
