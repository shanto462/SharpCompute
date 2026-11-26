// SharpCompute.Samples.ComputeLab.Pages.SaltAndPepper (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SaltAndPepper, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int size;
    uint seed;
    float density;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 0, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, seed) == 4, "SharpCompute: offset of '__Constants.seed' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, density) == 8, "SharpCompute: offset of '__Constants.density' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_SaltAndPepper(
    device uint* clean [[buffer(0)]],
    device uint* noisy [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int((__tid.y * uint(__c.size)) + __tid.x);
    uint h = ::Hash(uint(i) ^ (__c.seed * 2654435769u));
    bool hit = (h & 16777215u) < uint(metal::max(__c.density * 16777216.0f, 0.0f));
    noisy[i] = hit ? ((h >> 31) == 0u ? 4278190080u : 4294967295u) : clean[i];
}
