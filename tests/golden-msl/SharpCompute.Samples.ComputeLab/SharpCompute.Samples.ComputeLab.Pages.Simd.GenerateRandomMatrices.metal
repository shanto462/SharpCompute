// SharpCompute.Samples.ComputeLab.Pages.Simd.GenerateRandomMatrices (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_GenerateRandomMatrices, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int n;
    uint seed;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, n) == 0, "SharpCompute: offset of '__Constants.n' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, seed) == 4, "SharpCompute: offset of '__Constants.seed' does not match its C# layout");

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
static float Signed(uint hash);

static uint ShaderMath_Hash(uint x)
{
    x ^= x >> 16;
    x *= 2146121005u;
    x ^= x >> 15;
    x *= 2221713035u;
    x ^= x >> 16;
    return x;
}

static float Signed(uint hash)
{
    return (float(hash >> 8) * (1.0f / 8388608.0f)) - 1.0f;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_GenerateRandomMatrices(
    device float* a [[buffer(0)]],
    device float* b [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int((__tid.y * uint(__c.n)) + __tid.x);
    uint h = ::ShaderMath_Hash(uint(i) + (__c.seed * 2654435769u));
    a[i] = ::Signed(h);
    b[i] = ::Signed(::ShaderMath_Hash(h ^ 2246822507u));
}
