// SharpCompute.Samples.ComputeLab.Pages.ShadeInPlace (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_ShadeInPlace, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    uint cutoff;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, cutoff) == 0, "SharpCompute: offset of '__Constants.cutoff' does not match its C# layout");

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

static uint Shade(uint value, uint index);

static uint Shade(uint value, uint index)
{
    uint h = value ^ (index * 2654435769u);
    for (int round = 0; round < 256; round = int(uint(round) + 1u))
    {
        h ^= h >> 15;
        h *= 739982445u;
        h ^= h >> 12;
        h *= 695872825u;
        h ^= h >> 15;
    }
    return h | 1u;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_ShadeInPlace(
    device uint* source [[buffer(0)]],
    device uint* shaded [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    if (source[i] >= __c.cutoff)
    {
        shaded[i] = ::Shade(source[i], uint(i));
    }
}
