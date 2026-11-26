// SharpCompute.Samples.ComputeLab.Pages.Simd.FoldInOneGroup (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_FoldInOneGroup, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int count;
    int slot;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 0, "SharpCompute: offset of '__Constants.count' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, slot) == 4, "SharpCompute: offset of '__Constants.slot' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_FoldInOneGroup(
    device float* partials [[buffer(0)]],
    device float* totals [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    float local = 0.0f;
    for (int i = int(__tid.x); i < __c.count; i = int(uint(i) + uint(32)))
    {
        local += partials[i];
    }
    float total = metal::simd_sum(local);
    if (metal::simd_is_first())
    {
        totals[__c.slot] = total;
    }
}
