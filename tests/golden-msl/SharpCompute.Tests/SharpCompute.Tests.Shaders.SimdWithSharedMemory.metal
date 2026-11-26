// SharpCompute.Tests.Shaders.SimdWithSharedMemory (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdWithSharedMemory, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 3;

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

kernel void SharpCompute_Tests_Shaders_SimdWithSharedMemory(
    const device int* input [[buffer(0)]],
    device int* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gsize [[threads_per_threadgroup]],
    uint __sgid [[simdgroup_index_in_threadgroup]],
    uint __sgroups [[simdgroups_per_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup int sums[4];
    int i = int(__tid.x);
    int sum = metal::simd_sum(input[i]);
    if (metal::simd_is_first())
    {
        sums[int(__sgid)] = sum;
    }
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    int total = 0;
    for (int group = 0; group < int(__sgroups); group = int(uint(group) + 1u))
    {
        total = int(uint(total) + uint(sums[group]));
    }
    output[int((uint(i) * uint(::Stride)) + 0u)] = total;
    output[int((uint(i) * uint(::Stride)) + 1u)] = metal::simd_prefix_exclusive_sum(int(uint(total) - uint(sum)));
    output[int((uint(i) * uint(::Stride)) + 2u)] = int(__gsize.x * __gsize.y * __gsize.z);
}
