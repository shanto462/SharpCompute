// SharpCompute.Tests.Threadgroups.NeighbourExchange (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_NeighbourExchange, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Threads = 32;

static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");

struct Sample
{
    packed_float3 Position;
    int Weight;
};
static_assert(sizeof(Sample) == 16, "SharpCompute: size of 'Sample' does not match its C# layout");
static_assert(__builtin_offsetof(Sample, Position) == 0, "SharpCompute: offset of 'Sample.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Sample, Weight) == 12, "SharpCompute: offset of 'Sample.Weight' does not match its C# layout");

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

kernel void SharpCompute_Tests_Threadgroups_NeighbourExchange(
    device packed_float3* positions [[buffer(0)]],
    device int* weights [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    threadgroup packed_float3 vectors[32];
    threadgroup Sample samples[32];
    int local = int(__gindex);
    int i = int((__tid.y * __grid.x) + __tid.x);
    vectors[local] = float3(float(int(__tid.x)), float(int(__tid.y)), float(local));
    Sample sample;
    sample.Position = float3(float(i), 2.0f * float(i), float(int(-uint(i))));
    sample.Weight = int(uint(i) + 1u);
    samples[local] = sample;
    samples[local].Weight = int(uint(samples[local].Weight) + 1000u);
    metal::threadgroup_barrier(metal::mem_flags::mem_device | metal::mem_flags::mem_threadgroup);
    int next = (int(uint(local) + 1u)) % ::Threads;
    positions[i] = vectors[next] + samples[next].Position;
    weights[i] = samples[next].Weight;
}
