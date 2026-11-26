// SharpCompute.Tests.Conformance.ConformanceFloatOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceFloatOperators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 21;

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

kernel void SharpCompute_Tests_Conformance_ConformanceFloatOperators(
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
    float x = a[i];
    float y = b[i];
    float z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = long(as_type<uint>(-x));
    output[int(uint(o) + 1u)] = long(as_type<uint>(+x));
    output[int(uint(o) + 2u)] = long(as_type<uint>(x + y));
    output[int(uint(o) + 3u)] = long(as_type<uint>(x - y));
    output[int(uint(o) + 4u)] = long(as_type<uint>(x * y));
    output[int(uint(o) + 5u)] = long(as_type<uint>(x / y));
    output[int(uint(o) + 6u)] = long(as_type<uint>(metal::fmod(x, y)));
    output[int(uint(o) + 7u)] = (x == y) ? 1L : 0L;
    output[int(uint(o) + 8u)] = (x != y) ? 1L : 0L;
    output[int(uint(o) + 9u)] = (x < y) ? 1L : 0L;
    output[int(uint(o) + 10u)] = (x <= y) ? 1L : 0L;
    output[int(uint(o) + 11u)] = (x > y) ? 1L : 0L;
    output[int(uint(o) + 12u)] = (x >= y) ? 1L : 0L;
    float t0 = x;
    t0 += y;
    output[int(uint(o) + 13u)] = long(as_type<uint>(t0));
    float t1 = x;
    t1 -= y;
    output[int(uint(o) + 14u)] = long(as_type<uint>(t1));
    float t2 = x;
    t2 *= y;
    output[int(uint(o) + 15u)] = long(as_type<uint>(t2));
    float t3 = x;
    t3 /= y;
    output[int(uint(o) + 16u)] = long(as_type<uint>(t3));
    float t4 = x;
    t4 = metal::fmod(t4, y);
    output[int(uint(o) + 17u)] = long(as_type<uint>(t4));
    float t5 = x;
    t5++;
    output[int(uint(o) + 18u)] = long(as_type<uint>(t5));
    float t6 = x;
    t6--;
    output[int(uint(o) + 19u)] = long(as_type<uint>(t6));
    output[int(uint(o) + 20u)] = long(as_type<uint>(x * y + z));
}
