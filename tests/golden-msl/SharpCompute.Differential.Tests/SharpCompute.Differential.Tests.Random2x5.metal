// SharpCompute.Differential.Tests.Random2x5 (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random2x5, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline char __ms_sat_char(float x) { float y = x == x ? x : 0.0f; return char(metal::clamp(y, -128.0f, 127.0f)); }
inline char __ms_sat_char(half x) { return __ms_sat_char(float(x)); }
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline int2 __ms_sat_int2(float2 x) { float2 y = metal::select(float2(0.0f), x, x == x); int2 r = int2(metal::clamp(y, float2(-2147483648.0f), float2(2147483520.0f))); return metal::select(r, int2(2147483647), x >= 2147483648.0f); }
inline int2 __ms_sat_int2(half2 x) { return __ms_sat_int2(float2(x)); }
inline ulong __ms_sat_ulong(float x) { float y = x > 0.0f ? x : 0.0f; ulong r = ulong(metal::min(y, 18446742974197923840.0f)); return x >= 18446744073709551616.0f ? 18446744073709551615UL : r; }
inline ulong __ms_sat_ulong(half x) { return __ms_sat_ulong(float(x)); }

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
    ushort us0 = ushort(input[int(uint(o) + 12u)]);
    ushort us1 = ushort(input[int(uint(o) + 13u)]);
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
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float2 f2a = float2(as_type<float>(uint(input[int(uint(o) + 33u)])), as_type<float>(uint(input[int(uint(o) + 34u)])));
    float2 f2b = float2(as_type<float>(uint(input[int(uint(o) + 35u)])), as_type<float>(uint(input[int(uint(o) + 36u)])));
    int3 i3b = int3(int(input[int(uint(o) + 76u)]), int(input[int(uint(o) + 77u)]), int(input[int(uint(o) + 78u)]));
    uint3 u3a = uint3(uint(input[int(uint(o) + 91u)]), uint(input[int(uint(o) + 92u)]), uint(input[int(uint(o) + 93u)]));
    uint3 u3b = uint3(uint(input[int(uint(o) + 94u)]), uint(input[int(uint(o) + 95u)]), uint(input[int(uint(o) + 96u)]));
    uint4 u4a = uint4(uint(input[int(uint(o) + 97u)]), uint(input[int(uint(o) + 98u)]), uint(input[int(uint(o) + 99u)]), uint(input[int(uint(o) + 100u)]));
    uint4 u4b = uint4(uint(input[int(uint(o) + 101u)]), uint(input[int(uint(o) + 102u)]), uint(input[int(uint(o) + 103u)]), uint(input[int(uint(o) + 104u)]));
    uint3 c0_0 = u3b;
    output[int(uint(p) + 0u)] = (metal::all((uint3(c0_0) != uint3(uint3(u0, u2, u0)))) && metal::all(bool3(f0 == f1))) ? 1L : 0L;
    char c1_0 = __ms_sat_char(h2);
    --c1_0;
    output[int(uint(p) + 1u)] = long(int(short(c1_0)) << __ms_sat_int(h2));
    uint c2_0 = 1u - u1;
    uint c2_1 = metal::min(u0, u2);
    uint c2_2 = metal::rotate(2147483649u, (0u - uint(i1)) & 31u);
    output[int(uint(p) + 2u)] = long((+((c2_0 == 0u) ? 0u : u0 % c2_0)) - (((c2_2 == 0u) ? 0u : c2_1 / c2_2) - uint(ushort(int(uint(by0) * uint(sb2))))));
    uint c3_0 = u2;
    uint c3_1 = ++c3_0;
    uint c3_2 = u2;
    c3_2 *= u1;
    output[int(uint(p) + 3u)] = long(metal::abs(uint(by0) ^ 2147483648u) - (b1 ? (u1 * c3_1) : (c3_2 << i0)));
    uint4 c4_0 = u3a.zyyx;
    uint4 c4_1 = metal::popcount(metal::abs(u4b));
    uint4 c4_2 = c4_0 % metal::select(c4_1, uint4(1u), (uint4(c4_1) == 0u));
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
    uint c9_0 = uint(metal::popcount(us0));
    uint c9_1 = u4a.y;
    c9_1 *= (+u0);
    c9_0 -= c9_1;
    output[int(uint(p) + 13u)] = long(as_type<ushort>(half(c9_0)));
    float c10_0 = f2;
    float c10_1 = c10_0--;
    float c10_2 = c10_1 * (b1 ? f2 : f0);
    float c10_3 = --c10_2;
    output[int(uint(p) + 14u)] = long(as_type<uint>(-c10_3));
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
    output[int(uint(p) + 21u)] = long(metal::clz(as_type<int>(-f1)));
}
