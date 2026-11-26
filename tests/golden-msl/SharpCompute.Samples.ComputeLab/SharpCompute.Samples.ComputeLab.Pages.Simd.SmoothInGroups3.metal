// SharpCompute.Samples.ComputeLab.Pages.Simd.SmoothInGroups3 (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_Simd_SmoothInGroups3, compile options None
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

kernel void SharpCompute_Samples_ComputeLab_Pages_Simd_SmoothInGroups3(
    device float* source [[buffer(0)]],
    device float* target [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint __lane [[thread_index_in_simdgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float v = source[i];
    int lane = int(__lane);
    int active = metal::simd_sum(int(true));
    float left = metal::simd_shuffle_up(v, ushort(1));
    float down = metal::simd_shuffle_down(v, ushort(1));
    float right = int(uint(lane) + 1u) < active ? down : v;
    target[i] = (left + (2.0f * v) + right) * 0.25f;
}
