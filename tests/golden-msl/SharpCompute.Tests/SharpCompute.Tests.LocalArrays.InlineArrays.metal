// SharpCompute.Tests.LocalArrays.InlineArrays (SharpCompute.Tests): entry point SharpCompute_Tests_LocalArrays_InlineArrays, compile options None
#include <metal_stdlib>

using namespace metal;

struct Quad
{
    float _element[4];
};
static_assert(sizeof(Quad) == 16, "SharpCompute: size of 'Quad' does not match its C# layout");
static_assert(__builtin_offsetof(Quad, _element) == 0, "SharpCompute: offset of 'Quad._element' does not match its C# layout");

struct Tagged
{
    int Tag;
    Quad Values;
};
static_assert(sizeof(Tagged) == 20, "SharpCompute: size of 'Tagged' does not match its C# layout");
static_assert(__builtin_offsetof(Tagged, Tag) == 0, "SharpCompute: offset of 'Tagged.Tag' does not match its C# layout");
static_assert(__builtin_offsetof(Tagged, Values) == 4, "SharpCompute: offset of 'Tagged.Values' does not match its C# layout");

struct __Constants
{
    Quad scale;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, scale) == 0, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");

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

kernel void SharpCompute_Tests_LocalArrays_InlineArrays(
    device Quad* quads [[buffer(0)]],
    device Tagged* tagged [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    Quad q = quads[i];
    for (int k = 0; k < 4; k = int(uint(k) + 1u))
    {
        q._element[k] = (q._element[k] * __c.scale._element[k]) + float(tagged[i].Tag);
    }
    quads[i] = q;
    float sum = 0.0f;
    for (int __i_value = 0; __i_value < 4; __i_value++)
    {
        float value = q._element[__i_value];
        sum += value;
    }
    tagged[i].Values._element[i % 4] = sum;
    tagged[i].Tag = int(uint(tagged[i].Tag) + 1u);
}
