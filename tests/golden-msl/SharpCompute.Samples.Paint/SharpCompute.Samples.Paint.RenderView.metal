// SharpCompute.Samples.Paint.RenderView (SharpCompute.Samples.Paint): entry point SharpCompute_Samples_Paint_RenderView, compile options None
#include <metal_stdlib>

using namespace metal;

constant int HudLeft = 12;
constant int HudTop = 12;
constant int HudWidth = 224;
constant int HudHeight = 76;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int width;
    packed_float4 brushColor;
    float brushRadius;
    float hardness;
    float opacity;
    int erase;
    int picker;
    packed_float2 wheelCenter;
    float wheelRadius;
};
static_assert(sizeof(__Constants) == 52, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, brushColor) == 4, "SharpCompute: offset of '__Constants.brushColor' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, brushRadius) == 20, "SharpCompute: offset of '__Constants.brushRadius' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, hardness) == 24, "SharpCompute: offset of '__Constants.hardness' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, opacity) == 28, "SharpCompute: offset of '__Constants.opacity' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, erase) == 32, "SharpCompute: offset of '__Constants.erase' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, picker) == 36, "SharpCompute: offset of '__Constants.picker' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, wheelCenter) == 40, "SharpCompute: offset of '__Constants.wheelCenter' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, wheelRadius) == 48, "SharpCompute: offset of '__Constants.wheelRadius' does not match its C# layout");

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

static float3 Checker(int x, int y);
static float3 HueSaturation(float hue, float saturation);
static float Coverage(float distance, float radius, float hardness);
static uint Pack(float3 color);

static float3 Checker(int x, int y)
{
    float shade = ((int(uint(x / 16) + uint(y / 16))) & 1) == 0 ? 0.86f : 0.74f;
    return float3(shade, shade, shade);
}

static float3 HueSaturation(float hue, float saturation)
{
    float r = metal::saturate(metal::abs((hue * 6.0f) - 3.0f) - 1.0f);
    float g = metal::saturate(2.0f - metal::abs((hue * 6.0f) - 2.0f));
    float b = metal::saturate(2.0f - metal::abs((hue * 6.0f) - 4.0f));
    return metal::mix(float3(1.0f, 1.0f, 1.0f), float3(r, g, b), saturation);
}

static float Coverage(float distance, float radius, float hardness)
{
    float band = metal::max(1.0f - hardness, 1.0f / radius);
    float f = metal::saturate((1.0f - (distance / radius)) / band);
    return f * f * (3.0f - (2.0f * f));
}

static uint Pack(float3 color)
{
    uint3 c = uint3((metal::saturate(color) * 255.0f) + float3(0.5f, 0.5f, 0.5f));
    return c.x | (c.y << 8) | (c.z << 16) | (255u << 24);
}

kernel void SharpCompute_Samples_Paint_RenderView(
    device packed_float4* canvas [[buffer(0)]],
    device uint* pixels [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float4 c = canvas[int((uint(y) * uint(__c.width)) + uint(x))];
    float3 rgb = c.xyz + (::Checker(x, y) * (1.0f - c.w));
    float2 p = float2(float(x) + 0.5f, float(y) + 0.5f);
    if (__c.picker != 0)
    {
        rgb *= 0.35f;
        float2 d = p - __c.wheelCenter;
        float r = metal::length(d);
        float inside = metal::saturate(__c.wheelRadius - r + 0.5f);
        if (inside > 0.0f)
        {
            float hue = (metal::atan2(d.y, d.x) / 6.2831855f) + 0.5f;
            rgb = metal::mix(rgb, ::HueSaturation(hue, metal::saturate(r / __c.wheelRadius)), inside);
        }
    }
    if (x >= ::HudLeft && y >= ::HudTop && x < int(uint(::HudLeft) + uint(::HudWidth)) && y < int(uint(::HudTop) + uint(::HudHeight)))
    {
        rgb = metal::mix(rgb, float3(0.08f, 0.08f, 0.1f), 0.8f);
        float swatchDistance = metal::length(p - float2(12.0f + 38.0f, 12.0f + 38.0f));
        float3 swatch = __c.erase != 0 ? ::Checker(x / 2, y / 2) : __c.brushColor.xyz;
        rgb = metal::mix(rgb, float3(1.0f, 1.0f, 1.0f), metal::saturate(29.0f - swatchDistance));
        rgb = metal::mix(rgb, swatch, metal::saturate(27.0f - swatchDistance));
        float previewRadius = metal::min(__c.brushRadius, 32.0f);
        float previewDistance = metal::length(p - float2(12.0f + 148.0f, 12.0f + 38.0f));
        float3 ink = __c.erase != 0 ? float3(1.0f, 1.0f, 1.0f) : __c.brushColor.xyz;
        rgb = metal::mix(rgb, ink, ::Coverage(previewDistance, previewRadius, __c.hardness) * __c.opacity);
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = ::Pack(rgb);
}
