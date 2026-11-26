// SharpCompute.Differential.Tests.Random1x2Model (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random1x2Model, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer vector arithmetic (MslPrelude.VectorArithmetic).
inline char2 __ms_add(char2 a, char2 b) { return char2(char(uchar(a.x) + uchar(b.x)), char(uchar(a.y) + uchar(b.y))); }
inline char3 __ms_add(char3 a, char3 b) { return char3(char(uchar(a.x) + uchar(b.x)), char(uchar(a.y) + uchar(b.y)), char(uchar(a.z) + uchar(b.z))); }
inline char4 __ms_add(char4 a, char4 b) { return char4(char(uchar(a.x) + uchar(b.x)), char(uchar(a.y) + uchar(b.y)), char(uchar(a.z) + uchar(b.z)), char(uchar(a.w) + uchar(b.w))); }
inline uchar2 __ms_add(uchar2 a, uchar2 b) { return uchar2(uchar(a.x + b.x), uchar(a.y + b.y)); }
inline uchar3 __ms_add(uchar3 a, uchar3 b) { return uchar3(uchar(a.x + b.x), uchar(a.y + b.y), uchar(a.z + b.z)); }
inline uchar4 __ms_add(uchar4 a, uchar4 b) { return uchar4(uchar(a.x + b.x), uchar(a.y + b.y), uchar(a.z + b.z), uchar(a.w + b.w)); }
inline short2 __ms_add(short2 a, short2 b) { return short2(short(ushort(a.x) + ushort(b.x)), short(ushort(a.y) + ushort(b.y))); }
inline short3 __ms_add(short3 a, short3 b) { return short3(short(ushort(a.x) + ushort(b.x)), short(ushort(a.y) + ushort(b.y)), short(ushort(a.z) + ushort(b.z))); }
inline short4 __ms_add(short4 a, short4 b) { return short4(short(ushort(a.x) + ushort(b.x)), short(ushort(a.y) + ushort(b.y)), short(ushort(a.z) + ushort(b.z)), short(ushort(a.w) + ushort(b.w))); }
inline ushort2 __ms_add(ushort2 a, ushort2 b) { return ushort2(ushort(a.x + b.x), ushort(a.y + b.y)); }
inline ushort3 __ms_add(ushort3 a, ushort3 b) { return ushort3(ushort(a.x + b.x), ushort(a.y + b.y), ushort(a.z + b.z)); }
inline ushort4 __ms_add(ushort4 a, ushort4 b) { return ushort4(ushort(a.x + b.x), ushort(a.y + b.y), ushort(a.z + b.z), ushort(a.w + b.w)); }
inline int2 __ms_add(int2 a, int2 b) { return int2(int(uint(a.x) + uint(b.x)), int(uint(a.y) + uint(b.y))); }
inline int3 __ms_add(int3 a, int3 b) { return int3(int(uint(a.x) + uint(b.x)), int(uint(a.y) + uint(b.y)), int(uint(a.z) + uint(b.z))); }
inline int4 __ms_add(int4 a, int4 b) { return int4(int(uint(a.x) + uint(b.x)), int(uint(a.y) + uint(b.y)), int(uint(a.z) + uint(b.z)), int(uint(a.w) + uint(b.w))); }
inline uint2 __ms_add(uint2 a, uint2 b) { return uint2(a.x + b.x, a.y + b.y); }
inline uint3 __ms_add(uint3 a, uint3 b) { return uint3(a.x + b.x, a.y + b.y, a.z + b.z); }
inline uint4 __ms_add(uint4 a, uint4 b) { return uint4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w); }
inline long2 __ms_add(long2 a, long2 b) { return long2(long(ulong(a.x) + ulong(b.x)), long(ulong(a.y) + ulong(b.y))); }
inline long3 __ms_add(long3 a, long3 b) { return long3(long(ulong(a.x) + ulong(b.x)), long(ulong(a.y) + ulong(b.y)), long(ulong(a.z) + ulong(b.z))); }
inline long4 __ms_add(long4 a, long4 b) { return long4(long(ulong(a.x) + ulong(b.x)), long(ulong(a.y) + ulong(b.y)), long(ulong(a.z) + ulong(b.z)), long(ulong(a.w) + ulong(b.w))); }
inline ulong2 __ms_add(ulong2 a, ulong2 b) { return ulong2(a.x + b.x, a.y + b.y); }
inline ulong3 __ms_add(ulong3 a, ulong3 b) { return ulong3(a.x + b.x, a.y + b.y, a.z + b.z); }
inline ulong4 __ms_add(ulong4 a, ulong4 b) { return ulong4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w); }
inline char2 __ms_sub(char2 a, char2 b) { return char2(char(uchar(a.x) - uchar(b.x)), char(uchar(a.y) - uchar(b.y))); }
inline char3 __ms_sub(char3 a, char3 b) { return char3(char(uchar(a.x) - uchar(b.x)), char(uchar(a.y) - uchar(b.y)), char(uchar(a.z) - uchar(b.z))); }
inline char4 __ms_sub(char4 a, char4 b) { return char4(char(uchar(a.x) - uchar(b.x)), char(uchar(a.y) - uchar(b.y)), char(uchar(a.z) - uchar(b.z)), char(uchar(a.w) - uchar(b.w))); }
inline uchar2 __ms_sub(uchar2 a, uchar2 b) { return uchar2(uchar(a.x - b.x), uchar(a.y - b.y)); }
inline uchar3 __ms_sub(uchar3 a, uchar3 b) { return uchar3(uchar(a.x - b.x), uchar(a.y - b.y), uchar(a.z - b.z)); }
inline uchar4 __ms_sub(uchar4 a, uchar4 b) { return uchar4(uchar(a.x - b.x), uchar(a.y - b.y), uchar(a.z - b.z), uchar(a.w - b.w)); }
inline short2 __ms_sub(short2 a, short2 b) { return short2(short(ushort(a.x) - ushort(b.x)), short(ushort(a.y) - ushort(b.y))); }
inline short3 __ms_sub(short3 a, short3 b) { return short3(short(ushort(a.x) - ushort(b.x)), short(ushort(a.y) - ushort(b.y)), short(ushort(a.z) - ushort(b.z))); }
inline short4 __ms_sub(short4 a, short4 b) { return short4(short(ushort(a.x) - ushort(b.x)), short(ushort(a.y) - ushort(b.y)), short(ushort(a.z) - ushort(b.z)), short(ushort(a.w) - ushort(b.w))); }
inline ushort2 __ms_sub(ushort2 a, ushort2 b) { return ushort2(ushort(a.x - b.x), ushort(a.y - b.y)); }
inline ushort3 __ms_sub(ushort3 a, ushort3 b) { return ushort3(ushort(a.x - b.x), ushort(a.y - b.y), ushort(a.z - b.z)); }
inline ushort4 __ms_sub(ushort4 a, ushort4 b) { return ushort4(ushort(a.x - b.x), ushort(a.y - b.y), ushort(a.z - b.z), ushort(a.w - b.w)); }
inline int2 __ms_sub(int2 a, int2 b) { return int2(int(uint(a.x) - uint(b.x)), int(uint(a.y) - uint(b.y))); }
inline int3 __ms_sub(int3 a, int3 b) { return int3(int(uint(a.x) - uint(b.x)), int(uint(a.y) - uint(b.y)), int(uint(a.z) - uint(b.z))); }
inline int4 __ms_sub(int4 a, int4 b) { return int4(int(uint(a.x) - uint(b.x)), int(uint(a.y) - uint(b.y)), int(uint(a.z) - uint(b.z)), int(uint(a.w) - uint(b.w))); }
inline uint2 __ms_sub(uint2 a, uint2 b) { return uint2(a.x - b.x, a.y - b.y); }
inline uint3 __ms_sub(uint3 a, uint3 b) { return uint3(a.x - b.x, a.y - b.y, a.z - b.z); }
inline uint4 __ms_sub(uint4 a, uint4 b) { return uint4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w); }
inline long2 __ms_sub(long2 a, long2 b) { return long2(long(ulong(a.x) - ulong(b.x)), long(ulong(a.y) - ulong(b.y))); }
inline long3 __ms_sub(long3 a, long3 b) { return long3(long(ulong(a.x) - ulong(b.x)), long(ulong(a.y) - ulong(b.y)), long(ulong(a.z) - ulong(b.z))); }
inline long4 __ms_sub(long4 a, long4 b) { return long4(long(ulong(a.x) - ulong(b.x)), long(ulong(a.y) - ulong(b.y)), long(ulong(a.z) - ulong(b.z)), long(ulong(a.w) - ulong(b.w))); }
inline ulong2 __ms_sub(ulong2 a, ulong2 b) { return ulong2(a.x - b.x, a.y - b.y); }
inline ulong3 __ms_sub(ulong3 a, ulong3 b) { return ulong3(a.x - b.x, a.y - b.y, a.z - b.z); }
inline ulong4 __ms_sub(ulong4 a, ulong4 b) { return ulong4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w); }
template <typename T, typename V> inline V __ms_add_to(thread T& target, V value) { V result = __ms_add(V(target), value); target = result; return result; }
template <typename T, typename V> inline V __ms_add_to(device T& target, V value) { V result = __ms_add(V(target), value); target = result; return result; }
template <typename T, typename V> inline V __ms_add_to(threadgroup T& target, V value) { V result = __ms_add(V(target), value); target = result; return result; }
template <typename T, typename V> inline V __ms_sub_from(thread T& target, V value) { V result = __ms_sub(V(target), value); target = result; return result; }
template <typename T, typename V> inline V __ms_sub_from(device T& target, V value) { V result = __ms_sub(V(target), value); target = result; return result; }
template <typename T, typename V> inline V __ms_sub_from(threadgroup T& target, V value) { V result = __ms_sub(V(target), value); target = result; return result; }
// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline ushort __ms_sat_ushort(float x) { float y = x > 0.0f ? x : 0.0f; return ushort(metal::min(y, 65535.0f)); }
inline ushort __ms_sat_ushort(half x) { return __ms_sat_ushort(float(x)); }

constant int Slots = 42;

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
static float3 GpuModel_Flush_1(float3 value);
static float GpuModel_FlushTiny(float value);
static float3 GpuModel_FlushTiny_1(float3 value);

static float GpuModel_Flush(float value)
{
    uint bits = as_type<uint>(value);
    return (bits & 2139095040u) == 0u ? as_type<float>(bits & 2147483648u) : value;
}

static float3 GpuModel_Flush_1(float3 value)
{
    return float3(::GpuModel_Flush(value.x), ::GpuModel_Flush(value.y), ::GpuModel_Flush(value.z));
}

static float GpuModel_FlushTiny(float value)
{
    uint bits = as_type<uint>(value);
    return (bits & 2147483647u) <= 8388608u ? as_type<float>(bits & 2147483648u) : value;
}

static float3 GpuModel_FlushTiny_1(float3 value)
{
    return float3(::GpuModel_FlushTiny(value.x), ::GpuModel_FlushTiny(value.y), ::GpuModel_FlushTiny(value.z));
}

kernel void SharpCompute_Differential_Tests_Random1x2Model(
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
    uchar by0 = uchar(input[int(uint(o) + 3u)]);
    uchar by2 = uchar(input[int(uint(o) + 5u)]);
    char sb0 = char(input[int(uint(o) + 6u)]);
    char sb1 = char(input[int(uint(o) + 7u)]);
    short sh0 = short(input[int(uint(o) + 9u)]);
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
    long l0 = long(input[int(uint(o) + 21u)]);
    ulong ul2 = input[int(uint(o) + 26u)];
    float f0 = as_type<float>(uint(input[int(uint(o) + 27u)]));
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    float f2 = as_type<float>(uint(input[int(uint(o) + 29u)]));
    half h0 = as_type<half>(ushort(input[int(uint(o) + 30u)]));
    half h1 = as_type<half>(ushort(input[int(uint(o) + 31u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float3 f3a = float3(as_type<float>(uint(input[int(uint(o) + 37u)])), as_type<float>(uint(input[int(uint(o) + 38u)])), as_type<float>(uint(input[int(uint(o) + 39u)])));
    half3 h3a = half3(as_type<half>(ushort(input[int(uint(o) + 55u)])), as_type<half>(ushort(input[int(uint(o) + 56u)])), as_type<half>(ushort(input[int(uint(o) + 57u)])));
    half3 h3b = half3(as_type<half>(ushort(input[int(uint(o) + 58u)])), as_type<half>(ushort(input[int(uint(o) + 59u)])), as_type<half>(ushort(input[int(uint(o) + 60u)])));
    int3 i3a = int3(int(input[int(uint(o) + 73u)]), int(input[int(uint(o) + 74u)]), int(input[int(uint(o) + 75u)]));
    int3 i3b = int3(int(input[int(uint(o) + 76u)]), int(input[int(uint(o) + 77u)]), int(input[int(uint(o) + 78u)]));
    uint2 u2a = uint2(uint(input[int(uint(o) + 87u)]), uint(input[int(uint(o) + 88u)]));
    uint4 u4b = uint4(uint(input[int(uint(o) + 101u)]), uint(input[int(uint(o) + 102u)]), uint(input[int(uint(o) + 103u)]), uint(input[int(uint(o) + 104u)]));
    bool2 b2b = bool2((input[int(uint(o) + 107u)] & 1UL) != 0UL, (input[int(uint(o) + 108u)] & 1UL) != 0UL);
    bool3 b3a = bool3((input[int(uint(o) + 109u)] & 1UL) != 0UL, (input[int(uint(o) + 110u)] & 1UL) != 0UL, (input[int(uint(o) + 111u)] & 1UL) != 0UL);
    bool3 b3b = bool3((input[int(uint(o) + 112u)] & 1UL) != 0UL, (input[int(uint(o) + 113u)] & 1UL) != 0UL, (input[int(uint(o) + 114u)] & 1UL) != 0UL);
    float c1_v0_0 = float(u0);
    float c1_v0_1 = c1_v0_0;
    c1_v0_0 = ::GpuModel_Flush(::GpuModel_Flush(c1_v0_0) - 1.0f);
    output[int(uint(p) + 0u)] = long(as_type<uint>(float3(c1_v0_1).y));
    float c1_v1_0 = float(u0);
    float c1_v1_1 = c1_v1_0;
    c1_v1_0 = ::GpuModel_Flush(::GpuModel_Flush(c1_v1_0) - 1.0f);
    output[int(uint(p) + 1u)] = long(as_type<uint>(float3(c1_v1_1).y));
    float c1_v2_0 = float(u0);
    float c1_v2_1 = c1_v2_0;
    c1_v2_0 = ::GpuModel_Flush(c1_v2_0 - 1.0f);
    output[int(uint(p) + 2u)] = long(as_type<uint>(float3(c1_v2_1).y));
    half c4_v0_0 = half(as_type<uint>(metal::abs(f0)));
    --c4_v0_0;
    output[int(uint(p) + 3u)] = long(as_type<ushort>(c4_v0_0 - ((b2b.x ? (b2 ? half(-3.0f) : h1) : (-h1)) - half(as_type<short>(h0)))));
    half c4_v1_0 = half(as_type<uint>(metal::abs(f0)));
    --c4_v1_0;
    output[int(uint(p) + 4u)] = long(as_type<ushort>(c4_v1_0 - ((b2b.x ? (b2 ? half(-3.0f) : h1) : (-h1)) - half(as_type<short>(h0)))));
    half c4_v2_0 = half(as_type<uint>(metal::abs(f0)));
    --c4_v2_0;
    output[int(uint(p) + 5u)] = long(as_type<ushort>(c4_v2_0 - ((b2b.x ? (b2 ? half(-3.0f) : h1) : (-h1)) - half(as_type<short>(h0)))));
    float3 c5_v0_0 = ::GpuModel_Flush_1(::GpuModel_Flush_1(float3(1E+30f, -1.0f, 2.1474836E+09f)) * ::GpuModel_Flush_1(b2 ? f3a : float3(-2.5f, -0.0f, 0.5f)));
    float3 c5_v0_1 = metal::select(c5_v0_0, float3(int3(uint3(metal::select(i3a, i3b, b3b)) * uint3(int3(i0)))), (half3(metal::abs(-h3b)) != half3((h0 - h2) + metal::fmod(half3(half(6E-08f), half(65504.0f), half(1.0f)), h3a))));
    output[int(uint(p) + 6u)] = long(as_type<uint>(c5_v0_1.x));
    output[int(uint(p) + 7u)] = long(as_type<uint>(c5_v0_1.y));
    output[int(uint(p) + 8u)] = long(as_type<uint>(c5_v0_1.z));
    float3 c5_v1_0 = ::GpuModel_FlushTiny_1(::GpuModel_Flush_1(float3(1E+30f, -1.0f, 2.1474836E+09f)) * ::GpuModel_Flush_1(b2 ? f3a : float3(-2.5f, -0.0f, 0.5f)));
    float3 c5_v1_1 = metal::select(c5_v1_0, float3(int3(uint3(metal::select(i3a, i3b, b3b)) * uint3(int3(i0)))), (half3(metal::abs(-h3b)) != half3((h0 - h2) + metal::fmod(half3(half(6E-08f), half(65504.0f), half(1.0f)), h3a))));
    output[int(uint(p) + 9u)] = long(as_type<uint>(c5_v1_1.x));
    output[int(uint(p) + 10u)] = long(as_type<uint>(c5_v1_1.y));
    output[int(uint(p) + 11u)] = long(as_type<uint>(c5_v1_1.z));
    float3 c5_v2_0 = ::GpuModel_FlushTiny_1(float3(1E+30f, -1.0f, 2.1474836E+09f) * (b2 ? f3a : float3(-2.5f, -0.0f, 0.5f)));
    float3 c5_v2_1 = metal::select(c5_v2_0, float3(int3(uint3(metal::select(i3a, i3b, b3b)) * uint3(int3(i0)))), (half3(metal::abs(-h3b)) != half3((h0 - h2) + metal::fmod(half3(half(6E-08f), half(65504.0f), half(1.0f)), h3a))));
    output[int(uint(p) + 12u)] = long(as_type<uint>(c5_v2_1.x));
    output[int(uint(p) + 13u)] = long(as_type<uint>(c5_v2_1.y));
    output[int(uint(p) + 14u)] = long(as_type<uint>(c5_v2_1.z));
    float c6_v0_0 = -f1;
    float c6_v0_1 = c6_v0_0;
    c6_v0_0 = ::GpuModel_Flush(::GpuModel_Flush(c6_v0_0) - 1.0f);
    output[int(uint(p) + 15u)] = long(as_type<uint>(float4(f3a, c6_v0_1).x));
    float c6_v1_0 = -f1;
    float c6_v1_1 = c6_v1_0;
    c6_v1_0 = ::GpuModel_Flush(::GpuModel_Flush(c6_v1_0) - 1.0f);
    output[int(uint(p) + 16u)] = long(as_type<uint>(float4(f3a, c6_v1_1).x));
    float c6_v2_0 = -f1;
    float c6_v2_1 = c6_v2_0;
    c6_v2_0 = ::GpuModel_Flush(c6_v2_0 - 1.0f);
    output[int(uint(p) + 17u)] = long(as_type<uint>(float4(f3a, c6_v2_1).x));
    output[int(uint(p) + 18u)] = long(as_type<uint>(::GpuModel_Flush(metal::sin(::GpuModel_Flush(-::GpuModel_Flush(float(sb1) + ::GpuModel_Flush(10.0f)))))));
    output[int(uint(p) + 19u)] = long(as_type<uint>(::GpuModel_Flush(metal::sin(::GpuModel_Flush(-::GpuModel_Flush(float(sb1) + ::GpuModel_Flush(10.0f)))))));
    output[int(uint(p) + 20u)] = long(as_type<uint>(::GpuModel_Flush(metal::sin(-::GpuModel_Flush(float(sb1) + 10.0f)))));
    long c11_v0_0 = l0;
    int c11_v0_1 = int(metal::reverse_bits(sh0));
    int c11_v0_2 = int(__ms_sat_ushort(::GpuModel_Flush(f0)));
    output[int(uint(p) + 21u)] = long(metal::min(ulong(short(c11_v0_0)), ul2 >> ((c11_v0_2 == 0 || (c11_v0_1 == -2147483647 - 1 && c11_v0_2 == -1)) ? 0 : c11_v0_1 / c11_v0_2)));
    long c11_v1_0 = l0;
    int c11_v1_1 = int(metal::reverse_bits(sh0));
    int c11_v1_2 = int(__ms_sat_ushort(::GpuModel_Flush(f0)));
    output[int(uint(p) + 22u)] = long(metal::min(ulong(short(c11_v1_0)), ul2 >> ((c11_v1_2 == 0 || (c11_v1_1 == -2147483647 - 1 && c11_v1_2 == -1)) ? 0 : c11_v1_1 / c11_v1_2)));
    long c11_v2_0 = l0;
    int c11_v2_1 = int(metal::reverse_bits(sh0));
    int c11_v2_2 = int(__ms_sat_ushort(f0));
    output[int(uint(p) + 23u)] = long(metal::min(ulong(short(c11_v2_0)), ul2 >> ((c11_v2_2 == 0 || (c11_v2_1 == -2147483647 - 1 && c11_v2_2 == -1)) ? 0 : c11_v2_1 / c11_v2_2)));
    float c12_v0_0 = f0;
    float c12_v0_1 = c12_v0_0;
    c12_v0_0 = ::GpuModel_Flush(::GpuModel_Flush(c12_v0_0) - 1.0f);
    float c12_v0_2 = -1E+30f;
    int c12_v0_3 = int(uint(b0 ? i2 : i2) - uint(as_type<ushort>(h0)));
    output[int(uint(p) + 24u)] = long((::GpuModel_Flush(f2) <= ::GpuModel_Flush(::GpuModel_Flush(metal::clamp(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f0) * ::GpuModel_Flush(f2))), ::GpuModel_Flush(c12_v0_1), ::GpuModel_Flush(c12_v0_2))))) ? (int(uint((int3(__ms_sub(uint3(i3b), uint3(i3a)))).z) >> i2)) : c12_v0_3);
    float c12_v1_0 = f0;
    float c12_v1_1 = c12_v1_0;
    c12_v1_0 = ::GpuModel_Flush(::GpuModel_Flush(c12_v1_0) - 1.0f);
    float c12_v1_2 = -1E+30f;
    int c12_v1_3 = int(uint(b0 ? i2 : i2) - uint(as_type<ushort>(h0)));
    output[int(uint(p) + 25u)] = long((::GpuModel_Flush(f2) <= ::GpuModel_Flush(::GpuModel_Flush(metal::clamp(::GpuModel_Flush(::GpuModel_FlushTiny(::GpuModel_Flush(f0) * ::GpuModel_Flush(f2))), ::GpuModel_Flush(c12_v1_1), ::GpuModel_Flush(c12_v1_2))))) ? (int(uint((int3(__ms_sub(uint3(i3b), uint3(i3a)))).z) >> i2)) : c12_v1_3);
    float c12_v2_0 = f0;
    float c12_v2_1 = c12_v2_0;
    c12_v2_0 = ::GpuModel_Flush(c12_v2_0 - 1.0f);
    float c12_v2_2 = -1E+30f;
    int c12_v2_3 = int(uint(b0 ? i2 : i2) - uint(as_type<ushort>(h0)));
    output[int(uint(p) + 26u)] = long((f2 <= ::GpuModel_Flush(metal::clamp(::GpuModel_FlushTiny(f0 * f2), c12_v2_1, c12_v2_2))) ? (int(uint((int3(__ms_sub(uint3(i3b), uint3(i3a)))).z) >> i2)) : c12_v2_3);
    uint3 c13_v0_0 = as_type<uint3>(f3a);
    output[int(uint(p) + 27u)] = long(c13_v0_0.x);
    output[int(uint(p) + 28u)] = long(c13_v0_0.y);
    output[int(uint(p) + 29u)] = long(c13_v0_0.z);
    uint3 c13_v1_0 = as_type<uint3>(f3a);
    output[int(uint(p) + 30u)] = long(c13_v1_0.x);
    output[int(uint(p) + 31u)] = long(c13_v1_0.y);
    output[int(uint(p) + 32u)] = long(c13_v1_0.z);
    uint3 c13_v2_0 = as_type<uint3>(f3a);
    output[int(uint(p) + 33u)] = long(c13_v2_0.x);
    output[int(uint(p) + 34u)] = long(c13_v2_0.y);
    output[int(uint(p) + 35u)] = long(c13_v2_0.z);
    output[int(uint(p) + 36u)] = long(as_type<uint>(-::GpuModel_Flush(metal::trunc(::GpuModel_Flush(float(metal::clamp(1431655765, i2, i2)))))));
    output[int(uint(p) + 37u)] = long(as_type<uint>(-::GpuModel_Flush(metal::trunc(::GpuModel_Flush(float(metal::clamp(1431655765, i2, i2)))))));
    output[int(uint(p) + 38u)] = long(as_type<uint>(-::GpuModel_Flush(metal::trunc(float(metal::clamp(1431655765, i2, i2))))));
    int c15_v0_0 = i2;
    int c15_v0_1 = int(reinterpret_cast<thread uint&>(c15_v0_0)--);
    uint c15_v0_2 = uint(us2);
    uint c15_v0_3 = metal::abs(as_type<uint>(f1));
    output[int(uint(p) + 39u)] = long((metal::abs(u1 >> i2) << metal::min(int(uint(sb0) + uint(i1)), c15_v0_1)) + ((c15_v0_3 == 0u) ? 0u : c15_v0_2 % c15_v0_3));
    int c15_v1_0 = i2;
    int c15_v1_1 = int(reinterpret_cast<thread uint&>(c15_v1_0)--);
    uint c15_v1_2 = uint(us2);
    uint c15_v1_3 = metal::abs(as_type<uint>(f1));
    output[int(uint(p) + 40u)] = long((metal::abs(u1 >> i2) << metal::min(int(uint(sb0) + uint(i1)), c15_v1_1)) + ((c15_v1_3 == 0u) ? 0u : c15_v1_2 % c15_v1_3));
    int c15_v2_0 = i2;
    int c15_v2_1 = int(reinterpret_cast<thread uint&>(c15_v2_0)--);
    uint c15_v2_2 = uint(us2);
    uint c15_v2_3 = metal::abs(as_type<uint>(f1));
    output[int(uint(p) + 41u)] = long((metal::abs(u1 >> i2) << metal::min(int(uint(sb0) + uint(i1)), c15_v2_1)) + ((c15_v2_3 == 0u) ? 0u : c15_v2_2 % c15_v2_3));
}
