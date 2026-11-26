// SharpCompute.Samples.ComputeLab.Pages.GenerateImage (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_GenerateImage, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

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

kernel void SharpCompute_Samples_ComputeLab_Pages_GenerateImage(
    device uint* image [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float2 uv = float2(float(x), float(y)) / float(__c.size);
    float a = 0.5f + (0.5f * metal::sin(6.2831855f * ((uv.x * 1.5f) + (uv.y * 0.4f) + (__c.time * 0.11f))));
    float b = 0.5f + (0.5f * metal::sin(6.2831855f * ((uv.y * 2.2f) - (uv.x * 0.7f) - (__c.time * 0.07f))));
    float2 center = float2(0.5f + (0.3f * metal::cos(__c.time * 0.5f)), 0.5f + (0.3f * metal::sin(__c.time * 0.37f)));
    float glow = metal::smoothstep(0.42f, 0.0f, metal::length(uv - center));
    uint tick = uint(__ms_sat_int(__c.time * 10.0f));
    float noise = float(::ShaderMath_Hash(uint(x) + (::ShaderMath_Hash(uint(y) + (tick * 7919u)) * 31u)) >> 8) * (1.0f / 16777216.0f);
    float3 color = (float3(0.78f, 0.3f, 0.12f) * a) + (float3(0.08f, 0.3f, 0.7f) * b * b);
    color = float3(0.12f) + (metal::mix(color, float3(0.84f, 0.8f, 0.68f), glow * glow) * 0.92f) + ((noise - 0.5f) * 0.12f);
    image[int((uint(y) * uint(__c.size)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(color, 1.0f));
}
