// SharpCompute.Tests.EndToEnd.Logic (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Logic, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int threshold;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, threshold) == 0, "SharpCompute: offset of '__Constants.threshold' does not match its C# layout");

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

static bool Odd(int v);
static float Pick(float a, float b, bool first);
static int Pick_1(int a, int b);
static float Chain(float v, int depth);

static bool Odd(int v)
{
    return (v & 1) != 0;
}

static float Pick(float a, float b, bool first)
{
    return first ? a : b;
}

static int Pick_1(int a, int b)
{
    return a > b ? a : b;
}

static float Chain(float v, int depth)
{
    float t = ::Pick(v, -v, depth > 2);
    return t + float(::Pick_1(depth, 1));
}

kernel void SharpCompute_Tests_EndToEnd_Logic(
    device float* o [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    bool big = i > __c.threshold;
    bool odd = ::Odd(i);
    bool both = (big && odd) || (!big && !odd);
    float v = both ? (big ? 3.0f : 2.0f) : (odd ? 1.0f : 0.0f);
    if (!(i < 3) && (i != 7 || big) && i <= 100 && i >= 0)
    {
        v += 10.0f;
    }
    v += ::Chain(v, i % 5);
    bool flip = false;
    for (int a = 0, b = 6; a < b; a = int(uint(a) + 1u), b = int(uint(b) - 1u))
    {
        flip = !flip;
        if (a == 1)
        {
            continue;
        }
        v += float(int(uint(a) * uint(b)));
    }
    o[i] = flip ? -v : v;
}
