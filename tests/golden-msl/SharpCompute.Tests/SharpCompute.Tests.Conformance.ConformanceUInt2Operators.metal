// SharpCompute.Tests.Conformance.ConformanceUInt2Operators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceUInt2Operators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 50;

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

kernel void SharpCompute_Tests_Conformance_ConformanceUInt2Operators(
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
    uint2 x = uint2(a[int(2u * uint(i) + 0u)], a[int(2u * uint(i) + 1u)]);
    uint2 y = uint2(b[int(2u * uint(i) + 0u)], b[int(2u * uint(i) + 1u)]);
    uint2 z = uint2(c[int(2u * uint(i) + 0u)], c[int(2u * uint(i) + 1u)]);
    int n = s[i];
    uint2 v0 = x + y;
    output[int(uint(o) + 0u)] = long(v0.x);
    output[int(uint(o) + 1u)] = long(v0.y);
    uint2 v2 = x - y;
    output[int(uint(o) + 2u)] = long(v2.x);
    output[int(uint(o) + 3u)] = long(v2.y);
    uint2 v4 = x * y;
    output[int(uint(o) + 4u)] = long(v4.x);
    output[int(uint(o) + 5u)] = long(v4.y);
    uint2 d = metal::select(y, uint2(1u), (uint2(y) == 0u));
    uint2 v6 = x / d;
    output[int(uint(o) + 6u)] = long(v6.x);
    output[int(uint(o) + 7u)] = long(v6.y);
    uint2 v8 = x % d;
    output[int(uint(o) + 8u)] = long(v8.x);
    output[int(uint(o) + 9u)] = long(v8.y);
    uint2 v10 = x & y;
    output[int(uint(o) + 10u)] = long(v10.x);
    output[int(uint(o) + 11u)] = long(v10.y);
    uint2 v12 = x | y;
    output[int(uint(o) + 12u)] = long(v12.x);
    output[int(uint(o) + 13u)] = long(v12.y);
    uint2 v14 = x ^ y;
    output[int(uint(o) + 14u)] = long(v14.x);
    output[int(uint(o) + 15u)] = long(v14.y);
    uint2 v16 = ~x;
    output[int(uint(o) + 16u)] = long(v16.x);
    output[int(uint(o) + 17u)] = long(v16.y);
    uint2 v18 = x << y;
    output[int(uint(o) + 18u)] = long(v18.x);
    output[int(uint(o) + 19u)] = long(v18.y);
    uint2 v20 = x >> y;
    output[int(uint(o) + 20u)] = long(v20.x);
    output[int(uint(o) + 21u)] = long(v20.y);
    uint2 v22 = x << n;
    output[int(uint(o) + 22u)] = long(v22.x);
    output[int(uint(o) + 23u)] = long(v22.y);
    uint2 v24 = x >> n;
    output[int(uint(o) + 24u)] = long(v24.x);
    output[int(uint(o) + 25u)] = long(v24.y);
    uint2 v26 = x + y.x;
    output[int(uint(o) + 26u)] = long(v26.x);
    output[int(uint(o) + 27u)] = long(v26.y);
    uint2 v28 = y.x - x;
    output[int(uint(o) + 28u)] = long(v28.x);
    output[int(uint(o) + 29u)] = long(v28.y);
    uint2 v30 = x * y.x;
    output[int(uint(o) + 30u)] = long(v30.x);
    output[int(uint(o) + 31u)] = long(v30.y);
    bool2 v32 = x < y;
    output[int(uint(o) + 32u)] = (v32.x) ? 1L : 0L;
    output[int(uint(o) + 33u)] = (v32.y) ? 1L : 0L;
    bool2 v34 = x <= y;
    output[int(uint(o) + 34u)] = (v34.x) ? 1L : 0L;
    output[int(uint(o) + 35u)] = (v34.y) ? 1L : 0L;
    bool2 v36 = x > y;
    output[int(uint(o) + 36u)] = (v36.x) ? 1L : 0L;
    output[int(uint(o) + 37u)] = (v36.y) ? 1L : 0L;
    bool2 v38 = x >= y;
    output[int(uint(o) + 38u)] = (v38.x) ? 1L : 0L;
    output[int(uint(o) + 39u)] = (v38.y) ? 1L : 0L;
    bool2 v40 = (uint2(x) == uint2(y));
    output[int(uint(o) + 40u)] = (v40.x) ? 1L : 0L;
    output[int(uint(o) + 41u)] = (v40.y) ? 1L : 0L;
    bool2 v42 = (uint2(x) != uint2(y));
    output[int(uint(o) + 42u)] = (v42.x) ? 1L : 0L;
    output[int(uint(o) + 43u)] = (v42.y) ? 1L : 0L;
    uint2 v44 = metal::select(z, x, x < y);
    output[int(uint(o) + 44u)] = long(v44.x);
    output[int(uint(o) + 45u)] = long(v44.y);
    uint2 t = x;
    t += y;
    uint2 v46 = t;
    output[int(uint(o) + 46u)] = long(v46.x);
    output[int(uint(o) + 47u)] = long(v46.y);
    uint2 t2 = x;
    t2 *= y;
    uint2 v48 = t2;
    output[int(uint(o) + 48u)] = long(v48.x);
    output[int(uint(o) + 49u)] = long(v48.y);
}
