// SharpCompute.Samples.ComputeLab.Pages.DrawHalfPrecision (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_DrawHalfPrecision, compile options None
#include <metal_stdlib>

using namespace metal;

constant int PanelY = 128;
constant int FirstPanelX = 24;
constant int PanelSize = 296;
constant int PanelGap = 16;
constant float ErrorSteps = 8.0f;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");
static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");

struct __Constants
{
    int width;
    float errorScale;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, errorScale) == 4, "SharpCompute: offset of '__Constants.errorScale' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_DrawHalfPrecision(
    device uint* pixels [[buffer(0)]],
    const device packed_float4* source [[buffer(1)]],
    device packed_half4* __ms_half [[buffer(2)]],
    device float* error [[buffer(3)]],
    device uint* highlights [[buffer(4)]],
    constant __Constants& __c [[buffer(5)]],
    constant __GridWindow& __window [[buffer(6), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    int py = int(uint(y) - uint(::PanelY));
    int column = int(uint(x) - uint(::FirstPanelX));
    if (py >= 0 && py < ::PanelSize && column >= 0)
    {
        int panel = column / (int(uint(::PanelSize) + uint(::PanelGap)));
        int px = column % (int(uint(::PanelSize) + uint(::PanelGap)));
        if (panel < 4 && px < ::PanelSize)
        {
            int ix = (int(uint(px) * uint(1024))) / ::PanelSize;
            int iy = (int(uint(py) * uint(1024))) / ::PanelSize;
            int i = int((uint(iy) * uint(1024)) + uint(ix));
            if (panel == 0)
            {
                color = source[int(((uint(iy) + uint(8)) * uint(1040)) + uint(ix) + uint(8))].xyz;
            }
            else if (panel == 1)
            {
                color = float3(__ms_half[i].xyz);
            }
            else if (panel == 2)
            {
                float level = metal::ceil(metal::saturate(error[i] * __c.errorScale) * ::ErrorSteps) / ::ErrorSteps;
                color = metal::mix(float3(0.03f, 0.04f, 0.06f), float3(1.0f, 0.45f, 0.2f), metal::sqrt(level)) + float3(level * level * 0.6f);
            }
            else
            {
                float4 kept = metal::unpack_unorm4x8_to_float(highlights[i]);
                color = metal::mix(float3(0.03f, 0.04f, 0.06f), kept.xyz, kept.w);
            }
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
