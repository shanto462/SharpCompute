// SharpCompute.Samples.ComputeLab.Pages.DrawLocalArrays (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_DrawLocalArrays, compile options None
#include <metal_stdlib>

using namespace metal;

constant int PanelY = 104;
constant int PanelSize = 384;
constant int GaussianX = 864;
constant int MedianX = 448;
constant int InputX = 32;
constant int CropY = 360;
constant int CropX = 400;

struct __Constants
{
    int width;
    int size;
    int active;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 4, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, active) == 8, "SharpCompute: offset of '__Constants.active' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_DrawLocalArrays(
    device uint* pixels [[buffer(0)]],
    device uint* input [[buffer(1)]],
    device uint* median [[buffer(2)]],
    device uint* gaussian [[buffer(3)]],
    constant __Constants& __c [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    int py = int(uint(y) - uint(::PanelY));
    if (py >= -3 && py < int(uint(::PanelSize) + 3u))
    {
        int panel = x >= int(uint(::GaussianX) - 3u) ? 2 : (x >= int(uint(::MedianX) - 3u) ? 1 : 0);
        int left = panel == 0 ? ::InputX : (panel == 1 ? ::MedianX : ::GaussianX);
        int px = int(uint(x) - uint(left));
        if (px >= 0 && px < ::PanelSize && py >= 0 && py < ::PanelSize)
        {
            int i = int(((uint(::CropY) + uint(py / 2)) * uint(__c.size)) + uint(::CropX) + uint(px / 2));
            uint pixel = panel == 0 ? input[i] : (panel == 1 ? median[i] : gaussian[i]);
            color = metal::unpack_unorm4x8_to_float(pixel).xyz;
        }
        else if (px >= -3 && px < int(uint(::PanelSize) + 3u) && panel == int(uint(__c.active) + 1u))
        {
            color = float3(1.0f, 0.8f, 0.35f);
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
