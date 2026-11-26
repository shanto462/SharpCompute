// SharpCompute.Tests.Conformance.ConformanceFloat2Operators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceFloat2Operators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 36;

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

kernel void SharpCompute_Tests_Conformance_ConformanceFloat2Operators(
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
    float2 x = float2(a[int(2u * uint(i) + 0u)], a[int(2u * uint(i) + 1u)]);
    float2 y = float2(b[int(2u * uint(i) + 0u)], b[int(2u * uint(i) + 1u)]);
    float2 z = float2(c[int(2u * uint(i) + 0u)], c[int(2u * uint(i) + 1u)]);
    int n = s[i];
    float2 v0 = -x;
    output[int(uint(o) + 0u)] = long(as_type<uint>(v0.x));
    output[int(uint(o) + 1u)] = long(as_type<uint>(v0.y));
    float2 v2 = x + y;
    output[int(uint(o) + 2u)] = long(as_type<uint>(v2.x));
    output[int(uint(o) + 3u)] = long(as_type<uint>(v2.y));
    float2 v4 = x - y;
    output[int(uint(o) + 4u)] = long(as_type<uint>(v4.x));
    output[int(uint(o) + 5u)] = long(as_type<uint>(v4.y));
    float2 v6 = x * y;
    output[int(uint(o) + 6u)] = long(as_type<uint>(v6.x));
    output[int(uint(o) + 7u)] = long(as_type<uint>(v6.y));
    float2 v8 = x / y;
    output[int(uint(o) + 8u)] = long(as_type<uint>(v8.x));
    output[int(uint(o) + 9u)] = long(as_type<uint>(v8.y));
    float2 v10 = metal::fmod(x, y);
    output[int(uint(o) + 10u)] = long(as_type<uint>(v10.x));
    output[int(uint(o) + 11u)] = long(as_type<uint>(v10.y));
    float2 v12 = x + y.x;
    output[int(uint(o) + 12u)] = long(as_type<uint>(v12.x));
    output[int(uint(o) + 13u)] = long(as_type<uint>(v12.y));
    float2 v14 = y.x - x;
    output[int(uint(o) + 14u)] = long(as_type<uint>(v14.x));
    output[int(uint(o) + 15u)] = long(as_type<uint>(v14.y));
    float2 v16 = x * y.x;
    output[int(uint(o) + 16u)] = long(as_type<uint>(v16.x));
    output[int(uint(o) + 17u)] = long(as_type<uint>(v16.y));
    bool2 v18 = x < y;
    output[int(uint(o) + 18u)] = (v18.x) ? 1L : 0L;
    output[int(uint(o) + 19u)] = (v18.y) ? 1L : 0L;
    bool2 v20 = x <= y;
    output[int(uint(o) + 20u)] = (v20.x) ? 1L : 0L;
    output[int(uint(o) + 21u)] = (v20.y) ? 1L : 0L;
    bool2 v22 = x > y;
    output[int(uint(o) + 22u)] = (v22.x) ? 1L : 0L;
    output[int(uint(o) + 23u)] = (v22.y) ? 1L : 0L;
    bool2 v24 = x >= y;
    output[int(uint(o) + 24u)] = (v24.x) ? 1L : 0L;
    output[int(uint(o) + 25u)] = (v24.y) ? 1L : 0L;
    bool2 v26 = (float2(x) == float2(y));
    output[int(uint(o) + 26u)] = (v26.x) ? 1L : 0L;
    output[int(uint(o) + 27u)] = (v26.y) ? 1L : 0L;
    bool2 v28 = (float2(x) != float2(y));
    output[int(uint(o) + 28u)] = (v28.x) ? 1L : 0L;
    output[int(uint(o) + 29u)] = (v28.y) ? 1L : 0L;
    float2 v30 = metal::select(z, x, x < y);
    output[int(uint(o) + 30u)] = long(as_type<uint>(v30.x));
    output[int(uint(o) + 31u)] = long(as_type<uint>(v30.y));
    float2 t = x;
    t += y;
    float2 v32 = t;
    output[int(uint(o) + 32u)] = long(as_type<uint>(v32.x));
    output[int(uint(o) + 33u)] = long(as_type<uint>(v32.y));
    float2 t2 = x;
    t2 *= y;
    float2 v34 = t2;
    output[int(uint(o) + 34u)] = long(as_type<uint>(v34.x));
    output[int(uint(o) + 35u)] = long(as_type<uint>(v34.y));
}
