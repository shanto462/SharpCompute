// SharpCompute.Samples.ComputeLab.Pages.DrawFastMath (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_DrawFastMath, compile options None
#include <metal_stdlib>

using namespace metal;

constant int PanelY = 104;
constant int PanelSize = 512;
constant int FieldX = 80;
constant int ErrorX = 688;

struct __Constants
{
    int width;
    int size;
    float errorScale;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 4, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, errorScale) == 8, "SharpCompute: offset of '__Constants.errorScale' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_DrawFastMath(
    device uint* pixels [[buffer(0)]],
    device float* field [[buffer(1)]],
    device float* error [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    int py = int(uint(y) - uint(::PanelY));
    if (py >= 0 && py < ::PanelSize)
    {
        int fx = int(uint(x) - uint(::FieldX));
        int ex = int(uint(x) - uint(::ErrorX));
        if (fx >= 0 && fx < ::PanelSize)
        {
            float value = metal::saturate(field[int((uint(py) * 2u * uint(__c.size)) + (uint(fx) * 2u))] * 0.25f);
            float line = metal::smoothstep(0.42f, 0.5f, metal::abs(metal::fract(value * 12.0f) - 0.5f));
            color = metal::mix(float3(0.05f, 0.12f, 0.35f), float3(1.0f, 0.8f, 0.35f), value) * (1.0f - (0.35f * line));
        }
        else if (ex >= 0 && ex < ::PanelSize)
        {
            float level = metal::saturate(error[int((uint(py) * 2u * uint(__c.size)) + (uint(ex) * 2u))] * __c.errorScale);
            color = metal::mix(float3(0.03f, 0.04f, 0.06f), float3(1.0f, 0.45f, 0.2f), metal::sqrt(level)) + float3(level * level * 0.6f);
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
