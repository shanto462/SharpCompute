// SharpCompute.Tests.Shaders.IntegerSweep (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_IntegerSweep, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline long __ms_sat_long(float x) { float y = x == x ? x : 0.0f; long r = long(metal::clamp(y, -9223372036854775808.0f, 9223371487098961920.0f)); return x >= 9223372036854775808.0f ? 9223372036854775807L : r; }
inline long __ms_sat_long(half x) { return __ms_sat_long(float(x)); }

constant int Outputs = 8;

struct __Constants
{
    uchar tag;
    long key;
    short delta;
    ulong hash;
};
static_assert(sizeof(__Constants) == 32, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, tag) == 0, "SharpCompute: offset of '__Constants.tag' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, key) == 8, "SharpCompute: offset of '__Constants.key' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, delta) == 16, "SharpCompute: offset of '__Constants.delta' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, hash) == 24, "SharpCompute: offset of '__Constants.hash' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_IntegerSweep(
    const device float* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Outputs));
    float wide = input[int(uint(i) * uint(4))] * 4E+18f;
    output[o] = __ms_sat_long(wide);
    output[int(uint(o) + 1u)] = long(ulong(__c.key) + ulong(int(uint(__c.tag) * uint(__c.delta))) + ulong(i));
    output[int(uint(o) + 2u)] = long(__c.hash ^ ulong(__c.key)) >> (int(__c.tag) & 63);
    output[int(uint(o) + 3u)] = long(uchar(int(uint(__c.tag) + uint(i))));
    output[int(uint(o) + 4u)] = long(short(int(uint(__c.delta) * uint(i))));
    output[int(uint(o) + 5u)] = long(__c.hash * ulong(int(uint(i) + 1u)));
    output[int(uint(o) + 6u)] = long(ulong(__ms_sat_long(input[int((uint(i) * uint(4)) + 1u)])) * ulong(__c.key));
    output[int(uint(o) + 7u)] = long(int(uint(char(i)) + uint(ushort(int(uint(__c.delta) - uint(i))))));
}
