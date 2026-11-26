// SharpCompute.Tests.Conformance.ConformanceBool4Operators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceBool4Operators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 22;

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

kernel void SharpCompute_Tests_Conformance_ConformanceBool4Operators(
    const device int* a [[buffer(0)]],
    const device int* b [[buffer(1)]],
    const device int* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    bool4 x = bool4(a[int(4u * uint(i) + 0u)] != 0, a[int(4u * uint(i) + 1u)] != 0, a[int(4u * uint(i) + 2u)] != 0, a[int(4u * uint(i) + 3u)] != 0);
    bool4 y = bool4(b[int(4u * uint(i) + 0u)] != 0, b[int(4u * uint(i) + 1u)] != 0, b[int(4u * uint(i) + 2u)] != 0, b[int(4u * uint(i) + 3u)] != 0);
    bool4 z = bool4(c[int(4u * uint(i) + 0u)] != 0, c[int(4u * uint(i) + 1u)] != 0, c[int(4u * uint(i) + 2u)] != 0, c[int(4u * uint(i) + 3u)] != 0);
    int n = s[i];
    bool4 v0 = !x;
    output[int(uint(o) + 0u)] = (v0.x) ? 1L : 0L;
    output[int(uint(o) + 1u)] = (v0.y) ? 1L : 0L;
    output[int(uint(o) + 2u)] = (v0.z) ? 1L : 0L;
    output[int(uint(o) + 3u)] = (v0.w) ? 1L : 0L;
    bool4 v4 = x & y;
    output[int(uint(o) + 4u)] = (v4.x) ? 1L : 0L;
    output[int(uint(o) + 5u)] = (v4.y) ? 1L : 0L;
    output[int(uint(o) + 6u)] = (v4.z) ? 1L : 0L;
    output[int(uint(o) + 7u)] = (v4.w) ? 1L : 0L;
    bool4 v8 = x | y;
    output[int(uint(o) + 8u)] = (v8.x) ? 1L : 0L;
    output[int(uint(o) + 9u)] = (v8.y) ? 1L : 0L;
    output[int(uint(o) + 10u)] = (v8.z) ? 1L : 0L;
    output[int(uint(o) + 11u)] = (v8.w) ? 1L : 0L;
    bool4 v12 = x ^ y;
    output[int(uint(o) + 12u)] = (v12.x) ? 1L : 0L;
    output[int(uint(o) + 13u)] = (v12.y) ? 1L : 0L;
    output[int(uint(o) + 14u)] = (v12.z) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (v12.w) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (metal::any(x)) ? 1L : 0L;
    output[int(uint(o) + 17u)] = (metal::all(x)) ? 1L : 0L;
    bool4 v18 = metal::select(z, y, x);
    output[int(uint(o) + 18u)] = (v18.x) ? 1L : 0L;
    output[int(uint(o) + 19u)] = (v18.y) ? 1L : 0L;
    output[int(uint(o) + 20u)] = (v18.z) ? 1L : 0L;
    output[int(uint(o) + 21u)] = (v18.w) ? 1L : 0L;
}
