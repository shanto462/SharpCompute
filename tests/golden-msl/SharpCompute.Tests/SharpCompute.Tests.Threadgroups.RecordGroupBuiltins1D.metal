// SharpCompute.Tests.Threadgroups.RecordGroupBuiltins1D (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_RecordGroupBuiltins1D, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 4;

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

static int Count(uint3 __gsize, uint __gindex);

static int Count(uint3 __gsize, uint __gindex)
{
    return int(__gsize.x * __gsize.y * __gsize.z + (__gindex * 0u));
}

kernel void SharpCompute_Tests_Threadgroups_RecordGroupBuiltins1D(
    device int* values [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gsize [[threads_per_threadgroup]],
    uint3 __groups_in [[threadgroups_per_grid]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __groups = __windowed ? __groups_in + uint3(__window.groupOffset) : __groups_in;
    int i = int(__tid.x * uint(::Stride));
    values[int(uint(i) + 0u)] = int(__gsize.x);
    values[int(uint(i) + 1u)] = ::Count(__gsize, __gindex);
    values[int(uint(i) + 2u)] = int(__gindex);
    values[int(uint(i) + 3u)] = int(__groups.x);
}
