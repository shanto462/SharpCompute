// SharpCompute.Tests.Shaders.LocalArraysOfIntegers (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_LocalArraysOfIntegers, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 5;

struct Totals
{
    long _element[3];
};
static_assert(sizeof(Totals) == 24, "SharpCompute: size of 'Totals' does not match its C# layout");
static_assert(__builtin_offsetof(Totals, _element) == 0, "SharpCompute: offset of 'Totals._element' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_LocalArraysOfIntegers(
    const device uchar* bytes [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    uchar window[8] = {};
    long sums[3] = {};
    const ushort weights[8] = { ushort(1), ushort(4), ushort(6), ushort(4), ushort(1), ushort(60000), ushort(257), ushort(9) };
    const ulong primes[2] = { 1099511628211UL, 15485907386658061715UL };
    char signs[2] = { char(-1), char(1) };
    Totals totals = Totals{};
    ulong hash = 14695981039346656037UL;
    for (int k = 0; k < 8; k = int(uint(k) + 1u))
    {
        window[k] = bytes[int(uint(i) + uint(k))];
    }
    for (int k = 0; k < 8; k = int(uint(k) + 1u))
    {
        sums[k % 3] = long(ulong(sums[k % 3]) + ulong(int(uint(window[k]) * uint(weights[k]) * uint(signs[k & 1]))));
        totals._element[k % 3] = long(ulong(totals._element[k % 3]) + ulong(long(window[k]) << (int(uint(k) * 7u))));
        hash = (hash ^ ulong(window[k])) * primes[0];
    }
    int o = int(uint(i) * uint(::Stride));
    output[int(uint(o) + 0u)] = sums[0];
    output[int(uint(o) + 1u)] = long(ulong(sums[1]) - ulong(sums[2]));
    output[int(uint(o) + 2u)] = totals._element[0] ^ totals._element[1];
    output[int(uint(o) + 3u)] = totals._element[2];
    output[int(uint(o) + 4u)] = long((hash ^ (hash >> 32)) * primes[1]);
}
