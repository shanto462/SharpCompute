// SharpCompute.Tests.CpuExecution.RecordBuiltins2D (SharpCompute.Tests): entry point SharpCompute_Tests_CpuExecution_RecordBuiltins2D, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float2) == 8, "SharpCompute: size of 'packed_float2' does not match its C# layout");

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

kernel void SharpCompute_Tests_CpuExecution_RecordBuiltins2D(
    device int* ids [[buffer(0)]],
    device packed_float2* normalized [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]],
    uint3 __gid_in [[threadgroup_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    int2 tid = int2(__tid.xy);
    int i = int((__tid.y * __grid.x) + __tid.x);
    ids[int((uint(i) * 8u) + 0u)] = tid.x;
    ids[int((uint(i) * 8u) + 1u)] = tid.y;
    ids[int((uint(i) * 8u) + 2u)] = int2(__grid.xy).x;
    ids[int((uint(i) * 8u) + 3u)] = int(__grid.y);
    ids[int((uint(i) * 8u) + 4u)] = int(__gtid.x);
    ids[int((uint(i) * 8u) + 5u)] = int2(__gtid.xy).y;
    ids[int((uint(i) * 8u) + 6u)] = int(__gid.x);
    ids[int((uint(i) * 8u) + 7u)] = int2(__gid.xy).y;
    normalized[i] = float2(float(__tid.x) / float(metal::max(1, int(__grid.x) - 1)), (float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1))).y);
}
