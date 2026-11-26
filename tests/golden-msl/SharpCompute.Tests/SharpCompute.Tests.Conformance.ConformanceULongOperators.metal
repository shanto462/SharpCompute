// SharpCompute.Tests.Conformance.ConformanceULongOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceULongOperators, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline ulong __ms_nonzero(ulong x) { return __builtin_constant_p(x) ? x : x | ulong(metal::reverse_bits(x) == ulong(0)); }

constant int Slots = 53;

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
    output[int(uint(o) + 5u)] = long((y == 0UL) ? 0UL : x / __ms_nonzero(y));
    output[int(uint(o) + 6u)] = long((y == 0UL) ? 0UL : x % __ms_nonzero(y));
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
        t6 /= __ms_nonzero(y);
    }
    output[int(uint(o) + 25u)] = long(t6);
    ulong t7 = x;
    if (!(y == 0UL))
    {
        t7 %= __ms_nonzero(y);
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
    t11 <<= 17;
    output[int(uint(o) + 30u)] = long(t11);
    ulong t12 = x;
    t12 <<= 33;
    output[int(uint(o) + 31u)] = long(t12);
    ulong t13 = x;
    t13 <<= 21;
    output[int(uint(o) + 32u)] = long(t13);
    ulong t14 = x;
    t14 <<= 63;
    output[int(uint(o) + 33u)] = long(t14);
    ulong t15 = x;
    t15 >>= 17;
    output[int(uint(o) + 34u)] = long(t15);
    ulong t16 = x;
    t16 >>= 33;
    output[int(uint(o) + 35u)] = long(t16);
    ulong t17 = x;
    t17 >>= 21;
    output[int(uint(o) + 36u)] = long(t17);
    ulong t18 = x;
    t18 >>= 63;
    output[int(uint(o) + 37u)] = long(t18);
    ulong t19 = x;
    t19 >>= 17;
    output[int(uint(o) + 38u)] = long(t19);
    ulong t20 = x;
    t20 >>= 33;
    output[int(uint(o) + 39u)] = long(t20);
    ulong t21 = x;
    t21 >>= 21;
    output[int(uint(o) + 40u)] = long(t21);
    ulong t22 = x;
    t22 >>= 63;
    output[int(uint(o) + 41u)] = long(t22);
    output[int(uint(o) + 42u)] = long(x << 33);
    output[int(uint(o) + 43u)] = long(x >> 33);
    output[int(uint(o) + 44u)] = long(x >> 33);
    ulong t23 = x;
    ulong u23 = t23++;
    output[int(uint(o) + 45u)] = long(u23);
    output[int(uint(o) + 46u)] = long(t23);
    ulong t24 = x;
    ulong u24 = t24--;
    output[int(uint(o) + 47u)] = long(u24);
    output[int(uint(o) + 48u)] = long(t24);
    ulong t25 = x;
    ulong u25 = ++t25;
    output[int(uint(o) + 49u)] = long(u25);
    output[int(uint(o) + 50u)] = long(t25);
    ulong t26 = x;
    ulong u26 = --t26;
    output[int(uint(o) + 51u)] = long(u26);
    output[int(uint(o) + 52u)] = long(t26);
}
