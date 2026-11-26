// SharpCompute.Samples.Shaders.MandelbrotPalette (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_MandelbrotPalette, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    float blend;
    float phaseA;
    float densityA;
    float phaseB;
    float densityB;
};
static_assert(sizeof(__Constants) == 20, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, blend) == 0, "SharpCompute: offset of '__Constants.blend' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, phaseA) == 4, "SharpCompute: offset of '__Constants.phaseA' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, densityA) == 8, "SharpCompute: offset of '__Constants.densityA' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, phaseB) == 12, "SharpCompute: offset of '__Constants.phaseB' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, densityB) == 16, "SharpCompute: offset of '__Constants.densityB' does not match its C# layout");

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

static float3 Palette(float t);
static float3 Interior(float4 iteration, float phase);
static float3 Shade(float4 iteration, float phase, float density);

static float3 Palette(float t)
{
    return float3(0.5f, 0.5f, 0.5f) + (float3(0.5f, 0.5f, 0.5f) * metal::cos(6.2831855f * (float3(t, t, t) + float3(0.0f, 0.15f, 0.3f))));
}

static float3 Interior(float4 iteration, float phase)
{
    float r = iteration.y;
    float t = iteration.w + (0.5f * r) + (0.618034f * metal::fract(iteration.z * 0.618034f)) + phase;
    float3 color = ::Palette(t);
    float luma = metal::dot(color, float3(0.299f, 0.587f, 0.114f));
    color = metal::mix(float3(luma, luma, luma), color, 0.5f);
    float rings = 0.5f + (0.5f * metal::cos(6.2831855f * 4.0f * r));
    return (float3(0.4f, 0.4f, 0.4f) + color) * (0.15f + (0.1f * rings));
}

static float3 Shade(float4 iteration, float phase, float density)
{
    if (iteration.x < 0.0f)
    {
        return ::Interior(iteration, phase);
    }
    return ::Palette((metal::sqrt(metal::max(iteration.y, 0.0f)) * density) + phase);
}

kernel void SharpCompute_Samples_Shaders_MandelbrotPalette(
    texture2d<float, access::write> __output [[texture(0)]],
    device packed_float4* layerA [[buffer(0)]],
    device packed_float4* layerB [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int index = int((__tid.y * __grid.x) + __tid.x);
    float3 color = ::Shade(layerA[index], __c.phaseA, __c.densityA);
    if (__c.blend > 0.0f)
    {
        color = metal::mix(color, ::Shade(layerB[index], __c.phaseB, __c.densityB), __c.blend);
    }
    __output.write(float4(color, 1.0f), __tid.xy);
    return;
}
