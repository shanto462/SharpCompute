// SharpCompute.Differential.Tests.Random3x5Model (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random3x5Model, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute prelude (MslPrelude.Source).
inline float __ms_radians(float x) { return x * 0.017453292519943295f; }
inline float2 __ms_radians(float2 x) { return x * 0.017453292519943295f; }
inline float3 __ms_radians(float3 x) { return x * 0.017453292519943295f; }
inline float4 __ms_radians(float4 x) { return x * 0.017453292519943295f; }
inline half __ms_radians(half x) { return x * half(0.017453292519943295f); }
inline half2 __ms_radians(half2 x) { return x * half(0.017453292519943295f); }
inline half3 __ms_radians(half3 x) { return x * half(0.017453292519943295f); }
inline half4 __ms_radians(half4 x) { return x * half(0.017453292519943295f); }
inline float __ms_degrees(float x) { return x * 57.29577951308232f; }
inline float2 __ms_degrees(float2 x) { return x * 57.29577951308232f; }
inline float3 __ms_degrees(float3 x) { return x * 57.29577951308232f; }
inline float4 __ms_degrees(float4 x) { return x * 57.29577951308232f; }
inline half __ms_degrees(half x) { return x * half(57.29577951308232f); }
inline half2 __ms_degrees(half2 x) { return x * half(57.29577951308232f); }
inline half3 __ms_degrees(half3 x) { return x * half(57.29577951308232f); }
inline half4 __ms_degrees(half4 x) { return x * half(57.29577951308232f); }
inline int __ms_atomic_compare_exchange(device metal::atomic_int* location, int comparand, int value) { int expected = comparand; while (!metal::atomic_compare_exchange_weak_explicit(location, &expected, value, metal::memory_order_relaxed, metal::memory_order_relaxed) && expected == comparand) { } return expected; }
inline uint __ms_atomic_compare_exchange(device metal::atomic_uint* location, uint comparand, uint value) { uint expected = comparand; while (!metal::atomic_compare_exchange_weak_explicit(location, &expected, value, metal::memory_order_relaxed, metal::memory_order_relaxed) && expected == comparand) { } return expected; }
inline int __ms_atomic_compare_exchange(threadgroup metal::atomic_int* location, int comparand, int value) { int expected = comparand; while (!metal::atomic_compare_exchange_weak_explicit(location, &expected, value, metal::memory_order_relaxed, metal::memory_order_relaxed) && expected == comparand) { } return expected; }
inline uint __ms_atomic_compare_exchange(threadgroup metal::atomic_uint* location, uint comparand, uint value) { uint expected = comparand; while (!metal::atomic_compare_exchange_weak_explicit(location, &expected, value, metal::memory_order_relaxed, metal::memory_order_relaxed) && expected == comparand) { } return expected; }
inline float __ms_atomic_fetch_add_float(threadgroup metal::atomic_uint* location, float value) { uint expected = metal::atomic_load_explicit(location, metal::memory_order_relaxed); while (!metal::atomic_compare_exchange_weak_explicit(location, &expected, as_type<uint>(as_type<float>(expected) + value), metal::memory_order_relaxed, metal::memory_order_relaxed)) { } return as_type<float>(expected); }
inline long __ms_float_to_long(float x) { return x >= 9223372036854775808.0f ? 9223372036854775807L : (x < -9223372036854775808.0f ? (-9223372036854775807L - 1L) : long(x)); }
inline float __ms_math_min(float x, float y) { return x != y ? (metal::isnan(x) ? x : (x < y ? x : y)) : (metal::signbit(x) ? x : y); }
inline float __ms_math_max(float x, float y) { return x != y ? (metal::isnan(x) ? x : (y < x ? x : y)) : (metal::signbit(y) ? x : y); }
inline float __ms_math_clamp(float x, float lo, float hi) { return x < lo ? lo : (x > hi ? hi : x); }
inline uchar __ms_math_clamp(uchar x, uchar lo, uchar hi) { return x < lo ? lo : (x > hi ? hi : x); }
inline char __ms_math_clamp(char x, char lo, char hi) { return x < lo ? lo : (x > hi ? hi : x); }
inline short __ms_math_clamp(short x, short lo, short hi) { return x < lo ? lo : (x > hi ? hi : x); }
inline ushort __ms_math_clamp(ushort x, ushort lo, ushort hi) { return x < lo ? lo : (x > hi ? hi : x); }
inline int __ms_math_clamp(int x, int lo, int hi) { return x < lo ? lo : (x > hi ? hi : x); }
inline uint __ms_math_clamp(uint x, uint lo, uint hi) { return x < lo ? lo : (x > hi ? hi : x); }
inline long __ms_math_clamp(long x, long lo, long hi) { return x < lo ? lo : (x > hi ? hi : x); }
inline ulong __ms_math_clamp(ulong x, ulong lo, ulong hi) { return x < lo ? lo : (x > hi ? hi : x); }
inline float __ms_number_clamp(float x, float lo, float hi) { return __ms_math_min(__ms_math_max(x, lo), hi); }
inline half __ms_half_rem(half x, half y) { return metal::copysign(metal::fmod(x, y), x); }
inline float __ms_number_lerp(float a, float b, float t) { return metal::fma(a, 1.0f - t, b * t); }
inline int __ms_ilogb(float x) { return metal::isnan(x) ? 2147483647 : metal::ilogb(x); }
inline uchar __ms_rotate_left(uchar x, int count) { return metal::rotate(x, uchar(uint(count) & 7u)); }
inline uchar __ms_rotate_right(uchar x, int count) { return metal::rotate(x, uchar((0u - uint(count)) & 7u)); }
inline char __ms_rotate_left(char x, int count) { return metal::rotate(x, char(uint(count) & 7u)); }
inline char __ms_rotate_right(char x, int count) { return metal::rotate(x, char((0u - uint(count)) & 7u)); }
inline short __ms_rotate_left(short x, int count) { return metal::rotate(x, short(uint(count) & 15u)); }
inline short __ms_rotate_right(short x, int count) { return metal::rotate(x, short((0u - uint(count)) & 15u)); }
inline ushort __ms_rotate_left(ushort x, int count) { return metal::rotate(x, ushort(uint(count) & 15u)); }
inline ushort __ms_rotate_right(ushort x, int count) { return metal::rotate(x, ushort((0u - uint(count)) & 15u)); }
inline int __ms_rotate_left(int x, int count) { return metal::rotate(x, int(uint(count) & 31u)); }
inline int __ms_rotate_right(int x, int count) { return metal::rotate(x, int((0u - uint(count)) & 31u)); }
inline uint __ms_rotate_left(uint x, int count) { return metal::rotate(x, uint(uint(count) & 31u)); }
inline uint __ms_rotate_right(uint x, int count) { return metal::rotate(x, uint((0u - uint(count)) & 31u)); }
inline long __ms_rotate_left(long x, int count) { return metal::rotate(x, long(uint(count) & 63u)); }
inline long __ms_rotate_right(long x, int count) { return metal::rotate(x, long((0u - uint(count)) & 63u)); }
inline ulong __ms_rotate_left(ulong x, int count) { return metal::rotate(x, ulong(uint(count) & 63u)); }
inline ulong __ms_rotate_right(ulong x, int count) { return metal::rotate(x, ulong((0u - uint(count)) & 63u)); }
inline char2 __ms_neg(char2 x) { return char2(char(-uchar(x.x)), char(-uchar(x.y))); }
inline char3 __ms_neg(char3 x) { return char3(char(-uchar(x.x)), char(-uchar(x.y)), char(-uchar(x.z))); }
inline char4 __ms_neg(char4 x) { return char4(char(-uchar(x.x)), char(-uchar(x.y)), char(-uchar(x.z)), char(-uchar(x.w))); }
inline uchar2 __ms_neg(uchar2 x) { return uchar2(uchar(-x.x), uchar(-x.y)); }
inline uchar3 __ms_neg(uchar3 x) { return uchar3(uchar(-x.x), uchar(-x.y), uchar(-x.z)); }
inline uchar4 __ms_neg(uchar4 x) { return uchar4(uchar(-x.x), uchar(-x.y), uchar(-x.z), uchar(-x.w)); }
inline short2 __ms_neg(short2 x) { return short2(short(-ushort(x.x)), short(-ushort(x.y))); }
inline short3 __ms_neg(short3 x) { return short3(short(-ushort(x.x)), short(-ushort(x.y)), short(-ushort(x.z))); }
inline short4 __ms_neg(short4 x) { return short4(short(-ushort(x.x)), short(-ushort(x.y)), short(-ushort(x.z)), short(-ushort(x.w))); }
inline ushort2 __ms_neg(ushort2 x) { return ushort2(ushort(-x.x), ushort(-x.y)); }
inline ushort3 __ms_neg(ushort3 x) { return ushort3(ushort(-x.x), ushort(-x.y), ushort(-x.z)); }
inline ushort4 __ms_neg(ushort4 x) { return ushort4(ushort(-x.x), ushort(-x.y), ushort(-x.z), ushort(-x.w)); }
inline int2 __ms_neg(int2 x) { return int2(int(-uint(x.x)), int(-uint(x.y))); }
inline int3 __ms_neg(int3 x) { return int3(int(-uint(x.x)), int(-uint(x.y)), int(-uint(x.z))); }
inline int4 __ms_neg(int4 x) { return int4(int(-uint(x.x)), int(-uint(x.y)), int(-uint(x.z)), int(-uint(x.w))); }
inline uint2 __ms_neg(uint2 x) { return uint2(-x.x, -x.y); }
inline uint3 __ms_neg(uint3 x) { return uint3(-x.x, -x.y, -x.z); }
inline uint4 __ms_neg(uint4 x) { return uint4(-x.x, -x.y, -x.z, -x.w); }
inline long2 __ms_neg(long2 x) { return long2(long(-ulong(x.x)), long(-ulong(x.y))); }
inline long3 __ms_neg(long3 x) { return long3(long(-ulong(x.x)), long(-ulong(x.y)), long(-ulong(x.z))); }
inline long4 __ms_neg(long4 x) { return long4(long(-ulong(x.x)), long(-ulong(x.y)), long(-ulong(x.z)), long(-ulong(x.w))); }
inline ulong2 __ms_neg(ulong2 x) { return ulong2(-x.x, -x.y); }
inline ulong3 __ms_neg(ulong3 x) { return ulong3(-x.x, -x.y, -x.z); }
inline ulong4 __ms_neg(ulong4 x) { return ulong4(-x.x, -x.y, -x.z, -x.w); }
inline int2 __ms_mul(int2 a, int2 b) { return int2(int(uint(a.x) * uint(b.x)), int(uint(a.y) * uint(b.y))); }
inline int3 __ms_mul(int3 a, int3 b) { return int3(int(uint(a.x) * uint(b.x)), int(uint(a.y) * uint(b.y)), int(uint(a.z) * uint(b.z))); }
inline int4 __ms_mul(int4 a, int4 b) { return int4(int(uint(a.x) * uint(b.x)), int(uint(a.y) * uint(b.y)), int(uint(a.z) * uint(b.z)), int(uint(a.w) * uint(b.w))); }
inline uint2 __ms_mul(uint2 a, uint2 b) { return uint2(a.x * b.x, a.y * b.y); }
inline uint3 __ms_mul(uint3 a, uint3 b) { return uint3(a.x * b.x, a.y * b.y, a.z * b.z); }
inline uint4 __ms_mul(uint4 a, uint4 b) { return uint4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w); }
struct __ms_packed_float4x4 { packed_float4 columns[4]; };
static_assert(sizeof(__ms_packed_float4x4) == 64 && alignof(__ms_packed_float4x4) == 4, "SharpCompute: layout of '__ms_packed_float4x4' does not match its C# layout");
inline float4x4 __ms_unpack(__ms_packed_float4x4 m) { return float4x4(float4(m.columns[0]), float4(m.columns[1]), float4(m.columns[2]), float4(m.columns[3])); }
inline __ms_packed_float4x4 __ms_pack(float4x4 m) { return __ms_packed_float4x4{{m[0], m[1], m[2], m[3]}}; }
inline bool __ms_wave_all_equal(float v) { return metal::simd_all(v == metal::simd_broadcast_first(v)); }
inline bool __ms_wave_all_equal(float2 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(float3 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(float4 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(int v) { return metal::simd_all(v == metal::simd_broadcast_first(v)); }
inline bool __ms_wave_all_equal(int2 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(int3 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(int4 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(uint v) { return metal::simd_all(v == metal::simd_broadcast_first(v)); }
inline bool __ms_wave_all_equal(uint2 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(uint3 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(uint4 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(half v) { return metal::simd_all(v == metal::simd_broadcast_first(v)); }
inline bool __ms_wave_all_equal(half2 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(half3 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
inline bool __ms_wave_all_equal(half4 v) { return metal::simd_all(metal::all(v == metal::simd_broadcast_first(v))); }
template <typename T> inline metal::simdgroup_matrix<T, 8, 8> __ms_wave_matrix_load(const device T* source, ulong stride, bool transpose) { metal::simdgroup_matrix<T, 8, 8> matrix; metal::simdgroup_load(matrix, source, stride, ulong2(0), transpose); return matrix; }
template <typename T> inline metal::simdgroup_matrix<T, 8, 8> __ms_wave_matrix_load(const threadgroup T* source, ulong stride, bool transpose) { metal::simdgroup_matrix<T, 8, 8> matrix; metal::simdgroup_load(matrix, source, stride, ulong2(0), transpose); return matrix; }
template <typename R, typename T, typename U> inline metal::simdgroup_matrix<R, 8, 8> __ms_wave_matrix_multiply(metal::simdgroup_matrix<T, 8, 8> a, metal::simdgroup_matrix<U, 8, 8> b) { metal::simdgroup_matrix<R, 8, 8> product; metal::simdgroup_multiply(product, a, b); return product; }
template <typename R, typename T, typename U, typename V> inline metal::simdgroup_matrix<R, 8, 8> __ms_wave_matrix_multiply_accumulate(metal::simdgroup_matrix<T, 8, 8> a, metal::simdgroup_matrix<U, 8, 8> b, metal::simdgroup_matrix<V, 8, 8> c) { metal::simdgroup_matrix<R, 8, 8> sum; metal::simdgroup_multiply_accumulate(sum, a, b, c); return sum; }

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
static float3 GpuModel_Flush_1(float3 value);
static float GpuModel_FlushTiny(float value);

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

kernel void SharpCompute_Differential_Tests_Random3x5Model(
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
    uchar by1 = uchar(input[int(uint(o) + 4u)]);
    uchar by2 = uchar(input[int(uint(o) + 5u)]);
    char sb0 = char(input[int(uint(o) + 6u)]);
    char sb1 = char(input[int(uint(o) + 7u)]);
    char sb2 = char(input[int(uint(o) + 8u)]);
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
    ulong ul0 = input[int(uint(o) + 24u)];
    ulong ul2 = input[int(uint(o) + 26u)];
    float f0 = as_type<float>(uint(input[int(uint(o) + 27u)]));
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    float f2 = as_type<float>(uint(input[int(uint(o) + 29u)]));
    half h0 = as_type<half>(ushort(input[int(uint(o) + 30u)]));
    half h1 = as_type<half>(ushort(input[int(uint(o) + 31u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float3 f3a = float3(as_type<float>(uint(input[int(uint(o) + 37u)])), as_type<float>(uint(input[int(uint(o) + 38u)])), as_type<float>(uint(input[int(uint(o) + 39u)])));
    float3 f3b = float3(as_type<float>(uint(input[int(uint(o) + 40u)])), as_type<float>(uint(input[int(uint(o) + 41u)])), as_type<float>(uint(input[int(uint(o) + 42u)])));
    float4 f4a = float4(as_type<float>(uint(input[int(uint(o) + 43u)])), as_type<float>(uint(input[int(uint(o) + 44u)])), as_type<float>(uint(input[int(uint(o) + 45u)])), as_type<float>(uint(input[int(uint(o) + 46u)])));
    half2 h2a = half2(as_type<half>(ushort(input[int(uint(o) + 51u)])), as_type<half>(ushort(input[int(uint(o) + 52u)])));
    half3 h3b = half3(as_type<half>(ushort(input[int(uint(o) + 58u)])), as_type<half>(ushort(input[int(uint(o) + 59u)])), as_type<half>(ushort(input[int(uint(o) + 60u)])));
    int2 i2b = int2(int(input[int(uint(o) + 71u)]), int(input[int(uint(o) + 72u)]));
    uint2 u2a = uint2(uint(input[int(uint(o) + 87u)]), uint(input[int(uint(o) + 88u)]));
    uint3 u3b = uint3(uint(input[int(uint(o) + 94u)]), uint(input[int(uint(o) + 95u)]), uint(input[int(uint(o) + 96u)]));
    uint4 u4b = uint4(uint(input[int(uint(o) + 101u)]), uint(input[int(uint(o) + 102u)]), uint(input[int(uint(o) + 103u)]), uint(input[int(uint(o) + 104u)]));
    bool3 b3a = bool3((input[int(uint(o) + 109u)] & 1UL) != 0UL, (input[int(uint(o) + 110u)] & 1UL) != 0UL, (input[int(uint(o) + 111u)] & 1UL) != 0UL);
    uint c1_v0_0 = metal::ctz(u1);
    c1_v0_0 <<= (b2 ? i0 : i1);
    bool2 c1_v0_1 = (u2 == c1_v0_0) ? (((u2a >> i0) ^ u3b.xx) > as_type<uint4>(f4a).wz) : (half(i1) > (b3a.z ? h3b.yy : half2(i2b)));
    output[int(uint(p) + 0u)] = (c1_v0_1.x) ? 1L : 0L;
    output[int(uint(p) + 1u)] = (c1_v0_1.y) ? 1L : 0L;
    uint c1_v1_0 = metal::ctz(u1);
    c1_v1_0 <<= (b2 ? i0 : i1);
    bool2 c1_v1_1 = (u2 == c1_v1_0) ? (((u2a >> i0) ^ u3b.xx) > as_type<uint4>(f4a).wz) : (half(i1) > (b3a.z ? h3b.yy : half2(i2b)));
    output[int(uint(p) + 2u)] = (c1_v1_1.x) ? 1L : 0L;
    output[int(uint(p) + 3u)] = (c1_v1_1.y) ? 1L : 0L;
    uint c1_v2_0 = metal::ctz(u1);
    c1_v2_0 <<= (b2 ? i0 : i1);
    bool2 c1_v2_1 = (u2 == c1_v2_0) ? (((u2a >> i0) ^ u3b.xx) > as_type<uint4>(f4a).wz) : (half(i1) > (b3a.z ? h3b.yy : half2(i2b)));
    output[int(uint(p) + 4u)] = (c1_v2_1.x) ? 1L : 0L;
    output[int(uint(p) + 5u)] = (c1_v2_1.y) ? 1L : 0L;
    output[int(uint(p) + 6u)] = long(as_type<uint>(-::GpuModel_Flush_1(metal::clamp(::GpuModel_Flush_1(f3a), ::GpuModel_Flush_1(f3b), ::GpuModel_Flush_1(f3a))).xxyy.w));
    output[int(uint(p) + 7u)] = long(as_type<uint>(-::GpuModel_Flush_1(metal::clamp(::GpuModel_Flush_1(f3a), ::GpuModel_Flush_1(f3b), ::GpuModel_Flush_1(f3a))).xxyy.w));
    output[int(uint(p) + 8u)] = long(as_type<uint>(-::GpuModel_Flush_1(metal::clamp(f3a, f3b, f3a)).xxyy.w));
    short c7_v0_0 = short(metal::clamp(int(::GpuModel_Flush(f2)), -32768, 32767));
    short c7_v0_1 = c7_v0_0--;
    ushort c7_v0_2 = __ms_rotate_left(us2, i1);
    ushort c7_v0_3 = c7_v0_2--;
    ushort c7_v0_4 = c7_v0_3;
    ushort c7_v0_5 = as_type<ushort>(h0);
    ushort c7_v0_6 = ushort(1000);
    c7_v0_5 &= int(__ms_math_clamp(us2, metal::min(c7_v0_6, us0), metal::max(c7_v0_6, us0)));
    c7_v0_4 -= int(c7_v0_5);
    int c7_v0_7 = int(c7_v0_1);
    int c7_v0_8 = int(c7_v0_4);
    output[int(uint(p) + 9u)] = long((c7_v0_8 == 0 || (c7_v0_7 == -2147483647 - 1 && c7_v0_8 == -1)) ? 0 : c7_v0_7 % c7_v0_8);
    short c7_v1_0 = short(metal::clamp(int(::GpuModel_Flush(f2)), -32768, 32767));
    short c7_v1_1 = c7_v1_0--;
    ushort c7_v1_2 = __ms_rotate_left(us2, i1);
    ushort c7_v1_3 = c7_v1_2--;
    ushort c7_v1_4 = c7_v1_3;
    ushort c7_v1_5 = as_type<ushort>(h0);
    ushort c7_v1_6 = ushort(1000);
    c7_v1_5 &= int(__ms_math_clamp(us2, metal::min(c7_v1_6, us0), metal::max(c7_v1_6, us0)));
    c7_v1_4 -= int(c7_v1_5);
    int c7_v1_7 = int(c7_v1_1);
    int c7_v1_8 = int(c7_v1_4);
    output[int(uint(p) + 10u)] = long((c7_v1_8 == 0 || (c7_v1_7 == -2147483647 - 1 && c7_v1_8 == -1)) ? 0 : c7_v1_7 % c7_v1_8);
    short c7_v2_0 = short(metal::clamp(int(f2), -32768, 32767));
    short c7_v2_1 = c7_v2_0--;
    ushort c7_v2_2 = __ms_rotate_left(us2, i1);
    ushort c7_v2_3 = c7_v2_2--;
    ushort c7_v2_4 = c7_v2_3;
    ushort c7_v2_5 = as_type<ushort>(h0);
    ushort c7_v2_6 = ushort(1000);
    c7_v2_5 &= int(__ms_math_clamp(us2, metal::min(c7_v2_6, us0), metal::max(c7_v2_6, us0)));
    c7_v2_4 -= int(c7_v2_5);
    int c7_v2_7 = int(c7_v2_1);
    int c7_v2_8 = int(c7_v2_4);
    output[int(uint(p) + 11u)] = long((c7_v2_8 == 0 || (c7_v2_7 == -2147483647 - 1 && c7_v2_8 == -1)) ? 0 : c7_v2_7 % c7_v2_8);
    float c10_v0_0 = ::GpuModel_Flush(::GpuModel_Flush(f1) * ::GpuModel_Flush(f1));
    float c10_v0_1 = c10_v0_0;
    c10_v0_0 = ::GpuModel_Flush(::GpuModel_Flush(c10_v0_0) - 1.0f);
    float c10_v0_2 = ::GpuModel_Flush(float(uint(us2) + u1));
    float c10_v0_3 = ::GpuModel_Flush(c10_v0_1);
    output[int(uint(p) + 12u)] = long(as_type<uint>(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(metal::round(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(metal::fract(::GpuModel_Flush(f1)))) * ::GpuModel_Flush(-f0)))))) + ::GpuModel_Flush(::GpuModel_Flush(c10_v0_2 > c10_v0_3 ? 0.0f : __ms_math_clamp(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f0) - ::GpuModel_Flush(f0))), c10_v0_2, c10_v0_3))))));
    float c10_v1_0 = ::GpuModel_FlushTiny(::GpuModel_Flush(f1) * ::GpuModel_Flush(f1));
    float c10_v1_1 = c10_v1_0;
    c10_v1_0 = ::GpuModel_Flush(::GpuModel_Flush(c10_v1_0) - 1.0f);
    float c10_v1_2 = ::GpuModel_Flush(float(uint(us2) + u1));
    float c10_v1_3 = ::GpuModel_Flush(c10_v1_1);
    output[int(uint(p) + 13u)] = long(as_type<uint>(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(metal::round(::GpuModel_Flush(::GpuModel_FlushTiny(::GpuModel_Flush(::GpuModel_Flush(metal::fract(::GpuModel_Flush(f1)))) * ::GpuModel_Flush(-f0)))))) + ::GpuModel_Flush(::GpuModel_Flush(c10_v1_2 > c10_v1_3 ? 0.0f : __ms_math_clamp(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f0) - ::GpuModel_Flush(f0))), c10_v1_2, c10_v1_3))))));
    float c10_v2_0 = ::GpuModel_FlushTiny(f1 * f1);
    float c10_v2_1 = c10_v2_0;
    c10_v2_0 = ::GpuModel_Flush(c10_v2_0 - 1.0f);
    float c10_v2_2 = float(uint(us2) + u1);
    float c10_v2_3 = c10_v2_1;
    output[int(uint(p) + 14u)] = long(as_type<uint>(::GpuModel_Flush(::GpuModel_Flush(metal::round(::GpuModel_FlushTiny(::GpuModel_Flush(metal::fract(f1)) * (-f0)))) + ::GpuModel_Flush(c10_v2_2 > c10_v2_3 ? 0.0f : __ms_math_clamp(::GpuModel_Flush(f0 - f0), c10_v2_2, c10_v2_3)))));
    float c13_v0_0 = 1E-10f;
    c13_v0_0 = ::GpuModel_Flush(::GpuModel_Flush(c13_v0_0) + 1.0f);
    char c13_v0_1 = sb2;
    c13_v0_1 = char(uint(int(c13_v0_1)) >> i0);
    output[int(uint(p) + 15u)] = long(as_type<uint>(::GpuModel_Flush(metal::floor(::GpuModel_Flush((long(ushort(65535)) < (l1 >> i2)) ? ::GpuModel_Flush(::GpuModel_Flush(c13_v0_0) * ::GpuModel_Flush(-f0)) : float(c13_v0_1))))));
    float c13_v1_0 = 1E-10f;
    c13_v1_0 = ::GpuModel_Flush(::GpuModel_Flush(c13_v1_0) + 1.0f);
    char c13_v1_1 = sb2;
    c13_v1_1 = char(uint(int(c13_v1_1)) >> i0);
    output[int(uint(p) + 16u)] = long(as_type<uint>(::GpuModel_Flush(metal::floor(::GpuModel_Flush((long(ushort(65535)) < (l1 >> i2)) ? ::GpuModel_FlushTiny(::GpuModel_Flush(c13_v1_0) * ::GpuModel_Flush(-f0)) : float(c13_v1_1))))));
    float c13_v2_0 = 1E-10f;
    c13_v2_0 = ::GpuModel_Flush(c13_v2_0 + 1.0f);
    char c13_v2_1 = sb2;
    c13_v2_1 = char(uint(int(c13_v2_1)) >> i0);
    output[int(uint(p) + 17u)] = long(as_type<uint>(::GpuModel_Flush(metal::floor((long(ushort(65535)) < (l1 >> i2)) ? ::GpuModel_FlushTiny(c13_v2_0 * (-f0)) : float(c13_v2_1)))));
}
