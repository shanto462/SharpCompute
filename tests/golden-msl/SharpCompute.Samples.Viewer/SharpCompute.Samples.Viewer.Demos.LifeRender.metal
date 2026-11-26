// SharpCompute.Samples.Viewer.Demos.LifeRender (SharpCompute.Samples.Viewer): entry point SharpCompute_Samples_Viewer_Demos_LifeRender, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline uint3 __ms_sat_uint3(float3 x) { float3 y = metal::select(float3(0.0f), x, x > 0.0f); uint3 r = uint3(metal::min(y, float3(4294967040.0f))); return metal::select(r, uint3(4294967295u), x >= 4294967296.0f); }
inline uint3 __ms_sat_uint3(half3 x) { return __ms_sat_uint3(float3(x)); }

struct __Constants
{
    int gridWidth;
    int gridHeight;
    int imageWidth;
    int imageHeight;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, gridWidth) == 0, "SharpCompute: offset of '__Constants.gridWidth' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, gridHeight) == 4, "SharpCompute: offset of '__Constants.gridHeight' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, imageWidth) == 8, "SharpCompute: offset of '__Constants.imageWidth' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, imageHeight) == 12, "SharpCompute: offset of '__Constants.imageHeight' does not match its C# layout");

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

static uint PackRgba(float3 color);

static uint PackRgba(float3 color)
{
    uint3 c = __ms_sat_uint3((metal::saturate(color) * 255.0f) + float3(0.5f, 0.5f, 0.5f));
    return c.x | (c.y << 8) | (c.z << 16) | (255u << 24);
}

kernel void SharpCompute_Samples_Viewer_Demos_LifeRender(
    device uint* cells [[buffer(0)]],
    device uint* pixels [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int px = int(__tid.x);
    int py = int(__tid.y);
    float fx = (float(px) + 0.5f) * float(__c.gridWidth) / float(__c.imageWidth);
    float fy = (float(py) + 0.5f) * float(__c.gridHeight) / float(__c.imageHeight);
    int cx = metal::min(__ms_sat_int(fx), int(uint(__c.gridWidth) - 1u));
    int cy = metal::min(__ms_sat_int(fy), int(uint(__c.gridHeight) - 1u));
    uint age = cells[int((uint(cy) * uint(__c.gridWidth)) + uint(cx))];
    float3 color = float3(0.035f, 0.04f, 0.07f);
    if (age != 0u)
    {
        float t = metal::saturate(float(age - 1u) / 48.0f);
        color = metal::mix(float3(0.65f, 1.0f, 0.78f), float3(0.12f, 0.3f, 0.85f), metal::sqrt(t));
    }
    float cellWidth = float(__c.imageWidth) / float(__c.gridWidth);
    float cellHeight = float(__c.imageHeight) / float(__c.gridHeight);
    if (cellWidth >= 4.0f && cellHeight >= 4.0f)
    {
        float localX = (fx - float(cx)) * cellWidth;
        float localY = (fy - float(cy)) * cellHeight;
        if (localX < 1.0f || localY < 1.0f)
        {
            color *= 0.6f;
        }
    }
    pixels[int((uint(py) * uint(__c.imageWidth)) + uint(px))] = ::PackRgba(color);
}
