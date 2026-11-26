// SharpCompute.Tests.Intrinsics.HalfConversions (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_HalfConversions, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int4 __ms_sat_int4(float4 x) { float4 y = metal::select(float4(0.0f), x, x == x); int4 r = int4(metal::clamp(y, float4(-2147483648.0f), float4(2147483520.0f))); return metal::select(r, int4(2147483647), x >= 2147483648.0f); }
inline int4 __ms_sat_int4(half4 x) { return __ms_sat_int4(float4(x)); }
inline uint4 __ms_sat_uint4(float4 x) { float4 y = metal::select(float4(0.0f), x, x > 0.0f); uint4 r = uint4(metal::min(y, float4(4294967040.0f))); return metal::select(r, uint4(4294967295u), x >= 4294967296.0f); }
inline uint4 __ms_sat_uint4(half4 x) { return __ms_sat_uint4(float4(x)); }

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");
static_assert(sizeof(packed_int4) == 16, "SharpCompute: size of 'packed_int4' does not match its C# layout");
static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");
static_assert(sizeof(packed_uint4) == 16, "SharpCompute: size of 'packed_uint4' does not match its C# layout");

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

kernel void SharpCompute_Tests_Intrinsics_HalfConversions(
    const device packed_float4* floats [[buffer(0)]],
    const device packed_int4* ints [[buffer(1)]],
    const device packed_half4* halves [[buffer(2)]],
    device packed_half4* toHalf [[buffer(3)]],
    device packed_float4* toFloat [[buffer(4)]],
    device packed_int4* toInt [[buffer(5)]],
    device packed_uint4* toUInt [[buffer(6)]],
    constant __GridWindow& __window [[buffer(7), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    toHalf[int(uint(i) * 3u)] = half4(floats[i]);
    toHalf[int((uint(i) * 3u) + 1u)] = half4(ints[i]);
    toHalf[int((uint(i) * 3u) + 2u)] = half4(uint4(ints[i]));
    toFloat[i] = float4(halves[i]);
    toInt[i] = __ms_sat_int4(halves[i]);
    toUInt[i] = __ms_sat_uint4(halves[i]);
}
