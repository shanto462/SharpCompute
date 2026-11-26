// SharpCompute.Samples.ComputeLab.Pages.CompactAbove (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_CompactAbove, compile options None
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

kernel void SharpCompute_Samples_ComputeLab_Pages_CompactAbove(
    device uint* source [[buffer(0)]],
    device uint* output [[buffer(1)]],
    device int* counter [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    if (source[i] >= __c.cutoff)
    {
        output[metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counter[0]), 1, metal::memory_order_relaxed)] = uint(i);
    }
}
