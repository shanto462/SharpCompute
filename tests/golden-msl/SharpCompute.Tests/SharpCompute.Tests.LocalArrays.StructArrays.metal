// SharpCompute.Tests.LocalArrays.StructArrays (SharpCompute.Tests): entry point SharpCompute_Tests_LocalArrays_StructArrays, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 7;

struct Weighted
{
    packed_float3 Position;
    int Weight;
};
static_assert(sizeof(Weighted) == 16, "SharpCompute: size of 'Weighted' does not match its C# layout");
static_assert(__builtin_offsetof(Weighted, Position) == 0, "SharpCompute: offset of 'Weighted.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Weighted, Weight) == 12, "SharpCompute: offset of 'Weighted.Weight' does not match its C# layout");

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

static void Swap(thread Weighted& a, thread Weighted& b);

static void Swap(thread Weighted& a, thread Weighted& b)
{
    Weighted t = a;
    a = b;
    b = t;
}

kernel void SharpCompute_Tests_LocalArrays_StructArrays(
    const device int* seeds [[buffer(0)]],
    device float* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    Weighted particles[4] = {};
    for (int k = 0; k < 4; k = int(uint(k) + 1u))
    {
        int seed = seeds[int((uint(i) * 4u) + uint(k))];
        particles[k].Position = float3(float(seed % 7), float(seed % 5), float(k));
        particles[k].Weight = seed % 11;
    }
    for (int a = 0; a < 4; a = int(uint(a) + 1u))
    {
        for (int b = int(uint(a) + 1u); b < 4; b = int(uint(b) + 1u))
        {
            if (particles[b].Weight < particles[a].Weight)
            {
                ::Swap(particles[a], particles[b]);
            }
        }
    }
    float3 sum = float3(0.0f, 0.0f, 0.0f);
    int o = int(uint(i) * uint(::Slots));
    for (int k = 0; k < 4; k = int(uint(k) + 1u))
    {
        sum = (sum * 2.0f) + particles[k].Position;
        output[int(uint(o) + uint(k))] = float(particles[k].Weight);
    }
    output[int(uint(o) + 4u)] = sum.x;
    output[int(uint(o) + 5u)] = sum.y;
    output[int(uint(o) + 6u)] = sum.z;
}
