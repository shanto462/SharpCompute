// SharpCompute.Tests.Conformance.Semantics.VectorNegationShapesShader (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_VectorNegationShapesShader, compile options None
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

constant int Slots = 148;

static_assert(sizeof(packed_int3) == 12, "SharpCompute: size of 'packed_int3' does not match its C# layout");

struct NegationCell
{
    packed_int3 V;
    packed_int2 P;
    packed_uint4 U;
};
static_assert(sizeof(NegationCell) == 36, "SharpCompute: size of 'NegationCell' does not match its C# layout");
static_assert(__builtin_offsetof(NegationCell, V) == 0, "SharpCompute: offset of 'NegationCell.V' does not match its C# layout");
static_assert(__builtin_offsetof(NegationCell, P) == 12, "SharpCompute: offset of 'NegationCell.P' does not match its C# layout");
static_assert(__builtin_offsetof(NegationCell, U) == 20, "SharpCompute: offset of 'NegationCell.U' does not match its C# layout");

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

static int MinusOne(int value);
static int Same(int value);

static int MinusOne(int value)
{
    return int(uint(value & 0) - 1u);
}

static int Same(int value)
{
    return value;
}

kernel void SharpCompute_Tests_Conformance_Semantics_VectorNegationShapesShader(
    const device int* pairs [[buffer(0)]],
    device packed_int3* vectors [[buffer(1)]],
    device NegationCell* cells [[buffer(2)]],
    device int* output [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup packed_int3 Tile[64];
    int i = int(__tid.x);
    int a = pairs[int(2u * uint(i))];
    int b = pairs[int((2u * uint(i)) + 1u)];
    int o = int(uint(i) * uint(::Slots));
    {
        int3 w = int3(int(uint(a) + 1u), int(uint(b) - 1u), int(uint(a ^ b) + 1u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_add(uint3(~w), uint3(1u)));
        output[int(uint(o) + 0u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 1u)] = t.x;
        output[int(uint(o) + 2u)] = t.y;
        output[int(uint(o) + 3u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 2u), int(uint(b) - 2u), int(uint(a ^ b) + 2u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_sub(uint3(w), 2u * uint3(w)));
        output[int(uint(o) + 4u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 5u)] = t.x;
        output[int(uint(o) + 6u)] = t.y;
        output[int(uint(o) + 7u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 3u), int(uint(b) - 3u), int(uint(a ^ b) + 3u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_sub(uint3(w), __ms_add(uint3(w), uint3(w))));
        output[int(uint(o) + 8u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 9u)] = t.x;
        output[int(uint(o) + 10u)] = t.y;
        output[int(uint(o) + 11u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 4u), int(uint(b) - 4u), int(uint(a ^ b) + 4u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_sub(uint3(w), uint3(w << 1)));
        output[int(uint(o) + 12u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 13u)] = t.x;
        output[int(uint(o) + 14u)] = t.y;
        output[int(uint(o) + 15u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 5u), int(uint(b) - 5u), int(uint(a ^ b) + 5u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = __ms_neg(__ms_neg(w));
        output[int(uint(o) + 16u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 17u)] = t.x;
        output[int(uint(o) + 18u)] = t.y;
        output[int(uint(o) + 19u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 6u), int(uint(b) - 6u), int(uint(a ^ b) + 6u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_add(uint3(w), __ms_neg(uint3(w))));
        output[int(uint(o) + 20u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 21u)] = t.x;
        output[int(uint(o) + 22u)] = t.y;
        output[int(uint(o) + 23u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 7u), int(uint(b) - 7u), int(uint(a ^ b) + 7u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_sub(__ms_neg(uint3(w)), uint3(w)));
        output[int(uint(o) + 24u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 25u)] = t.x;
        output[int(uint(o) + 26u)] = t.y;
        output[int(uint(o) + 27u)] = t.z;
    }
    {
        int2 w = int2(int(uint(a) + 8u), int(uint(b) - 8u));
        int2 c = int2(1000, 2000);
        int2 n = int2(__ms_add(uint2(~w), uint2(1u)));
        output[int(uint(o) + 28u)] = n.x;
        int2 t = int2(__ms_sub((__ms_add(uint2(w), uint2(c))) * 3u, uint2(w)));
        output[int(uint(o) + 29u)] = t.x;
        output[int(uint(o) + 30u)] = t.y;
    }
    {
        int2 w = int2(int(uint(a) + 9u), int(uint(b) - 9u));
        int2 c = int2(1000, 2000);
        int2 n = int2(__ms_sub(uint2(w), 2u * uint2(w)));
        output[int(uint(o) + 31u)] = n.x;
        int2 t = int2(__ms_sub((__ms_add(uint2(w), uint2(c))) * 3u, uint2(w)));
        output[int(uint(o) + 32u)] = t.x;
        output[int(uint(o) + 33u)] = t.y;
    }
    {
        int2 w = int2(int(uint(a) + 10u), int(uint(b) - 10u));
        int2 c = int2(1000, 2000);
        int2 n = int2(__ms_sub(uint2(w), __ms_add(uint2(w), uint2(w))));
        output[int(uint(o) + 34u)] = n.x;
        int2 t = int2(__ms_sub((__ms_add(uint2(w), uint2(c))) * 3u, uint2(w)));
        output[int(uint(o) + 35u)] = t.x;
        output[int(uint(o) + 36u)] = t.y;
    }
    {
        int2 w = int2(int(uint(a) + 11u), int(uint(b) - 11u));
        int2 c = int2(1000, 2000);
        int2 n = int2(__ms_sub(uint2(w), uint2(w << 1)));
        output[int(uint(o) + 37u)] = n.x;
        int2 t = int2(__ms_sub((__ms_add(uint2(w), uint2(c))) * 3u, uint2(w)));
        output[int(uint(o) + 38u)] = t.x;
        output[int(uint(o) + 39u)] = t.y;
    }
    {
        int2 w = int2(int(uint(a) + 12u), int(uint(b) - 12u));
        int2 c = int2(1000, 2000);
        int2 n = __ms_neg(__ms_neg(w));
        output[int(uint(o) + 40u)] = n.x;
        int2 t = int2(__ms_sub((__ms_add(uint2(w), uint2(c))) * 3u, uint2(w)));
        output[int(uint(o) + 41u)] = t.x;
        output[int(uint(o) + 42u)] = t.y;
    }
    {
        int2 w = int2(int(uint(a) + 13u), int(uint(b) - 13u));
        int2 c = int2(1000, 2000);
        int2 n = int2(__ms_add(uint2(w), __ms_neg(uint2(w))));
        output[int(uint(o) + 43u)] = n.x;
        int2 t = int2(__ms_sub((__ms_add(uint2(w), uint2(c))) * 3u, uint2(w)));
        output[int(uint(o) + 44u)] = t.x;
        output[int(uint(o) + 45u)] = t.y;
    }
    {
        int2 w = int2(int(uint(a) + 14u), int(uint(b) - 14u));
        int2 c = int2(1000, 2000);
        int2 n = int2(__ms_sub(__ms_neg(uint2(w)), uint2(w)));
        output[int(uint(o) + 46u)] = n.x;
        int2 t = int2(__ms_sub((__ms_add(uint2(w), uint2(c))) * 3u, uint2(w)));
        output[int(uint(o) + 47u)] = t.x;
        output[int(uint(o) + 48u)] = t.y;
    }
    {
        int4 w = int4(int(uint(a) + 15u), int(uint(b) - 15u), int(uint(a ^ b) + 15u), int(uint(a) + uint(b) + 15u));
        int4 c = int4(1000, 2000, 3000, 4000);
        int4 n = int4(__ms_add(uint4(~w), uint4(1u)));
        output[int(uint(o) + 49u)] = int(uint(n.x) + uint(n.w));
        int4 t = int4(__ms_sub((__ms_add(uint4(w), uint4(c))) * 3u, uint4(w)));
        output[int(uint(o) + 50u)] = t.x;
        output[int(uint(o) + 51u)] = t.y;
        output[int(uint(o) + 52u)] = t.z;
        output[int(uint(o) + 53u)] = t.w;
    }
    {
        int4 w = int4(int(uint(a) + 16u), int(uint(b) - 16u), int(uint(a ^ b) + 16u), int(uint(a) + uint(b) + 16u));
        int4 c = int4(1000, 2000, 3000, 4000);
        int4 n = int4(__ms_sub(uint4(w), 2u * uint4(w)));
        output[int(uint(o) + 54u)] = int(uint(n.x) + uint(n.w));
        int4 t = int4(__ms_sub((__ms_add(uint4(w), uint4(c))) * 3u, uint4(w)));
        output[int(uint(o) + 55u)] = t.x;
        output[int(uint(o) + 56u)] = t.y;
        output[int(uint(o) + 57u)] = t.z;
        output[int(uint(o) + 58u)] = t.w;
    }
    {
        int4 w = int4(int(uint(a) + 17u), int(uint(b) - 17u), int(uint(a ^ b) + 17u), int(uint(a) + uint(b) + 17u));
        int4 c = int4(1000, 2000, 3000, 4000);
        int4 n = int4(__ms_sub(uint4(w), __ms_add(uint4(w), uint4(w))));
        output[int(uint(o) + 59u)] = int(uint(n.x) + uint(n.w));
        int4 t = int4(__ms_sub((__ms_add(uint4(w), uint4(c))) * 3u, uint4(w)));
        output[int(uint(o) + 60u)] = t.x;
        output[int(uint(o) + 61u)] = t.y;
        output[int(uint(o) + 62u)] = t.z;
        output[int(uint(o) + 63u)] = t.w;
    }
    {
        int4 w = int4(int(uint(a) + 18u), int(uint(b) - 18u), int(uint(a ^ b) + 18u), int(uint(a) + uint(b) + 18u));
        int4 c = int4(1000, 2000, 3000, 4000);
        int4 n = int4(__ms_sub(uint4(w), uint4(w << 1)));
        output[int(uint(o) + 64u)] = int(uint(n.x) + uint(n.w));
        int4 t = int4(__ms_sub((__ms_add(uint4(w), uint4(c))) * 3u, uint4(w)));
        output[int(uint(o) + 65u)] = t.x;
        output[int(uint(o) + 66u)] = t.y;
        output[int(uint(o) + 67u)] = t.z;
        output[int(uint(o) + 68u)] = t.w;
    }
    {
        int4 w = int4(int(uint(a) + 19u), int(uint(b) - 19u), int(uint(a ^ b) + 19u), int(uint(a) + uint(b) + 19u));
        int4 c = int4(1000, 2000, 3000, 4000);
        int4 n = __ms_neg(__ms_neg(w));
        output[int(uint(o) + 69u)] = int(uint(n.x) + uint(n.w));
        int4 t = int4(__ms_sub((__ms_add(uint4(w), uint4(c))) * 3u, uint4(w)));
        output[int(uint(o) + 70u)] = t.x;
        output[int(uint(o) + 71u)] = t.y;
        output[int(uint(o) + 72u)] = t.z;
        output[int(uint(o) + 73u)] = t.w;
    }
    {
        int4 w = int4(int(uint(a) + 20u), int(uint(b) - 20u), int(uint(a ^ b) + 20u), int(uint(a) + uint(b) + 20u));
        int4 c = int4(1000, 2000, 3000, 4000);
        int4 n = int4(__ms_add(uint4(w), __ms_neg(uint4(w))));
        output[int(uint(o) + 74u)] = int(uint(n.x) + uint(n.w));
        int4 t = int4(__ms_sub((__ms_add(uint4(w), uint4(c))) * 3u, uint4(w)));
        output[int(uint(o) + 75u)] = t.x;
        output[int(uint(o) + 76u)] = t.y;
        output[int(uint(o) + 77u)] = t.z;
        output[int(uint(o) + 78u)] = t.w;
    }
    {
        int4 w = int4(int(uint(a) + 21u), int(uint(b) - 21u), int(uint(a ^ b) + 21u), int(uint(a) + uint(b) + 21u));
        int4 c = int4(1000, 2000, 3000, 4000);
        int4 n = int4(__ms_sub(__ms_neg(uint4(w)), uint4(w)));
        output[int(uint(o) + 79u)] = int(uint(n.x) + uint(n.w));
        int4 t = int4(__ms_sub((__ms_add(uint4(w), uint4(c))) * 3u, uint4(w)));
        output[int(uint(o) + 80u)] = t.x;
        output[int(uint(o) + 81u)] = t.y;
        output[int(uint(o) + 82u)] = t.z;
        output[int(uint(o) + 83u)] = t.w;
    }
    {
        uint3 w = uint3(uint(int(uint(a) + 22u)), uint(int(uint(b) - 22u)), uint(int(uint(a ^ b) + 22u)));
        uint3 c = uint3(1000u, 2000u, 3000u);
        uint3 n = __ms_add(~w, uint3(1u));
        output[int(uint(o) + 84u)] = int(n.x + n.z);
        uint3 t = __ms_sub((__ms_add(w, c)) * 3u, w);
        output[int(uint(o) + 85u)] = int(t.x);
        output[int(uint(o) + 86u)] = int(t.y);
        output[int(uint(o) + 87u)] = int(t.z);
    }
    {
        uint3 w = uint3(uint(int(uint(a) + 23u)), uint(int(uint(b) - 23u)), uint(int(uint(a ^ b) + 23u)));
        uint3 c = uint3(1000u, 2000u, 3000u);
        uint3 n = __ms_sub(w, 2u * w);
        output[int(uint(o) + 88u)] = int(n.x + n.z);
        uint3 t = __ms_sub((__ms_add(w, c)) * 3u, w);
        output[int(uint(o) + 89u)] = int(t.x);
        output[int(uint(o) + 90u)] = int(t.y);
        output[int(uint(o) + 91u)] = int(t.z);
    }
    {
        uint3 w = uint3(uint(int(uint(a) + 24u)), uint(int(uint(b) - 24u)), uint(int(uint(a ^ b) + 24u)));
        uint3 c = uint3(1000u, 2000u, 3000u);
        uint3 n = __ms_sub(w, __ms_add(w, w));
        output[int(uint(o) + 92u)] = int(n.x + n.z);
        uint3 t = __ms_sub((__ms_add(w, c)) * 3u, w);
        output[int(uint(o) + 93u)] = int(t.x);
        output[int(uint(o) + 94u)] = int(t.y);
        output[int(uint(o) + 95u)] = int(t.z);
    }
    {
        uint3 w = uint3(uint(int(uint(a) + 25u)), uint(int(uint(b) - 25u)), uint(int(uint(a ^ b) + 25u)));
        uint3 c = uint3(1000u, 2000u, 3000u);
        uint3 n = __ms_sub(w, w << 1);
        output[int(uint(o) + 96u)] = int(n.x + n.z);
        uint3 t = __ms_sub((__ms_add(w, c)) * 3u, w);
        output[int(uint(o) + 97u)] = int(t.x);
        output[int(uint(o) + 98u)] = int(t.y);
        output[int(uint(o) + 99u)] = int(t.z);
    }
    {
        uint3 w = uint3(uint(int(uint(a) + 26u)), uint(int(uint(b) - 26u)), uint(int(uint(a ^ b) + 26u)));
        uint3 c = uint3(1000u, 2000u, 3000u);
        uint3 n = __ms_neg(__ms_neg(w));
        output[int(uint(o) + 100u)] = int(n.x + n.z);
        uint3 t = __ms_sub((__ms_add(w, c)) * 3u, w);
        output[int(uint(o) + 101u)] = int(t.x);
        output[int(uint(o) + 102u)] = int(t.y);
        output[int(uint(o) + 103u)] = int(t.z);
    }
    {
        uint3 w = uint3(uint(int(uint(a) + 27u)), uint(int(uint(b) - 27u)), uint(int(uint(a ^ b) + 27u)));
        uint3 c = uint3(1000u, 2000u, 3000u);
        uint3 n = __ms_add(w, __ms_neg(w));
        output[int(uint(o) + 104u)] = int(n.x + n.z);
        uint3 t = __ms_sub((__ms_add(w, c)) * 3u, w);
        output[int(uint(o) + 105u)] = int(t.x);
        output[int(uint(o) + 106u)] = int(t.y);
        output[int(uint(o) + 107u)] = int(t.z);
    }
    {
        uint3 w = uint3(uint(int(uint(a) + 28u)), uint(int(uint(b) - 28u)), uint(int(uint(a ^ b) + 28u)));
        uint3 c = uint3(1000u, 2000u, 3000u);
        uint3 n = __ms_sub(__ms_neg(w), w);
        output[int(uint(o) + 108u)] = int(n.x + n.z);
        uint3 t = __ms_sub((__ms_add(w, c)) * 3u, w);
        output[int(uint(o) + 109u)] = int(t.x);
        output[int(uint(o) + 110u)] = int(t.y);
        output[int(uint(o) + 111u)] = int(t.z);
    }
    {
        int3 w = int3(int(uint(a) + 29u), int(uint(b) - 29u), int(uint(a ^ b) + 29u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = ~(int3(__ms_sub(uint3(w), uint3(1u))));
        output[int(uint(o) + 112u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 113u)] = t.x;
        output[int(uint(o) + 114u)] = t.y;
        output[int(uint(o) + 115u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 30u), int(uint(b) - 30u), int(uint(a ^ b) + 30u));
        int3 c = int3(1000, 2000, 3000);
        int s = ::MinusOne(a);
        int3 n = int3(uint3(w) * uint(s));
        output[int(uint(o) + 116u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 117u)] = t.x;
        output[int(uint(o) + 118u)] = t.y;
        output[int(uint(o) + 119u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 31u), int(uint(b) - 31u), int(uint(a ^ b) + 31u));
        int3 c = int3(1000, 2000, 3000);
        int s = ::MinusOne(a);
        int3 n = w / s;
        output[int(uint(o) + 120u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_sub((__ms_add(uint3(w), uint3(c))) * 3u, uint3(w)));
        output[int(uint(o) + 121u)] = t.x;
        output[int(uint(o) + 122u)] = t.y;
        output[int(uint(o) + 123u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 32u), int(uint(b) - 32u), int(uint(a ^ b) + 32u));
        int3 c = int3(1000, 2000, 3000);
        int s = ::MinusOne(a);
        int3 n = int3(uint3(w) * uint(s));
        output[int(uint(o) + 124u)] = int(uint(n.x) + uint(n.z));
        int3 t = int3(__ms_add(__ms_add(__ms_add(uint3(w) * 3u, uint3(c)), uint3(~w)), uint3(1u)));
        output[int(uint(o) + 125u)] = t.x;
        output[int(uint(o) + 126u)] = t.y;
        output[int(uint(o) + 127u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 33u), int(uint(b) - 33u), int(uint(a ^ b) + 33u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_add(uint3(~w), uint3(1u)));
        output[int(uint(o) + 128u)] = int(uint(n.x) + uint(n.z));
        int3 t = w;
        t = __ms_add(t, c);
        t = int3(uint3(t) * 3u);
        t = __ms_sub(t, w);
        output[int(uint(o) + 129u)] = t.x;
        output[int(uint(o) + 130u)] = t.y;
        output[int(uint(o) + 131u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 34u), int(uint(b) - 34u), int(uint(a ^ b) + 34u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_add(uint3(~w), uint3(1u)));
        output[int(uint(o) + 132u)] = int(uint(n.x) + uint(n.z));
        int3 t = w;
        __ms_add_to(t, int3(::Same(1000), ::Same(2000), ::Same(3000)));
        t = int3(uint3(t) * 3u);
        __ms_sub_from(t, int3(::Same(w.x), w.y, w.z));
        output[int(uint(o) + 133u)] = t.x;
        output[int(uint(o) + 134u)] = t.y;
        output[int(uint(o) + 135u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 35u), int(uint(b) - 35u), int(uint(a ^ b) + 35u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_sub(uint3(w), 2u * uint3(w)));
        output[int(uint(o) + 136u)] = int(uint(n.x) + uint(n.z));
        vectors[i] = w;
        __ms_add_to(vectors[::Same(i)], c);
        vectors[i] = int3(uint3(vectors[i]) * 3u);
        __ms_sub_from(vectors[::Same(i)], w);
        int3 t = vectors[i];
        output[int(uint(o) + 137u)] = t.x;
        output[int(uint(o) + 138u)] = t.y;
        output[int(uint(o) + 139u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 36u), int(uint(b) - 36u), int(uint(a ^ b) + 36u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_sub(uint3(w), __ms_add(uint3(w), uint3(w))));
        output[int(uint(o) + 140u)] = int(uint(n.x) + uint(n.z));
        cells[i].V = w;
        __ms_add_to(cells[::Same(i)].V, c);
        cells[i].V = int3(uint3(cells[i].V) * 3u);
        __ms_sub_from(cells[::Same(i)].V, w);
        int3 t = cells[i].V;
        output[int(uint(o) + 141u)] = t.x;
        output[int(uint(o) + 142u)] = t.y;
        output[int(uint(o) + 143u)] = t.z;
    }
    {
        int3 w = int3(int(uint(a) + 37u), int(uint(b) - 37u), int(uint(a ^ b) + 37u));
        int3 c = int3(1000, 2000, 3000);
        int3 n = int3(__ms_sub(uint3(w), uint3(w << 1)));
        output[int(uint(o) + 144u)] = int(uint(n.x) + uint(n.z));
        int g = int(__gtid.x);
        Tile[g] = w;
        __ms_add_to(Tile[::Same(g)], c);
        Tile[g] = int3(uint3(Tile[g]) * 3u);
        __ms_sub_from(Tile[::Same(g)], w);
        int3 t = Tile[g];
        output[int(uint(o) + 145u)] = t.x;
        output[int(uint(o) + 146u)] = t.y;
        output[int(uint(o) + 147u)] = t.z;
    }
}
