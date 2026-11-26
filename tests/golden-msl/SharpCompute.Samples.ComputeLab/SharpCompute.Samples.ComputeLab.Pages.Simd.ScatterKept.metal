// SharpCompute.Samples.ComputeLab.Pages.Simd.ScatterKept (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_ScatterKept, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_int2) == 8, "SharpCompute: size of 'packed_int2' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_ScatterKept(
    const device packed_int2* particles [[buffer(0)]],
    device uint* ballots [[buffer(1)]],
    device int* groupOffsets [[buffer(2)]],
    device int* blockOffsets [[buffer(3)]],
    device packed_int2* compacted [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint __lane [[thread_index_in_simdgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int group = i / 32;
    bool keep = ((ballots[group] >> int(__lane)) & 1u) != 0u;
    int keptBefore = metal::simd_prefix_exclusive_sum(keep ? 1 : 0);
    int slot = int(uint(blockOffsets[group / 32]) + uint(groupOffsets[group]) + uint(keptBefore));
    if (keep)
    {
        compacted[slot] = particles[i];
    }
}
