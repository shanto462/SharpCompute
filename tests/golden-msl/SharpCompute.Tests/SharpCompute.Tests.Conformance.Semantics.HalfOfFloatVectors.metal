// SharpCompute.Tests.Conformance.Semantics.HalfOfFloatVectors (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_HalfOfFloatVectors, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline half2 __ms_to_half2(float2 x) { return metal::select(half2(x), metal::select(half2(INFINITY), half2(-INFINITY), x < 0.0f), metal::abs(x) >= 65520.0f); }
inline half3 __ms_to_half3(float3 x) { return metal::select(half3(x), metal::select(half3(INFINITY), half3(-INFINITY), x < 0.0f), metal::abs(x) >= 65520.0f); }
inline half4 __ms_to_half4(float4 x) { return metal::select(half4(x), metal::select(half4(INFINITY), half4(-INFINITY), x < 0.0f), metal::abs(x) >= 65520.0f); }

constant int Slots = 9;

struct __Constants
{
    int count;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 0, "SharpCompute: offset of '__Constants.count' does not match its C# layout");

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

kernel void SharpCompute_Tests_Conformance_Semantics_HalfOfFloatVectors(
    const device float* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    float4 v = float4(input[i], input[(int(uint(i) + 1u)) % __c.count], input[(int(uint(i) + 2u)) % __c.count], input[(int(uint(i) + 3u)) % __c.count]);
    half4 h4 = __ms_to_half4(v);
    half3 h3 = __ms_to_half3(v.xyz);
    half2 h2 = __ms_to_half2(v.xy);
    output[int(uint(o) + 0u)] = long(as_type<ushort>(h4.x));
    output[int(uint(o) + 1u)] = long(as_type<ushort>(h4.y));
    output[int(uint(o) + 2u)] = long(as_type<ushort>(h4.z));
    output[int(uint(o) + 3u)] = long(as_type<ushort>(h4.w));
    output[int(uint(o) + 4u)] = long(as_type<ushort>(h3.x));
    output[int(uint(o) + 5u)] = long(as_type<ushort>(h3.y));
    output[int(uint(o) + 6u)] = long(as_type<ushort>(h3.z));
    output[int(uint(o) + 7u)] = long(as_type<ushort>(h2.x));
    output[int(uint(o) + 8u)] = long(as_type<ushort>(h2.y));
}
