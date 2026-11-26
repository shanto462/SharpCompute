// SharpCompute.Samples.ComputeLab.Pages.Simd.VoteInGroups (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_VoteInGroups, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_int2) == 8, "SharpCompute: size of 'packed_int2' does not match its C# layout");

struct __Constants
{
    int centerX;
    int centerY;
    int radiusSquared;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, centerX) == 0, "SharpCompute: offset of '__Constants.centerX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, centerY) == 4, "SharpCompute: offset of '__Constants.centerY' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radiusSquared) == 8, "SharpCompute: offset of '__Constants.radiusSquared' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_VoteInGroups(
    const device packed_int2* particles [[buffer(0)]],
    device uint* ballots [[buffer(1)]],
    device int* groupCounts [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int group = i / 32;
    int2 d = int2(uint2(particles[i]) - uint2(int2(__c.centerX, __c.centerY)));
    bool keep = int((uint(d.x) * uint(d.x)) + (uint(d.y) * uint(d.y))) < __c.radiusSquared;
    uint ballot = uint(metal::simd_vote::vote_t(metal::simd_ballot(keep)));
    int count = metal::simd_sum(int(keep));
    if (metal::simd_is_first())
    {
        ballots[group] = ballot;
        groupCounts[group] = count;
    }
}
