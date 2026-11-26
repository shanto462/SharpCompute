// SharpCompute.Differential.Tests.Random2x4Model (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random2x4Model, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline short __ms_sat_short(float x) { float y = x == x ? x : 0.0f; return short(metal::clamp(y, -32768.0f, 32767.0f)); }
inline short __ms_sat_short(half x) { return __ms_sat_short(float(x)); }
inline ushort __ms_sat_ushort(float x) { float y = x > 0.0f ? x : 0.0f; return ushort(metal::min(y, 65535.0f)); }
inline ushort __ms_sat_ushort(half x) { return __ms_sat_ushort(float(x)); }
inline half __ms_to_half(float x) { return metal::abs(x) >= 65520.0f ? (x < 0.0f ? half(-INFINITY) : half(INFINITY)) : half(x); }
// SharpCompute integer helpers (MslPrelude.Integers).
inline char __ms_popcount(char x) { return char(metal::popcount(uint(uchar(x)))); }

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

static float GpuModel_Flush(float value);
static float2 GpuModel_Flush_1(float2 value);
static bool GpuModel_IsSmallestNormal(float value);
static bool GpuModel_IsBelow(float rounded, float error, float bound);
static float GpuModel_Zero(float value);
static float GpuModel_FlushProduct(float result, float a, float b);
static float GpuModel_FlushQuotient(float result, float a, float b);
static float2 GpuModel_FlushQuotient_1(float2 result, float2 a, float2 b);
static float GpuModel_FlushFma(float result, float a, float b, float c);
static float2 GpuModel_FlushFma_1(float2 result, float2 a, float2 b, float2 c);

static float GpuModel_Flush(float value)
{
    uint bits = as_type<uint>(value);
    return (bits & 2139095040u) == 0u ? as_type<float>(bits & 2147483648u) : value;
}

static float2 GpuModel_Flush_1(float2 value)
{
    return float2(::GpuModel_Flush(value.x), ::GpuModel_Flush(value.y));
}

static bool GpuModel_IsSmallestNormal(float value)
{
    return (as_type<uint>(value) & 2147483647u) == 8388608u;
}

static bool GpuModel_IsBelow(float rounded, float error, float bound)
{
    return metal::abs(rounded) < bound || (metal::abs(rounded) == bound && (rounded > 0.0f ? error < 0.0f : error > 0.0f));
}

static float GpuModel_Zero(float value)
{
    return as_type<float>(as_type<uint>(value) & 2147483648u);
}

static float GpuModel_FlushProduct(float result, float a, float b)
{
    bool first = metal::abs(a) <= metal::abs(b);
    float small = (first ? a : b) * 1.0737418E+09f;
    float large = first ? b : a;
    float product = small * large;
    float error = metal::fma(small, large, -product);
    return ::GpuModel_IsSmallestNormal(result) && ::GpuModel_IsBelow(product, error, as_type<float>(260046848u)) ? ::GpuModel_Zero(result) : ::GpuModel_Flush(result);
}

static float GpuModel_FlushQuotient(float result, float a, float b)
{
    return ::GpuModel_IsSmallestNormal(result) && metal::abs(a) * 1.8446744E+19f < metal::abs(b) * as_type<float>(545259520u) ? ::GpuModel_Zero(result) : ::GpuModel_Flush(result);
}

static float2 GpuModel_FlushQuotient_1(float2 result, float2 a, float2 b)
{
    return float2(::GpuModel_FlushQuotient(result.x, a.x, b.x), ::GpuModel_FlushQuotient(result.y, a.y, b.y));
}

static float GpuModel_FlushFma(float result, float a, float b, float c)
{
    bool first = metal::abs(a) <= metal::abs(b);
    float small = (first ? a : b) * 1.1529215E+18f;
    float large = first ? b : a;
    float addend = c * 1.1529215E+18f;
    float sum = metal::fma(small, large, addend);
    float high = small * large;
    float low = metal::fma(small, large, -high);
    float s1 = addend + low;
    float t1 = s1 - addend;
    float e1 = (addend - (s1 - t1)) + (low - t1);
    float s2 = high + s1;
    float t2 = s2 - high;
    float e2 = (high - (s2 - t2)) + (s1 - t2);
    float error = ((s2 - sum) + e2) + e1;
    float productSign = small == 0.0f || large == 0.0f ? 0.0f : ((small > 0.0f) == (large > 0.0f) ? 1.0f : -1.0f);
    return ::GpuModel_IsSmallestNormal(result) && ::GpuModel_IsBelow(sum, metal::abs(high) < as_type<float>(226492416u) ? productSign : error, as_type<float>(511705088u)) ? ::GpuModel_Zero(result) : ::GpuModel_Flush(result);
}

static float2 GpuModel_FlushFma_1(float2 result, float2 a, float2 b, float2 c)
{
    return float2(::GpuModel_FlushFma(result.x, a.x, b.x, c.x), ::GpuModel_FlushFma(result.y, a.y, b.y, c.y));
}

kernel void SharpCompute_Differential_Tests_Random2x4Model(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int o = int(__tid.x * 123u);
    int p = int(__tid.x * uint(::Slots));
    bool b0 = (input[int(uint(o) + 0u)] & 1UL) != 0UL;
    bool b2 = (input[int(uint(o) + 2u)] & 1UL) != 0UL;
    char sb0 = char(input[int(uint(o) + 6u)]);
    char sb1 = char(input[int(uint(o) + 7u)]);
    char sb2 = char(input[int(uint(o) + 8u)]);
    short sh0 = short(input[int(uint(o) + 9u)]);
    ushort us1 = ushort(input[int(uint(o) + 13u)]);
    ushort us2 = ushort(input[int(uint(o) + 14u)]);
    int i2 = int(input[int(uint(o) + 17u)]);
    uint u0 = uint(input[int(uint(o) + 18u)]);
    uint u1 = uint(input[int(uint(o) + 19u)]);
    uint u2 = uint(input[int(uint(o) + 20u)]);
    long l0 = long(input[int(uint(o) + 21u)]);
    long l1 = long(input[int(uint(o) + 22u)]);
    long l2 = long(input[int(uint(o) + 23u)]);
    ulong ul0 = input[int(uint(o) + 24u)];
    float f0 = as_type<float>(uint(input[int(uint(o) + 27u)]));
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    float f2 = as_type<float>(uint(input[int(uint(o) + 29u)]));
    half h0 = as_type<half>(ushort(input[int(uint(o) + 30u)]));
    half h1 = as_type<half>(ushort(input[int(uint(o) + 31u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float2 f2a = float2(as_type<float>(uint(input[int(uint(o) + 33u)])), as_type<float>(uint(input[int(uint(o) + 34u)])));
    float2 f2b = float2(as_type<float>(uint(input[int(uint(o) + 35u)])), as_type<float>(uint(input[int(uint(o) + 36u)])));
    float3 f3b = float3(as_type<float>(uint(input[int(uint(o) + 40u)])), as_type<float>(uint(input[int(uint(o) + 41u)])), as_type<float>(uint(input[int(uint(o) + 42u)])));
    half3 h3b = half3(as_type<half>(ushort(input[int(uint(o) + 58u)])), as_type<half>(ushort(input[int(uint(o) + 59u)])), as_type<half>(ushort(input[int(uint(o) + 60u)])));
    half4 h4a = half4(as_type<half>(ushort(input[int(uint(o) + 61u)])), as_type<half>(ushort(input[int(uint(o) + 62u)])), as_type<half>(ushort(input[int(uint(o) + 63u)])), as_type<half>(ushort(input[int(uint(o) + 64u)])));
    int2 i2b = int2(int(input[int(uint(o) + 71u)]), int(input[int(uint(o) + 72u)]));
    int3 i3b = int3(int(input[int(uint(o) + 76u)]), int(input[int(uint(o) + 77u)]), int(input[int(uint(o) + 78u)]));
    uint2 u2a = uint2(uint(input[int(uint(o) + 87u)]), uint(input[int(uint(o) + 88u)]));
    uint2 u2b = uint2(uint(input[int(uint(o) + 89u)]), uint(input[int(uint(o) + 90u)]));
    bool2 b2b = bool2((input[int(uint(o) + 107u)] & 1UL) != 0UL, (input[int(uint(o) + 108u)] & 1UL) != 0UL);
    bool4 b4a = bool4((input[int(uint(o) + 115u)] & 1UL) != 0UL, (input[int(uint(o) + 116u)] & 1UL) != 0UL, (input[int(uint(o) + 117u)] & 1UL) != 0UL, (input[int(uint(o) + 118u)] & 1UL) != 0UL);
    float c0_v0_0 = f1;
    float2 c0_v0_1 = metal::fma(metal::fma(float2(f0), float2(f0), -float2(f1, f2)) - (f3b.y * f2b.y), f2a, ((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a)) / (f2 - c0_v0_0));
    output[int(uint(p) + 0u)] = long(as_type<uint>(c0_v0_1.x));
    output[int(uint(p) + 1u)] = long(as_type<uint>(c0_v0_1.y));
    float c0_v1_0 = f1;
    float2 c0_v1_1 = metal::fma(metal::fma(-float2(f3b.y), float2(f2b.y), (f0 * f0) - float2(f1, f2)), f2a, ((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a)) / (f2 - c0_v1_0));
    output[int(uint(p) + 2u)] = long(as_type<uint>(c0_v1_1.x));
    output[int(uint(p) + 3u)] = long(as_type<uint>(c0_v1_1.y));
    float c0_v2_0 = f1;
    float2 c0_v2_1 = metal::fma(metal::fma(-float2(f3b.y), float2(f2b.y), metal::fma(float2(f0), float2(f0), -float2(f1, f2))), f2a, ((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a)) / (f2 - c0_v2_0));
    output[int(uint(p) + 4u)] = long(as_type<uint>(c0_v2_1.x));
    output[int(uint(p) + 5u)] = long(as_type<uint>(c0_v2_1.y));
    float c0_v3_0 = f1;
    float2 c0_v3_1 = ::GpuModel_Flush_1(metal::fma(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f0) * ::GpuModel_Flush(f0))) - ::GpuModel_Flush_1(float2(f1, f2)))) - ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f3b.y) * ::GpuModel_Flush(f2b.y))))), ::GpuModel_Flush_1(f2a), ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a))) / ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v3_0)))))));
    output[int(uint(p) + 6u)] = long(as_type<uint>(c0_v3_1.x));
    output[int(uint(p) + 7u)] = long(as_type<uint>(c0_v3_1.y));
    float c0_v4_0 = f1;
    float2 c0_v4_1 = ::GpuModel_Flush_1(metal::fma(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(metal::fma(float2(::GpuModel_Flush(f0)), float2(::GpuModel_Flush(f0)), -::GpuModel_Flush_1(float2(f1, f2))))) - ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f3b.y) * ::GpuModel_Flush(f2b.y))))), ::GpuModel_Flush_1(f2a), ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a))) / ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v4_0)))))));
    output[int(uint(p) + 8u)] = long(as_type<uint>(c0_v4_1.x));
    output[int(uint(p) + 9u)] = long(as_type<uint>(c0_v4_1.y));
    float c0_v5_0 = f1;
    float2 c0_v5_1 = ::GpuModel_Flush_1(metal::fma(::GpuModel_Flush_1(::GpuModel_Flush_1(metal::fma(-float2(::GpuModel_Flush(f3b.y)), float2(::GpuModel_Flush(f2b.y)), ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f0) * ::GpuModel_Flush(f0))) - ::GpuModel_Flush_1(float2(f1, f2))))))), ::GpuModel_Flush_1(f2a), ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a))) / ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v5_0)))))));
    output[int(uint(p) + 10u)] = long(as_type<uint>(c0_v5_1.x));
    output[int(uint(p) + 11u)] = long(as_type<uint>(c0_v5_1.y));
    float c0_v6_0 = f1;
    float2 c0_v6_1 = ::GpuModel_Flush_1(metal::fma(::GpuModel_Flush_1(::GpuModel_Flush_1(metal::fma(-float2(::GpuModel_Flush(f3b.y)), float2(::GpuModel_Flush(f2b.y)), ::GpuModel_Flush_1(::GpuModel_Flush_1(metal::fma(float2(::GpuModel_Flush(f0)), float2(::GpuModel_Flush(f0)), -::GpuModel_Flush_1(float2(f1, f2)))))))), ::GpuModel_Flush_1(f2a), ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a))) / ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v6_0)))))));
    output[int(uint(p) + 12u)] = long(as_type<uint>(c0_v6_1.x));
    output[int(uint(p) + 13u)] = long(as_type<uint>(c0_v6_1.y));
    float c0_v7_0 = ::GpuModel_Flush(f0);
    float c0_v7_1 = ::GpuModel_Flush(f0);
    float c0_v7_2 = ::GpuModel_Flush(f3b.y);
    float c0_v7_3 = ::GpuModel_Flush(f2b.y);
    float c0_v7_4 = f1;
    float2 c0_v7_5 = ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a)));
    float c0_v7_6 = ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v7_4)));
    float2 c0_v7_7 = float2(c0_v7_6);
    float2 c0_v7_8 = ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush(::GpuModel_FlushProduct(c0_v7_0 * c0_v7_1, c0_v7_0, c0_v7_1)) - ::GpuModel_Flush_1(float2(f1, f2)))) - ::GpuModel_Flush(::GpuModel_FlushProduct(c0_v7_2 * c0_v7_3, c0_v7_2, c0_v7_3))));
    float2 c0_v7_9 = ::GpuModel_Flush_1(f2a);
    float2 c0_v7_10 = ::GpuModel_Flush_1(::GpuModel_FlushQuotient_1(c0_v7_5 / c0_v7_6, c0_v7_5, c0_v7_7));
    float2 c0_v7_11 = ::GpuModel_FlushFma_1(metal::fma(c0_v7_8, c0_v7_9, c0_v7_10), c0_v7_8, c0_v7_9, c0_v7_10);
    output[int(uint(p) + 14u)] = long(as_type<uint>(c0_v7_11.x));
    output[int(uint(p) + 15u)] = long(as_type<uint>(c0_v7_11.y));
    float2 c0_v8_0 = float2(::GpuModel_Flush(f0));
    float2 c0_v8_1 = float2(::GpuModel_Flush(f0));
    float2 c0_v8_2 = -::GpuModel_Flush_1(float2(f1, f2));
    float c0_v8_3 = ::GpuModel_Flush(f3b.y);
    float c0_v8_4 = ::GpuModel_Flush(f2b.y);
    float c0_v8_5 = f1;
    float2 c0_v8_6 = ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a)));
    float c0_v8_7 = ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v8_5)));
    float2 c0_v8_8 = float2(c0_v8_7);
    float2 c0_v8_9 = ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_FlushFma_1(metal::fma(c0_v8_0, c0_v8_1, c0_v8_2), c0_v8_0, c0_v8_1, c0_v8_2)) - ::GpuModel_Flush(::GpuModel_FlushProduct(c0_v8_3 * c0_v8_4, c0_v8_3, c0_v8_4))));
    float2 c0_v8_10 = ::GpuModel_Flush_1(f2a);
    float2 c0_v8_11 = ::GpuModel_Flush_1(::GpuModel_FlushQuotient_1(c0_v8_6 / c0_v8_7, c0_v8_6, c0_v8_8));
    float2 c0_v8_12 = ::GpuModel_FlushFma_1(metal::fma(c0_v8_9, c0_v8_10, c0_v8_11), c0_v8_9, c0_v8_10, c0_v8_11);
    output[int(uint(p) + 16u)] = long(as_type<uint>(c0_v8_12.x));
    output[int(uint(p) + 17u)] = long(as_type<uint>(c0_v8_12.y));
    float c0_v9_0 = ::GpuModel_Flush(f0);
    float c0_v9_1 = ::GpuModel_Flush(f0);
    float2 c0_v9_2 = -float2(::GpuModel_Flush(f3b.y));
    float2 c0_v9_3 = float2(::GpuModel_Flush(f2b.y));
    float2 c0_v9_4 = ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush(::GpuModel_FlushProduct(c0_v9_0 * c0_v9_1, c0_v9_0, c0_v9_1)) - ::GpuModel_Flush_1(float2(f1, f2))));
    float c0_v9_5 = f1;
    float2 c0_v9_6 = ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a)));
    float c0_v9_7 = ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v9_5)));
    float2 c0_v9_8 = float2(c0_v9_7);
    float2 c0_v9_9 = ::GpuModel_Flush_1(::GpuModel_FlushFma_1(metal::fma(c0_v9_2, c0_v9_3, c0_v9_4), c0_v9_2, c0_v9_3, c0_v9_4));
    float2 c0_v9_10 = ::GpuModel_Flush_1(f2a);
    float2 c0_v9_11 = ::GpuModel_Flush_1(::GpuModel_FlushQuotient_1(c0_v9_6 / c0_v9_7, c0_v9_6, c0_v9_8));
    float2 c0_v9_12 = ::GpuModel_FlushFma_1(metal::fma(c0_v9_9, c0_v9_10, c0_v9_11), c0_v9_9, c0_v9_10, c0_v9_11);
    output[int(uint(p) + 18u)] = long(as_type<uint>(c0_v9_12.x));
    output[int(uint(p) + 19u)] = long(as_type<uint>(c0_v9_12.y));
    float2 c0_v10_0 = float2(::GpuModel_Flush(f0));
    float2 c0_v10_1 = float2(::GpuModel_Flush(f0));
    float2 c0_v10_2 = -::GpuModel_Flush_1(float2(f1, f2));
    float2 c0_v10_3 = -float2(::GpuModel_Flush(f3b.y));
    float2 c0_v10_4 = float2(::GpuModel_Flush(f2b.y));
    float2 c0_v10_5 = ::GpuModel_Flush_1(::GpuModel_FlushFma_1(metal::fma(c0_v10_0, c0_v10_1, c0_v10_2), c0_v10_0, c0_v10_1, c0_v10_2));
    float c0_v10_6 = f1;
    float2 c0_v10_7 = ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a)));
    float c0_v10_8 = ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v10_6)));
    float2 c0_v10_9 = float2(c0_v10_8);
    float2 c0_v10_10 = ::GpuModel_Flush_1(::GpuModel_FlushFma_1(metal::fma(c0_v10_3, c0_v10_4, c0_v10_5), c0_v10_3, c0_v10_4, c0_v10_5));
    float2 c0_v10_11 = ::GpuModel_Flush_1(f2a);
    float2 c0_v10_12 = ::GpuModel_Flush_1(::GpuModel_FlushQuotient_1(c0_v10_7 / c0_v10_8, c0_v10_7, c0_v10_9));
    float2 c0_v10_13 = ::GpuModel_FlushFma_1(metal::fma(c0_v10_10, c0_v10_11, c0_v10_12), c0_v10_10, c0_v10_11, c0_v10_12);
    output[int(uint(p) + 20u)] = long(as_type<uint>(c0_v10_13.x));
    output[int(uint(p) + 21u)] = long(as_type<uint>(c0_v10_13.y));
    float c0_v11_0 = f3b.y;
    float c0_v11_1 = f2b.y;
    float c0_v11_2 = f1;
    float2 c0_v11_3 = ::GpuModel_Flush_1((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a));
    float c0_v11_4 = ::GpuModel_Flush(f2 - c0_v11_2);
    float2 c0_v11_5 = float2(c0_v11_4);
    float2 c0_v11_6 = ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_FlushProduct(f0 * f0, f0, f0) - float2(f1, f2)) - ::GpuModel_FlushProduct(c0_v11_0 * c0_v11_1, c0_v11_0, c0_v11_1));
    float2 c0_v11_7 = ::GpuModel_FlushQuotient_1(c0_v11_3 / c0_v11_4, c0_v11_3, c0_v11_5);
    float2 c0_v11_8 = ::GpuModel_FlushFma_1(metal::fma(c0_v11_6, f2a, c0_v11_7), c0_v11_6, f2a, c0_v11_7);
    output[int(uint(p) + 22u)] = long(as_type<uint>(c0_v11_8.x));
    output[int(uint(p) + 23u)] = long(as_type<uint>(c0_v11_8.y));
    float2 c0_v12_0 = float2(f0);
    float2 c0_v12_1 = float2(f0);
    float2 c0_v12_2 = -float2(f1, f2);
    float c0_v12_3 = f3b.y;
    float c0_v12_4 = f2b.y;
    float c0_v12_5 = f1;
    float2 c0_v12_6 = ::GpuModel_Flush_1((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a));
    float c0_v12_7 = ::GpuModel_Flush(f2 - c0_v12_5);
    float2 c0_v12_8 = float2(c0_v12_7);
    float2 c0_v12_9 = ::GpuModel_Flush_1(::GpuModel_FlushFma_1(metal::fma(c0_v12_0, c0_v12_1, c0_v12_2), c0_v12_0, c0_v12_1, c0_v12_2) - ::GpuModel_FlushProduct(c0_v12_3 * c0_v12_4, c0_v12_3, c0_v12_4));
    float2 c0_v12_10 = ::GpuModel_FlushQuotient_1(c0_v12_6 / c0_v12_7, c0_v12_6, c0_v12_8);
    float2 c0_v12_11 = ::GpuModel_FlushFma_1(metal::fma(c0_v12_9, f2a, c0_v12_10), c0_v12_9, f2a, c0_v12_10);
    output[int(uint(p) + 24u)] = long(as_type<uint>(c0_v12_11.x));
    output[int(uint(p) + 25u)] = long(as_type<uint>(c0_v12_11.y));
    float2 c0_v13_0 = -float2(f3b.y);
    float2 c0_v13_1 = float2(f2b.y);
    float2 c0_v13_2 = ::GpuModel_Flush_1(::GpuModel_FlushProduct(f0 * f0, f0, f0) - float2(f1, f2));
    float c0_v13_3 = f1;
    float2 c0_v13_4 = ::GpuModel_Flush_1((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a));
    float c0_v13_5 = ::GpuModel_Flush(f2 - c0_v13_3);
    float2 c0_v13_6 = float2(c0_v13_5);
    float2 c0_v13_7 = ::GpuModel_FlushFma_1(metal::fma(c0_v13_0, c0_v13_1, c0_v13_2), c0_v13_0, c0_v13_1, c0_v13_2);
    float2 c0_v13_8 = ::GpuModel_FlushQuotient_1(c0_v13_4 / c0_v13_5, c0_v13_4, c0_v13_6);
    float2 c0_v13_9 = ::GpuModel_FlushFma_1(metal::fma(c0_v13_7, f2a, c0_v13_8), c0_v13_7, f2a, c0_v13_8);
    output[int(uint(p) + 26u)] = long(as_type<uint>(c0_v13_9.x));
    output[int(uint(p) + 27u)] = long(as_type<uint>(c0_v13_9.y));
    float2 c0_v14_0 = float2(f0);
    float2 c0_v14_1 = float2(f0);
    float2 c0_v14_2 = -float2(f1, f2);
    float2 c0_v14_3 = -float2(f3b.y);
    float2 c0_v14_4 = float2(f2b.y);
    float2 c0_v14_5 = ::GpuModel_FlushFma_1(metal::fma(c0_v14_0, c0_v14_1, c0_v14_2), c0_v14_0, c0_v14_1, c0_v14_2);
    float c0_v14_6 = f1;
    float2 c0_v14_7 = ::GpuModel_Flush_1((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a));
    float c0_v14_8 = ::GpuModel_Flush(f2 - c0_v14_6);
    float2 c0_v14_9 = float2(c0_v14_8);
    float2 c0_v14_10 = ::GpuModel_FlushFma_1(metal::fma(c0_v14_3, c0_v14_4, c0_v14_5), c0_v14_3, c0_v14_4, c0_v14_5);
    float2 c0_v14_11 = ::GpuModel_FlushQuotient_1(c0_v14_7 / c0_v14_8, c0_v14_7, c0_v14_9);
    float2 c0_v14_12 = ::GpuModel_FlushFma_1(metal::fma(c0_v14_10, f2a, c0_v14_11), c0_v14_10, f2a, c0_v14_11);
    output[int(uint(p) + 28u)] = long(as_type<uint>(c0_v14_12.x));
    output[int(uint(p) + 29u)] = long(as_type<uint>(c0_v14_12.y));
    char c1_v0_0 = char(126);
    c1_v0_0++;
    char c1_v0_1 = char(u0);
    c1_v0_1 += int(__ms_popcount(sb0));
    output[int(uint(p) + 30u)] = long(int(__ms_sat_ushort(::GpuModel_Flush(::GpuModel_Flush(metal::floor(::GpuModel_Flush(f1)))))) | int(metal::min((64L > l2) ? c1_v0_0 : sb0, c1_v0_1)));
    char c1_v1_0 = char(126);
    c1_v1_0++;
    char c1_v1_1 = char(u0);
    c1_v1_1 += int(__ms_popcount(sb0));
    output[int(uint(p) + 31u)] = long(int(__ms_sat_ushort(::GpuModel_Flush(::GpuModel_Flush(metal::floor(::GpuModel_Flush(f1)))))) | int(metal::min((64L > l2) ? c1_v1_0 : sb0, c1_v1_1)));
    char c1_v2_0 = char(126);
    c1_v2_0++;
    char c1_v2_1 = char(u0);
    c1_v2_1 += int(__ms_popcount(sb0));
    output[int(uint(p) + 32u)] = long(int(__ms_sat_ushort(::GpuModel_Flush(metal::floor(f1)))) | int(metal::min((64L > l2) ? c1_v2_0 : sb0, c1_v2_1)));
    bool c2_v0_0 = metal::isnan(::GpuModel_Flush(f2)) && (b2b.y ? (::GpuModel_Flush(f1) < ::GpuModel_Flush(f2)) : (b0 ? true : b2));
    output[int(uint(p) + 33u)] = (c2_v0_0) ? 1L : 0L;
    bool c2_v1_0 = metal::isnan(::GpuModel_Flush(f2)) && (b2b.y ? (::GpuModel_Flush(f1) < ::GpuModel_Flush(f2)) : (b0 ? true : b2));
    output[int(uint(p) + 34u)] = (c2_v1_0) ? 1L : 0L;
    bool c2_v2_0 = metal::isnan(f2) && (b2b.y ? (f1 < f2) : (b0 ? true : b2));
    output[int(uint(p) + 35u)] = (c2_v2_0) ? 1L : 0L;
    float2 c5_v0_0 = float2(half2(i2b));
    output[int(uint(p) + 36u)] = long(as_type<uint>(c5_v0_0.x));
    output[int(uint(p) + 37u)] = long(as_type<uint>(c5_v0_0.y));
    float2 c5_v1_0 = float2(half2(i2b));
    output[int(uint(p) + 38u)] = long(as_type<uint>(c5_v1_0.x));
    output[int(uint(p) + 39u)] = long(as_type<uint>(c5_v1_0.y));
    float2 c5_v2_0 = float2(half2(i2b));
    output[int(uint(p) + 40u)] = long(as_type<uint>(c5_v2_0.x));
    output[int(uint(p) + 41u)] = long(as_type<uint>(c5_v2_0.y));
    output[int(uint(p) + 42u)] = long(as_type<uint>(float3(i3b).y));
    output[int(uint(p) + 43u)] = long(as_type<uint>(float3(i3b).y));
    output[int(uint(p) + 44u)] = long(as_type<uint>(float3(i3b).y));
    output[int(uint(p) + 45u)] = long(as_type<ushort>((((::GpuModel_Flush(f0) > ::GpuModel_Flush(f1)) ? __ms_to_half(::GpuModel_Flush(f0)) : (h1 - half(6E-08f))) - half(__ms_sat_short(::GpuModel_Flush(f2)))) / (half(metal::clamp(sb1, char(-1), char(-64))) * h1)));
    output[int(uint(p) + 46u)] = long(as_type<ushort>((((::GpuModel_Flush(f0) > ::GpuModel_Flush(f1)) ? __ms_to_half(::GpuModel_Flush(f0)) : (h1 - half(6E-08f))) - half(__ms_sat_short(::GpuModel_Flush(f2)))) / (half(metal::clamp(sb1, char(-1), char(-64))) * h1)));
    output[int(uint(p) + 47u)] = long(as_type<ushort>((((f0 > f1) ? __ms_to_half(f0) : (h1 - half(6E-08f))) - half(__ms_sat_short(f2))) / (half(metal::clamp(sb1, char(-1), char(-64))) * h1)));
    float c13_v0_0 = float(l2);
    output[int(uint(p) + 48u)] = long(as_type<uint>(c13_v0_0));
    float c13_v1_0 = float(l2);
    output[int(uint(p) + 49u)] = long(as_type<uint>(c13_v1_0));
    float c13_v2_0 = float(l2);
    output[int(uint(p) + 50u)] = long(as_type<uint>(c13_v2_0));
    output[int(uint(p) + 51u)] = long(as_type<uint>(::GpuModel_Flush(::GpuModel_Flush(f1) / ::GpuModel_Flush(f0))));
    float c14_v1_0 = ::GpuModel_Flush(f1);
    float c14_v1_1 = ::GpuModel_Flush(f0);
    output[int(uint(p) + 52u)] = long(as_type<uint>(::GpuModel_FlushQuotient(c14_v1_0 / c14_v1_1, c14_v1_0, c14_v1_1)));
    output[int(uint(p) + 53u)] = long(as_type<uint>(::GpuModel_FlushQuotient(f1 / f0, f1, f0)));
}
