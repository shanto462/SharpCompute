// SharpCompute.Tests.Intrinsics.SmallMatrixMath (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_SmallMatrixMath, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 16;

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

kernel void SharpCompute_Tests_Intrinsics_SmallMatrixMath(
    const device float* elements [[buffer(0)]],
    device float* results [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    int e = int(uint(t) * 9u);
    float2x2 m2 = float2x2(elements[e], elements[int(uint(e) + 1u)], elements[int(uint(e) + 2u)], elements[int(uint(e) + 3u)]);
    float3x3 m3 = float3x3(elements[e], elements[int(uint(e) + 1u)], elements[int(uint(e) + 2u)], elements[int(uint(e) + 3u)], elements[int(uint(e) + 4u)], elements[int(uint(e) + 5u)], elements[int(uint(e) + 6u)], elements[int(uint(e) + 7u)], elements[int(uint(e) + 8u)]);
    float2 v2 = float2(elements[int(uint(e) + 4u)], elements[int(uint(e) + 5u)]);
    float3 v3 = float3(elements[int(uint(e) + 6u)], elements[int(uint(e) + 7u)], elements[int(uint(e) + 8u)]);
    int o = int(uint(t) * uint(::Stride));
    results[int(uint(o) + 0u)] = metal::determinant(m2);
    results[int(uint(o) + 1u)] = metal::determinant(m3);
    results[int(uint(o) + 2u)] = metal::determinant(metal::transpose(m2));
    results[int(uint(o) + 3u)] = metal::determinant(metal::transpose(m3));
    float2 a2 = (float2(v2) * m2);
    float2 b2 = (metal::transpose(m2) * float2(v2));
    float3 a3 = (float3(v3) * m3);
    float3 b3 = (metal::transpose(m3) * float3(v3));
    results[int(uint(o) + 4u)] = a2.x;
    results[int(uint(o) + 5u)] = a2.y;
    results[int(uint(o) + 6u)] = b2.x;
    results[int(uint(o) + 7u)] = b2.y;
    results[int(uint(o) + 8u)] = a3.x;
    results[int(uint(o) + 9u)] = a3.y;
    results[int(uint(o) + 10u)] = a3.z;
    results[int(uint(o) + 11u)] = b3.x;
    results[int(uint(o) + 12u)] = b3.y;
    results[int(uint(o) + 13u)] = b3.z;
    results[int(uint(o) + 14u)] = metal::determinant((metal::transpose(m2) * m2));
    results[int(uint(o) + 15u)] = metal::determinant((m3 * m3));
}
