// SharpCompute.Tests.Conformance.ConformanceUInt3Operators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceUInt3Operators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 75;

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

kernel void SharpCompute_Tests_Conformance_ConformanceUInt3Operators(
    const device uint* a [[buffer(0)]],
    const device uint* b [[buffer(1)]],
    const device uint* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    uint3 x = uint3(a[int(3u * uint(i) + 0u)], a[int(3u * uint(i) + 1u)], a[int(3u * uint(i) + 2u)]);
    uint3 y = uint3(b[int(3u * uint(i) + 0u)], b[int(3u * uint(i) + 1u)], b[int(3u * uint(i) + 2u)]);
    uint3 z = uint3(c[int(3u * uint(i) + 0u)], c[int(3u * uint(i) + 1u)], c[int(3u * uint(i) + 2u)]);
    int n = s[i];
    uint3 v0 = x + y;
    output[int(uint(o) + 0u)] = long(v0.x);
    output[int(uint(o) + 1u)] = long(v0.y);
    output[int(uint(o) + 2u)] = long(v0.z);
    uint3 v3 = x - y;
    output[int(uint(o) + 3u)] = long(v3.x);
    output[int(uint(o) + 4u)] = long(v3.y);
    output[int(uint(o) + 5u)] = long(v3.z);
    uint3 v6 = x * y;
    output[int(uint(o) + 6u)] = long(v6.x);
    output[int(uint(o) + 7u)] = long(v6.y);
    output[int(uint(o) + 8u)] = long(v6.z);
    uint3 d = metal::select(y, uint3(1u), (uint3(y) == 0u));
    uint3 v9 = x / d;
    output[int(uint(o) + 9u)] = long(v9.x);
    output[int(uint(o) + 10u)] = long(v9.y);
    output[int(uint(o) + 11u)] = long(v9.z);
    uint3 v12 = x % d;
    output[int(uint(o) + 12u)] = long(v12.x);
    output[int(uint(o) + 13u)] = long(v12.y);
    output[int(uint(o) + 14u)] = long(v12.z);
    uint3 v15 = x & y;
    output[int(uint(o) + 15u)] = long(v15.x);
    output[int(uint(o) + 16u)] = long(v15.y);
    output[int(uint(o) + 17u)] = long(v15.z);
    uint3 v18 = x | y;
    output[int(uint(o) + 18u)] = long(v18.x);
    output[int(uint(o) + 19u)] = long(v18.y);
    output[int(uint(o) + 20u)] = long(v18.z);
    uint3 v21 = x ^ y;
    output[int(uint(o) + 21u)] = long(v21.x);
    output[int(uint(o) + 22u)] = long(v21.y);
    output[int(uint(o) + 23u)] = long(v21.z);
    uint3 v24 = ~x;
    output[int(uint(o) + 24u)] = long(v24.x);
    output[int(uint(o) + 25u)] = long(v24.y);
    output[int(uint(o) + 26u)] = long(v24.z);
    uint3 v27 = x << y;
    output[int(uint(o) + 27u)] = long(v27.x);
    output[int(uint(o) + 28u)] = long(v27.y);
    output[int(uint(o) + 29u)] = long(v27.z);
    uint3 v30 = x >> y;
    output[int(uint(o) + 30u)] = long(v30.x);
    output[int(uint(o) + 31u)] = long(v30.y);
    output[int(uint(o) + 32u)] = long(v30.z);
    uint3 v33 = x << n;
    output[int(uint(o) + 33u)] = long(v33.x);
    output[int(uint(o) + 34u)] = long(v33.y);
    output[int(uint(o) + 35u)] = long(v33.z);
    uint3 v36 = x >> n;
    output[int(uint(o) + 36u)] = long(v36.x);
    output[int(uint(o) + 37u)] = long(v36.y);
    output[int(uint(o) + 38u)] = long(v36.z);
    uint3 v39 = x + y.x;
    output[int(uint(o) + 39u)] = long(v39.x);
    output[int(uint(o) + 40u)] = long(v39.y);
    output[int(uint(o) + 41u)] = long(v39.z);
    uint3 v42 = y.x - x;
    output[int(uint(o) + 42u)] = long(v42.x);
    output[int(uint(o) + 43u)] = long(v42.y);
    output[int(uint(o) + 44u)] = long(v42.z);
    uint3 v45 = x * y.x;
    output[int(uint(o) + 45u)] = long(v45.x);
    output[int(uint(o) + 46u)] = long(v45.y);
    output[int(uint(o) + 47u)] = long(v45.z);
    bool3 v48 = x < y;
    output[int(uint(o) + 48u)] = (v48.x) ? 1L : 0L;
    output[int(uint(o) + 49u)] = (v48.y) ? 1L : 0L;
    output[int(uint(o) + 50u)] = (v48.z) ? 1L : 0L;
    bool3 v51 = x <= y;
    output[int(uint(o) + 51u)] = (v51.x) ? 1L : 0L;
    output[int(uint(o) + 52u)] = (v51.y) ? 1L : 0L;
    output[int(uint(o) + 53u)] = (v51.z) ? 1L : 0L;
    bool3 v54 = x > y;
    output[int(uint(o) + 54u)] = (v54.x) ? 1L : 0L;
    output[int(uint(o) + 55u)] = (v54.y) ? 1L : 0L;
    output[int(uint(o) + 56u)] = (v54.z) ? 1L : 0L;
    bool3 v57 = x >= y;
    output[int(uint(o) + 57u)] = (v57.x) ? 1L : 0L;
    output[int(uint(o) + 58u)] = (v57.y) ? 1L : 0L;
    output[int(uint(o) + 59u)] = (v57.z) ? 1L : 0L;
    bool3 v60 = (uint3(x) == uint3(y));
    output[int(uint(o) + 60u)] = (v60.x) ? 1L : 0L;
    output[int(uint(o) + 61u)] = (v60.y) ? 1L : 0L;
    output[int(uint(o) + 62u)] = (v60.z) ? 1L : 0L;
    bool3 v63 = (uint3(x) != uint3(y));
    output[int(uint(o) + 63u)] = (v63.x) ? 1L : 0L;
    output[int(uint(o) + 64u)] = (v63.y) ? 1L : 0L;
    output[int(uint(o) + 65u)] = (v63.z) ? 1L : 0L;
    uint3 v66 = metal::select(z, x, x < y);
    output[int(uint(o) + 66u)] = long(v66.x);
    output[int(uint(o) + 67u)] = long(v66.y);
    output[int(uint(o) + 68u)] = long(v66.z);
    uint3 t = x;
    t += y;
    uint3 v69 = t;
    output[int(uint(o) + 69u)] = long(v69.x);
    output[int(uint(o) + 70u)] = long(v69.y);
    output[int(uint(o) + 71u)] = long(v69.z);
    uint3 t2 = x;
    t2 *= y;
    uint3 v72 = t2;
    output[int(uint(o) + 72u)] = long(v72.x);
    output[int(uint(o) + 73u)] = long(v72.y);
    output[int(uint(o) + 74u)] = long(v72.z);
}
