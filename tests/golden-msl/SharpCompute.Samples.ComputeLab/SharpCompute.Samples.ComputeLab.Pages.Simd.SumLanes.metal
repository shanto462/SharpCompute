// SharpCompute.Samples.ComputeLab.Pages.Simd.SumLanes (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_SumLanes, compile options None
#include <metal_stdlib>

using namespace metal;

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

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_SumLanes(
    const device float* input [[buffer(0)]],
    device float* partials [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float sum = metal::simd_sum(input[i]);
    if (metal::simd_is_first())
    {
        partials[i / 32] = sum;
    }
}
