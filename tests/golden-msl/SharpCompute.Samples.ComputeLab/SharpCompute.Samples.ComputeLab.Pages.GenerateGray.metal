// SharpCompute.Samples.ComputeLab.Pages.GenerateGray (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_GenerateGray, compile options None
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

kernel void SharpCompute_Samples_ComputeLab_Pages_GenerateGray(
    device uchar* image [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float2 uv = float2(float(x), float(y)) / float(__c.size);
    float2 light = float2(0.5f + (0.22f * metal::cos(__c.time * 0.4f)), 0.46f + (0.2f * metal::sin(__c.time * 0.31f)));
    float distance = metal::length(uv - light);
    float bands = 0.5f + (0.5f * metal::sin(6.2831855f * ((uv.x * 1.4f) + (0.3f * metal::sin((uv.y * 5.0f) + (__c.time * 0.3f))))));
    float rings = 0.5f + (0.5f * metal::cos(46.0f * distance));
    float glow = metal::smoothstep(0.55f, 0.0f, distance);
    float noise = float(::Hash(uint(x) + (::Hash(uint(y)) * 31u)) >> 8) * (1.0f / 16777216.0f);
    float value = (0.4f * bands) + (0.25f * rings * glow) + (0.35f * glow * glow);
    float gray = 0.29f + (0.34f * value) + ((noise - 0.5f) * 0.012f);
    image[int((uint(y) * uint(__c.size)) + uint(x))] = uchar(gray * 255.0f);
}
