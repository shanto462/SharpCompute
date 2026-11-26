// SharpCompute.Tests.Conformance.ConformanceShortOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceShortOperators, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 45;

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
    output[int(uint(o) + 6u)] = long((int(y) == 0) ? 0 : int(x) / int(y));
    output[int(uint(o) + 7u)] = long((int(y) == 0) ? 0 : int(x) % int(y));
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
        t6 /= int(y);
    }
    output[int(uint(o) + 32u)] = long(t6);
    short t7 = x;
    if (!(int(y) == 0))
    {
        t7 %= int(y);
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
    short u11 = t11++;
    output[int(uint(o) + 37u)] = long(u11);
    output[int(uint(o) + 38u)] = long(t11);
    short t12 = x;
    short u12 = t12--;
    output[int(uint(o) + 39u)] = long(u12);
    output[int(uint(o) + 40u)] = long(t12);
    short t13 = x;
    short u13 = ++t13;
    output[int(uint(o) + 41u)] = long(u13);
    output[int(uint(o) + 42u)] = long(t13);
    short t14 = x;
    short u14 = --t14;
    output[int(uint(o) + 43u)] = long(u14);
    output[int(uint(o) + 44u)] = long(t14);
}
