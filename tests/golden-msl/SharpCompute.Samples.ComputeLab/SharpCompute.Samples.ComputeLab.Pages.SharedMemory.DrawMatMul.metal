// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.DrawMatMul (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_DrawMatMul, compile options None
#include <metal_stdlib>

using namespace metal;

constant int PanelY = 104;
constant int FirstPanelX = 32;
constant int PanelSize = 384;
constant int PanelPitch = 416;

struct __Constants
{
    int width;
    int n;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, n) == 4, "SharpCompute: offset of '__Constants.n' does not match its C# layout");

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

static float3 Heat(float value);

static float3 Heat(float value)
{
    float t = metal::saturate(metal::abs(value));
    float3 hue = value < 0.0f ? float3(0.2f, 0.5f, 1.0f) : float3(1.0f, 0.78f, 0.3f);
    return metal::mix(float3(0.03f, 0.04f, 0.06f), hue, metal::sqrt(t));
}

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_DrawMatMul(
    device uint* pixels [[buffer(0)]],
    device float* a [[buffer(1)]],
    device float* b [[buffer(2)]],
    device float* c [[buffer(3)]],
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
            int element = int((uint(metal::min(int(uint(__c.n) - 1u), int(uint(py) * uint(__c.n)) / ::PanelSize)) * uint(__c.n)) + uint(metal::min(int(uint(__c.n) - 1u), int(uint(u) * uint(__c.n)) / ::PanelSize)));
            if (panel == 0)
            {
                color = ::Heat(a[element]);
            }
            else if (panel == 1)
            {
                color = ::Heat(b[element]);
            }
            else
            {
                color = ::Heat(c[element] * (16.0f / float(__c.n)));
            }
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
