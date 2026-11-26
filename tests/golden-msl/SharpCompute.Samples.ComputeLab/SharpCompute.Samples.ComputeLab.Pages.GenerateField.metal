// SharpCompute.Samples.ComputeLab.Pages.GenerateField (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_GenerateField, compile options None
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

static uint ShaderMath_Hash(uint x);

static uint ShaderMath_Hash(uint x)
{
    x ^= x >> 16;
    x *= 2146121005u;
    x ^= x >> 15;
    x *= 2221713035u;
    x ^= x >> 16;
    return x;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_GenerateField(
    device uint* source [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float2 uv = float2(float(x), float(y)) / float(__c.size);
    float2 c1 = float2(0.5f + (0.35f * metal::cos(__c.time * 0.31f)), 0.5f + (0.35f * metal::sin(__c.time * 0.43f)));
    float2 c2 = float2(0.5f - (0.3f * metal::sin(__c.time * 0.27f)), 0.5f + (0.3f * metal::cos(__c.time * 0.19f)));
    float waves = metal::sin((metal::length(uv - c1) * 38.0f) - (__c.time * 1.7f)) + metal::sin((metal::length(uv - c2) * 27.0f) + (__c.time * 1.1f));
    float grain = float(::ShaderMath_Hash(uint(x) + (::ShaderMath_Hash(uint(y)) * 31u)) >> 8) * (1.0f / 16777216.0f);
    float value = metal::saturate(0.5f + (0.22f * waves) + ((grain - 0.5f) * 0.18f));
    source[int((uint(y) * uint(__c.size)) + uint(x))] = uint(metal::max(value * 65535.0f, 0.0f));
}
