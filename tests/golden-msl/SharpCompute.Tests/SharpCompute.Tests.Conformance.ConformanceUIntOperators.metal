// SharpCompute.Tests.Conformance.ConformanceUIntOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceUIntOperators, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline uint __ms_nonzero(uint x) { return x | uint(metal::reverse_bits(x) == uint(0)); }

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

kernel void SharpCompute_Tests_Conformance_ConformanceUIntOperators(
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
    uint x = a[i];
    uint y = b[i];
    uint z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = long(+x);
    output[int(uint(o) + 1u)] = (long(-ulong(x)));
    output[int(uint(o) + 2u)] = long(~x);
    output[int(uint(o) + 3u)] = long(x + y);
    output[int(uint(o) + 4u)] = long(x - y);
    output[int(uint(o) + 5u)] = long(x * y);
    output[int(uint(o) + 6u)] = long((y == 0u) ? 0u : x / __ms_nonzero(y));
    output[int(uint(o) + 7u)] = long((y == 0u) ? 0u : x % __ms_nonzero(y));
    output[int(uint(o) + 8u)] = long(x & y);
    output[int(uint(o) + 9u)] = long(x | y);
    output[int(uint(o) + 10u)] = long(x ^ y);
    output[int(uint(o) + 11u)] = long(x << n);
    output[int(uint(o) + 12u)] = long(x >> n);
    output[int(uint(o) + 13u)] = long(x >> n);
    output[int(uint(o) + 14u)] = (x == y) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (x != y) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (x < y) ? 1L : 0L;
    output[int(uint(o) + 17u)] = (x <= y) ? 1L : 0L;
    output[int(uint(o) + 18u)] = (x > y) ? 1L : 0L;
    output[int(uint(o) + 19u)] = (x >= y) ? 1L : 0L;
    uint t0 = x;
    t0 += y;
    output[int(uint(o) + 20u)] = long(t0);
    uint t1 = x;
    t1 -= y;
    output[int(uint(o) + 21u)] = long(t1);
    uint t2 = x;
    t2 *= y;
    output[int(uint(o) + 22u)] = long(t2);
    uint t3 = x;
    t3 &= y;
    output[int(uint(o) + 23u)] = long(t3);
    uint t4 = x;
    t4 |= y;
    output[int(uint(o) + 24u)] = long(t4);
    uint t5 = x;
    t5 ^= y;
    output[int(uint(o) + 25u)] = long(t5);
    uint t6 = x;
    if (!(y == 0u))
    {
        t6 /= __ms_nonzero(y);
    }
    output[int(uint(o) + 26u)] = long(t6);
    uint t7 = x;
    if (!(y == 0u))
    {
        t7 %= __ms_nonzero(y);
    }
    output[int(uint(o) + 27u)] = long(t7);
    uint t8 = x;
    t8 <<= n;
    output[int(uint(o) + 28u)] = long(t8);
    uint t9 = x;
    t9 >>= n;
    output[int(uint(o) + 29u)] = long(t9);
    uint t10 = x;
    t10 >>= n;
    output[int(uint(o) + 30u)] = long(t10);
    uint t11 = x;
    t11 <<= 17;
    output[int(uint(o) + 31u)] = long(t11);
    uint t12 = x;
    t12 <<= 1;
    output[int(uint(o) + 32u)] = long(t12);
    uint t13 = x;
    t13 <<= 21;
    output[int(uint(o) + 33u)] = long(t13);
    uint t14 = x;
    t14 <<= 31;
    output[int(uint(o) + 34u)] = long(t14);
    uint t15 = x;
    t15 >>= 17;
    output[int(uint(o) + 35u)] = long(t15);
    uint t16 = x;
    t16 >>= 1;
    output[int(uint(o) + 36u)] = long(t16);
    uint t17 = x;
    t17 >>= 21;
    output[int(uint(o) + 37u)] = long(t17);
    uint t18 = x;
    t18 >>= 31;
    output[int(uint(o) + 38u)] = long(t18);
    uint t19 = x;
    t19 >>= 17;
    output[int(uint(o) + 39u)] = long(t19);
    uint t20 = x;
    t20 >>= 1;
    output[int(uint(o) + 40u)] = long(t20);
    uint t21 = x;
    t21 >>= 21;
    output[int(uint(o) + 41u)] = long(t21);
    uint t22 = x;
    t22 >>= 31;
    output[int(uint(o) + 42u)] = long(t22);
    output[int(uint(o) + 43u)] = long(x << 1);
    output[int(uint(o) + 44u)] = long(x >> 1);
    output[int(uint(o) + 45u)] = long(x >> 1);
    uint t23 = x;
    uint u23 = t23++;
    output[int(uint(o) + 46u)] = long(u23);
    output[int(uint(o) + 47u)] = long(t23);
    uint t24 = x;
    uint u24 = t24--;
    output[int(uint(o) + 48u)] = long(u24);
    output[int(uint(o) + 49u)] = long(t24);
    uint t25 = x;
    uint u25 = ++t25;
    output[int(uint(o) + 50u)] = long(u25);
    output[int(uint(o) + 51u)] = long(t25);
    uint t26 = x;
    uint u26 = --t26;
    output[int(uint(o) + 52u)] = long(u26);
    output[int(uint(o) + 53u)] = long(t26);
}
