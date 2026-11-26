// SharpCompute.Differential.Tests.FoldedDivisions (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_FoldedDivisions, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return __builtin_constant_p(x) ? x : x | int(metal::reverse_bits(x) == int(0)); }
inline uint __ms_nonzero_c(uint x) { uint u = uint(x); return uint(u | (((u - 1u) & ~u) >> 31)); }
inline uint3 __ms_nonzero_c(uint3 x) { uint3 u = uint3(x); return uint3(u | (((u - 1u) & ~u) >> 31)); }
inline long __ms_nonzero_c(long x) { ulong u = ulong(x); return long(u | (((u - 1UL) & ~u) >> 63)); }

constant int Slots = 8;

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

kernel void SharpCompute_Differential_Tests_FoldedDivisions(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    ushort u = ushort(input[t]);
    int o = int(uint(t) * uint(::Slots));
    int a = 1431655765;
    int two = 2;
    if (two != 0)
    {
        a %= __ms_nonzero(two);
    }
    output[int(uint(o) + 0u)] = long(a);
    uint b = 2863311530u;
    uint d = uint(false ? u : ushort(2));
    output[int(uint(o) + 1u)] = long(d == 0u ? 0u : b % __ms_nonzero_c(d));
    output[int(uint(o) + 2u)] = long(d == 0u ? 0u : b / __ms_nonzero_c(d));
    uint e = false ? uint(u) : 12u;
    output[int(uint(o) + 3u)] = long(b / __ms_nonzero_c(e));
    output[int(uint(o) + 4u)] = long(b % __ms_nonzero_c(e));
    long f = 1431655765L;
    long g = false ? long(u) : 6L;
    output[int(uint(o) + 5u)] = f / __ms_nonzero_c(g);
    uint3 h = uint3(2863311530u, 1431655765u, 100u);
    uint3 k = uint3(false ? uint(u) : 2u, 4u, 6u);
    uint3 q = h % __ms_nonzero_c(k);
    output[int(uint(o) + 6u)] = long(q.x);
    output[int(uint(o) + 7u)] = long(q.y + q.z);
}
