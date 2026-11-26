// SharpCompute.Differential.Tests.Random3x3 (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random3x3, compile options None
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
// SharpCompute float and half conversions (MslPrelude.Conversions).
inline uchar __ms_sat_uchar(float x) { float y = x > 0.0f ? x : 0.0f; return uchar(metal::min(y, 255.0f)); }
inline uchar __ms_sat_uchar(half x) { return __ms_sat_uchar(float(x)); }
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline int3 __ms_sat_int3(float3 x) { float3 y = metal::select(float3(0.0f), x, x == x); int3 r = int3(metal::clamp(y, float3(-2147483648.0f), float3(2147483520.0f))); return metal::select(r, int3(2147483647), x >= 2147483648.0f); }
inline int3 __ms_sat_int3(half3 x) { return __ms_sat_int3(float3(x)); }
// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return __builtin_constant_p(x) ? x : x | int(metal::reverse_bits(x) == int(0)); }
inline int4 __ms_nonzero(int4 x) { return int4(__ms_nonzero(x.x), __ms_nonzero(x.y), __ms_nonzero(x.z), __ms_nonzero(x.w)); }
inline uint __ms_nonzero(uint x) { return __builtin_constant_p(x) ? x : x | uint(metal::reverse_bits(x) == uint(0)); }
inline ulong __ms_popcount(ulong x) { return metal::popcount(metal::reverse_bits(x)); }

constant int Slots = 24;

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

kernel void SharpCompute_Differential_Tests_Random3x3(
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
    uchar by1 = uchar(input[int(uint(o) + 4u)]);
    uchar by2 = uchar(input[int(uint(o) + 5u)]);
    char sb0 = char(input[int(uint(o) + 6u)]);
    char sb1 = char(input[int(uint(o) + 7u)]);
    char sb2 = char(input[int(uint(o) + 8u)]);
    short sh0 = short(input[int(uint(o) + 9u)]);
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
    long l1 = long(input[int(uint(o) + 22u)]);
    long l2 = long(input[int(uint(o) + 23u)]);
    ulong ul0 = input[int(uint(o) + 24u)];
    ulong ul1 = input[int(uint(o) + 25u)];
    ulong ul2 = input[int(uint(o) + 26u)];
    float f0 = as_type<float>(uint(input[int(uint(o) + 27u)]));
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    float f2 = as_type<float>(uint(input[int(uint(o) + 29u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float3 f3a = float3(as_type<float>(uint(input[int(uint(o) + 37u)])), as_type<float>(uint(input[int(uint(o) + 38u)])), as_type<float>(uint(input[int(uint(o) + 39u)])));
    float4 f4a = float4(as_type<float>(uint(input[int(uint(o) + 43u)])), as_type<float>(uint(input[int(uint(o) + 44u)])), as_type<float>(uint(input[int(uint(o) + 45u)])), as_type<float>(uint(input[int(uint(o) + 46u)])));
    half2 h2a = half2(as_type<half>(ushort(input[int(uint(o) + 51u)])), as_type<half>(ushort(input[int(uint(o) + 52u)])));
    half2 h2b = half2(as_type<half>(ushort(input[int(uint(o) + 53u)])), as_type<half>(ushort(input[int(uint(o) + 54u)])));
    half3 h3a = half3(as_type<half>(ushort(input[int(uint(o) + 55u)])), as_type<half>(ushort(input[int(uint(o) + 56u)])), as_type<half>(ushort(input[int(uint(o) + 57u)])));
    half3 h3b = half3(as_type<half>(ushort(input[int(uint(o) + 58u)])), as_type<half>(ushort(input[int(uint(o) + 59u)])), as_type<half>(ushort(input[int(uint(o) + 60u)])));
    int2 i2a = int2(int(input[int(uint(o) + 69u)]), int(input[int(uint(o) + 70u)]));
    int3 i3b = int3(int(input[int(uint(o) + 76u)]), int(input[int(uint(o) + 77u)]), int(input[int(uint(o) + 78u)]));
    int4 i4a = int4(int(input[int(uint(o) + 79u)]), int(input[int(uint(o) + 80u)]), int(input[int(uint(o) + 81u)]), int(input[int(uint(o) + 82u)]));
    int4 i4b = int4(int(input[int(uint(o) + 83u)]), int(input[int(uint(o) + 84u)]), int(input[int(uint(o) + 85u)]), int(input[int(uint(o) + 86u)]));
    bool2 b2a = bool2((input[int(uint(o) + 105u)] & 1UL) != 0UL, (input[int(uint(o) + 106u)] & 1UL) != 0UL);
    bool3 b3a = bool3((input[int(uint(o) + 109u)] & 1UL) != 0UL, (input[int(uint(o) + 110u)] & 1UL) != 0UL, (input[int(uint(o) + 111u)] & 1UL) != 0UL);
    bool4 b4a = bool4((input[int(uint(o) + 115u)] & 1UL) != 0UL, (input[int(uint(o) + 116u)] & 1UL) != 0UL, (input[int(uint(o) + 117u)] & 1UL) != 0UL, (input[int(uint(o) + 118u)] & 1UL) != 0UL);
    output[int(uint(p) + 0u)] = long(as_type<uint>(metal::sin(f1 + 1E-10f)));
    short c1_0 = __ms_rotate_left(short(16), i2);
    int c1_1 = int(sb1);
    int c1_2 = int(sb0);
    ushort c1_3 = us2;
    output[int(uint(p) + 1u)] = long(__ms_rotate_left(__ms_rotate_left(c1_0, int(uint(int(us2)) >> i2a.y)), int(uint(__ms_rotate_right(metal::max(by1, by0), (c1_2 == 0 || (c1_1 == -2147483647 - 1 && c1_2 == -1)) ? 0 : c1_1 % __ms_nonzero(c1_2))) + uint((int(short(1000)) <= int(sh2)) ? c1_3 : metal::min(us0, us2)))));
    uint c2_0 = u0 >> i1;
    char c2_1 = metal::max(sb1, sb0);
    c2_1 |= int(char(i2));
    int c2_2 = int(c2_1);
    int c2_3 = int(char(metal::min(2147483649u, u0)));
    output[int(uint(p) + 2u)] = long(metal::rotate(metal::max(u0 & (2u >> i1), (u1 == 0u) ? 0u : c2_0 % __ms_nonzero(u1)), (0u - uint((c2_3 == 0 || (c2_2 == -2147483647 - 1 && c2_3 == -1)) ? 0 : c2_2 % __ms_nonzero(c2_3))) & 31u));
    int4 c3_0 = int4(__ms_sat_int(h2a.x), int3(uint(sh0) * (__ms_neg(uint3(i3b))))).xyzy;
    output[int(uint(p) + 3u)] = long(c3_0.x);
    output[int(uint(p) + 4u)] = long(c3_0.y);
    output[int(uint(p) + 5u)] = long(c3_0.z);
    output[int(uint(p) + 6u)] = long(c3_0.w);
    output[int(uint(p) + 7u)] = long(int(metal::ctz(metal::clamp(metal::reverse_bits(ul0), __ms_popcount(ul1 * ul1), ul1))));
    int3 c5_0 = int3(i2, i2, 0);
    int3 c5_1 = int3(__ms_sub(uint3((half(u0) != metal::sign(half2(h2)).x) ? as_type<int3>(f3a) : c5_0), uint3(__ms_sat_int3(h3b - h3a) & int3(i1, i1, i1))));
    output[int(uint(p) + 8u)] = long(c5_1.x);
    output[int(uint(p) + 9u)] = long(c5_1.y);
    output[int(uint(p) + 10u)] = long(c5_1.z);
    int c6_0 = __ms_sat_int(f1 + f0);
    int c6_1 = int(reinterpret_cast<thread uint&>(c6_0)--);
    output[int(uint(p) + 11u)] = long(as_type<uint>(float(c6_1)));
    char c7_0 = metal::max(sb2, sb1);
    char c7_1 = b2 ? sb2 : sb0;
    if (!(int(c7_1) == 0))
    {
        c7_0 /= __ms_nonzero(int(c7_1));
    }
    ushort c7_2 = us1;
    c7_2 >>= 1;
    uchar c7_3 = b2 ? by0 : by2;
    uchar c7_4 = by2;
    c7_3 |= int(c7_4);
    output[int(uint(p) + 12u)] = ((int(char(metal::max(by1, by1))) >= int(c7_0)) & (int(metal::ctz(c7_2)) <= int(c7_3))) ? 1L : 0L;
    ushort c8_0 = ushort(metal::abs(u0));
    ushort c8_1 = ++c8_0;
    uchar c8_2 = metal::reverse_bits(uchar(u1));
    uchar c8_3 = by0;
    c8_3 |= int(by0);
    uchar c8_4 = c8_3;
    c8_2 *= uint(c8_4);
    output[int(uint(p) + 13u)] = long(int(uint(c8_1) * uint(c8_2)));
    int c9_0 = int((b0 ^ (1E-30f > f2)) ? uchar(char(u1)) : uchar(char(l1)));
    int c9_1 = int(ushort(256));
    output[int(uint(p) + 14u)] = long((c9_1 == 0 || (c9_0 == -2147483647 - 1 && c9_1 == -1)) ? 0 : c9_0 % __ms_nonzero(c9_1));
    long c10_0 = l2;
    c10_0 <<= i1;
    long c10_1 = c10_0;
    c10_1 <<= metal::min(i0, i0);
    output[int(uint(p) + 15u)] = long(uint(c10_1 & long(metal::max(i2, i1))));
    int4 c11_0 = b2 ? i4a : i4a;
    int4 c11_1 = metal::select(i4b, i4a, b4a);
    int4 c11_2 = int4(__ms_sub(uint4(metal::select(c11_0 / __ms_nonzero(metal::select(c11_1, int4(1), (int4(c11_1) == 0) | ((int4(c11_0) == -2147483647 - 1) & (int4(c11_1) == -1)))), int4(__ms_add(uint4(metal::select(i4a, i4b, b4a)), uint4(uint(sh2) - uint(us1)))), (half2(h2b) == half2(h2b)).xxyy)), uint4(as_type<int4>(f4a))));
    output[int(uint(p) + 16u)] = long(c11_2.x);
    output[int(uint(p) + 17u)] = long(c11_2.y);
    output[int(uint(p) + 18u)] = long(c11_2.z);
    output[int(uint(p) + 19u)] = long(c11_2.w);
    uchar c12_0 = __ms_rotate_left(by2, i1);
    uchar c12_1 = uchar(254);
    uchar c12_2 = c12_1++;
    c12_0 ^= int(c12_2);
    output[int(uint(p) + 20u)] = long(uint(ushort(u1 & 3u)) - uint((false ? (f2 < f2) : b3a.x) ? c12_0 : metal::min(uchar(sh0), __ms_rotate_left(by0, i2))));
    float c13_0 = metal::copysign(metal::max(f1, f0), -f2);
    float c13_1 = ++c13_0;
    uchar c13_2 = b1 ? uchar(200) : by0;
    uchar c13_3 = uchar(ul2);
    c13_3 += int(__ms_sat_uchar(f2));
    uchar c13_4 = by2;
    c13_4 -= int(by0);
    output[int(uint(p) + 21u)] = (c13_1 >= float(metal::clamp(c13_2, c13_3, metal::max(c13_4, __ms_rotate_left(by2, i1))))) ? 1L : 0L;
    int c14_0 = 2;
    int c14_1 = c14_0;
    int c14_2 = 7;
    int c14_3 = c14_2;
    c14_3 = int(uint(c14_3) - (-uint(i2)));
    c14_1 <<= metal::max(c14_3, i1);
    output[int(uint(p) + 22u)] = long(c14_1);
    uchar c15_0 = by1;
    uchar c15_1 = c15_0;
    uchar c15_2 = c15_1++;
    uchar c15_3 = by1;
    uchar c15_4 = uchar(31);
    if (!(int(c15_4) == 0))
    {
        c15_3 %= __ms_nonzero(int(c15_4));
    }
    output[int(uint(p) + 23u)] = long(metal::clamp(metal::any(b2a ^ b2a) ? __ms_rotate_left(uchar(sh1), int(us0) & int(by2)) : c15_2, metal::abs((u1 < 2147483647u) ? metal::clamp(uchar(2), by2, by1) : c15_3), by2));
}
