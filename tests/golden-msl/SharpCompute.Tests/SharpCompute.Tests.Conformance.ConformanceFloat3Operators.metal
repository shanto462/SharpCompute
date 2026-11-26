// SharpCompute.Tests.Conformance.ConformanceFloat3Operators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceFloat3Operators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 54;

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

kernel void SharpCompute_Tests_Conformance_ConformanceFloat3Operators(
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
    float3 x = float3(a[int(3u * uint(i) + 0u)], a[int(3u * uint(i) + 1u)], a[int(3u * uint(i) + 2u)]);
    float3 y = float3(b[int(3u * uint(i) + 0u)], b[int(3u * uint(i) + 1u)], b[int(3u * uint(i) + 2u)]);
    float3 z = float3(c[int(3u * uint(i) + 0u)], c[int(3u * uint(i) + 1u)], c[int(3u * uint(i) + 2u)]);
    int n = s[i];
    float3 v0 = -x;
    output[int(uint(o) + 0u)] = long(as_type<uint>(v0.x));
    output[int(uint(o) + 1u)] = long(as_type<uint>(v0.y));
    output[int(uint(o) + 2u)] = long(as_type<uint>(v0.z));
    float3 v3 = x + y;
    output[int(uint(o) + 3u)] = long(as_type<uint>(v3.x));
    output[int(uint(o) + 4u)] = long(as_type<uint>(v3.y));
    output[int(uint(o) + 5u)] = long(as_type<uint>(v3.z));
    float3 v6 = x - y;
    output[int(uint(o) + 6u)] = long(as_type<uint>(v6.x));
    output[int(uint(o) + 7u)] = long(as_type<uint>(v6.y));
    output[int(uint(o) + 8u)] = long(as_type<uint>(v6.z));
    float3 v9 = x * y;
    output[int(uint(o) + 9u)] = long(as_type<uint>(v9.x));
    output[int(uint(o) + 10u)] = long(as_type<uint>(v9.y));
    output[int(uint(o) + 11u)] = long(as_type<uint>(v9.z));
    float3 v12 = x / y;
    output[int(uint(o) + 12u)] = long(as_type<uint>(v12.x));
    output[int(uint(o) + 13u)] = long(as_type<uint>(v12.y));
    output[int(uint(o) + 14u)] = long(as_type<uint>(v12.z));
    float3 v15 = metal::fmod(x, y);
    output[int(uint(o) + 15u)] = long(as_type<uint>(v15.x));
    output[int(uint(o) + 16u)] = long(as_type<uint>(v15.y));
    output[int(uint(o) + 17u)] = long(as_type<uint>(v15.z));
    float3 v18 = x + y.x;
    output[int(uint(o) + 18u)] = long(as_type<uint>(v18.x));
    output[int(uint(o) + 19u)] = long(as_type<uint>(v18.y));
    output[int(uint(o) + 20u)] = long(as_type<uint>(v18.z));
    float3 v21 = y.x - x;
    output[int(uint(o) + 21u)] = long(as_type<uint>(v21.x));
    output[int(uint(o) + 22u)] = long(as_type<uint>(v21.y));
    output[int(uint(o) + 23u)] = long(as_type<uint>(v21.z));
    float3 v24 = x * y.x;
    output[int(uint(o) + 24u)] = long(as_type<uint>(v24.x));
    output[int(uint(o) + 25u)] = long(as_type<uint>(v24.y));
    output[int(uint(o) + 26u)] = long(as_type<uint>(v24.z));
    bool3 v27 = x < y;
    output[int(uint(o) + 27u)] = (v27.x) ? 1L : 0L;
    output[int(uint(o) + 28u)] = (v27.y) ? 1L : 0L;
    output[int(uint(o) + 29u)] = (v27.z) ? 1L : 0L;
    bool3 v30 = x <= y;
    output[int(uint(o) + 30u)] = (v30.x) ? 1L : 0L;
    output[int(uint(o) + 31u)] = (v30.y) ? 1L : 0L;
    output[int(uint(o) + 32u)] = (v30.z) ? 1L : 0L;
    bool3 v33 = x > y;
    output[int(uint(o) + 33u)] = (v33.x) ? 1L : 0L;
    output[int(uint(o) + 34u)] = (v33.y) ? 1L : 0L;
    output[int(uint(o) + 35u)] = (v33.z) ? 1L : 0L;
    bool3 v36 = x >= y;
    output[int(uint(o) + 36u)] = (v36.x) ? 1L : 0L;
    output[int(uint(o) + 37u)] = (v36.y) ? 1L : 0L;
    output[int(uint(o) + 38u)] = (v36.z) ? 1L : 0L;
    bool3 v39 = (float3(x) == float3(y));
    output[int(uint(o) + 39u)] = (v39.x) ? 1L : 0L;
    output[int(uint(o) + 40u)] = (v39.y) ? 1L : 0L;
    output[int(uint(o) + 41u)] = (v39.z) ? 1L : 0L;
    bool3 v42 = (float3(x) != float3(y));
    output[int(uint(o) + 42u)] = (v42.x) ? 1L : 0L;
    output[int(uint(o) + 43u)] = (v42.y) ? 1L : 0L;
    output[int(uint(o) + 44u)] = (v42.z) ? 1L : 0L;
    float3 v45 = metal::select(z, x, x < y);
    output[int(uint(o) + 45u)] = long(as_type<uint>(v45.x));
    output[int(uint(o) + 46u)] = long(as_type<uint>(v45.y));
    output[int(uint(o) + 47u)] = long(as_type<uint>(v45.z));
    float3 t = x;
    t += y;
    float3 v48 = t;
    output[int(uint(o) + 48u)] = long(as_type<uint>(v48.x));
    output[int(uint(o) + 49u)] = long(as_type<uint>(v48.y));
    output[int(uint(o) + 50u)] = long(as_type<uint>(v48.z));
    float3 t2 = x;
    t2 *= y;
    float3 v51 = t2;
    output[int(uint(o) + 51u)] = long(as_type<uint>(v51.x));
    output[int(uint(o) + 52u)] = long(as_type<uint>(v51.y));
    output[int(uint(o) + 53u)] = long(as_type<uint>(v51.z));
}
