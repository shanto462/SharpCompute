// SharpCompute.Tests.Conformance.ConformanceFloat4Operators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceFloat4Operators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 72;

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

kernel void SharpCompute_Tests_Conformance_ConformanceFloat4Operators(
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
    float4 v0 = -x;
    output[int(uint(o) + 0u)] = long(as_type<uint>(v0.x));
    output[int(uint(o) + 1u)] = long(as_type<uint>(v0.y));
    output[int(uint(o) + 2u)] = long(as_type<uint>(v0.z));
    output[int(uint(o) + 3u)] = long(as_type<uint>(v0.w));
    float4 v4 = x + y;
    output[int(uint(o) + 4u)] = long(as_type<uint>(v4.x));
    output[int(uint(o) + 5u)] = long(as_type<uint>(v4.y));
    output[int(uint(o) + 6u)] = long(as_type<uint>(v4.z));
    output[int(uint(o) + 7u)] = long(as_type<uint>(v4.w));
    float4 v8 = x - y;
    output[int(uint(o) + 8u)] = long(as_type<uint>(v8.x));
    output[int(uint(o) + 9u)] = long(as_type<uint>(v8.y));
    output[int(uint(o) + 10u)] = long(as_type<uint>(v8.z));
    output[int(uint(o) + 11u)] = long(as_type<uint>(v8.w));
    float4 v12 = x * y;
    output[int(uint(o) + 12u)] = long(as_type<uint>(v12.x));
    output[int(uint(o) + 13u)] = long(as_type<uint>(v12.y));
    output[int(uint(o) + 14u)] = long(as_type<uint>(v12.z));
    output[int(uint(o) + 15u)] = long(as_type<uint>(v12.w));
    float4 v16 = x / y;
    output[int(uint(o) + 16u)] = long(as_type<uint>(v16.x));
    output[int(uint(o) + 17u)] = long(as_type<uint>(v16.y));
    output[int(uint(o) + 18u)] = long(as_type<uint>(v16.z));
    output[int(uint(o) + 19u)] = long(as_type<uint>(v16.w));
    float4 v20 = metal::fmod(x, y);
    output[int(uint(o) + 20u)] = long(as_type<uint>(v20.x));
    output[int(uint(o) + 21u)] = long(as_type<uint>(v20.y));
    output[int(uint(o) + 22u)] = long(as_type<uint>(v20.z));
    output[int(uint(o) + 23u)] = long(as_type<uint>(v20.w));
    float4 v24 = x + y.x;
    output[int(uint(o) + 24u)] = long(as_type<uint>(v24.x));
    output[int(uint(o) + 25u)] = long(as_type<uint>(v24.y));
    output[int(uint(o) + 26u)] = long(as_type<uint>(v24.z));
    output[int(uint(o) + 27u)] = long(as_type<uint>(v24.w));
    float4 v28 = y.x - x;
    output[int(uint(o) + 28u)] = long(as_type<uint>(v28.x));
    output[int(uint(o) + 29u)] = long(as_type<uint>(v28.y));
    output[int(uint(o) + 30u)] = long(as_type<uint>(v28.z));
    output[int(uint(o) + 31u)] = long(as_type<uint>(v28.w));
    float4 v32 = x * y.x;
    output[int(uint(o) + 32u)] = long(as_type<uint>(v32.x));
    output[int(uint(o) + 33u)] = long(as_type<uint>(v32.y));
    output[int(uint(o) + 34u)] = long(as_type<uint>(v32.z));
    output[int(uint(o) + 35u)] = long(as_type<uint>(v32.w));
    bool4 v36 = x < y;
    output[int(uint(o) + 36u)] = (v36.x) ? 1L : 0L;
    output[int(uint(o) + 37u)] = (v36.y) ? 1L : 0L;
    output[int(uint(o) + 38u)] = (v36.z) ? 1L : 0L;
    output[int(uint(o) + 39u)] = (v36.w) ? 1L : 0L;
    bool4 v40 = x <= y;
    output[int(uint(o) + 40u)] = (v40.x) ? 1L : 0L;
    output[int(uint(o) + 41u)] = (v40.y) ? 1L : 0L;
    output[int(uint(o) + 42u)] = (v40.z) ? 1L : 0L;
    output[int(uint(o) + 43u)] = (v40.w) ? 1L : 0L;
    bool4 v44 = x > y;
    output[int(uint(o) + 44u)] = (v44.x) ? 1L : 0L;
    output[int(uint(o) + 45u)] = (v44.y) ? 1L : 0L;
    output[int(uint(o) + 46u)] = (v44.z) ? 1L : 0L;
    output[int(uint(o) + 47u)] = (v44.w) ? 1L : 0L;
    bool4 v48 = x >= y;
    output[int(uint(o) + 48u)] = (v48.x) ? 1L : 0L;
    output[int(uint(o) + 49u)] = (v48.y) ? 1L : 0L;
    output[int(uint(o) + 50u)] = (v48.z) ? 1L : 0L;
    output[int(uint(o) + 51u)] = (v48.w) ? 1L : 0L;
    bool4 v52 = (float4(x) == float4(y));
    output[int(uint(o) + 52u)] = (v52.x) ? 1L : 0L;
    output[int(uint(o) + 53u)] = (v52.y) ? 1L : 0L;
    output[int(uint(o) + 54u)] = (v52.z) ? 1L : 0L;
    output[int(uint(o) + 55u)] = (v52.w) ? 1L : 0L;
    bool4 v56 = (float4(x) != float4(y));
    output[int(uint(o) + 56u)] = (v56.x) ? 1L : 0L;
    output[int(uint(o) + 57u)] = (v56.y) ? 1L : 0L;
    output[int(uint(o) + 58u)] = (v56.z) ? 1L : 0L;
    output[int(uint(o) + 59u)] = (v56.w) ? 1L : 0L;
    float4 v60 = metal::select(z, x, x < y);
    output[int(uint(o) + 60u)] = long(as_type<uint>(v60.x));
    output[int(uint(o) + 61u)] = long(as_type<uint>(v60.y));
    output[int(uint(o) + 62u)] = long(as_type<uint>(v60.z));
    output[int(uint(o) + 63u)] = long(as_type<uint>(v60.w));
    float4 t = x;
    t += y;
    float4 v64 = t;
    output[int(uint(o) + 64u)] = long(as_type<uint>(v64.x));
    output[int(uint(o) + 65u)] = long(as_type<uint>(v64.y));
    output[int(uint(o) + 66u)] = long(as_type<uint>(v64.z));
    output[int(uint(o) + 67u)] = long(as_type<uint>(v64.w));
    float4 t2 = x;
    t2 *= y;
    float4 v68 = t2;
    output[int(uint(o) + 68u)] = long(as_type<uint>(v68.x));
    output[int(uint(o) + 69u)] = long(as_type<uint>(v68.y));
    output[int(uint(o) + 70u)] = long(as_type<uint>(v68.z));
    output[int(uint(o) + 71u)] = long(as_type<uint>(v68.w));
}
