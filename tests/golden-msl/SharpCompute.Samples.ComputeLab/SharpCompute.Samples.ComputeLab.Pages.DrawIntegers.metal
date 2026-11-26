// SharpCompute.Samples.ComputeLab.Pages.DrawIntegers (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_DrawIntegers, compile options None
#include <metal_stdlib>

using namespace metal;

constant int PanelX = 32;
constant uint Background = 4279636238u;
constant int PanelPitch = 416;
constant int PanelY = 96;
constant int ChartY = 504;
constant int PanelSize = 384;
constant uint Marker = 4284140799u;
constant int HashBlock = 4;
constant int ChartHeight = 128;
constant uint ChartBackground = 4280227862u;

struct __Constants
{
    int width;
    int size;
    uint tallestBefore;
    uint tallestAfter;
    ulong largest;
};
static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 4, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, tallestBefore) == 8, "SharpCompute: offset of '__Constants.tallestBefore' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, tallestAfter) == 12, "SharpCompute: offset of '__Constants.tallestAfter' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, largest) == 16, "SharpCompute: offset of '__Constants.largest' does not match its C# layout");

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

static uint Rgb(uint r, uint g, uint b);
static uint Gray(uchar level);
static uint HashColor(ulong hash);
static uint HashPanel(int u, int v, device ulong* hashes, constant __Constants& __c);
static uint HashBits(int u, int c, device ulong* hashes, constant __Constants& __c);
static uint Chart(int panel, int u, int c, device uint* before, device uint* after, device uchar* lut, constant __Constants& __c);

static uint Rgb(uint r, uint g, uint b)
{
    return 4278190080u | (b << 16) | (g << 8) | r;
}

static uint Gray(uchar level)
{
    return ::Rgb(uint(level), uint(level), uint(level));
}

static uint HashColor(ulong hash)
{
    return ::Rgb(uint(hash >> 56), uint((hash >> 28) & 255UL), uint(hash & 255UL));
}

static uint HashPanel(int u, int v, device ulong* hashes, constant __Constants& __c)
{
    int winner = int(__c.largest & 1048575UL);
    int dx = metal::abs(int(uint(u) - uint(int(uint(winner % __c.size) * uint(::PanelSize)) / __c.size)));
    int dy = metal::abs(int(uint(v) - uint(int(uint(winner / __c.size) * uint(::PanelSize)) / __c.size)));
    if (dx <= 15 && dy <= 15 && (dx > 1 || dy > 1))
    {
        bool cross = (dx <= 1 && dy >= 5) || (dy <= 1 && dx >= 5);
        bool frame = dx >= 14 || dy >= 14;
        return cross || frame ? ::Marker : 4278190080u;
    }
    int column = int(uint(u / ::HashBlock) * uint(::HashBlock));
    int row = int(uint(v / ::HashBlock) * uint(::HashBlock));
    return ::HashColor(hashes[int((uint(int(uint(row) * uint(__c.size)) / ::PanelSize) * uint(__c.size)) + uint(int(uint(column) * uint(__c.size)) / ::PanelSize))]);
}

static uint HashBits(int u, int c, device ulong* hashes, constant __Constants& __c)
{
    ulong hash = hashes[int((uint(u / ::HashBlock) * uint(::HashBlock)) * uint(__c.size)) / ::PanelSize];
    int bit = int(63u - uint(c / 2));
    bool set = ((hash >> bit) & 1UL) != 0UL;
    if (((bit / 8) & 1) == 0)
    {
        return set ? ::Rgb(110u, 235u, 130u) : ::Rgb(22u, 50u, 30u);
    }
    return set ? ::Rgb(100u, 170u, 255u) : ::Rgb(22u, 34u, 62u);
}

static uint Chart(int panel, int u, int c, device uint* before, device uint* after, device uchar* lut, constant __Constants& __c)
{
    int bin = int(uint(u) * uint(256)) / ::PanelSize;
    uint count = panel == 0 ? before[bin] : after[bin];
    uint tallest = panel == 0 ? __c.tallestBefore : __c.tallestAfter;
    int height = int(count * 128u / tallest);
    int fromBottom = int(uint(::ChartHeight) - 1u - uint(c));
    uint color = ::ChartBackground;
    if (fromBottom < height || (count != 0u && fromBottom == 0))
    {
        color = ::Rgb(70u + uint(bin / 2), 120u + uint(bin / 3), 215u);
    }
    if (panel == 0 && metal::abs(int(uint(fromBottom) - uint(int(uint(lut[bin]) * (uint(::ChartHeight) - 1u)) / 255))) <= 1)
    {
        color = ::Marker;
    }
    return color;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_DrawIntegers(
    device uint* pixels [[buffer(0)]],
    device uchar* original [[buffer(1)]],
    device uchar* equalised [[buffer(2)]],
    device uint* before [[buffer(3)]],
    device uint* after [[buffer(4)]],
    device uchar* lut [[buffer(5)]],
    device ulong* hashes [[buffer(6)]],
    constant __Constants& __c [[buffer(7)]],
    constant __GridWindow& __window [[buffer(8), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    int px = int(uint(x) - uint(::PanelX));
    uint color = ::Background;
    if (px >= 0)
    {
        int panel = px / ::PanelPitch;
        int u = int(uint(px) - (uint(panel) * uint(::PanelPitch)));
        int v = int(uint(y) - uint(::PanelY));
        int c = int(uint(y) - uint(::ChartY));
        if (panel < 3 && u < ::PanelSize)
        {
            if (v >= 0 && v < ::PanelSize)
            {
                int i = int((uint(int(uint(v) * uint(__c.size)) / ::PanelSize) * uint(__c.size)) + uint(int(uint(u) * uint(__c.size)) / ::PanelSize));
                if (panel == 0)
                {
                    color = ::Gray(original[i]);
                }
                else if (panel == 1)
                {
                    color = ::Gray(equalised[i]);
                }
                else
                {
                    color = ::HashPanel(u, v, hashes, __c);
                }
            }
            else if (c >= 0 && c < ::ChartHeight)
            {
                color = panel == 2 ? ::HashBits(u, c, hashes, __c) : ::Chart(panel, u, c, before, after, lut, __c);
            }
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = color;
}
