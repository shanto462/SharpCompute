// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.GenerateValues (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_GenerateValues, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int side;
    float time;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, side) == 0, "SharpCompute: offset of '__Constants.side' does not match its C# layout");
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

static uint Hash(uint x);

static uint Hash(uint x)
{
    x ^= x >> 16;
    x *= 2146121005u;
    x ^= x >> 15;
    x *= 2221713035u;
    x ^= x >> 16;
    return x;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_GenerateValues(
    device float* values [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float2 uv = float2(float(x), float(y)) / float(__c.side);
    float2 c1 = float2(0.5f + (0.3f * metal::cos(__c.time * 0.37f)), 0.5f + (0.3f * metal::sin(__c.time * 0.29f)));
    float2 c2 = float2(0.5f - (0.35f * metal::sin(__c.time * 0.23f)), 0.5f + (0.25f * metal::cos(__c.time * 0.41f)));
    float waves = metal::sin((metal::length(uv - c1) * 31.0f) - (__c.time * 1.3f)) + metal::sin((metal::length(uv - c2) * 19.0f) + (__c.time * 0.9f));
    float grain = float(::Hash(uint(x) + (::Hash(uint(y)) * 31u)) >> 8) * (1.0f / 16777216.0f);
    values[int((uint(y) * uint(__c.side)) + uint(x))] = metal::saturate(0.5f + (0.21f * waves) + ((grain - 0.5f) * 0.16f));
}
