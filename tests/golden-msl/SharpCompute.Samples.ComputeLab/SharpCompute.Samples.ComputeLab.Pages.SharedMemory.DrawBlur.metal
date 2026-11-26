// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.DrawBlur (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_DrawBlur, compile options None
#include <metal_stdlib>

using namespace metal;

constant int PanelY = 104;
constant int FirstPanelX = 32;
constant int PanelSize = 384;
constant int PanelPitch = 408;

struct __Constants
{
    int width;
    int size;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 4, "SharpCompute: offset of '__Constants.size' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_DrawBlur(
    device uint* pixels [[buffer(0)]],
    device float* image [[buffer(1)]],
    device float* blurred [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    int py = int(uint(y) - uint(::PanelY));
    int px = int(uint(x) - uint(::FirstPanelX));
    if (py >= 0 && py < ::PanelSize && px >= 0)
    {
        int panel = px / ::PanelPitch;
        int u = px % ::PanelPitch;
        if (panel < 2 && u < ::PanelSize)
        {
            int element = int((uint(metal::min(int(uint(__c.size) - 1u), int(uint(py) * uint(__c.size)) / ::PanelSize)) * uint(__c.size)) + uint(metal::min(int(uint(__c.size) - 1u), int(uint(u) * uint(__c.size)) / ::PanelSize)));
            float value = panel == 0 ? image[element] : blurred[element];
            color = metal::mix(float3(0.02f, 0.03f, 0.09f), float3(1.0f, 0.86f, 0.55f), value);
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
