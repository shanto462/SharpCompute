// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.DrawReduction (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_DrawReduction, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

constant int PanelY = 104;
constant int PanelSize = 512;
constant int PanelX = 32;
constant int BarsX = 568;
constant int BarsWidth = 208;

struct __Constants
{
    int width;
    int side;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, side) == 4, "SharpCompute: offset of '__Constants.side' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_DrawReduction(
    device uint* pixels [[buffer(0)]],
    device float* values [[buffer(1)]],
    device float* sums [[buffer(2)]],
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
        int scale = __c.side / ::PanelSize;
        int px = int(uint(x) - uint(::PanelX));
        int bx = int(uint(x) - uint(::BarsX));
        if (px >= 0 && px < ::PanelSize)
        {
            float value = values[int(((uint(py) * uint(scale)) * uint(__c.side)) + (uint(px) * uint(scale)))];
            color = metal::mix(float3(0.03f, 0.05f, 0.16f), float3(1.0f, 0.82f, 0.4f), value);
        }
        else if (bx >= 0 && bx < ::BarsWidth)
        {
            int bar = py / 2;
            float mean = sums[bar] / (16.0f * float(__c.side));
            bool filled = bx < __ms_sat_int(mean * 208.0f);
            color = filled ? metal::mix(float3(0.1f, 0.35f, 0.75f), float3(1.0f, 0.82f, 0.4f), mean) * ((bar & 1) == 0 ? 1.0f : 0.82f) : float3(0.03f, 0.04f, 0.06f);
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
