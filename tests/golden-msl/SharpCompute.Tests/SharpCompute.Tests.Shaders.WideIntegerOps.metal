// SharpCompute.Tests.Shaders.WideIntegerOps (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_WideIntegerOps, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 64;
constant long Multiplier = 7109453100751455733L;
constant ulong Mixer = 13787848793156543929UL;
constant long Lowest = -9223372036854775807L - 1L;
constant ulong HighBit = 1UL << 63;

struct __Constants
{
    uint __length_longs;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __length_longs) == 0, "SharpCompute: offset of '__Constants.__length_longs' does not match its C# layout");

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

static long Fold(long x, ulong y);
static void Swap(thread long& x, thread long& y);

static long Fold(long x, ulong y)
{
    ulong z = ulong(x) + y;
    z = (z ^ (z >> 30)) * ::Mixer;
    z = (z ^ (z >> 27)) * 10723151780598845931UL;
    return long(z ^ (z >> 31));
}

static void Swap(thread long& x, thread long& y)
{
    long t = x;
    x = y;
    y = t;
}

kernel void SharpCompute_Tests_Shaders_WideIntegerOps(
    const device long* longs [[buffer(0)]],
    const device ulong* ulongs [[buffer(1)]],
    device long* output [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int j = (int((uint(i) * 5u) + 1u)) % int(__c.__length_longs);
    int o = int(uint(i) * uint(::Stride));
    long a = longs[i];
    long b = longs[j];
    ulong u = ulongs[i];
    ulong v = ulongs[j];
    int count = int(uint(i % 140) - 5u);
    output[int(uint(o) + 0u)] = long(ulong(a) + ulong(b));
    output[int(uint(o) + 1u)] = long(ulong(a) - ulong(b));
    output[int(uint(o) + 2u)] = long(ulong(a) * ulong(b));
    output[int(uint(o) + 3u)] = b != 0L && !(a == -9223372036854775807L - 1L && b == -1L) ? a / b : 0L;
    output[int(uint(o) + 4u)] = b != 0L && !(a == -9223372036854775807L - 1L && b == -1L) ? a % b : 0L;
    output[int(uint(o) + 5u)] = long(u + v);
    output[int(uint(o) + 6u)] = long(u - v);
    output[int(uint(o) + 7u)] = long(u * v);
    output[int(uint(o) + 8u)] = v != 0UL ? long(u / v) : 0L;
    output[int(uint(o) + 9u)] = v != 0UL ? long(u % v) : 0L;
    output[int(uint(o) + 10u)] = a << count;
    output[int(uint(o) + 11u)] = a >> count;
    output[int(uint(o) + 12u)] = long(ulong(a) >> count);
    output[int(uint(o) + 13u)] = long(u << count);
    output[int(uint(o) + 14u)] = long(u >> count);
    output[int(uint(o) + 15u)] = a & b;
    output[int(uint(o) + 16u)] = a | b;
    output[int(uint(o) + 17u)] = a ^ b;
    output[int(uint(o) + 18u)] = ~a;
    output[int(uint(o) + 19u)] = long(-ulong(a));
    output[int(uint(o) + 20u)] = long((u & v) | (~u ^ v));
    output[int(uint(o) + 21u)] = long((a < b ? 1 : 0) | (a <= b ? 2 : 0) | (a > b ? 4 : 0) | (a >= b ? 8 : 0) | (a == b ? 16 : 0) | (a != b ? 32 : 0) | (u < v ? 64 : 0) | (u >= v ? 128 : 0) | (u == v ? 256 : 0));
    int n = int(a);
    uint m = uint(u);
    output[int(uint(o) + 22u)] = long(ulong(n) + ulong(a));
    output[int(uint(o) + 23u)] = long(ulong(m) - ulong(n));
    output[int(uint(o) + 24u)] = long(-ulong(m));
    output[int(uint(o) + 25u)] = long(ulong(n) * ulong(n));
    output[int(uint(o) + 26u)] = long(ulong(m) * ulong(m));
    output[int(uint(o) + 27u)] = long(long(n) < long(m) ? 1 : 0);
    output[int(uint(o) + 28u)] = long(int(a));
    output[int(uint(o) + 29u)] = long(uint(a));
    output[int(uint(o) + 30u)] = long(short(a));
    output[int(uint(o) + 31u)] = long(ushort(a));
    output[int(uint(o) + 32u)] = long(uchar(a));
    output[int(uint(o) + 33u)] = long(char(a));
    output[int(uint(o) + 34u)] = long(u);
    output[int(uint(o) + 35u)] = long(int(u));
    output[int(uint(o) + 36u)] = long(uint(a));
    output[int(uint(o) + 37u)] = long(ulong(char(a)));
    output[int(uint(o) + 38u)] = long(ulong(short(a)));
    long ca = a;
    ca = long(ulong(ca) + ulong(b));
    ca ^= ca >> 33;
    ca = long(ulong(ca) * ulong(::Multiplier));
    ca = long(ulong(ca) - ulong(n));
    ca <<= 3;
    ca >>= 1;
    ca |= 1L;
    ca &= ~16L;
    ca = long(ulong(ca) + 1UL);
    output[int(uint(o) + 39u)] = ca;
    ulong cu = u;
    cu += v;
    cu ^= cu >> 29;
    cu *= ::Mixer;
    cu >>= 2;
    cu <<= 1;
    cu %= 1000003UL;
    cu--;
    output[int(uint(o) + 40u)] = long(cu);
    long picked = a > b ? a : ::Lowest;
    output[int(uint(o) + 41u)] = picked;
    output[int(uint(o) + 42u)] = long(18446744073709551615UL - u);
    output[int(uint(o) + 43u)] = long(ulong(9223372036854775807L) - ulong(a & 9223372036854775807L));
    output[int(uint(o) + 44u)] = long(u | ::HighBit);
    output[int(uint(o) + 45u)] = ::Fold(a, u);
    ::Swap(a, b);
    output[int(uint(o) + 46u)] = long(ulong(a) - 1UL);
    output[int(uint(o) + 47u)] = long(ulong(b) + 1UL);
    long bucket;
    switch (u & 3UL)
    {
        case 0UL:
        {
            bucket = 1L << 40;
            break;
        }
        case 1UL:
        {
            bucket = -1L;
            break;
        }
        default:
        {
            bucket = 9223372036854775807L;
            break;
        }
    }
    output[int(uint(o) + 48u)] = bucket;
    output[int(uint(o) + 49u)] = long(ulong(long(0)) + ulong(0));
    output[int(uint(o) + 50u)] = long(int(uint(n) * uint(n)));
    output[int(uint(o) + 51u)] = long(uint(n) * m);
}
