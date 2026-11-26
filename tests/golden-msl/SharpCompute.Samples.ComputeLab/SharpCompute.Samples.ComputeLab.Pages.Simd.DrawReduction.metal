// SharpCompute.Samples.ComputeLab.Pages.Simd.DrawReduction (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_DrawReduction, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline uint __ms_sat_uint(float x) { float y = x > 0.0f ? x : 0.0f; uint r = uint(metal::min(y, 4294967040.0f)); return x >= 4294967296.0f ? 4294967295u : r; }
inline uint __ms_sat_uint(half x) { return __ms_sat_uint(float(x)); }

constant int FirstRow = 6;
constant int RowsPerMethod = 7;
constant int Bars = 7;
constant int BarX = 168;
constant int BarWidth = 1080;

struct __Constants
{
    int width;
    int active;
    float time;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, active) == 4, "SharpCompute: offset of '__Constants.active' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 8, "SharpCompute: offset of '__Constants.time' does not match its C# layout");

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

static uint Pack(float3 color);

static uint Pack(float3 color)
{
    float3 c = metal::round(metal::saturate(color) * 255.0f);
    return __ms_sat_uint(c.x) | (__ms_sat_uint(c.y) << 8) | (__ms_sat_uint(c.z) << 16) | 4278190080u;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_DrawReduction(
    device uint* pixels [[buffer(0)]],
    device float* bars [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    int ty = int(uint(y) - uint(16));
    int row = int(uint(ty / 16) - uint(::FirstRow));
    if (ty >= 0 && row >= 0)
    {
        int block = row / ::RowsPerMethod;
        int line = row % ::RowsPerMethod;
        if (block == __c.active && line < 6 && x >= 8 && x < 12)
        {
            color = float3(1.0f, 0.8f, 0.35f);
        }
        int bar = int((uint(block) * 2u) + uint((int(uint(line) - 2u)) / 2));
        if (line >= 2 && line < 6 && bar < ::Bars && x >= ::BarX && x < int(uint(::BarX) + uint(::BarWidth)))
        {
            int v = int(uint(ty) - ((uint(::FirstRow) + (uint(block) * uint(::RowsPerMethod)) + 2u + (uint((int(uint(line) - 2u)) / 2) * 2u)) * uint(16)));
            float length = bars[bar];
            if (length >= 0.0f && v >= 3 && v < 29)
            {
                int u = int(uint(x) - uint(::BarX));
                int filled = metal::max(3, __ms_sat_int(metal::saturate(length) * 1080.0f));
                if (u < filled)
                {
                    bool isError = (bar & 1) == 1;
                    float3 fill = float3(0.3f, 0.62f, 0.9f);
                    if (isError)
                    {
                        fill = float3(0.95f, 0.42f, 0.36f);
                    }
                    else if (block == __c.active)
                    {
                        fill = float3(1.0f, 0.8f, 0.35f);
                    }
                    else if (bar == int(uint(::Bars) - 1u))
                    {
                        fill = float3(0.62f, 0.66f, 0.72f);
                    }
                    float gloss = 0.78f + (0.22f * (1.0f - metal::abs((float(v) - 16.0f) / 13.0f)));
                    float ripple = isError ? 1.0f : 0.93f + (0.07f * metal::sin((float(u) * 0.05f) - (__c.time * 3.0f)));
                    color = fill * gloss * ripple;
                }
                else
                {
                    color = u % (::BarWidth / 10) == 0 ? float3(0.09f, 0.1f, 0.13f) : float3(0.03f, 0.04f, 0.06f);
                }
            }
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = ::Pack(color);
}
