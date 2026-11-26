// SharpCompute.Tests.EndToEnd.Literals (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Literals, compile options None
#include <metal_stdlib>

using namespace metal;

constant float Third = 0.33333334f;

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

kernel void SharpCompute_Tests_EndToEnd_Literals(
    device float* o [[buffer(0)]],
    device int* oi [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    const float tiny = 1E-07f;
    const int big = 2147483632;
    float v = 0.0f;
    if (i == 0)
    {
        v = tiny;
    }
    else if (i == 1)
    {
        v = 3.4028235E+38f;
    }
    else if (i == 2)
    {
        v = 1E-45f;
    }
    else if (i == 3)
    {
        v = 3.1415927f;
    }
    else if (i == 4)
    {
        v = NAN;
    }
    else if (i == 5)
    {
        v = INFINITY;
    }
    else if (i == 6)
    {
        v = -INFINITY;
    }
    else if (i == 7)
    {
        v = ::Third;
    }
    else if (i == 8)
    {
        v = -0.0f;
    }
    else if (i == 9)
    {
        v = 16777216.0f;
    }
    else if (i == 10)
    {
        v = 0.1f;
    }
    else if (i == 11)
    {
        v = 123456790.0f;
    }
    else if (i == 12)
    {
        v = 3.4028235E+38f;
    }
    else if (i == 13)
    {
        v = -3.4028235E+38f;
    }
    else if (i == 14)
    {
        v = 1.1754944E-38f;
    }
    else if (i == 15)
    {
        v = 2.7182817f;
    }
    o[i] = v;
    int w = 0;
    if (i == 0)
    {
        w = -2147483647 - 1;
    }
    else if (i == 1)
    {
        w = 2147483647;
    }
    else if (i == 2)
    {
        w = big;
    }
    else if (i == 3)
    {
        w = -1;
    }
    else if (i == 4)
    {
        w = -2147483647;
    }
    else if (i == 5)
    {
        w = 65;
    }
    else if (i == 6)
    {
        w = -16;
    }
    else if (i == 7)
    {
        w = 1000000;
    }
    oi[i] = w;
}
