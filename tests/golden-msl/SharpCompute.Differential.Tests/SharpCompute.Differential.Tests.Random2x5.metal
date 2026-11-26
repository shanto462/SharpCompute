// SharpCompute.Differential.Tests.Random2x5 (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random2x5, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline int2 __ms_sat_int2(float2 x) { float2 y = metal::select(float2(0.0f), x, x == x); int2 r = int2(metal::clamp(y, float2(-2147483648.0f), float2(2147483520.0f))); return metal::select(r, int2(2147483647), x >= 2147483648.0f); }
inline int2 __ms_sat_int2(half2 x) { return __ms_sat_int2(float2(x)); }
inline ulong __ms_sat_ulong(float x) { float y = x > 0.0f ? x : 0.0f; ulong r = ulong(metal::min(y, 18446742974197923840.0f)); return x >= 18446744073709551616.0f ? 18446744073709551615UL : r; }
inline ulong __ms_sat_ulong(half x) { return __ms_sat_ulong(float(x)); }
// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return x | int(metal::reverse_bits(x) == int(0)); }
inline uint __ms_nonzero(uint x) { return x | uint(metal::reverse_bits(x) == uint(0)); }
inline uint4 __ms_nonzero(uint4 x) { return x | uint4(metal::reverse_bits(x) == uint4(0)); }
inline short __ms_popcount(short x) { return short(metal::popcount(uint(ushort(x)))); }
inline ushort __ms_popcount(ushort x) { return ushort(metal::popcount(uint(ushort(x)))); }
inline int __ms_popcount(int x) { return metal::popcount(metal::reverse_bits(x)); }
inline uint4 __ms_popcount(uint4 x) { return metal::popcount(metal::reverse_bits(x)); }
inline int __ms_clz(int x) { return x < 0 ? 0 : metal::clz(x); }

constant int Slots = 22;

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

kernel void SharpCompute_Differential_Tests_Random2x5(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int o = int(__tid.x * 123u);
    int p = int(__tid.x * uint(::Slots));
    bool b1 = (input[int(uint(o) + 1u)] & 1UL) != 0UL;
    uchar by0 = uchar(input[int(uint(o) + 3u)]);
    uchar by1 = uchar(input[int(uint(o) + 4u)]);
    uchar by2 = uchar(input[int(uint(o) + 5u)]);
    char sb1 = char(input[int(uint(o) + 7u)]);
    char sb2 = char(input[int(uint(o) + 8u)]);
    short sh1 = short(input[int(uint(o) + 10u)]);
    short sh2 = short(input[int(uint(o) + 11u)]);
    ushort us0 = ushort(input[int(uint(o) + 12u)]);
    ushort us1 = ushort(input[int(uint(o) + 13u)]);
    ushort us2 = ushort(input[int(uint(o) + 14u)]);
    int i0 = int(input[int(uint(o) + 15u)]);
    int i1 = int(input[int(uint(o) + 16u)]);
    int i2 = int(input[int(uint(o) + 17u)]);
    uint u0 = uint(input[int(uint(o) + 18u)]);
    uint u1 = uint(input[int(uint(o) + 19u)]);
    uint u2 = uint(input[int(uint(o) + 20u)]);
    long l1 = long(input[int(uint(o) + 22u)]);
    long l2 = long(input[int(uint(o) + 23u)]);
    ulong ul0 = input[int(uint(o) + 24u)];
    float f0 = as_type<float>(uint(input[int(uint(o) + 27u)]));
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    float f2 = as_type<float>(uint(input[int(uint(o) + 29u)]));
    half h0 = as_type<half>(ushort(input[int(uint(o) + 30u)]));
    float2 f2a = float2(as_type<float>(uint(input[int(uint(o) + 33u)])), as_type<float>(uint(input[int(uint(o) + 34u)])));
    float2 f2b = float2(as_type<float>(uint(input[int(uint(o) + 35u)])), as_type<float>(uint(input[int(uint(o) + 36u)])));
    int3 i3b = int3(int(input[int(uint(o) + 76u)]), int(input[int(uint(o) + 77u)]), int(input[int(uint(o) + 78u)]));
    uint3 u3a = uint3(uint(input[int(uint(o) + 91u)]), uint(input[int(uint(o) + 92u)]), uint(input[int(uint(o) + 93u)]));
    uint4 u4a = uint4(uint(input[int(uint(o) + 97u)]), uint(input[int(uint(o) + 98u)]), uint(input[int(uint(o) + 99u)]), uint(input[int(uint(o) + 100u)]));
    uint4 u4b = uint4(uint(input[int(uint(o) + 101u)]), uint(input[int(uint(o) + 102u)]), uint(input[int(uint(o) + 103u)]), uint(input[int(uint(o) + 104u)]));
    short c0_0 = short(2);
    if (!(int(sh2) == 0))
    {
        c0_0 %= __ms_nonzero(int(sh2));
    }
    output[int(uint(p) + 0u)] = long(as_type<ushort>(-half(metal::clz(c0_0))));
    short c1_0 = sh2;
    if (!(int(sh2) == 0))
    {
        c1_0 /= __ms_nonzero(int(sh2));
    }
    short c1_1 = c1_0;
    short c1_2 = __ms_popcount(sh1);
    if (!(int(c1_2) == 0))
    {
        c1_1 %= __ms_nonzero(int(c1_2));
    }
    short c1_3 = c1_1;
    output[int(uint(p) + 1u)] = long(ushort(c1_3));
    uint c2_0 = 1u - u1;
    uint c2_1 = metal::min(u0, u2);
    uint c2_2 = metal::rotate(2147483649u, (0u - uint(i1)) & 31u);
    output[int(uint(p) + 2u)] = long((+((c2_0 == 0u) ? 0u : u0 % __ms_nonzero(c2_0))) - (((c2_2 == 0u) ? 0u : c2_1 / __ms_nonzero(c2_2)) - uint(ushort(int(uint(by0) * uint(sb2))))));
    uint c3_0 = u2;
    uint c3_1 = ++c3_0;
    uint c3_2 = u2;
    c3_2 *= u1;
    output[int(uint(p) + 3u)] = long(metal::abs(uint(by0) ^ 2147483648u) - (b1 ? (u1 * c3_1) : (c3_2 << i0)));
    uint4 c4_0 = u3a.zyyx;
    uint4 c4_1 = __ms_popcount(metal::abs(u4b));
    uint4 c4_2 = c4_0 % __ms_nonzero(metal::select(c4_1, uint4(1u), (uint4(c4_1) == 0u)));
    output[int(uint(p) + 4u)] = long(c4_2.x);
    output[int(uint(p) + 5u)] = long(c4_2.y);
    output[int(uint(p) + 6u)] = long(c4_2.z);
    output[int(uint(p) + 7u)] = long(c4_2.w);
    half c5_0 = h0;
    --c5_0;
    half c5_1 = c5_0;
    output[int(uint(p) + 8u)] = long(ul0 + __ms_sat_ulong(c5_1));
    output[int(uint(p) + 9u)] = long(as_type<uint>(metal::fract(metal::abs(metal::sqrt(float(sh1))))));
    int2 c7_0 = __ms_sat_int2(-metal::min(float2(f1, f0), f2a / f2b));
    output[int(uint(p) + 10u)] = long(c7_0.x);
    output[int(uint(p) + 11u)] = long(c7_0.y);
    output[int(uint(p) + 12u)] = long(as_type<uint>(float(u0) + 1E-10f));
    uint c9_0 = uint(__ms_popcount(us0));
    uint c9_1 = u4a.y;
    c9_1 *= (+u0);
    c9_0 -= c9_1;
    output[int(uint(p) + 13u)] = long(as_type<ushort>(half(c9_0)));
    output[int(uint(p) + 14u)] = long(__ms_popcount(int(uint(ushort(metal::clamp(sh2, sh2, sh1))) * uint(char(metal::min(us2, us2))))));
    char c11_0 = char(64);
    char c11_1 = c11_0++;
    output[int(uint(p) + 15u)] = long(ushort(c11_1));
    output[int(uint(p) + 16u)] = long(__ms_sat_int((f2 + f0) - (f0 + f1)) >> int(long(ulong(metal::min(l2, l1)) * (ulong(l2) - ulong(l2)))));
    char c13_0 = sb1;
    uchar c13_1 = metal::min(by2, by0);
    c13_1 >>= (int(us1) >> i2);
    uchar c13_2 = c13_1;
    uchar c13_3 = uchar(1);
    c13_3 &= int(by1);
    uchar c13_4 = c13_3;
    c13_4 <<= (b1 ? i1 : i1);
    c13_2 |= int(c13_4);
    output[int(uint(p) + 17u)] = long(metal::min(uchar(~int(c13_0)), c13_2));
    int3 c14_0 = i3b.yxz;
    output[int(uint(p) + 18u)] = long(c14_0.x);
    output[int(uint(p) + 19u)] = long(c14_0.y);
    output[int(uint(p) + 20u)] = long(c14_0.z);
    output[int(uint(p) + 21u)] = long(__ms_clz(as_type<int>(-f1)));
}
