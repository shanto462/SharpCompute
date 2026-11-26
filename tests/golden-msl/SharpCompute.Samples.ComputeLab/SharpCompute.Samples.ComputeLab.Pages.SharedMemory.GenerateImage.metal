// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.GenerateImage (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_GenerateImage, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int size;
    float time;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 0, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 4, "SharpCompute: offset of '__Constants.time' does not match its C# layout");

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

static float Grain(uint x, uint y);

static float Grain(uint x, uint y)
{
    uint h = x + (y * 2654435769u);
    h ^= h >> 16;
    h *= 2146121005u;
    h ^= h >> 15;
    h *= 2221713035u;
    h ^= h >> 16;
    return float(h >> 8) * (1.0f / 16777216.0f);
}

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_GenerateImage(
    device float* image [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float2 uv = float2(float(x), float(y)) / float(__c.size);
    float value = ((int(uint(x >> 6) + uint(y >> 6))) & 1) == 0 ? 0.16f : 0.3f;
    float2 a = float2(0.5f + (0.28f * metal::cos(__c.time * 0.5f)), 0.5f + (0.28f * metal::sin(__c.time * 0.5f)));
    float2 b = float2(0.5f + (0.33f * metal::cos((__c.time * 0.31f) + 2.1f)), 0.5f + (0.2f * metal::sin((__c.time * 0.43f) + 1.0f)));
    float2 c = float2(0.3f + (0.15f * metal::sin(__c.time * 0.27f)), 0.72f + (0.12f * metal::cos(__c.time * 0.37f)));
    if (metal::length(uv - a) < 0.11f || metal::length(uv - b) < 0.07f || metal::length(uv - c) < 0.05f)
    {
        value = 1.0f;
    }
    float ring = metal::abs(metal::length(uv - float2(0.5f, 0.5f)) - 0.42f);
    if (ring < 0.006f || ((x & 127) < 3 && uv.y > 0.86f))
    {
        value = 0.9f;
    }
    image[int((uint(y) * uint(__c.size)) + uint(x))] = metal::saturate(value + ((::Grain(uint(x), uint(y)) - 0.5f) * 0.08f));
}
