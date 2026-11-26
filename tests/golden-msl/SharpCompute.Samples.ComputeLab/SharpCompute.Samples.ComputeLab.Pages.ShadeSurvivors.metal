// SharpCompute.Samples.ComputeLab.Pages.ShadeSurvivors (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_ShadeSurvivors, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Rounds = 256;

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
    for (int round = 0; round < ::Rounds; round = int(uint(round) + 1u))
    {
        h ^= h >> 15;
        h *= 739982445u;
        h ^= h >> 12;
        h *= 695872825u;
        h ^= h >> 15;
    }
    return h | 1u;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_ShadeSurvivors(
    device uint* survivors [[buffer(0)]],
    device int* counter [[buffer(1)]],
    device uint* source [[buffer(2)]],
    device uint* shaded [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int slot = int(__tid.x);
    if (slot < counter[0])
    {
        uint index = survivors[slot];
        shaded[int(index)] = ::Shade(source[int(index)], index);
    }
}
