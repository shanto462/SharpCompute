// SharpCompute.Tests.Intrinsics.MulPackedOperands (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_MulPackedOperands, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");

struct __Constants
{
    packed_float3 offset;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, offset) == 0, "SharpCompute: offset of '__Constants.offset' does not match its C# layout");

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

static float3 Pick(int index, device packed_float3* points);

static float3 Pick(int index, device packed_float3* points)
{
    return points[index];
}

kernel void SharpCompute_Tests_Intrinsics_MulPackedOperands(
    device packed_float3* points [[buffer(0)]],
    device packed_float3* results [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float3x3 m = float3x3(2.0f, 0.0f, 0.0f, 0.0f, 3.0f, 0.0f, 1.0f, 0.0f, 4.0f);
    float2x2 r = float2x2(0.0f, 1.0f, -1.0f, 0.0f);
    results[int((uint(i) * 4u) + 0u)] = (float3(i > 1 ? points[i] : points[0]) * m);
    results[int((uint(i) * 4u) + 1u)] = (m * float3(i > 1 ? points[i] : __c.offset));
    results[int((uint(i) * 4u) + 2u)] = (float3(::Pick(i, points)) * m) + (m * float3(::Pick(0, points)));
    results[int((uint(i) * 4u) + 3u)] = float3((float2(points[i].xy) * r) + (r * float2(points[i].zx)), 0.0f);
}
