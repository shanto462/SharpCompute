// SharpCompute.Samples.ComputeLab.Pages.Simd.DrawShuffle (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_DrawShuffle, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline uint __ms_sat_uint(float x) { float y = x > 0.0f ? x : 0.0f; uint r = uint(metal::min(y, 4294967040.0f)); return x >= 4294967296.0f ? 4294967295u : r; }
inline uint __ms_sat_uint(half x) { return __ms_sat_uint(float(x)); }

constant int PlotX = 40;
constant int PlotY = 132;
constant int PlotWidth = 1200;
constant int PlotHeight = 380;
constant int MeansCurve = 3;
constant int GroupsCurve = 2;
constant int GlobalCurve = 1;
constant int InputCurve = 0;
constant int LegendRow = 32;

struct __Constants
{
    int width;
    int count;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 4, "SharpCompute: offset of '__Constants.count' does not match its C# layout");

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

static float ToY(float value);
static float3 CurveColor(int curve);
static float Value(int curve, float position, device float* plot, constant __Constants& __c);
static uint Pack(float3 color);

static float ToY(float value)
{
    return 132.0f + (380.0f * (1.0f - value));
}

static float3 CurveColor(int curve)
{
    if (curve == ::GroupsCurve)
    {
        return float3(1.0f, 0.8f, 0.35f);
    }
    if (curve == ::GlobalCurve)
    {
        return float3(0.45f, 0.85f, 1.0f);
    }
    if (curve == ::MeansCurve)
    {
        return float3(0.8f, 0.84f, 0.9f);
    }
    return float3(0.42f, 0.47f, 0.56f);
}

static float Value(int curve, float position, device float* plot, constant __Constants& __c)
{
    float p = metal::clamp(position, 0.0f, float(__c.count) - 1.0f);
    int i = __ms_sat_int(p);
    int j = metal::min(int(uint(i) + 1u), int(uint(__c.count) - 1u));
    return metal::mix(plot[int((uint(curve) * uint(__c.count)) + uint(i))], plot[int((uint(curve) * uint(__c.count)) + uint(j))], p - float(i));
}

static uint Pack(float3 color)
{
    float3 c = metal::round(metal::saturate(color) * 255.0f);
    return __ms_sat_uint(c.x) | (__ms_sat_uint(c.y) << 8) | (__ms_sat_uint(c.z) << 16) | 4278190080u;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_DrawShuffle(
    device uint* pixels [[buffer(0)]],
    device float* plot [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    int px = int(uint(x) - uint(::PlotX));
    int py = int(uint(y) - uint(::PlotY));
    if (px >= 0 && px < ::PlotWidth && py >= 0 && py < ::PlotHeight)
    {
        int sample = int(uint(px) * uint(__c.count)) / ::PlotWidth;
        int before = px == 0 ? 0 : int((uint(px) - 1u) * uint(__c.count)) / ::PlotWidth;
        bool groupEdge = sample / 32 != before / 32;
        bool threadGroupEdge = px == 0 || sample / 256 != before / 256;
        color = ((sample / 32) & 1) == 0 ? float3(0.028f, 0.036f, 0.055f) : float3(0.04f, 0.05f, 0.072f);
        if (threadGroupEdge)
        {
            color = float3(0.4f, 0.45f, 0.55f);
        }
        else if (groupEdge)
        {
            color = float3(0.13f, 0.155f, 0.2f);
        }
        float mean = ::ToY(plot[int((uint(::MeansCurve) * uint(__c.count)) + uint(sample))]);
        if (metal::abs(float(y) + 0.5f - mean) < 1.0f && (px / 3) % 2 == 0)
        {
            color = ::CurveColor(::MeansCurve);
        }
        float samplesPerPixel = float(__c.count) / 1200.0f;
        float from = (float(px) * samplesPerPixel) - 0.5f;
        float to = (float(int(uint(px) + 1u)) * samplesPerPixel) - 0.5f;
        for (int curve = ::InputCurve; curve <= ::GroupsCurve; curve = int(uint(curve) + 1u))
        {
            float y0 = ::ToY(::Value(curve, from, plot, __c));
            float y1 = ::ToY(::Value(curve, to, plot, __c));
            float __ms_half = curve == ::InputCurve ? 0.8f : 1.2f;
            if (float(y) + 0.5f >= metal::min(y0, y1) - __ms_half && float(y) + 0.5f <= metal::max(y0, y1) + __ms_half)
            {
                color = ::CurveColor(curve);
            }
        }
    }
    int legendY = int(uint(y) - (uint(16) + (uint(::LegendRow) * uint(16))));
    int line = legendY / 16;
    int v = legendY % 16;
    if (legendY >= 0 && line < 3 && v >= 6 && v < 9)
    {
        if (x >= 24 && x < 72)
        {
            color = ::CurveColor(line == 0 ? ::GroupsCurve : line == 1 ? ::GlobalCurve : ::InputCurve);
        }
        else if (line == 2 && x >= 264 && x < 312 && ((int(uint(x) - 264u)) / 3) % 2 == 0)
        {
            color = ::CurveColor(::MeansCurve);
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = ::Pack(color);
}
