// SharpCompute.Tests.Shaders.FloatIntegerConversions (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_FloatIntegerConversions, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline char __ms_sat_char(float x) { float y = x == x ? x : 0.0f; return char(metal::clamp(y, -128.0f, 127.0f)); }
inline char __ms_sat_char(half x) { return __ms_sat_char(float(x)); }
inline uchar __ms_sat_uchar(float x) { float y = x > 0.0f ? x : 0.0f; return uchar(metal::min(y, 255.0f)); }
inline uchar __ms_sat_uchar(half x) { return __ms_sat_uchar(float(x)); }
inline short __ms_sat_short(float x) { float y = x == x ? x : 0.0f; return short(metal::clamp(y, -32768.0f, 32767.0f)); }
inline short __ms_sat_short(half x) { return __ms_sat_short(float(x)); }
inline ushort __ms_sat_ushort(float x) { float y = x > 0.0f ? x : 0.0f; return ushort(metal::min(y, 65535.0f)); }
inline ushort __ms_sat_ushort(half x) { return __ms_sat_ushort(float(x)); }
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline uint __ms_sat_uint(float x) { float y = x > 0.0f ? x : 0.0f; uint r = uint(metal::min(y, 4294967040.0f)); return x >= 4294967296.0f ? 4294967295u : r; }
inline uint __ms_sat_uint(half x) { return __ms_sat_uint(float(x)); }
inline long __ms_sat_long(float x) { float y = x == x ? x : 0.0f; long r = long(metal::clamp(y, -9223372036854775808.0f, 9223371487098961920.0f)); return x >= 9223372036854775808.0f ? 9223372036854775807L : r; }
inline long __ms_sat_long(half x) { return __ms_sat_long(float(x)); }
inline ulong __ms_sat_ulong(float x) { float y = x > 0.0f ? x : 0.0f; ulong r = ulong(metal::min(y, 18446742974197923840.0f)); return x >= 18446744073709551616.0f ? 18446744073709551615UL : r; }
inline ulong __ms_sat_ulong(half x) { return __ms_sat_ulong(float(x)); }

constant int IntegerStride = 16;
constant int RealStride = 8;

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

kernel void SharpCompute_Tests_Shaders_FloatIntegerConversions(
    const device float* floats [[buffer(0)]],
    const device long* longs [[buffer(1)]],
    device long* integers [[buffer(2)]],
    device float* reals [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::IntegerStride));
    float f = floats[i];
    half h = half(f);
    integers[int(uint(o) + 0u)] = __ms_sat_long(f);
    integers[int(uint(o) + 1u)] = long(__ms_sat_ulong(f));
    integers[int(uint(o) + 2u)] = long(__ms_sat_uchar(f));
    integers[int(uint(o) + 3u)] = long(__ms_sat_char(f));
    integers[int(uint(o) + 4u)] = long(__ms_sat_short(f));
    integers[int(uint(o) + 5u)] = long(__ms_sat_ushort(f));
    integers[int(uint(o) + 6u)] = long(__ms_sat_int(f));
    integers[int(uint(o) + 7u)] = long(__ms_sat_uint(f));
    integers[int(uint(o) + 8u)] = __ms_sat_long(h);
    integers[int(uint(o) + 9u)] = long(__ms_sat_ulong(h));
    integers[int(uint(o) + 10u)] = long(__ms_sat_uchar(h));
    integers[int(uint(o) + 11u)] = long(__ms_sat_char(h));
    integers[int(uint(o) + 12u)] = long(__ms_sat_short(h));
    integers[int(uint(o) + 13u)] = long(__ms_sat_ushort(h));
    integers[int(uint(o) + 14u)] = __ms_sat_long(f * 0.5f);
    integers[int(uint(o) + 15u)] = __ms_sat_long(metal::floor(f));
    int r = int(uint(i) * uint(::RealStride));
    long l = longs[i];
    reals[int(uint(r) + 0u)] = float(l);
    reals[int(uint(r) + 1u)] = float(ulong(l));
    reals[int(uint(r) + 2u)] = float(uchar(l));
    reals[int(uint(r) + 3u)] = float(char(l));
    reals[int(uint(r) + 4u)] = float(short(l));
    reals[int(uint(r) + 5u)] = float(ushort(l));
    reals[int(uint(r) + 6u)] = float(half(char(l)));
    reals[int(uint(r) + 7u)] = float(l) * 0.5f;
}
