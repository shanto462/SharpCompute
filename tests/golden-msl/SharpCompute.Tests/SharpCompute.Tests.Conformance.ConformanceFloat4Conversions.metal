// SharpCompute.Tests.Conformance.ConformanceFloat4Conversions (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceFloat4Conversions, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 12;

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

kernel void SharpCompute_Tests_Conformance_ConformanceFloat4Conversions(
    const device float* a [[buffer(0)]],
    const device float* b [[buffer(1)]],
    const device float* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    float4 x = float4(a[int(4u * uint(i) + 0u)], a[int(4u * uint(i) + 1u)], a[int(4u * uint(i) + 2u)], a[int(4u * uint(i) + 3u)]);
    float4 y = float4(b[int(4u * uint(i) + 0u)], b[int(4u * uint(i) + 1u)], b[int(4u * uint(i) + 2u)], b[int(4u * uint(i) + 3u)]);
    float4 z = float4(c[int(4u * uint(i) + 0u)], c[int(4u * uint(i) + 1u)], c[int(4u * uint(i) + 2u)], c[int(4u * uint(i) + 3u)]);
    int n = s[i];
    half4 v0 = half4(x);
    output[int(uint(o) + 0u)] = long(as_type<ushort>(v0.x));
    output[int(uint(o) + 1u)] = long(as_type<ushort>(v0.y));
    output[int(uint(o) + 2u)] = long(as_type<ushort>(v0.z));
    output[int(uint(o) + 3u)] = long(as_type<ushort>(v0.w));
    int4 v4 = int4(x);
    output[int(uint(o) + 4u)] = long(v4.x);
    output[int(uint(o) + 5u)] = long(v4.y);
    output[int(uint(o) + 6u)] = long(v4.z);
    output[int(uint(o) + 7u)] = long(v4.w);
    uint4 v8 = uint4(x);
    output[int(uint(o) + 8u)] = long(v8.x);
    output[int(uint(o) + 9u)] = long(v8.y);
    output[int(uint(o) + 10u)] = long(v8.z);
    output[int(uint(o) + 11u)] = long(v8.w);
}
