// SharpCompute.Tests.Conformance.ConformanceULongOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceULongOperators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 38;

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

kernel void SharpCompute_Tests_Conformance_ConformanceULongOperators(
    const device ulong* a [[buffer(0)]],
    const device ulong* b [[buffer(1)]],
    const device ulong* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    ulong x = a[i];
    ulong y = b[i];
    ulong z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = long(+x);
    output[int(uint(o) + 1u)] = long(~x);
    output[int(uint(o) + 2u)] = long(x + y);
    output[int(uint(o) + 3u)] = long(x - y);
    output[int(uint(o) + 4u)] = long(x * y);
    output[int(uint(o) + 5u)] = long((y == 0UL) ? 0UL : x / y);
    output[int(uint(o) + 6u)] = long((y == 0UL) ? 0UL : x % y);
    output[int(uint(o) + 7u)] = long(x & y);
    output[int(uint(o) + 8u)] = long(x | y);
    output[int(uint(o) + 9u)] = long(x ^ y);
    output[int(uint(o) + 10u)] = long(x << n);
    output[int(uint(o) + 11u)] = long(x >> n);
    output[int(uint(o) + 12u)] = long(x >> n);
    output[int(uint(o) + 13u)] = (x == y) ? 1L : 0L;
    output[int(uint(o) + 14u)] = (x != y) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (x < y) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (x <= y) ? 1L : 0L;
    output[int(uint(o) + 17u)] = (x > y) ? 1L : 0L;
    output[int(uint(o) + 18u)] = (x >= y) ? 1L : 0L;
    ulong t0 = x;
    t0 += y;
    output[int(uint(o) + 19u)] = long(t0);
    ulong t1 = x;
    t1 -= y;
    output[int(uint(o) + 20u)] = long(t1);
    ulong t2 = x;
    t2 *= y;
    output[int(uint(o) + 21u)] = long(t2);
    ulong t3 = x;
    t3 &= y;
    output[int(uint(o) + 22u)] = long(t3);
    ulong t4 = x;
    t4 |= y;
    output[int(uint(o) + 23u)] = long(t4);
    ulong t5 = x;
    t5 ^= y;
    output[int(uint(o) + 24u)] = long(t5);
    ulong t6 = x;
    if (!(y == 0UL))
    {
        t6 /= y;
    }
    output[int(uint(o) + 25u)] = long(t6);
    ulong t7 = x;
    if (!(y == 0UL))
    {
        t7 %= y;
    }
    output[int(uint(o) + 26u)] = long(t7);
    ulong t8 = x;
    t8 <<= n;
    output[int(uint(o) + 27u)] = long(t8);
    ulong t9 = x;
    t9 >>= n;
    output[int(uint(o) + 28u)] = long(t9);
    ulong t10 = x;
    t10 >>= n;
    output[int(uint(o) + 29u)] = long(t10);
    ulong t11 = x;
    ulong u11 = t11++;
    output[int(uint(o) + 30u)] = long(u11);
    output[int(uint(o) + 31u)] = long(t11);
    ulong t12 = x;
    ulong u12 = t12--;
    output[int(uint(o) + 32u)] = long(u12);
    output[int(uint(o) + 33u)] = long(t12);
    ulong t13 = x;
    ulong u13 = ++t13;
    output[int(uint(o) + 34u)] = long(u13);
    output[int(uint(o) + 35u)] = long(t13);
    ulong t14 = x;
    ulong u14 = --t14;
    output[int(uint(o) + 36u)] = long(u14);
    output[int(uint(o) + 37u)] = long(t14);
}
