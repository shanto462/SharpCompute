// SharpCompute.Differential.Tests.Random2x4Model (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random2x4Model, compile options None
#include <metal_stdlib>

using namespace metal;

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
static float GpuModel_FlushTiny(float value);
static float2 GpuModel_FlushTiny_1(float2 value);

static float GpuModel_Flush(float value)
{
    uint bits = as_type<uint>(value);
    return (bits & 2139095040u) == 0u ? as_type<float>(bits & 2147483648u) : value;
}

static float2 GpuModel_Flush_1(float2 value)
{
    return float2(::GpuModel_Flush(value.x), ::GpuModel_Flush(value.y));
}

static float GpuModel_FlushTiny(float value)
{
    uint bits = as_type<uint>(value);
    return (bits & 2147483647u) <= 8388608u ? as_type<float>(bits & 2147483648u) : value;
}

static float2 GpuModel_FlushTiny_1(float2 value)
{
    return float2(::GpuModel_FlushTiny(value.x), ::GpuModel_FlushTiny(value.y));
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
    uchar by1 = uchar(input[int(uint(o) + 4u)]);
    uchar by2 = uchar(input[int(uint(o) + 5u)]);
    char sb0 = char(input[int(uint(o) + 6u)]);
    char sb1 = char(input[int(uint(o) + 7u)]);
    char sb2 = char(input[int(uint(o) + 8u)]);
    ushort us1 = ushort(input[int(uint(o) + 13u)]);
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
    float c0_v7_0 = f1;
    float2 c0_v7_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush(::GpuModel_FlushTiny(::GpuModel_Flush(f0) * ::GpuModel_Flush(f0))) - ::GpuModel_Flush_1(float2(f1, f2)))) - ::GpuModel_Flush(::GpuModel_FlushTiny(::GpuModel_Flush(f3b.y) * ::GpuModel_Flush(f2b.y))))), ::GpuModel_Flush_1(f2a), ::GpuModel_Flush_1(::GpuModel_FlushTiny_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a))) / ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v7_0)))))));
    output[int(uint(p) + 14u)] = long(as_type<uint>(c0_v7_1.x));
    output[int(uint(p) + 15u)] = long(as_type<uint>(c0_v7_1.y));
    float c0_v8_0 = f1;
    float2 c0_v8_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_FlushTiny_1(metal::fma(float2(::GpuModel_Flush(f0)), float2(::GpuModel_Flush(f0)), -::GpuModel_Flush_1(float2(f1, f2))))) - ::GpuModel_Flush(::GpuModel_FlushTiny(::GpuModel_Flush(f3b.y) * ::GpuModel_Flush(f2b.y))))), ::GpuModel_Flush_1(f2a), ::GpuModel_Flush_1(::GpuModel_FlushTiny_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a))) / ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v8_0)))))));
    output[int(uint(p) + 16u)] = long(as_type<uint>(c0_v8_1.x));
    output[int(uint(p) + 17u)] = long(as_type<uint>(c0_v8_1.y));
    float c0_v9_0 = f1;
    float2 c0_v9_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_1(::GpuModel_FlushTiny_1(metal::fma(-float2(::GpuModel_Flush(f3b.y)), float2(::GpuModel_Flush(f2b.y)), ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush(::GpuModel_FlushTiny(::GpuModel_Flush(f0) * ::GpuModel_Flush(f0))) - ::GpuModel_Flush_1(float2(f1, f2))))))), ::GpuModel_Flush_1(f2a), ::GpuModel_Flush_1(::GpuModel_FlushTiny_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a))) / ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v9_0)))))));
    output[int(uint(p) + 18u)] = long(as_type<uint>(c0_v9_1.x));
    output[int(uint(p) + 19u)] = long(as_type<uint>(c0_v9_1.y));
    float c0_v10_0 = f1;
    float2 c0_v10_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_1(::GpuModel_FlushTiny_1(metal::fma(-float2(::GpuModel_Flush(f3b.y)), float2(::GpuModel_Flush(f2b.y)), ::GpuModel_Flush_1(::GpuModel_FlushTiny_1(metal::fma(float2(::GpuModel_Flush(f0)), float2(::GpuModel_Flush(f0)), -::GpuModel_Flush_1(float2(f1, f2)))))))), ::GpuModel_Flush_1(f2a), ::GpuModel_Flush_1(::GpuModel_FlushTiny_1(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(false ? float2(1E+10f, 1E-10f) : f2b) + ::GpuModel_Flush_1(true ? float2(3.0f, -3.4028235E+38f) : f2a))) / ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - ::GpuModel_Flush(c0_v10_0)))))));
    output[int(uint(p) + 20u)] = long(as_type<uint>(c0_v10_1.x));
    output[int(uint(p) + 21u)] = long(as_type<uint>(c0_v10_1.y));
    float c0_v11_0 = f1;
    float2 c0_v11_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_FlushTiny(f0 * f0) - float2(f1, f2)) - ::GpuModel_FlushTiny(f3b.y * f2b.y)), f2a, ::GpuModel_FlushTiny_1(::GpuModel_Flush_1((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a)) / ::GpuModel_Flush(f2 - c0_v11_0))));
    output[int(uint(p) + 22u)] = long(as_type<uint>(c0_v11_1.x));
    output[int(uint(p) + 23u)] = long(as_type<uint>(c0_v11_1.y));
    float c0_v12_0 = f1;
    float2 c0_v12_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_1(::GpuModel_FlushTiny_1(metal::fma(float2(f0), float2(f0), -float2(f1, f2))) - ::GpuModel_FlushTiny(f3b.y * f2b.y)), f2a, ::GpuModel_FlushTiny_1(::GpuModel_Flush_1((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a)) / ::GpuModel_Flush(f2 - c0_v12_0))));
    output[int(uint(p) + 24u)] = long(as_type<uint>(c0_v12_1.x));
    output[int(uint(p) + 25u)] = long(as_type<uint>(c0_v12_1.y));
    float c0_v13_0 = f1;
    float2 c0_v13_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_FlushTiny_1(metal::fma(-float2(f3b.y), float2(f2b.y), ::GpuModel_Flush_1(::GpuModel_FlushTiny(f0 * f0) - float2(f1, f2)))), f2a, ::GpuModel_FlushTiny_1(::GpuModel_Flush_1((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a)) / ::GpuModel_Flush(f2 - c0_v13_0))));
    output[int(uint(p) + 26u)] = long(as_type<uint>(c0_v13_1.x));
    output[int(uint(p) + 27u)] = long(as_type<uint>(c0_v13_1.y));
    float c0_v14_0 = f1;
    float2 c0_v14_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_FlushTiny_1(metal::fma(-float2(f3b.y), float2(f2b.y), ::GpuModel_FlushTiny_1(metal::fma(float2(f0), float2(f0), -float2(f1, f2))))), f2a, ::GpuModel_FlushTiny_1(::GpuModel_Flush_1((false ? float2(1E+10f, 1E-10f) : f2b) + (true ? float2(3.0f, -3.4028235E+38f) : f2a)) / ::GpuModel_Flush(f2 - c0_v14_0))));
    output[int(uint(p) + 28u)] = long(as_type<uint>(c0_v14_1.x));
    output[int(uint(p) + 29u)] = long(as_type<uint>(c0_v14_1.y));
    char c1_v0_0 = char(126);
    c1_v0_0++;
    char c1_v0_1 = char(u0);
    c1_v0_1 += int(metal::popcount(sb0));
    output[int(uint(p) + 30u)] = long(int(ushort(metal::clamp(int(::GpuModel_Flush(::GpuModel_Flush(metal::floor(::GpuModel_Flush(f1))))), 0, 65535))) | int(metal::min((64L > l2) ? c1_v0_0 : sb0, c1_v0_1)));
    char c1_v1_0 = char(126);
    c1_v1_0++;
    char c1_v1_1 = char(u0);
    c1_v1_1 += int(metal::popcount(sb0));
    output[int(uint(p) + 31u)] = long(int(ushort(metal::clamp(int(::GpuModel_Flush(::GpuModel_Flush(metal::floor(::GpuModel_Flush(f1))))), 0, 65535))) | int(metal::min((64L > l2) ? c1_v1_0 : sb0, c1_v1_1)));
    char c1_v2_0 = char(126);
    c1_v2_0++;
    char c1_v2_1 = char(u0);
    c1_v2_1 += int(metal::popcount(sb0));
    output[int(uint(p) + 32u)] = long(int(ushort(metal::clamp(int(::GpuModel_Flush(metal::floor(f1))), 0, 65535))) | int(metal::min((64L > l2) ? c1_v2_0 : sb0, c1_v2_1)));
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
    output[int(uint(p) + 45u)] = long(as_type<ushort>((((::GpuModel_Flush(f0) > ::GpuModel_Flush(f1)) ? half(::GpuModel_Flush(f0)) : (h1 - half(6E-08f))) - half(short(metal::clamp(int(::GpuModel_Flush(f2)), -32768, 32767)))) / (half(metal::clamp(sb1, char(-1), char(-64))) * h1)));
    output[int(uint(p) + 46u)] = long(as_type<ushort>((((::GpuModel_Flush(f0) > ::GpuModel_Flush(f1)) ? half(::GpuModel_Flush(f0)) : (h1 - half(6E-08f))) - half(short(metal::clamp(int(::GpuModel_Flush(f2)), -32768, 32767)))) / (half(metal::clamp(sb1, char(-1), char(-64))) * h1)));
    output[int(uint(p) + 47u)] = long(as_type<ushort>((((f0 > f1) ? half(f0) : (h1 - half(6E-08f))) - half(short(metal::clamp(int(f2), -32768, 32767)))) / (half(metal::clamp(sb1, char(-1), char(-64))) * h1)));
    float c13_v0_0 = float(l2);
    output[int(uint(p) + 48u)] = long(as_type<uint>(c13_v0_0));
    float c13_v1_0 = float(l2);
    output[int(uint(p) + 49u)] = long(as_type<uint>(c13_v1_0));
    float c13_v2_0 = float(l2);
    output[int(uint(p) + 50u)] = long(as_type<uint>(c13_v2_0));
    output[int(uint(p) + 51u)] = long(as_type<uint>(::GpuModel_Flush(::GpuModel_Flush(f1) / ::GpuModel_Flush(f0))));
    output[int(uint(p) + 52u)] = long(as_type<uint>(::GpuModel_FlushTiny(::GpuModel_Flush(f1) / ::GpuModel_Flush(f0))));
    output[int(uint(p) + 53u)] = long(as_type<uint>(::GpuModel_FlushTiny(f1 / f0)));
}
