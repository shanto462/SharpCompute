// SharpCompute.Tests.LocalArrays.HelperWritesCallersArray (SharpCompute.Tests): entry point SharpCompute_Tests_LocalArrays_HelperWritesCallersArray, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 11;

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

static void Double(thread float* values, int __len_values);
static void Ramp(thread float* values, int __len_values, float start);
static void Squares(thread int* values, int __len_values, int offset);
static float Total(thread const float* floats, int __len_floats, thread const int* ints, int __len_ints);

static void Double(thread float* values, int __len_values)
{
    for (int __i_value = 0; __i_value < __len_values; __i_value++)
    {
        thread float& value = values[__i_value];
        value *= 2.0f;
    }
}

static void Ramp(thread float* values, int __len_values, float start)
{
    for (int k = 0; k < __len_values; k = int(uint(k) + 1u))
    {
        values[k] = start + float(k);
    }
    ::Double(values, __len_values);
}

static void Squares(thread int* values, int __len_values, int offset)
{
    for (int k = 0; k < __len_values; k = int(uint(k) + 1u))
    {
        values[k] = int((uint(k) + uint(offset)) * (uint(k) + uint(offset)));
    }
}

static float Total(thread const float* floats, int __len_floats, thread const int* ints, int __len_ints)
{
    float total = 0.0f;
    for (int __i_f = 0; __i_f < __len_floats; __i_f++)
    {
        float f = floats[__i_f];
        total += f;
    }
    for (int __i_n = 0; __i_n < __len_ints; __i_n++)
    {
        int n = ints[__i_n];
        total -= float(n);
    }
    return total;
}

kernel void SharpCompute_Tests_LocalArrays_HelperWritesCallersArray(
    device float* output [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float floats[6] = {};
    int ints[4] = {};
    ::Ramp(floats, 6, float(i));
    ::Squares(ints, 4, i % 3);
    int o = int(uint(i) * uint(::Slots));
    for (int k = 0; k < 6; k = int(uint(k) + 1u))
    {
        output[int(uint(o) + uint(k))] = floats[k];
    }
    for (int k = 0; k < 4; k = int(uint(k) + 1u))
    {
        output[int(uint(o) + 6u + uint(k))] = float(ints[k]);
    }
    output[int(uint(o) + 10u)] = ::Total(floats, 6, ints, 4);
}
