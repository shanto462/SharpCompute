// SharpCompute.Tests.LocalArrays.SortEight (SharpCompute.Tests): entry point SharpCompute_Tests_LocalArrays_SortEight, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Width = 8;

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

static void CompareSwap(thread float& a, thread float& b);
static void Sort(thread float* values, int __len_values);

static void CompareSwap(thread float& a, thread float& b)
{
    float low = metal::min(a, b);
    b = metal::max(a, b);
    a = low;
}

static void Sort(thread float* values, int __len_values)
{
    const int network[38] = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 1, 2, 5, 6, 0, 4, 3, 7, 1, 5, 2, 6, 1, 4, 3, 6, 2, 4, 3, 5, 3, 4 };
    for (int k = 0; k < 38; k = int(uint(k) + 2u))
    {
        ::CompareSwap(values[network[k]], values[network[int(uint(k) + 1u)]]);
    }
}

kernel void SharpCompute_Tests_LocalArrays_SortEight(
    const device float* input [[buffer(0)]],
    device float* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int row = int(__tid.x * uint(::Width));
    float values[8] = {};
    for (int i = 0; i < 8; i = int(uint(i) + 1u))
    {
        values[i] = input[int(uint(row) + uint(i))];
    }
    ::Sort(values, 8);
    for (int i = 0; i < 8; i = int(uint(i) + 1u))
    {
        output[int(uint(row) + uint(i))] = values[i];
    }
}
