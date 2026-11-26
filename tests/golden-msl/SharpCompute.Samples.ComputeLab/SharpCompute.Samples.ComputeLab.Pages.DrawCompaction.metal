// SharpCompute.Samples.ComputeLab.Pages.DrawCompaction (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_DrawCompaction, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return __builtin_constant_p(x) ? x : x | int(metal::reverse_bits(x) == int(0)); }

constant int PanelY = 104;
constant int FirstPanelX = 32;
constant int PanelSize = 384;
constant int PanelPitch = 416;

struct __Constants
{
    int width;
    int size;
    int kept;
    uint cutoff;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 4, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, kept) == 8, "SharpCompute: offset of '__Constants.kept' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, cutoff) == 12, "SharpCompute: offset of '__Constants.cutoff' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_DrawCompaction(
    device uint* pixels [[buffer(0)]],
    device uint* source [[buffer(1)]],
    device uint* marks [[buffer(2)]],
    device uint* order [[buffer(3)]],
    constant __Constants& __c [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
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
            int element = int((uint(int(uint(py) * 8u) / 3) * uint(__c.size)) + uint(int(uint(u) * 8u) / 3));
            bool keep = source[element] >= __c.cutoff;
            if (panel == 0)
            {
                float value = float(source[element]) * (1.0f / 65535.0f);
                color = keep ? float3(1.0f, 0.62f + (0.3f * value), 0.12f) * (0.45f + (0.55f * value)) : float3(value * 0.55f);
            }
            else if (panel == 1)
            {
                bool marked = marks[element] != 0u;
                color = marked == keep ? (marked ? float3(0.3f, 0.85f, 1.0f) : float3(0.03f, 0.04f, 0.06f)) : float3(1.0f, 0.1f, 0.1f);
            }
            else
            {
                int stride = metal::max(1, __c.kept / (int(uint(::PanelSize) * uint(::PanelSize))));
                int slot = int(((uint(py) * uint(::PanelSize)) + uint(u)) * uint(stride));
                if (slot < __c.kept)
                {
                    int index = int(order[slot]);
                    float sx = float(index % __ms_nonzero(__c.size)) / float(__c.size);
                    float sy = float(index / __ms_nonzero(__c.size)) / float(__c.size);
                    color = float3(0.15f + (0.85f * sy), 0.25f + (0.5f * sx), 1.0f - (0.8f * sy));
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
