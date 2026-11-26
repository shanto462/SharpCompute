// SharpCompute.Samples.ComputeLab.Pages.DrawPrivatisedHistogram (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_DrawPrivatisedHistogram, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return x | int(metal::reverse_bits(x) == int(0)); }

constant int PanelY = 104;
constant int PanelSize = 512;
constant int ImageX = 48;
constant int ChartX = 624;
constant int LowerChartY = 392;
constant int ChartHeight = 224;
constant int ChartWidth = 608;

struct __Constants
{
    int width;
    int size;
    int bins;
    int max;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 4, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, bins) == 8, "SharpCompute: offset of '__Constants.bins' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, max) == 12, "SharpCompute: offset of '__Constants.max' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_DrawPrivatisedHistogram(
    device uint* pixels [[buffer(0)]],
    device uint* image [[buffer(1)]],
    device int* globalCounts [[buffer(2)]],
    device int* counts [[buffer(3)]],
    constant __Constants& __c [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    int py = int(uint(y) - uint(::PanelY));
    if (py >= 0 && py < ::PanelSize)
    {
        int ix = int(uint(x) - uint(::ImageX));
        int cx = int(uint(x) - uint(::ChartX));
        bool lower = y >= ::LowerChartY;
        int fromBottom = lower ? int(uint(::PanelY) + uint(::PanelSize) - uint(y)) : int(uint(::PanelY) + uint(::ChartHeight) - uint(y));
        if (ix >= 0 && ix < ::PanelSize)
        {
            color = metal::unpack_unorm4x8_to_float(image[int((uint(py) * 2u * uint(__c.size)) + (uint(ix) * 2u))]).xyz;
        }
        else if (cx >= 0 && cx < ::ChartWidth && fromBottom > 0 && fromBottom <= ::ChartHeight)
        {
            int scaled = int(uint(cx) * uint(__c.bins));
            int bin = scaled / ::ChartWidth;
            int inside = (scaled % ::ChartWidth) / __ms_nonzero(__c.bins);
            int count = lower ? counts[bin] : globalCounts[bin];
            float height = float(count) * 224.0f / float(__c.max);
            float level = (float(bin) + 0.5f) / float(__c.bins);
            color = float3(0.085f, 0.095f, 0.12f);
            if (float(fromBottom) <= height && (__c.bins > 64 || inside >= 6))
            {
                color = lower ? metal::mix(float3(0.2f, 0.45f, 0.95f), float3(0.45f, 0.95f, 0.8f), level) : metal::mix(float3(0.95f, 0.4f, 0.2f), float3(1.0f, 0.85f, 0.4f), level);
                if (counts[bin] != globalCounts[bin])
                {
                    color = float3(1.0f, 0.1f, 0.1f);
                }
            }
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
