// SharpCompute.Tests.Shaders.SmallConstants (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SmallConstants, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

constant int Stride = 8;

struct __Constants
{
    uchar a;
    int b;
    char c;
    short d;
    uchar e;
    ushort f;
    float g;
};
static_assert(sizeof(__Constants) == 20, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, a) == 0, "SharpCompute: offset of '__Constants.a' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, b) == 4, "SharpCompute: offset of '__Constants.b' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, c) == 8, "SharpCompute: offset of '__Constants.c' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, d) == 10, "SharpCompute: offset of '__Constants.d' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, e) == 12, "SharpCompute: offset of '__Constants.e' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, f) == 14, "SharpCompute: offset of '__Constants.f' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, g) == 16, "SharpCompute: offset of '__Constants.g' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_SmallConstants(
    device int* output [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int o = int(__tid.x * uint(::Stride));
    output[int(uint(o) + 0u)] = int(__c.a);
    output[int(uint(o) + 1u)] = __c.b;
    output[int(uint(o) + 2u)] = int(__c.c);
    output[int(uint(o) + 3u)] = int(__c.d);
    output[int(uint(o) + 4u)] = int(__c.e);
    output[int(uint(o) + 5u)] = int(__c.f);
    output[int(uint(o) + 6u)] = __ms_sat_int(__c.g);
    output[int(uint(o) + 7u)] = int(uint(__c.a) + uint(__c.c) + uint(__c.d) + uint(__c.e) + uint(__c.f));
}
