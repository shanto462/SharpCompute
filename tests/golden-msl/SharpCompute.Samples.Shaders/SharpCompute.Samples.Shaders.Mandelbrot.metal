// SharpCompute.Samples.Shaders.Mandelbrot (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_Mandelbrot, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline uint3 __ms_sat_uint3(float3 x) { float3 y = metal::select(float3(0.0f), x, x > 0.0f); uint3 r = uint3(metal::min(y, float3(4294967040.0f))); return metal::select(r, uint3(4294967295u), x >= 4294967296.0f); }
inline uint3 __ms_sat_uint3(half3 x) { return __ms_sat_uint3(float3(x)); }

struct __Constants
{
    int width;
    int height;
    int maxIterations;
    packed_float2 center;
    float scale;
    float colorOffset;
};
static_assert(sizeof(__Constants) == 28, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, maxIterations) == 8, "SharpCompute: offset of '__Constants.maxIterations' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, center) == 12, "SharpCompute: offset of '__Constants.center' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, scale) == 20, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, colorOffset) == 24, "SharpCompute: offset of '__Constants.colorOffset' does not match its C# layout");

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

static uint PackRgba(float3 color);

static uint PackRgba(float3 color)
{
    uint3 c = __ms_sat_uint3((metal::saturate(color) * 255.0f) + float3(0.5f, 0.5f, 0.5f));
    return c.x | (c.y << 8) | (c.z << 16) | (255u << 24);
}

kernel void SharpCompute_Samples_Shaders_Mandelbrot(
    device uint* pixels [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int px = int(__tid.x);
    int py = int(__tid.y);
    float2 c = __c.center + (float2(float(px) - (float(__c.width) * 0.5f), float(py) - (float(__c.height) * 0.5f)) * (__c.scale * 2.0f / float(__c.height)));
    float2 z = float2(0.0f, 0.0f);
    int n = 0;
    while (n < __c.maxIterations && metal::dot(z, z) < 256.0f)
    {
        z = float2((z.x * z.x) - (z.y * z.y), 2.0f * z.x * z.y) + c;
        n = int(uint(n) + 1u);
    }
    float3 color = float3(0.0f, 0.0f, 0.0f);
    if (n < __c.maxIterations)
    {
        float smooth = float(n) + 1.0f - metal::log2(metal::log2(metal::length(z)));
        float t = (smooth * 0.03f) + __c.colorOffset;
        color = float3(0.5f, 0.5f, 0.5f) + (float3(0.5f, 0.5f, 0.5f) * metal::cos(6.28318f * (float3(t, t, t) + float3(0.0f, 0.1f, 0.2f))));
    }
    pixels[int((uint(py) * uint(__c.width)) + uint(px))] = ::PackRgba(color);
}
