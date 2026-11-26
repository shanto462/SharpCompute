// SharpCompute.Tests.Conformance.ConformanceBool3Operators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceBool3Operators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 17;

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

kernel void SharpCompute_Tests_Conformance_ConformanceBool3Operators(
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
    bool3 x = bool3(a[int(3u * uint(i) + 0u)] != 0, a[int(3u * uint(i) + 1u)] != 0, a[int(3u * uint(i) + 2u)] != 0);
    bool3 y = bool3(b[int(3u * uint(i) + 0u)] != 0, b[int(3u * uint(i) + 1u)] != 0, b[int(3u * uint(i) + 2u)] != 0);
    bool3 z = bool3(c[int(3u * uint(i) + 0u)] != 0, c[int(3u * uint(i) + 1u)] != 0, c[int(3u * uint(i) + 2u)] != 0);
    int n = s[i];
    bool3 v0 = !x;
    output[int(uint(o) + 0u)] = (v0.x) ? 1L : 0L;
    output[int(uint(o) + 1u)] = (v0.y) ? 1L : 0L;
    output[int(uint(o) + 2u)] = (v0.z) ? 1L : 0L;
    bool3 v3 = x & y;
    output[int(uint(o) + 3u)] = (v3.x) ? 1L : 0L;
    output[int(uint(o) + 4u)] = (v3.y) ? 1L : 0L;
    output[int(uint(o) + 5u)] = (v3.z) ? 1L : 0L;
    bool3 v6 = x | y;
    output[int(uint(o) + 6u)] = (v6.x) ? 1L : 0L;
    output[int(uint(o) + 7u)] = (v6.y) ? 1L : 0L;
    output[int(uint(o) + 8u)] = (v6.z) ? 1L : 0L;
    bool3 v9 = x ^ y;
    output[int(uint(o) + 9u)] = (v9.x) ? 1L : 0L;
    output[int(uint(o) + 10u)] = (v9.y) ? 1L : 0L;
    output[int(uint(o) + 11u)] = (v9.z) ? 1L : 0L;
    output[int(uint(o) + 12u)] = (metal::any(x)) ? 1L : 0L;
    output[int(uint(o) + 13u)] = (metal::all(x)) ? 1L : 0L;
    bool3 v14 = metal::select(z, y, x);
    output[int(uint(o) + 14u)] = (v14.x) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (v14.y) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (v14.z) ? 1L : 0L;
}
