// SharpCompute.Tests.Conformance.ConformanceLongOperators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceLongOperators, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline long __ms_nonzero(long x) { return __builtin_constant_p(x) ? x : x | long(metal::reverse_bits(x) == long(0)); }

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

kernel void SharpCompute_Tests_Conformance_ConformanceLongOperators(
    const device long* a [[buffer(0)]],
    const device long* b [[buffer(1)]],
    const device long* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    long x = a[i];
    long y = b[i];
    long z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = (+x);
    output[int(uint(o) + 1u)] = (long(-ulong(x)));
    output[int(uint(o) + 2u)] = (~x);
    output[int(uint(o) + 3u)] = (long(ulong(x) + ulong(y)));
    output[int(uint(o) + 4u)] = (long(ulong(x) - ulong(y)));
    output[int(uint(o) + 5u)] = (long(ulong(x) * ulong(y)));
    output[int(uint(o) + 6u)] = ((y == 0L || (x == -9223372036854775807L - 1L && y == -1L)) ? 0L : x / __ms_nonzero(y));
    output[int(uint(o) + 7u)] = ((y == 0L || (x == -9223372036854775807L - 1L && y == -1L)) ? 0L : x % __ms_nonzero(y));
    output[int(uint(o) + 8u)] = (x & y);
    output[int(uint(o) + 9u)] = (x | y);
    output[int(uint(o) + 10u)] = (x ^ y);
    output[int(uint(o) + 11u)] = (x << n);
    output[int(uint(o) + 12u)] = (x >> n);
    output[int(uint(o) + 13u)] = (long(ulong(x) >> n));
    output[int(uint(o) + 14u)] = (x == y) ? 1L : 0L;
    output[int(uint(o) + 15u)] = (x != y) ? 1L : 0L;
    output[int(uint(o) + 16u)] = (x < y) ? 1L : 0L;
    output[int(uint(o) + 17u)] = (x <= y) ? 1L : 0L;
    output[int(uint(o) + 18u)] = (x > y) ? 1L : 0L;
    output[int(uint(o) + 19u)] = (x >= y) ? 1L : 0L;
    long t0 = x;
    t0 = long(ulong(t0) + ulong(y));
    output[int(uint(o) + 20u)] = (t0);
    long t1 = x;
    t1 = long(ulong(t1) - ulong(y));
    output[int(uint(o) + 21u)] = (t1);
    long t2 = x;
    t2 = long(ulong(t2) * ulong(y));
    output[int(uint(o) + 22u)] = (t2);
    long t3 = x;
    t3 &= y;
    output[int(uint(o) + 23u)] = (t3);
    long t4 = x;
    t4 |= y;
    output[int(uint(o) + 24u)] = (t4);
    long t5 = x;
    t5 ^= y;
    output[int(uint(o) + 25u)] = (t5);
    long t6 = x;
    if (!(y == 0L || (x == -9223372036854775807L - 1L && y == -1L)))
    {
        t6 /= __ms_nonzero(y);
    }
    output[int(uint(o) + 26u)] = (t6);
    long t7 = x;
    if (!(y == 0L || (x == -9223372036854775807L - 1L && y == -1L)))
    {
        t7 %= __ms_nonzero(y);
    }
    output[int(uint(o) + 27u)] = (t7);
    long t8 = x;
    t8 <<= n;
    output[int(uint(o) + 28u)] = (t8);
    long t9 = x;
    t9 >>= n;
    output[int(uint(o) + 29u)] = (t9);
    long t10 = x;
    t10 = long(ulong(t10) >> n);
    output[int(uint(o) + 30u)] = (t10);
    long t11 = x;
    t11 <<= 17;
    output[int(uint(o) + 31u)] = (t11);
    long t12 = x;
    t12 <<= 33;
    output[int(uint(o) + 32u)] = (t12);
    long t13 = x;
    t13 <<= 21;
    output[int(uint(o) + 33u)] = (t13);
    long t14 = x;
    t14 <<= 63;
    output[int(uint(o) + 34u)] = (t14);
    long t15 = x;
    t15 >>= 17;
    output[int(uint(o) + 35u)] = (t15);
    long t16 = x;
    t16 >>= 33;
    output[int(uint(o) + 36u)] = (t16);
    long t17 = x;
    t17 >>= 21;
    output[int(uint(o) + 37u)] = (t17);
    long t18 = x;
    t18 >>= 63;
    output[int(uint(o) + 38u)] = (t18);
    long t19 = x;
    t19 = long(ulong(t19) >> 17);
    output[int(uint(o) + 39u)] = (t19);
    long t20 = x;
    t20 = long(ulong(t20) >> 33);
    output[int(uint(o) + 40u)] = (t20);
    long t21 = x;
    t21 = long(ulong(t21) >> 21);
    output[int(uint(o) + 41u)] = (t21);
    long t22 = x;
    t22 = long(ulong(t22) >> 63);
    output[int(uint(o) + 42u)] = (t22);
    output[int(uint(o) + 43u)] = (x << 33);
    output[int(uint(o) + 44u)] = (x >> 33);
    output[int(uint(o) + 45u)] = (long(ulong(x) >> 33));
    long t23 = x;
    long u23 = long(reinterpret_cast<thread ulong&>(t23)++);
    output[int(uint(o) + 46u)] = (u23);
    output[int(uint(o) + 47u)] = (t23);
    long t24 = x;
    long u24 = long(reinterpret_cast<thread ulong&>(t24)--);
    output[int(uint(o) + 48u)] = (u24);
    output[int(uint(o) + 49u)] = (t24);
    long t25 = x;
    long u25 = long(++reinterpret_cast<thread ulong&>(t25));
    output[int(uint(o) + 50u)] = (u25);
    output[int(uint(o) + 51u)] = (t25);
    long t26 = x;
    long u26 = long(--reinterpret_cast<thread ulong&>(t26));
    output[int(uint(o) + 52u)] = (u26);
    output[int(uint(o) + 53u)] = (t26);
}
