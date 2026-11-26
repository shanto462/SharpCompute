// SharpCompute.Tests.Threadgroups.TileMean (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_TileMean, compile options None
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

kernel void SharpCompute_Tests_Threadgroups_TileMean(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __GridWindow& __window [[buffer(0), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]],
    uint3 __gsize [[threads_per_threadgroup]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    threadgroup float tile[64];
    int local = int(__gindex);
    tile[local] = float((int((__tid.x * 7u) + (__tid.y * 13u))) % 32) / 32.0f;
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    float sum = 0.0f;
    for (int j = 0; j < int(__gsize.x * __gsize.y * __gsize.z); j = int(uint(j) + 1u))
    {
        sum += tile[j];
    }
    float mean = sum / float(int(__gsize.x * __gsize.y * __gsize.z));
    __output.write(float4(mean, tile[local], 0.0f, 1.0f), __tid.xy);
    return;
}
