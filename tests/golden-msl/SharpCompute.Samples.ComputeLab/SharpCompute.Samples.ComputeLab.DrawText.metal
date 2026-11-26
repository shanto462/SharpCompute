// SharpCompute.Samples.ComputeLab.DrawText (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_DrawText, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int width;
    int columns;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, columns) == 4, "SharpCompute: offset of '__Constants.columns' does not match its C# layout");

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

static float3 Color(uint index);

static float3 Color(uint index)
{
    if (index == 1u)
    {
        return float3(0.62f, 0.66f, 0.72f);
    }
    if (index == 2u)
    {
        return float3(0.45f, 0.95f, 0.55f);
    }
    if (index == 3u)
    {
        return float3(1.0f, 0.4f, 0.35f);
    }
    if (index == 4u)
    {
        return float3(1.0f, 0.8f, 0.35f);
    }
    if (index == 5u)
    {
        return float3(0.45f, 0.85f, 1.0f);
    }
    return float3(1.0f, 1.0f, 1.0f);
}

kernel void SharpCompute_Samples_ComputeLab_DrawText(
    device uint* pixels [[buffer(0)]],
    device uint* cells [[buffer(1)]],
    const device uint* glyphs [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    int ty = int(uint(y) - uint(16));
    if (ty < 0 || ty >= int(uint(43) * uint(16)) || x >= int(uint(__c.columns) * uint(12)))
    {
        return;
    }
    uint cell = cells[int((uint(ty / 16) * uint(__c.columns)) + uint(x / 12))];
    if (cell != 0u)
    {
        int i = int((uint(y) * uint(__c.width)) + uint(x));
        int gx = (x % 12) / 2;
        int gy = (ty % 16) / 2;
        bool lit = false;
        if (gx < 5 && gy < 7)
        {
            int bit = int((uint(gy) * uint(5)) + uint(gx));
            uint word = glyphs[int(((cell & 255u) * 2u) + uint(bit >> 5))];
            lit = ((word >> (bit & 31)) & 1u) != 0u;
        }
        if (lit)
        {
            pixels[i] = metal::pack_float_to_unorm4x8(float4(::Color((cell >> 8) & 15u), 1.0f));
        }
        else
        {
            float4 under = metal::unpack_unorm4x8_to_float(pixels[i]);
            pixels[i] = metal::pack_float_to_unorm4x8(float4(under.xyz * 0.22f, 1.0f));
        }
    }
}
