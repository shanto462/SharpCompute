// SharpCompute.Tests.Threadgroups.RecordGroupBuiltins3D (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_RecordGroupBuiltins3D, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 8;

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

kernel void SharpCompute_Tests_Threadgroups_RecordGroupBuiltins3D(
    device int* values [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]],
    uint3 __gsize [[threads_per_threadgroup]],
    uint3 __groups_in [[threadgroups_per_grid]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    const uint3 __groups = __windowed ? __groups_in + uint3(__window.groupOffset) : __groups_in;
    int3 tid = int3(__tid.xyz);
    int3 grid = int3(__grid.xyz);
    int3 size = int3(__gsize.xyz);
    int3 groups = int3(__groups.xyz);
    int i = int(((((uint(tid.z) * uint(grid.y)) + uint(tid.y)) * uint(grid.x)) + uint(tid.x)) * uint(::Stride));
    values[int(uint(i) + 0u)] = size.x;
    values[int(uint(i) + 1u)] = size.y;
    values[int(uint(i) + 2u)] = size.z;
    values[int(uint(i) + 3u)] = int(__gsize.x * __gsize.y * __gsize.z);
    values[int(uint(i) + 4u)] = int(__gindex);
    values[int(uint(i) + 5u)] = groups.x;
    values[int(uint(i) + 6u)] = groups.y;
    values[int(uint(i) + 7u)] = groups.z;
}
