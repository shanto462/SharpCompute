// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.GenerateMatrices (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_GenerateMatrices, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int n;
    float time;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, n) == 0, "SharpCompute: offset of '__Constants.n' does not match its C# layout");
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
    return (float(h >> 8) * (1.0f / 8388608.0f)) - 1.0f;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_GenerateMatrices(
    device float* a [[buffer(0)]],
    device float* b [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int column = int(__tid.x);
    int row = int(__tid.y);
    float2 uv = float2(float(column), float(row)) / float(__c.n);
    int i = int((uint(row) * uint(__c.n)) + uint(column));
    float bands = metal::cos(((uv.x - uv.y) * 9.0f) + (__c.time * 0.7f)) * metal::exp(-6.0f * metal::abs(uv.x - uv.y));
    float2 center = float2(0.5f + (0.25f * metal::cos(__c.time * 0.5f)), 0.5f + (0.25f * metal::sin(__c.time * 0.4f)));
    float rings = metal::sin((metal::length(uv - center) * 24.0f) - __c.time);
    a[i] = metal::clamp((0.85f * bands) + (0.15f * ::Grain(uint(column), uint(row))), -1.0f, 1.0f);
    b[i] = metal::clamp((0.8f * rings) + (0.2f * ::Grain(uint(row) + 7919u, uint(column))), -1.0f, 1.0f);
}
