// SharpCompute.Differential.Tests.Random2x0Model (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random2x0Model, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 35;

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
static float4 GpuModel_Flush_1(float4 value);
static float GpuModel_FlushTiny(float value);
static float2 GpuModel_Flush_2(float2 value);
static float2 GpuModel_FlushTiny_1(float2 value);

static float GpuModel_Flush(float value)
{
    uint bits = as_type<uint>(value);
    return (bits & 2139095040u) == 0u ? as_type<float>(bits & 2147483648u) : value;
}

static float4 GpuModel_Flush_1(float4 value)
{
    return float4(::GpuModel_Flush(value.x), ::GpuModel_Flush(value.y), ::GpuModel_Flush(value.z), ::GpuModel_Flush(value.w));
}

static float GpuModel_FlushTiny(float value)
{
    uint bits = as_type<uint>(value);
    return (bits & 2147483647u) <= 8388608u ? as_type<float>(bits & 2147483648u) : value;
}

static float2 GpuModel_Flush_2(float2 value)
{
    return float2(::GpuModel_Flush(value.x), ::GpuModel_Flush(value.y));
}

static float2 GpuModel_FlushTiny_1(float2 value)
{
    return float2(::GpuModel_FlushTiny(value.x), ::GpuModel_FlushTiny(value.y));
}

kernel void SharpCompute_Differential_Tests_Random2x0Model(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int o = int(__tid.x * 123u);
    int p = int(__tid.x * uint(::Slots));
    bool b0 = (input[int(uint(o) + 0u)] & 1UL) != 0UL;
    bool b1 = (input[int(uint(o) + 1u)] & 1UL) != 0UL;
    bool b2 = (input[int(uint(o) + 2u)] & 1UL) != 0UL;
    uchar by2 = uchar(input[int(uint(o) + 5u)]);
    char sb0 = char(input[int(uint(o) + 6u)]);
    char sb1 = char(input[int(uint(o) + 7u)]);
    short sh0 = short(input[int(uint(o) + 9u)]);
    short sh1 = short(input[int(uint(o) + 10u)]);
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
    float f0 = as_type<float>(uint(input[int(uint(o) + 27u)]));
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float2 f2a = float2(as_type<float>(uint(input[int(uint(o) + 33u)])), as_type<float>(uint(input[int(uint(o) + 34u)])));
    float2 f2b = float2(as_type<float>(uint(input[int(uint(o) + 35u)])), as_type<float>(uint(input[int(uint(o) + 36u)])));
    float3 f3a = float3(as_type<float>(uint(input[int(uint(o) + 37u)])), as_type<float>(uint(input[int(uint(o) + 38u)])), as_type<float>(uint(input[int(uint(o) + 39u)])));
    float4 f4a = float4(as_type<float>(uint(input[int(uint(o) + 43u)])), as_type<float>(uint(input[int(uint(o) + 44u)])), as_type<float>(uint(input[int(uint(o) + 45u)])), as_type<float>(uint(input[int(uint(o) + 46u)])));
    float4 f4b = float4(as_type<float>(uint(input[int(uint(o) + 47u)])), as_type<float>(uint(input[int(uint(o) + 48u)])), as_type<float>(uint(input[int(uint(o) + 49u)])), as_type<float>(uint(input[int(uint(o) + 50u)])));
    half2 h2a = half2(as_type<half>(ushort(input[int(uint(o) + 51u)])), as_type<half>(ushort(input[int(uint(o) + 52u)])));
    half2 h2b = half2(as_type<half>(ushort(input[int(uint(o) + 53u)])), as_type<half>(ushort(input[int(uint(o) + 54u)])));
    half3 h3a = half3(as_type<half>(ushort(input[int(uint(o) + 55u)])), as_type<half>(ushort(input[int(uint(o) + 56u)])), as_type<half>(ushort(input[int(uint(o) + 57u)])));
    int2 i2b = int2(int(input[int(uint(o) + 71u)]), int(input[int(uint(o) + 72u)]));
    int4 i4a = int4(int(input[int(uint(o) + 79u)]), int(input[int(uint(o) + 80u)]), int(input[int(uint(o) + 81u)]), int(input[int(uint(o) + 82u)]));
    int4 i4b = int4(int(input[int(uint(o) + 83u)]), int(input[int(uint(o) + 84u)]), int(input[int(uint(o) + 85u)]), int(input[int(uint(o) + 86u)]));
    uint2 u2b = uint2(uint(input[int(uint(o) + 89u)]), uint(input[int(uint(o) + 90u)]));
    uint4 u4b = uint4(uint(input[int(uint(o) + 101u)]), uint(input[int(uint(o) + 102u)]), uint(input[int(uint(o) + 103u)]), uint(input[int(uint(o) + 104u)]));
    bool2 b2b = bool2((input[int(uint(o) + 107u)] & 1UL) != 0UL, (input[int(uint(o) + 108u)] & 1UL) != 0UL);
    bool3 b3b = bool3((input[int(uint(o) + 112u)] & 1UL) != 0UL, (input[int(uint(o) + 113u)] & 1UL) != 0UL, (input[int(uint(o) + 114u)] & 1UL) != 0UL);
    float4 c1_v0_0 = f4b;
    float3 c1_v0_1 = f3a;
    c1_v0_0 = ::GpuModel_Flush_1(::GpuModel_Flush_1(c1_v0_0) + ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(metal::abs(b2 ? f4a : f4b)) - ::GpuModel_Flush_1(c1_v0_1.xzyy))));
    float4 c1_v0_2 = c1_v0_0;
    output[int(uint(p) + 0u)] = long(as_type<uint>(c1_v0_2.x));
    output[int(uint(p) + 1u)] = long(as_type<uint>(c1_v0_2.y));
    output[int(uint(p) + 2u)] = long(as_type<uint>(c1_v0_2.z));
    output[int(uint(p) + 3u)] = long(as_type<uint>(c1_v0_2.w));
    float4 c1_v1_0 = f4b;
    float3 c1_v1_1 = f3a;
    c1_v1_0 = ::GpuModel_Flush_1(::GpuModel_Flush_1(c1_v1_0) + ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(metal::abs(b2 ? f4a : f4b)) - ::GpuModel_Flush_1(c1_v1_1.xzyy))));
    float4 c1_v1_2 = c1_v1_0;
    output[int(uint(p) + 4u)] = long(as_type<uint>(c1_v1_2.x));
    output[int(uint(p) + 5u)] = long(as_type<uint>(c1_v1_2.y));
    output[int(uint(p) + 6u)] = long(as_type<uint>(c1_v1_2.z));
    output[int(uint(p) + 7u)] = long(as_type<uint>(c1_v1_2.w));
    float4 c1_v2_0 = f4b;
    float3 c1_v2_1 = f3a;
    c1_v2_0 = ::GpuModel_Flush_1(c1_v2_0 + ::GpuModel_Flush_1(metal::abs(b2 ? f4a : f4b) - c1_v2_1.xzyy));
    float4 c1_v2_2 = c1_v2_0;
    output[int(uint(p) + 8u)] = long(as_type<uint>(c1_v2_2.x));
    output[int(uint(p) + 9u)] = long(as_type<uint>(c1_v2_2.y));
    output[int(uint(p) + 10u)] = long(as_type<uint>(c1_v2_2.z));
    output[int(uint(p) + 11u)] = long(as_type<uint>(c1_v2_2.w));
    output[int(uint(p) + 12u)] = long(as_type<uint>(f4b.x));
    output[int(uint(p) + 13u)] = long(as_type<uint>(f4b.x));
    output[int(uint(p) + 14u)] = long(as_type<uint>(f4b.x));
    output[int(uint(p) + 15u)] = long(as_type<uint>(::GpuModel_Flush(metal::pow(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(b1 ? f0 : f0) * ::GpuModel_Flush(b1 ? f1 : f0))), ::GpuModel_Flush(::GpuModel_Flush(metal::rint(::GpuModel_Flush(::GpuModel_Flush(metal::round(::GpuModel_Flush(f0)))))))))));
    output[int(uint(p) + 16u)] = long(as_type<uint>(::GpuModel_Flush(metal::pow(::GpuModel_Flush(::GpuModel_FlushTiny(::GpuModel_Flush(b1 ? f0 : f0) * ::GpuModel_Flush(b1 ? f1 : f0))), ::GpuModel_Flush(::GpuModel_Flush(metal::rint(::GpuModel_Flush(::GpuModel_Flush(metal::round(::GpuModel_Flush(f0)))))))))));
    output[int(uint(p) + 17u)] = long(as_type<uint>(::GpuModel_Flush(metal::pow(::GpuModel_FlushTiny((b1 ? f0 : f0) * (b1 ? f1 : f0)), ::GpuModel_Flush(metal::rint(::GpuModel_Flush(metal::round(f0))))))));
    float2 c14_v0_0 = f2a;
    float2 c14_v0_1 = metal::fma(float2(f1, float(by2)), float2(metal::sign(h2a / h2b)), metal::fma(-c14_v0_0, metal::select(float2(16777216.0f, 3.4028235E+38f), f2b, b2b), f2b));
    output[int(uint(p) + 18u)] = long(as_type<uint>(c14_v0_1.x));
    output[int(uint(p) + 19u)] = long(as_type<uint>(c14_v0_1.y));
    float2 c14_v1_0 = f2a;
    float2 c14_v1_1 = ::GpuModel_Flush_2(metal::fma(::GpuModel_Flush_2(float2(f1, float(by2))), ::GpuModel_Flush_2(float2(metal::sign(h2a / h2b))), ::GpuModel_Flush_2(::GpuModel_Flush_2(::GpuModel_Flush_2(f2b) - ::GpuModel_Flush_2(::GpuModel_Flush_2(::GpuModel_Flush_2(c14_v1_0) * ::GpuModel_Flush_2(metal::select(float2(16777216.0f, 3.4028235E+38f), f2b, b2b))))))));
    output[int(uint(p) + 20u)] = long(as_type<uint>(c14_v1_1.x));
    output[int(uint(p) + 21u)] = long(as_type<uint>(c14_v1_1.y));
    float2 c14_v2_0 = f2a;
    float2 c14_v2_1 = ::GpuModel_Flush_2(metal::fma(::GpuModel_Flush_2(float2(f1, float(by2))), ::GpuModel_Flush_2(float2(metal::sign(h2a / h2b))), ::GpuModel_Flush_2(::GpuModel_Flush_2(metal::fma(-::GpuModel_Flush_2(c14_v2_0), ::GpuModel_Flush_2(metal::select(float2(16777216.0f, 3.4028235E+38f), f2b, b2b)), ::GpuModel_Flush_2(f2b))))));
    output[int(uint(p) + 22u)] = long(as_type<uint>(c14_v2_1.x));
    output[int(uint(p) + 23u)] = long(as_type<uint>(c14_v2_1.y));
    float2 c14_v3_0 = f2a;
    float2 c14_v3_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_2(float2(f1, float(by2))), ::GpuModel_Flush_2(float2(metal::sign(h2a / h2b))), ::GpuModel_Flush_2(::GpuModel_Flush_2(::GpuModel_Flush_2(f2b) - ::GpuModel_Flush_2(::GpuModel_FlushTiny_1(::GpuModel_Flush_2(c14_v3_0) * ::GpuModel_Flush_2(metal::select(float2(16777216.0f, 3.4028235E+38f), f2b, b2b))))))));
    output[int(uint(p) + 24u)] = long(as_type<uint>(c14_v3_1.x));
    output[int(uint(p) + 25u)] = long(as_type<uint>(c14_v3_1.y));
    float2 c14_v4_0 = f2a;
    float2 c14_v4_1 = ::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_2(float2(f1, float(by2))), ::GpuModel_Flush_2(float2(metal::sign(h2a / h2b))), ::GpuModel_Flush_2(::GpuModel_FlushTiny_1(metal::fma(-::GpuModel_Flush_2(c14_v4_0), ::GpuModel_Flush_2(metal::select(float2(16777216.0f, 3.4028235E+38f), f2b, b2b)), ::GpuModel_Flush_2(f2b))))));
    output[int(uint(p) + 26u)] = long(as_type<uint>(c14_v4_1.x));
    output[int(uint(p) + 27u)] = long(as_type<uint>(c14_v4_1.y));
    float2 c14_v5_0 = f2a;
    float2 c14_v5_1 = ::GpuModel_FlushTiny_1(metal::fma(float2(f1, float(by2)), float2(metal::sign(h2a / h2b)), ::GpuModel_Flush_2(f2b - ::GpuModel_FlushTiny_1(c14_v5_0 * metal::select(float2(16777216.0f, 3.4028235E+38f), f2b, b2b)))));
    output[int(uint(p) + 28u)] = long(as_type<uint>(c14_v5_1.x));
    output[int(uint(p) + 29u)] = long(as_type<uint>(c14_v5_1.y));
    float2 c14_v6_0 = f2a;
    float2 c14_v6_1 = ::GpuModel_FlushTiny_1(metal::fma(float2(f1, float(by2)), float2(metal::sign(h2a / h2b)), ::GpuModel_FlushTiny_1(metal::fma(-c14_v6_0, metal::select(float2(16777216.0f, 3.4028235E+38f), f2b, b2b), f2b))));
    output[int(uint(p) + 30u)] = long(as_type<uint>(c14_v6_1.x));
    output[int(uint(p) + 31u)] = long(as_type<uint>(c14_v6_1.y));
    output[int(uint(p) + 32u)] = long(as_type<uint>(::GpuModel_Flush(metal::distance(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(float4(i4a)) - ::GpuModel_Flush(metal::copysign(f1, -f1)))), ::GpuModel_Flush_1(f4a)))));
    output[int(uint(p) + 33u)] = long(as_type<uint>(::GpuModel_Flush(metal::distance(::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(float4(i4a)) - ::GpuModel_Flush(metal::copysign(f1, -f1)))), ::GpuModel_Flush_1(f4a)))));
    output[int(uint(p) + 34u)] = long(as_type<uint>(::GpuModel_Flush(metal::distance(::GpuModel_Flush_1(float4(i4a) - metal::copysign(f1, -f1)), f4a))));
}
