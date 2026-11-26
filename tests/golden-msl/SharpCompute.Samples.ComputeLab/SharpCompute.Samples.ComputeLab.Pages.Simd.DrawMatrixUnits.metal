// SharpCompute.Samples.ComputeLab.Pages.Simd.DrawMatrixUnits (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_DrawMatrixUnits, compile options None
#include <metal_stdlib>

using namespace metal;

constant int PanelX = 32;
constant int PanelY = 128;
constant int PanelSize = 384;
constant int Zoom = 4;
constant int BarX = 456;
constant int FirstBarY = 164;
constant int BarWidth = 792;
constant int BarPitch = 64;
constant int BarHeight = 24;

struct __Constants
{
    int width;
    int n;
    int active;
    float scale;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, n) == 4, "SharpCompute: offset of '__Constants.n' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, active) == 8, "SharpCompute: offset of '__Constants.active' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, scale) == 12, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_DrawMatrixUnits(
    device uint* pixels [[buffer(0)]],
    device float* c [[buffer(1)]],
    device float* bars [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    int px = int(uint(x) - uint(::PanelX));
    int py = int(uint(y) - uint(::PanelY));
    if (px >= 0 && px < ::PanelSize && py >= 0 && py < ::PanelSize)
    {
        int column = px / ::Zoom;
        int row = py / ::Zoom;
        color = ::Heat(c[int((uint(row) * uint(__c.n)) + uint(column))] * __c.scale);
        bool onColumn = px % ::Zoom == 0;
        bool onRow = py % ::Zoom == 0;
        if ((onColumn && column % 8 == 0) || (onRow && row % 8 == 0))
        {
            color = metal::mix(color, float3(0.02f, 0.03f, 0.05f), 0.75f);
        }
        if ((onColumn && column % 32 == 0) || (onRow && row % 16 == 0))
        {
            color = float3(0.3f, 0.55f, 0.95f);
        }
        if ((onColumn && column % 64 == 0) || (onRow && row % 32 == 0))
        {
            color = float3(1.0f, 0.8f, 0.35f);
        }
    }
    int bx = int(uint(x) - uint(::BarX));
    int by = int(uint(y) - uint(::FirstBarY));
    if (bx >= 0 && bx < ::BarWidth && by >= 0 && by < int(3u * uint(::BarPitch)) && by % ::BarPitch < ::BarHeight)
    {
        int __ms_kernel = by / ::BarPitch;
        float3 fill = __ms_kernel == __c.active ? float3(1.0f, 0.8f, 0.35f) : float3(0.3f, 0.55f, 0.95f);
        color = float(bx) < bars[__ms_kernel] * 792.0f ? fill : float3(0.1f, 0.12f, 0.16f);
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
