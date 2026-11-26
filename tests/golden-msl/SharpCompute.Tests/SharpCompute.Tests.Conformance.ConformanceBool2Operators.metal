// SharpCompute.Tests.Conformance.ConformanceBool2Operators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceBool2Operators, compile options None
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

kernel void SharpCompute_Tests_Conformance_ConformanceBool2Operators(
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
    bool2 x = bool2(a[int(2u * uint(i) + 0u)] != 0, a[int(2u * uint(i) + 1u)] != 0);
    bool2 y = bool2(b[int(2u * uint(i) + 0u)] != 0, b[int(2u * uint(i) + 1u)] != 0);
    bool2 z = bool2(c[int(2u * uint(i) + 0u)] != 0, c[int(2u * uint(i) + 1u)] != 0);
    int n = s[i];
    bool2 v0 = !x;
    output[int(uint(o) + 0u)] = (v0.x) ? 1L : 0L;
    output[int(uint(o) + 1u)] = (v0.y) ? 1L : 0L;
    bool2 v2 = x & y;
    output[int(uint(o) + 2u)] = (v2.x) ? 1L : 0L;
    output[int(uint(o) + 3u)] = (v2.y) ? 1L : 0L;
    bool2 v4 = x | y;
    output[int(uint(o) + 4u)] = (v4.x) ? 1L : 0L;
    output[int(uint(o) + 5u)] = (v4.y) ? 1L : 0L;
    bool2 v6 = x ^ y;
    output[int(uint(o) + 6u)] = (v6.x) ? 1L : 0L;
    output[int(uint(o) + 7u)] = (v6.y) ? 1L : 0L;
    output[int(uint(o) + 8u)] = (metal::any(x)) ? 1L : 0L;
    output[int(uint(o) + 9u)] = (metal::all(x)) ? 1L : 0L;
    bool2 v10 = metal::select(z, y, x);
    output[int(uint(o) + 10u)] = (v10.x) ? 1L : 0L;
    output[int(uint(o) + 11u)] = (v10.y) ? 1L : 0L;
}
