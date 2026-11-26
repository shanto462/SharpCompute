// SharpCompute.Tests.Intrinsics.HalfConversions (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_HalfConversions, compile options None
#include <metal_stdlib>

using namespace metal;

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
    toInt[i] = int4(halves[i]);
    toUInt[i] = uint4(halves[i]);
}
