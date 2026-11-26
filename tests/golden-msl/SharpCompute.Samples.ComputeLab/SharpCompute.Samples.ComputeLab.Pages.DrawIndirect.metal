// SharpCompute.Samples.ComputeLab.Pages.DrawIndirect (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_DrawIndirect, compile options None
#include <metal_stdlib>

using namespace metal;

constant int PanelY = 104;
constant int FirstPanelX = 32;
constant int PanelSize = 384;
constant int PanelPitch = 416;

struct __Constants
{
    int width;
    int size;
    int kept;
    int dispatched;
    uint cutoff;
    int compacted;
};
static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 4, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, kept) == 8, "SharpCompute: offset of '__Constants.kept' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, dispatched) == 12, "SharpCompute: offset of '__Constants.dispatched' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, cutoff) == 16, "SharpCompute: offset of '__Constants.cutoff' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, compacted) == 20, "SharpCompute: offset of '__Constants.compacted' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_DrawIndirect(
    device uint* pixels [[buffer(0)]],
    device uint* source [[buffer(1)]],
    device uint* shaded [[buffer(2)]],
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
        if (panel < 3 && u < ::PanelSize)
        {
            int element = int((uint(int(uint(py) * uint(__c.size)) / ::PanelSize) * uint(__c.size)) + uint(int(uint(u) * uint(__c.size)) / ::PanelSize));
            bool keep = source[element] >= __c.cutoff;
            if (panel == 0)
            {
                float value = float(source[element]) * (1.0f / 65535.0f);
                color = keep ? float3(1.0f, 0.62f + (0.3f * value), 0.12f) * (0.45f + (0.55f * value)) : float3(value * 0.55f);
            }
            else if (panel == 1)
            {
                uint result = shaded[element];
                if (result != 0u)
                {
                    float4 grain = metal::unpack_unorm4x8_to_float(result);
                    float value = float(source[element]) * (1.0f / 65535.0f);
                    color = (float3(0.15f, 0.55f, 0.85f) * (0.5f + (0.5f * value))) + (grain.xyz * 0.3f);
                }
                else
                {
                    color = float3(0.03f, 0.04f, 0.06f);
                }
            }
            else
            {
                bool worked = __c.compacted != 0 ? element < __c.kept : keep;
                if (worked)
                {
                    color = float3(0.3f, 0.85f, 1.0f);
                }
                else if (element < __c.dispatched)
                {
                    color = float3(0.6f, 0.16f, 0.14f);
                }
                else
                {
                    color = float3(0.03f, 0.04f, 0.06f);
                }
            }
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
