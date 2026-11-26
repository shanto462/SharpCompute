// SharpCompute.Samples.ComputeLab.Pages.Simd.SumStripsAndLanes (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_SumStripsAndLanes, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int strip;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, strip) == 0, "SharpCompute: offset of '__Constants.strip' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_SumStripsAndLanes(
    const device float* input [[buffer(0)]],
    device float* partials [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int __ms_thread = int(__tid.x);
    int first = int(uint(__ms_thread) * uint(__c.strip));
    float local = 0.0f;
    for (int k = 0; k < __c.strip; k = int(uint(k) + 1u))
    {
        local += input[int(uint(first) + uint(k))];
    }
    float sum = metal::simd_sum(local);
    if (metal::simd_is_first())
    {
        partials[__ms_thread / 32] = sum;
    }
}
