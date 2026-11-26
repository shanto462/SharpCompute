// SharpCompute.Tests.Shaders.DivideByMaybeZero (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_DivideByMaybeZero, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }
inline long __ms_nonzero(long x) { ulong u = ulong(x); return long(u | (((u - 1UL) & ~u) >> 63)); }
inline ulong __ms_nonzero(ulong x) { return x | (((x - 1UL) & ~x) >> 63); }

constant int Stride = 6;

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

kernel void SharpCompute_Tests_Shaders_DivideByMaybeZero(
    const device long* divisors [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Stride));
    long d = divisors[i];
    long x = long(1000003UL * ulong(int(uint(i) + 1u)));
    output[int(uint(o) + 0u)] = x / __ms_nonzero(d);
    output[int(uint(o) + 1u)] = x % __ms_nonzero(d);
    output[int(uint(o) + 2u)] = long(ulong(x) / __ms_nonzero(ulong(d)));
    output[int(uint(o) + 3u)] = long(int(uchar(x)) / __ms_nonzero(int(uchar(d))));
    output[int(uint(o) + 4u)] = long(int(short(x)) % __ms_nonzero(int(short(d))));
    output[int(uint(o) + 5u)] = long(int(ushort(x)) / __ms_nonzero(int(ushort(d))));
}
