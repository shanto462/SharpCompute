// SharpCompute.Samples.ComputeLab.Pages.DrawSharedCode (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_DrawSharedCode, compile options None
#include <metal_stdlib>

using namespace metal;

constant int PanelX = 40;
constant int PanelY = 104;
constant int SampleX = 848;
constant int SampleY = 392;
constant int SampleScale = 4;

static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");

struct __Constants
{
    int width;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");

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

static float3 ShaderMath_Tonemap(float3 __local_light);

static float3 ShaderMath_Tonemap(float3 __local_light)
{
    float3 x = __local_light * 0.6f;
    float3 curve = (x * ((x * 2.51f) + 0.03f)) / ((x * ((x * 2.43f) + 0.59f)) + 0.14f);
    return metal::sqrt(metal::saturate(curve));
}

kernel void SharpCompute_Samples_ComputeLab_Pages_DrawSharedCode(
    device uint* pixels [[buffer(0)]],
    device packed_float3* light [[buffer(1)]],
    device packed_float3* cpuSample [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    int ix = int(uint(x) - uint(::PanelX));
    int iy = int(uint(y) - uint(::PanelY));
    int sx = int(uint(x) - uint(::SampleX));
    int sy = int(uint(y) - uint(::SampleY));
    if (ix >= 0 && ix < 768 && iy >= 0 && iy < 512)
    {
        color = ::ShaderMath_Tonemap(light[int((uint(iy) * uint(768)) + uint(ix))]);
    }
    else if (sx >= 0 && sx < int(uint(96) * uint(::SampleScale)) && sy >= 0 && sy < int(uint(64) * uint(::SampleScale)))
    {
        color = ::ShaderMath_Tonemap(cpuSample[int((uint(sy / ::SampleScale) * uint(96)) + uint(sx / ::SampleScale))]);
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
