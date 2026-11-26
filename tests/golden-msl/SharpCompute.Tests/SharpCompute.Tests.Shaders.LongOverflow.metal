// SharpCompute.Tests.Shaders.LongOverflow (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_LongOverflow, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 16;

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

kernel void SharpCompute_Tests_Shaders_LongOverflow(
    const device long* pairs [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    long a = pairs[int(2u * uint(i))];
    long b = pairs[int((2u * uint(i)) + 1u)];
    int o = int(uint(i) * uint(::Slots));
    output[o] = long(ulong(a) * ulong(b));
    output[int(uint(o) + 1u)] = b > 1L ? (long(ulong(a) * ulong(b))) / b : 0L;
    output[int(uint(o) + 2u)] = long(long(ulong(a) + 1UL) > a ? 1 : 0);
    output[int(uint(o) + 3u)] = long(b > 0L && (long(ulong(a) + ulong(b))) < a ? 1 : 0);
    output[int(uint(o) + 4u)] = long(long(ulong(a) - 1UL) < a ? 1 : 0);
    output[int(uint(o) + 5u)] = long(ulong(a) * 2UL) / 2L;
    output[int(uint(o) + 6u)] = long(ulong(a) + ulong(b));
    output[int(uint(o) + 7u)] = long(ulong(a) - ulong(b));
    output[int(uint(o) + 8u)] = long(-ulong(a));
    output[int(uint(o) + 9u)] = long((ulong(a) * 6364136223846793005UL) + 1442695040888963407UL);
    output[int(uint(o) + 10u)] = long(ulong(int(a)) * ulong(int(b)));
    long x = a;
    x = long(ulong(x) + ulong(b));
    x = long(ulong(x) * ulong(b));
    x = long(ulong(x) - ulong(a));
    output[int(uint(o) + 11u)] = x;
    long y = a;
    y = long(ulong(y) + 1UL);
    output[int(uint(o) + 12u)] = y;
    y = a;
    y = long(ulong(y) - 1UL);
    output[int(uint(o) + 13u)] = y;
    long z = a;
    long before = long(reinterpret_cast<thread ulong&>(z)--);
    output[int(uint(o) + 14u)] = long(ulong(before) + (ulong(z) * 3UL));
    output[int(uint(o) + 15u)] = long((ulong(a) + ulong(b)) * (ulong(a) - ulong(b)));
}
