// SharpCompute.Tests.Conformance.ConformanceShortOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceShortOperators, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return x | int(metal::reverse_bits(x) == int(0)); }

constant int Slots = 60;

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

kernel void SharpCompute_Tests_Conformance_ConformanceShortOperators(
    const device short* a [[buffer(0)]],
    const device short* b [[buffer(1)]],
    const device short* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    short x = a[i];
    short y = b[i];
    short z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = long(+int(x));
    output[int(uint(o) + 1u)] = long(int(-uint(x)));
    output[int(uint(o) + 2u)] = long(~int(x));
    output[int(uint(o) + 3u)] = long(int(uint(x) + uint(y)));
    output[int(uint(o) + 4u)] = long(int(uint(x) - uint(y)));
    output[int(uint(o) + 5u)] = long(int(uint(x) * uint(y)));
    output[int(uint(o) + 6u)] = long((int(y) == 0) ? 0 : int(x) / __ms_nonzero(int(y)));
    output[int(uint(o) + 7u)] = long((int(y) == 0) ? 0 : int(x) % __ms_nonzero(int(y)));
    output[int(uint(o) + 8u)] = long(int(x) & int(y));
    output[int(uint(o) + 9u)] = long(int(x) | int(y));
    output[int(uint(o) + 10u)] = long(int(x) ^ int(y));
    output[int(uint(o) + 11u)] = long(int(x) << n);
    output[int(uint(o) + 12u)] = long(int(x) >> n);
    output[int(uint(o) + 13u)] = long(int(uint(int(x)) >> n));
    output[int(uint(o) + 14u)] = (int(x) == int(y)) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (int(x) != int(y)) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (int(x) < int(y)) ? 1L : 0L;
    output[int(uint(o) + 17u)] = (int(x) <= int(y)) ? 1L : 0L;
    output[int(uint(o) + 18u)] = (int(x) > int(y)) ? 1L : 0L;
    output[int(uint(o) + 19u)] = (int(x) >= int(y)) ? 1L : 0L;
    output[int(uint(o) + 20u)] = long(short(int(uint(x) + uint(y))));
    output[int(uint(o) + 21u)] = long(short(int(uint(x) - uint(y))));
    output[int(uint(o) + 22u)] = long(short(int(uint(x) * uint(y))));
    output[int(uint(o) + 23u)] = long(short(int(x) << n));
    output[int(uint(o) + 24u)] = long(short(int(-uint(x))));
    output[int(uint(o) + 25u)] = long(short(~int(x)));
    short t0 = x;
    t0 += int(y);
    output[int(uint(o) + 26u)] = long(t0);
    short t1 = x;
    t1 -= int(y);
    output[int(uint(o) + 27u)] = long(t1);
    short t2 = x;
    t2 *= uint(y);
    output[int(uint(o) + 28u)] = long(t2);
    short t3 = x;
    t3 &= int(y);
    output[int(uint(o) + 29u)] = long(t3);
    short t4 = x;
    t4 |= int(y);
    output[int(uint(o) + 30u)] = long(t4);
    short t5 = x;
    t5 ^= int(y);
    output[int(uint(o) + 31u)] = long(t5);
    short t6 = x;
    if (!(int(y) == 0))
    {
        t6 /= __ms_nonzero(int(y));
    }
    output[int(uint(o) + 32u)] = long(t6);
    short t7 = x;
    if (!(int(y) == 0))
    {
        t7 %= __ms_nonzero(int(y));
    }
    output[int(uint(o) + 33u)] = long(t7);
    short t8 = x;
    t8 <<= n;
    output[int(uint(o) + 34u)] = long(t8);
    short t9 = x;
    t9 >>= n;
    output[int(uint(o) + 35u)] = long(t9);
    short t10 = x;
    t10 = short(uint(int(t10)) >> n);
    output[int(uint(o) + 36u)] = long(t10);
    short t11 = x;
    t11 = short(0);
    output[int(uint(o) + 37u)] = long(t11);
    short t12 = x;
    t12 <<= 1;
    output[int(uint(o) + 38u)] = long(t12);
    short t13 = x;
    t13 = short(0);
    output[int(uint(o) + 39u)] = long(t13);
    short t14 = x;
    t14 = short(0);
    output[int(uint(o) + 40u)] = long(t14);
    short t15 = x;
    t15 >>= 15;
    output[int(uint(o) + 41u)] = long(t15);
    short t16 = x;
    t16 >>= 1;
    output[int(uint(o) + 42u)] = long(t16);
    short t17 = x;
    t17 >>= 15;
    output[int(uint(o) + 43u)] = long(t17);
    short t18 = x;
    t18 >>= 15;
    output[int(uint(o) + 44u)] = long(t18);
    short t19 = x;
    t19 = short(uint(int(t19)) >> 17);
    output[int(uint(o) + 45u)] = long(t19);
    short t20 = x;
    t20 = short(uint(int(t20)) >> 1);
    output[int(uint(o) + 46u)] = long(t20);
    short t21 = x;
    t21 = short(uint(int(t21)) >> 21);
    output[int(uint(o) + 47u)] = long(t21);
    short t22 = x;
    t22 = short(uint(int(t22)) >> 31);
    output[int(uint(o) + 48u)] = long(t22);
    output[int(uint(o) + 49u)] = long(int(x) << 1);
    output[int(uint(o) + 50u)] = long(int(x) >> 1);
    output[int(uint(o) + 51u)] = long(int(uint(int(x)) >> 1));
    short t23 = x;
    short u23 = t23++;
    output[int(uint(o) + 52u)] = long(u23);
    output[int(uint(o) + 53u)] = long(t23);
    short t24 = x;
    short u24 = t24--;
    output[int(uint(o) + 54u)] = long(u24);
    output[int(uint(o) + 55u)] = long(t24);
    short t25 = x;
    short u25 = ++t25;
    output[int(uint(o) + 56u)] = long(u25);
    output[int(uint(o) + 57u)] = long(t25);
    short t26 = x;
    short u26 = --t26;
    output[int(uint(o) + 58u)] = long(u26);
    output[int(uint(o) + 59u)] = long(t26);
}
