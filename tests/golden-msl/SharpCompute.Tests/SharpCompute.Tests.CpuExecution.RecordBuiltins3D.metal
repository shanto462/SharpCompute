// SharpCompute.Tests.CpuExecution.RecordBuiltins3D (SharpCompute.Tests): entry point SharpCompute_Tests_CpuExecution_RecordBuiltins3D, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");

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

kernel void SharpCompute_Tests_CpuExecution_RecordBuiltins3D(
    device int* ids [[buffer(0)]],
    device packed_float3* normalized [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]],
    uint3 __gid_in [[threadgroup_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    int3 tid = int3(__tid.xyz);
    int3 grid = int3(__grid.xyz);
    int3 local = int3(__gtid.xyz);
    int3 group = int3(__gid.xyz);
    int i = int((((uint(tid.z) * uint(grid.y)) + uint(tid.y)) * uint(grid.x)) + uint(tid.x));
    ids[int((uint(i) * 12u) + 0u)] = tid.x;
    ids[int((uint(i) * 12u) + 1u)] = tid.y;
    ids[int((uint(i) * 12u) + 2u)] = tid.z;
    ids[int((uint(i) * 12u) + 3u)] = grid.x;
    ids[int((uint(i) * 12u) + 4u)] = grid.y;
    ids[int((uint(i) * 12u) + 5u)] = grid.z;
    ids[int((uint(i) * 12u) + 6u)] = local.x;
    ids[int((uint(i) * 12u) + 7u)] = local.y;
    ids[int((uint(i) * 12u) + 8u)] = local.z;
    ids[int((uint(i) * 12u) + 9u)] = group.x;
    ids[int((uint(i) * 12u) + 10u)] = group.y;
    ids[int((uint(i) * 12u) + 11u)] = group.z;
    normalized[i] = float3(__tid.xyz) / float3(metal::max(int3(1), int3(__grid.xyz) - 1));
}
