// SharpCompute.Differential.Tests.Random1x4Model (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random1x4Model, compile options None
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

constant int Slots = 27;

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
static bool GpuModel_IsSmallestNormal(float value);
static bool GpuModel_IsBelow(float rounded, float error, float bound);
static float GpuModel_Zero(float value);
static float GpuModel_FlushProduct(float result, float a, float b);
static float GpuModel_FlushQuotient(float result, float a, float b);

static float GpuModel_Flush(float value)
{
    uint bits = as_type<uint>(value);
    return (bits & 2139095040u) == 0u ? as_type<float>(bits & 2147483648u) : value;
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

kernel void SharpCompute_Differential_Tests_Random1x4Model(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int o = int(__tid.x * 123u);
    int p = int(__tid.x * uint(::Slots));
    bool b2 = (input[int(uint(o) + 2u)] & 1UL) != 0UL;
    uchar by0 = uchar(input[int(uint(o) + 3u)]);
    uchar by1 = uchar(input[int(uint(o) + 4u)]);
    uchar by2 = uchar(input[int(uint(o) + 5u)]);
    char sb0 = char(input[int(uint(o) + 6u)]);
    char sb1 = char(input[int(uint(o) + 7u)]);
    char sb2 = char(input[int(uint(o) + 8u)]);
    short sh2 = short(input[int(uint(o) + 11u)]);
    ushort us0 = ushort(input[int(uint(o) + 12u)]);
    int i0 = int(input[int(uint(o) + 15u)]);
    int i1 = int(input[int(uint(o) + 16u)]);
    int i2 = int(input[int(uint(o) + 17u)]);
    uint u0 = uint(input[int(uint(o) + 18u)]);
    uint u2 = uint(input[int(uint(o) + 20u)]);
    long l0 = long(input[int(uint(o) + 21u)]);
    long l1 = long(input[int(uint(o) + 22u)]);
    long l2 = long(input[int(uint(o) + 23u)]);
    ulong ul0 = input[int(uint(o) + 24u)];
    ulong ul1 = input[int(uint(o) + 25u)];
    ulong ul2 = input[int(uint(o) + 26u)];
    float f0 = as_type<float>(uint(input[int(uint(o) + 27u)]));
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    float f2 = as_type<float>(uint(input[int(uint(o) + 29u)]));
    half h0 = as_type<half>(ushort(input[int(uint(o) + 30u)]));
    half h1 = as_type<half>(ushort(input[int(uint(o) + 31u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float2 f2a = float2(as_type<float>(uint(input[int(uint(o) + 33u)])), as_type<float>(uint(input[int(uint(o) + 34u)])));
    float2 f2b = float2(as_type<float>(uint(input[int(uint(o) + 35u)])), as_type<float>(uint(input[int(uint(o) + 36u)])));
    float4 f4b = float4(as_type<float>(uint(input[int(uint(o) + 47u)])), as_type<float>(uint(input[int(uint(o) + 48u)])), as_type<float>(uint(input[int(uint(o) + 49u)])), as_type<float>(uint(input[int(uint(o) + 50u)])));
    half2 h2a = half2(as_type<half>(ushort(input[int(uint(o) + 51u)])), as_type<half>(ushort(input[int(uint(o) + 52u)])));
    half2 h2b = half2(as_type<half>(ushort(input[int(uint(o) + 53u)])), as_type<half>(ushort(input[int(uint(o) + 54u)])));
    half4 h4b = half4(as_type<half>(ushort(input[int(uint(o) + 65u)])), as_type<half>(ushort(input[int(uint(o) + 66u)])), as_type<half>(ushort(input[int(uint(o) + 67u)])), as_type<half>(ushort(input[int(uint(o) + 68u)])));
    int2 i2a = int2(int(input[int(uint(o) + 69u)]), int(input[int(uint(o) + 70u)]));
    int2 i2b = int2(int(input[int(uint(o) + 71u)]), int(input[int(uint(o) + 72u)]));
    uint3 u3a = uint3(uint(input[int(uint(o) + 91u)]), uint(input[int(uint(o) + 92u)]), uint(input[int(uint(o) + 93u)]));
    uint3 u3b = uint3(uint(input[int(uint(o) + 94u)]), uint(input[int(uint(o) + 95u)]), uint(input[int(uint(o) + 96u)]));
    bool2 b2a = bool2((input[int(uint(o) + 105u)] & 1UL) != 0UL, (input[int(uint(o) + 106u)] & 1UL) != 0UL);
    bool3 b3b = bool3((input[int(uint(o) + 112u)] & 1UL) != 0UL, (input[int(uint(o) + 113u)] & 1UL) != 0UL, (input[int(uint(o) + 114u)] & 1UL) != 0UL);
    float c1_v0_0 = float(ul0);
    float c1_v0_1 = ::GpuModel_Flush(-metal::select(f2b, f2b, b2a).x);
    float c1_v0_2 = ::GpuModel_Flush(::GpuModel_Flush(metal::max(::GpuModel_Flush(c1_v0_0), ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f0) * ::GpuModel_Flush(f1))) / ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(-2.5f) + ::GpuModel_Flush(f0))))))));
    output[int(uint(p) + 0u)] = long(as_type<uint>(::GpuModel_Flush(c1_v0_1 > c1_v0_2 ? 0.0f : __ms_number_clamp(::GpuModel_Flush(::GpuModel_Flush(__ms_math_min(::GpuModel_Flush(::GpuModel_Flush(__ms_math_min(::GpuModel_Flush(f4b.x), ::GpuModel_Flush(f2a.x)))), ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) / ::GpuModel_Flush(1E-10f))) + ::GpuModel_Flush(-f0)))))), c1_v0_1, c1_v0_2))));
    float c1_v1_0 = ::GpuModel_Flush(f2);
    float c1_v1_1 = ::GpuModel_Flush(1E-10f);
    float c1_v1_2 = float(ul0);
    float c1_v1_3 = ::GpuModel_Flush(f0);
    float c1_v1_4 = ::GpuModel_Flush(f1);
    float c1_v1_5 = ::GpuModel_Flush(::GpuModel_FlushProduct(c1_v1_3 * c1_v1_4, c1_v1_3, c1_v1_4));
    float c1_v1_6 = ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(-2.5f) + ::GpuModel_Flush(f0)));
    float c1_v1_7 = ::GpuModel_Flush(-metal::select(f2b, f2b, b2a).x);
    float c1_v1_8 = ::GpuModel_Flush(::GpuModel_Flush(metal::max(::GpuModel_Flush(c1_v1_2), ::GpuModel_Flush(::GpuModel_FlushQuotient(c1_v1_5 / c1_v1_6, c1_v1_5, c1_v1_6)))));
    output[int(uint(p) + 1u)] = long(as_type<uint>(::GpuModel_Flush(c1_v1_7 > c1_v1_8 ? 0.0f : __ms_number_clamp(::GpuModel_Flush(::GpuModel_Flush(__ms_math_min(::GpuModel_Flush(::GpuModel_Flush(__ms_math_min(::GpuModel_Flush(f4b.x), ::GpuModel_Flush(f2a.x)))), ::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_FlushQuotient(c1_v1_0 / c1_v1_1, c1_v1_0, c1_v1_1)) + ::GpuModel_Flush(-f0)))))), c1_v1_7, c1_v1_8))));
    float c1_v2_0 = 1E-10f;
    float c1_v2_1 = float(ul0);
    float c1_v2_2 = ::GpuModel_FlushProduct(f0 * f1, f0, f1);
    float c1_v2_3 = ::GpuModel_Flush((-2.5f) + f0);
    float c1_v2_4 = -metal::select(f2b, f2b, b2a).x;
    float c1_v2_5 = ::GpuModel_Flush(metal::max(c1_v2_1, ::GpuModel_FlushQuotient(c1_v2_2 / c1_v2_3, c1_v2_2, c1_v2_3)));
    output[int(uint(p) + 2u)] = long(as_type<uint>(::GpuModel_Flush(c1_v2_4 > c1_v2_5 ? 0.0f : __ms_number_clamp(::GpuModel_Flush(__ms_math_min(::GpuModel_Flush(__ms_math_min(f4b.x, f2a.x)), ::GpuModel_Flush(::GpuModel_FlushQuotient(f2 / c1_v2_0, f2, c1_v2_0) + (-f0)))), c1_v2_4, c1_v2_5))));
    output[int(uint(p) + 3u)] = long(as_type<uint>(::GpuModel_Flush(metal::log10(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - float(sb1))) - float(metal::clamp(short(2), short(255), sh2))))))));
    output[int(uint(p) + 4u)] = long(as_type<uint>(::GpuModel_Flush(metal::log10(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) - float(sb1))) - float(metal::clamp(short(2), short(255), sh2))))))));
    output[int(uint(p) + 5u)] = long(as_type<uint>(::GpuModel_Flush(metal::log10(::GpuModel_Flush(::GpuModel_Flush(f2 - float(sb1)) - float(metal::clamp(short(2), short(255), sh2)))))));
    int c7_v0_0 = i2;
    c7_v0_0 >>= 65536;
    short c7_v0_1 = short(255);
    c7_v0_1 -= int(sh2);
    output[int(uint(p) + 6u)] = long(as_type<uint>(float(((2147483647u >> i1) >> c7_v0_0) << (int(c7_v0_1) << int(sb2)))));
    int c7_v1_0 = i2;
    c7_v1_0 >>= 65536;
    short c7_v1_1 = short(255);
    c7_v1_1 -= int(sh2);
    output[int(uint(p) + 7u)] = long(as_type<uint>(float(((2147483647u >> i1) >> c7_v1_0) << (int(c7_v1_1) << int(sb2)))));
    int c7_v2_0 = i2;
    c7_v2_0 >>= 65536;
    short c7_v2_1 = short(255);
    c7_v2_1 -= int(sh2);
    output[int(uint(p) + 8u)] = long(as_type<uint>(float(((2147483647u >> i1) >> c7_v2_0) << (int(c7_v2_1) << int(sb2)))));
    float c8_v0_0 = ::GpuModel_Flush(metal::floor(::GpuModel_Flush(f0)));
    float c8_v0_1 = c8_v0_0;
    c8_v0_0 = ::GpuModel_Flush(::GpuModel_Flush(c8_v0_0) + 1.0f);
    float c8_v0_2 = ::GpuModel_Flush(::GpuModel_Flush(f1) - ::GpuModel_Flush(f0));
    output[int(uint(p) + 9u)] = long(as_type<uint>(::GpuModel_Flush(metal::min(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(c8_v0_1) + ::GpuModel_Flush(c8_v0_2))), ::GpuModel_Flush(::GpuModel_Flush(metal::fract(::GpuModel_Flush(::GpuModel_Flush(float(l0 << 2) + ::GpuModel_Flush(-f0))))))))));
    float c8_v1_0 = ::GpuModel_Flush(metal::floor(::GpuModel_Flush(f0)));
    float c8_v1_1 = c8_v1_0;
    c8_v1_0 = ::GpuModel_Flush(::GpuModel_Flush(c8_v1_0) + 1.0f);
    float c8_v1_2 = ::GpuModel_Flush(::GpuModel_Flush(f1) - ::GpuModel_Flush(f0));
    output[int(uint(p) + 10u)] = long(as_type<uint>(::GpuModel_Flush(metal::min(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(c8_v1_1) + ::GpuModel_Flush(c8_v1_2))), ::GpuModel_Flush(::GpuModel_Flush(metal::fract(::GpuModel_Flush(::GpuModel_Flush(float(l0 << 2) + ::GpuModel_Flush(-f0))))))))));
    float c8_v2_0 = ::GpuModel_Flush(metal::floor(f0));
    float c8_v2_1 = c8_v2_0;
    c8_v2_0 = ::GpuModel_Flush(c8_v2_0 + 1.0f);
    float c8_v2_2 = ::GpuModel_Flush(f1 - f0);
    output[int(uint(p) + 11u)] = long(as_type<uint>(::GpuModel_Flush(metal::min(::GpuModel_Flush(c8_v2_1 + c8_v2_2), ::GpuModel_Flush(metal::fract(::GpuModel_Flush(float(l0 << 2) + (-f0))))))));
    uint4 c10_v0_0 = as_type<uint4>(f4b);
    output[int(uint(p) + 12u)] = long(c10_v0_0.x);
    output[int(uint(p) + 13u)] = long(c10_v0_0.y);
    output[int(uint(p) + 14u)] = long(c10_v0_0.z);
    output[int(uint(p) + 15u)] = long(c10_v0_0.w);
    uint4 c10_v1_0 = as_type<uint4>(f4b);
    output[int(uint(p) + 16u)] = long(c10_v1_0.x);
    output[int(uint(p) + 17u)] = long(c10_v1_0.y);
    output[int(uint(p) + 18u)] = long(c10_v1_0.z);
    output[int(uint(p) + 19u)] = long(c10_v1_0.w);
    uint4 c10_v2_0 = as_type<uint4>(f4b);
    output[int(uint(p) + 20u)] = long(c10_v2_0.x);
    output[int(uint(p) + 21u)] = long(c10_v2_0.y);
    output[int(uint(p) + 22u)] = long(c10_v2_0.z);
    output[int(uint(p) + 23u)] = long(c10_v2_0.w);
    uchar c11_v0_0 = by0;
    c11_v0_0 >>= 1073741824;
    char c11_v0_1 = char(c11_v0_0);
    --c11_v0_1;
    output[int(uint(p) + 24u)] = long(as_type<uint>(float(c11_v0_1)));
    uchar c11_v1_0 = by0;
    c11_v1_0 >>= 1073741824;
    char c11_v1_1 = char(c11_v1_0);
    --c11_v1_1;
    output[int(uint(p) + 25u)] = long(as_type<uint>(float(c11_v1_1)));
    uchar c11_v2_0 = by0;
    c11_v2_0 >>= 1073741824;
    char c11_v2_1 = char(c11_v2_0);
    --c11_v2_1;
    output[int(uint(p) + 26u)] = long(as_type<uint>(float(c11_v2_1)));
}
