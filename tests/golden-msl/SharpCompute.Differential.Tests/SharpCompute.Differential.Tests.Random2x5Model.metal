// SharpCompute.Differential.Tests.Random2x5Model (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random2x5Model, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline int2 __ms_sat_int2(float2 x) { float2 y = metal::select(float2(0.0f), x, x == x); int2 r = int2(metal::clamp(y, float2(-2147483648.0f), float2(2147483520.0f))); return metal::select(r, int2(2147483647), x >= 2147483648.0f); }
inline int2 __ms_sat_int2(half2 x) { return __ms_sat_int2(float2(x)); }
// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_clz(int x) { return x < 0 ? 0 : metal::clz(x); }

constant int Slots = 18;

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

kernel void SharpCompute_Differential_Tests_Random2x5Model(
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
    output[int(uint(p) + 0u)] = long(as_type<uint>(::GpuModel_Flush(metal::fract(::GpuModel_Flush(metal::abs(::GpuModel_Flush(metal::sqrt(::GpuModel_Flush(float(sh1))))))))));
    output[int(uint(p) + 1u)] = long(as_type<uint>(::GpuModel_Flush(metal::fract(::GpuModel_Flush(metal::abs(::GpuModel_Flush(metal::sqrt(::GpuModel_Flush(float(sh1))))))))));
    output[int(uint(p) + 2u)] = long(as_type<uint>(::GpuModel_Flush(metal::fract(metal::abs(::GpuModel_Flush(metal::sqrt(float(sh1))))))));
    int2 c7_v0_0 = __ms_sat_int2(::GpuModel_Flush_1(-::GpuModel_Flush_1(metal::min(::GpuModel_Flush_1(float2(f1, f0)), ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(f2a) / ::GpuModel_Flush_1(f2b)))))));
    output[int(uint(p) + 3u)] = long(c7_v0_0.x);
    output[int(uint(p) + 4u)] = long(c7_v0_0.y);
    int2 c7_v1_0 = __ms_sat_int2(::GpuModel_Flush_1(-::GpuModel_Flush_1(metal::min(::GpuModel_Flush_1(float2(f1, f0)), ::GpuModel_Flush_1(::GpuModel_FlushTiny_1(::GpuModel_Flush_1(f2a) / ::GpuModel_Flush_1(f2b)))))));
    output[int(uint(p) + 5u)] = long(c7_v1_0.x);
    output[int(uint(p) + 6u)] = long(c7_v1_0.y);
    int2 c7_v2_0 = __ms_sat_int2(-::GpuModel_Flush_1(metal::min(float2(f1, f0), ::GpuModel_FlushTiny_1(f2a / f2b))));
    output[int(uint(p) + 7u)] = long(c7_v2_0.x);
    output[int(uint(p) + 8u)] = long(c7_v2_0.y);
    output[int(uint(p) + 9u)] = long(as_type<uint>(::GpuModel_Flush(::GpuModel_Flush(float(u0)) + ::GpuModel_Flush(1E-10f))));
    output[int(uint(p) + 10u)] = long(as_type<uint>(::GpuModel_Flush(::GpuModel_Flush(float(u0)) + ::GpuModel_Flush(1E-10f))));
    output[int(uint(p) + 11u)] = long(as_type<uint>(::GpuModel_Flush(float(u0) + 1E-10f)));
    output[int(uint(p) + 12u)] = long(__ms_sat_int(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) + ::GpuModel_Flush(f0))) - ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f0) + ::GpuModel_Flush(f1)))))) >> int(long(ulong(metal::min(l2, l1)) * (ulong(l2) - ulong(l2)))));
    output[int(uint(p) + 13u)] = long(__ms_sat_int(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) + ::GpuModel_Flush(f0))) - ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f0) + ::GpuModel_Flush(f1)))))) >> int(long(ulong(metal::min(l2, l1)) * (ulong(l2) - ulong(l2)))));
    output[int(uint(p) + 14u)] = long(__ms_sat_int(::GpuModel_Flush(::GpuModel_Flush(f2 + f0) - ::GpuModel_Flush(f0 + f1))) >> int(long(ulong(metal::min(l2, l1)) * (ulong(l2) - ulong(l2)))));
    output[int(uint(p) + 15u)] = long(__ms_clz(as_type<int>(-f1)));
    output[int(uint(p) + 16u)] = long(__ms_clz(as_type<int>(-f1)));
    output[int(uint(p) + 17u)] = long(__ms_clz(as_type<int>(-f1)));
}
