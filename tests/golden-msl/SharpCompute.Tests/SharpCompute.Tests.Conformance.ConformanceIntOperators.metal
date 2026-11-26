// SharpCompute.Tests.Conformance.ConformanceIntOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceIntOperators, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return x | int(metal::reverse_bits(x) == int(0)); }

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

kernel void SharpCompute_Tests_Conformance_ConformanceIntOperators(
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
    int x = a[i];
    int y = b[i];
    int z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = long(+x);
    output[int(uint(o) + 1u)] = long(int(-uint(x)));
    output[int(uint(o) + 2u)] = long(~x);
    output[int(uint(o) + 3u)] = long(int(uint(x) + uint(y)));
    output[int(uint(o) + 4u)] = long(int(uint(x) - uint(y)));
    output[int(uint(o) + 5u)] = long(int(uint(x) * uint(y)));
    output[int(uint(o) + 6u)] = long((y == 0 || (x == -2147483647 - 1 && y == -1)) ? 0 : x / __ms_nonzero(y));
    output[int(uint(o) + 7u)] = long((y == 0 || (x == -2147483647 - 1 && y == -1)) ? 0 : x % __ms_nonzero(y));
    output[int(uint(o) + 8u)] = long(x & y);
    output[int(uint(o) + 9u)] = long(x | y);
    output[int(uint(o) + 10u)] = long(x ^ y);
    output[int(uint(o) + 11u)] = long(x << n);
    output[int(uint(o) + 12u)] = long(x >> n);
    output[int(uint(o) + 13u)] = long(int(uint(x) >> n));
    output[int(uint(o) + 14u)] = (x == y) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (x != y) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (x < y) ? 1L : 0L;
    output[int(uint(o) + 17u)] = (x <= y) ? 1L : 0L;
    output[int(uint(o) + 18u)] = (x > y) ? 1L : 0L;
    output[int(uint(o) + 19u)] = (x >= y) ? 1L : 0L;
    int t0 = x;
    t0 = int(uint(t0) + uint(y));
    output[int(uint(o) + 20u)] = long(t0);
    int t1 = x;
    t1 = int(uint(t1) - uint(y));
    output[int(uint(o) + 21u)] = long(t1);
    int t2 = x;
    t2 = int(uint(t2) * uint(y));
    output[int(uint(o) + 22u)] = long(t2);
    int t3 = x;
    t3 &= y;
    output[int(uint(o) + 23u)] = long(t3);
    int t4 = x;
    t4 |= y;
    output[int(uint(o) + 24u)] = long(t4);
    int t5 = x;
    t5 ^= y;
    output[int(uint(o) + 25u)] = long(t5);
    int t6 = x;
    if (!(y == 0 || (x == -2147483647 - 1 && y == -1)))
    {
        t6 /= __ms_nonzero(y);
    }
    output[int(uint(o) + 26u)] = long(t6);
    int t7 = x;
    if (!(y == 0 || (x == -2147483647 - 1 && y == -1)))
    {
        t7 %= __ms_nonzero(y);
    }
    output[int(uint(o) + 27u)] = long(t7);
    int t8 = x;
    t8 <<= n;
    output[int(uint(o) + 28u)] = long(t8);
    int t9 = x;
    t9 >>= n;
    output[int(uint(o) + 29u)] = long(t9);
    int t10 = x;
    t10 = int(uint(t10) >> n);
    output[int(uint(o) + 30u)] = long(t10);
    int t11 = x;
    t11 <<= 17;
    output[int(uint(o) + 31u)] = long(t11);
    int t12 = x;
    t12 <<= 1;
    output[int(uint(o) + 32u)] = long(t12);
    int t13 = x;
    t13 <<= 21;
    output[int(uint(o) + 33u)] = long(t13);
    int t14 = x;
    t14 <<= 31;
    output[int(uint(o) + 34u)] = long(t14);
    int t15 = x;
    t15 >>= 17;
    output[int(uint(o) + 35u)] = long(t15);
    int t16 = x;
    t16 >>= 1;
    output[int(uint(o) + 36u)] = long(t16);
    int t17 = x;
    t17 >>= 21;
    output[int(uint(o) + 37u)] = long(t17);
    int t18 = x;
    t18 >>= 31;
    output[int(uint(o) + 38u)] = long(t18);
    int t19 = x;
    t19 = int(uint(t19) >> 17);
    output[int(uint(o) + 39u)] = long(t19);
    int t20 = x;
    t20 = int(uint(t20) >> 1);
    output[int(uint(o) + 40u)] = long(t20);
    int t21 = x;
    t21 = int(uint(t21) >> 21);
    output[int(uint(o) + 41u)] = long(t21);
    int t22 = x;
    t22 = int(uint(t22) >> 31);
    output[int(uint(o) + 42u)] = long(t22);
    output[int(uint(o) + 43u)] = long(x << 1);
    output[int(uint(o) + 44u)] = long(x >> 1);
    output[int(uint(o) + 45u)] = long(int(uint(x) >> 1));
    int t23 = x;
    int u23 = int(reinterpret_cast<thread uint&>(t23)++);
    output[int(uint(o) + 46u)] = long(u23);
    output[int(uint(o) + 47u)] = long(t23);
    int t24 = x;
    int u24 = int(reinterpret_cast<thread uint&>(t24)--);
    output[int(uint(o) + 48u)] = long(u24);
    output[int(uint(o) + 49u)] = long(t24);
    int t25 = x;
    int u25 = int(++reinterpret_cast<thread uint&>(t25));
    output[int(uint(o) + 50u)] = long(u25);
    output[int(uint(o) + 51u)] = long(t25);
    int t26 = x;
    int u26 = int(--reinterpret_cast<thread uint&>(t26));
    output[int(uint(o) + 52u)] = long(u26);
    output[int(uint(o) + 53u)] = long(t26);
}
