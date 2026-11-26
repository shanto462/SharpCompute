// SharpCompute.Samples.ComputeLab.Pages.Simd.DrawCompaction (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_DrawCompaction, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline uint __ms_sat_uint(float x) { float y = x > 0.0f ? x : 0.0f; uint r = uint(metal::min(y, 4294967040.0f)); return x >= 4294967296.0f ? 4294967295u : r; }
inline uint __ms_sat_uint(half x) { return __ms_sat_uint(float(x)); }

constant int FieldX = 32;
constant int FieldY = 64;
constant int StripX = 60;
constant int StripY = 592;
constant int CellSize = 15;
constant int FieldSize = 480;
constant int StripPitch = 36;
constant int StripHeight = 32;
constant int StripCell = 32;

struct __Constants
{
    int width;
    float centerX;
    float centerY;
    float radius;
    int shownGroup;
    uint ballot;
};
static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, centerX) == 4, "SharpCompute: offset of '__Constants.centerX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, centerY) == 8, "SharpCompute: offset of '__Constants.centerY' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radius) == 12, "SharpCompute: offset of '__Constants.radius' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, shownGroup) == 16, "SharpCompute: offset of '__Constants.shownGroup' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, ballot) == 20, "SharpCompute: offset of '__Constants.ballot' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_DrawCompaction(
    device uint* pixels [[buffer(0)]],
    device uint* field [[buffer(1)]],
    device int* groupCounts [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 color = float3(0.055f, 0.065f, 0.085f);
    float3 gold = float3(1.0f, 0.8f, 0.35f);
    int fx = int(uint(x) - uint(::FieldX));
    int fy = int(uint(y) - uint(::FieldY));
    int sx = int(uint(x) - uint(::StripX));
    int sy = int(uint(y) - uint(::StripY));
    int shownX = int(uint(__c.shownGroup % 32) * uint(::CellSize));
    int shownY = int(uint(__c.shownGroup / 32) * uint(::CellSize));
    bool nearShown = fx >= int(uint(shownX) - 2u) && fx < int(uint(shownX) + uint(::CellSize) + 2u) && fy >= int(uint(shownY) - 2u) && fy < int(uint(shownY) + uint(::CellSize) + 2u);
    bool inShown = fx >= shownX && fx < int(uint(shownX) + uint(::CellSize)) && fy >= shownY && fy < int(uint(shownY) + uint(::CellSize));
    if (fx >= 0 && fx < ::FieldSize && fy >= 0 && fy < ::FieldSize)
    {
        int group = int((uint(fy / ::CellSize) * uint(32)) + uint(fx / ::CellSize));
        uint mark = field[int((uint(fy) * uint(::FieldSize)) + uint(fx))];
        float distance = metal::length(float2(float(fx) + 0.5f - __c.centerX, float(fy) + 0.5f - __c.centerY));
        color = metal::mix(float3(0.025f, 0.035f, 0.06f), float3(0.11f, 0.17f, 0.36f), float(groupCounts[group]) / 32.0f);
        if (mark == 1u)
        {
            color = float3(0.3f, 0.36f, 0.5f);
        }
        if (metal::abs(distance - __c.radius) < 1.0f)
        {
            color = float3(0.45f, 0.85f, 1.0f);
        }
        if (mark == 2u)
        {
            color = gold;
        }
    }
    if (nearShown && !inShown)
    {
        color = float3(1.0f, 1.0f, 1.0f);
    }
    if (sx >= 0 && sx < int(uint(32) * uint(::StripPitch)) && sy >= 0 && sy < ::StripHeight)
    {
        int lane = sx / ::StripPitch;
        int u = sx % ::StripPitch;
        if (u < ::StripCell)
        {
            bool kept = ((__c.ballot >> lane) & 1u) != 0u;
            bool edge = u == 0 || u == int(uint(::StripCell) - 1u) || sy == 0 || sy == int(uint(::StripHeight) - 1u);
            if (kept)
            {
                color = gold * (0.8f + (0.2f * (1.0f - metal::abs((float(sy) - 15.5f) / 16.0f))));
            }
            else
            {
                color = edge ? float3(0.3f, 0.36f, 0.5f) : float3(0.03f, 0.04f, 0.06f);
            }
        }
    }
    pixels[int((uint(y) * uint(__c.width)) + uint(x))] = ::Pack(color);
}
