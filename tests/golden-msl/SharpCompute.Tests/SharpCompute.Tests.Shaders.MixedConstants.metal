// SharpCompute.Tests.Shaders.MixedConstants (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_MixedConstants, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline long __ms_sat_long(float x) { float y = x == x ? x : 0.0f; long r = long(metal::clamp(y, -9223372036854775808.0f, 9223371487098961920.0f)); return x >= 9223372036854775808.0f ? 9223372036854775807L : r; }
inline long __ms_sat_long(half x) { return __ms_sat_long(float(x)); }

constant int Stride = 12;

struct __Constants
{
    uchar tag;
    long key;
    short delta;
    ulong hash;
    char sign;
    ushort count;
    int offset;
    long last;
    float scale;
    uint __length_output;
};
static_assert(sizeof(__Constants) == 56, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, tag) == 0, "SharpCompute: offset of '__Constants.tag' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, key) == 8, "SharpCompute: offset of '__Constants.key' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, delta) == 16, "SharpCompute: offset of '__Constants.delta' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, hash) == 24, "SharpCompute: offset of '__Constants.hash' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, sign) == 32, "SharpCompute: offset of '__Constants.sign' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 34, "SharpCompute: offset of '__Constants.count' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, offset) == 36, "SharpCompute: offset of '__Constants.offset' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, last) == 40, "SharpCompute: offset of '__Constants.last' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, scale) == 48, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __length_output) == 52, "SharpCompute: offset of '__Constants.__length_output' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_MixedConstants(
    device long* output [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int o = int(__tid.x * uint(::Stride));
    output[int(uint(o) + 0u)] = long(__c.tag);
    output[int(uint(o) + 1u)] = __c.key;
    output[int(uint(o) + 2u)] = long(__c.delta);
    output[int(uint(o) + 3u)] = long(__c.hash);
    output[int(uint(o) + 4u)] = long(__c.sign);
    output[int(uint(o) + 5u)] = long(__c.count);
    output[int(uint(o) + 6u)] = long(__c.offset);
    output[int(uint(o) + 7u)] = __c.last;
    output[int(uint(o) + 8u)] = __ms_sat_long(__c.scale);
    output[int(uint(o) + 9u)] = long(ulong(__c.key) + ulong(int(uint(__c.tag) * uint(__c.delta))) - ulong(__c.sign) + ulong(int(__tid.x)));
    output[int(uint(o) + 10u)] = long(__c.hash ^ ulong(__c.last)) >> (int(__c.count) & 63);
    output[int(uint(o) + 11u)] = long(int(__c.__length_output));
}
