// SharpCompute.Differential.Tests.Random2x2Model (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random2x2Model, compile options None
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
static float4 GpuModel_Flush_1(float4 value);
static float GpuModel_FlushTiny(float value);
static float4 GpuModel_FlushTiny_1(float4 value);
static float2 GpuModel_Flush_2(float2 value);
static float2 GpuModel_FlushTiny_2(float2 value);
static float3 GpuModel_Flush_3(float3 value);
static float3 GpuModel_FlushTiny_3(float3 value);

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

static float4 GpuModel_FlushTiny_1(float4 value)
{
    return float4(::GpuModel_FlushTiny(value.x), ::GpuModel_FlushTiny(value.y), ::GpuModel_FlushTiny(value.z), ::GpuModel_FlushTiny(value.w));
}

static float2 GpuModel_Flush_2(float2 value)
{
    return float2(::GpuModel_Flush(value.x), ::GpuModel_Flush(value.y));
}

static float2 GpuModel_FlushTiny_2(float2 value)
{
    return float2(::GpuModel_FlushTiny(value.x), ::GpuModel_FlushTiny(value.y));
}

static float3 GpuModel_Flush_3(float3 value)
{
    return float3(::GpuModel_Flush(value.x), ::GpuModel_Flush(value.y), ::GpuModel_Flush(value.z));
}

static float3 GpuModel_FlushTiny_3(float3 value)
{
    return float3(::GpuModel_FlushTiny(value.x), ::GpuModel_FlushTiny(value.y), ::GpuModel_FlushTiny(value.z));
}

kernel void SharpCompute_Differential_Tests_Random2x2Model(
    const device ulong* input [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int o = int(__tid.x * 123u);
    int p = int(__tid.x * uint(::Slots));
    bool b1 = (input[int(uint(o) + 1u)] & 1UL) != 0UL;
    bool b2 = (input[int(uint(o) + 2u)] & 1UL) != 0UL;
    uchar by1 = uchar(input[int(uint(o) + 4u)]);
    char sb0 = char(input[int(uint(o) + 6u)]);
    char sb1 = char(input[int(uint(o) + 7u)]);
    char sb2 = char(input[int(uint(o) + 8u)]);
    short sh1 = short(input[int(uint(o) + 10u)]);
    ushort us0 = ushort(input[int(uint(o) + 12u)]);
    ushort us1 = ushort(input[int(uint(o) + 13u)]);
    int i0 = int(input[int(uint(o) + 15u)]);
    int i2 = int(input[int(uint(o) + 17u)]);
    uint u0 = uint(input[int(uint(o) + 18u)]);
    uint u1 = uint(input[int(uint(o) + 19u)]);
    uint u2 = uint(input[int(uint(o) + 20u)]);
    long l0 = long(input[int(uint(o) + 21u)]);
    long l1 = long(input[int(uint(o) + 22u)]);
    float f0 = as_type<float>(uint(input[int(uint(o) + 27u)]));
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    float f2 = as_type<float>(uint(input[int(uint(o) + 29u)]));
    half h1 = as_type<half>(ushort(input[int(uint(o) + 31u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float2 f2a = float2(as_type<float>(uint(input[int(uint(o) + 33u)])), as_type<float>(uint(input[int(uint(o) + 34u)])));
    float2 f2b = float2(as_type<float>(uint(input[int(uint(o) + 35u)])), as_type<float>(uint(input[int(uint(o) + 36u)])));
    float3 f3a = float3(as_type<float>(uint(input[int(uint(o) + 37u)])), as_type<float>(uint(input[int(uint(o) + 38u)])), as_type<float>(uint(input[int(uint(o) + 39u)])));
    float3 f3b = float3(as_type<float>(uint(input[int(uint(o) + 40u)])), as_type<float>(uint(input[int(uint(o) + 41u)])), as_type<float>(uint(input[int(uint(o) + 42u)])));
    float4 f4a = float4(as_type<float>(uint(input[int(uint(o) + 43u)])), as_type<float>(uint(input[int(uint(o) + 44u)])), as_type<float>(uint(input[int(uint(o) + 45u)])), as_type<float>(uint(input[int(uint(o) + 46u)])));
    float4 f4b = float4(as_type<float>(uint(input[int(uint(o) + 47u)])), as_type<float>(uint(input[int(uint(o) + 48u)])), as_type<float>(uint(input[int(uint(o) + 49u)])), as_type<float>(uint(input[int(uint(o) + 50u)])));
    half2 h2a = half2(as_type<half>(ushort(input[int(uint(o) + 51u)])), as_type<half>(ushort(input[int(uint(o) + 52u)])));
    half2 h2b = half2(as_type<half>(ushort(input[int(uint(o) + 53u)])), as_type<half>(ushort(input[int(uint(o) + 54u)])));
    int2 i2a = int2(int(input[int(uint(o) + 69u)]), int(input[int(uint(o) + 70u)]));
    int2 i2b = int2(int(input[int(uint(o) + 71u)]), int(input[int(uint(o) + 72u)]));
    int3 i3a = int3(int(input[int(uint(o) + 73u)]), int(input[int(uint(o) + 74u)]), int(input[int(uint(o) + 75u)]));
    uint2 u2b = uint2(uint(input[int(uint(o) + 89u)]), uint(input[int(uint(o) + 90u)]));
    uint3 u3a = uint3(uint(input[int(uint(o) + 91u)]), uint(input[int(uint(o) + 92u)]), uint(input[int(uint(o) + 93u)]));
    uint3 u3b = uint3(uint(input[int(uint(o) + 94u)]), uint(input[int(uint(o) + 95u)]), uint(input[int(uint(o) + 96u)]));
    uint4 u4a = uint4(uint(input[int(uint(o) + 97u)]), uint(input[int(uint(o) + 98u)]), uint(input[int(uint(o) + 99u)]), uint(input[int(uint(o) + 100u)]));
    uint4 u4b = uint4(uint(input[int(uint(o) + 101u)]), uint(input[int(uint(o) + 102u)]), uint(input[int(uint(o) + 103u)]), uint(input[int(uint(o) + 104u)]));
    bool3 b3a = bool3((input[int(uint(o) + 109u)] & 1UL) != 0UL, (input[int(uint(o) + 110u)] & 1UL) != 0UL, (input[int(uint(o) + 111u)] & 1UL) != 0UL);
    bool4 b4b = bool4((input[int(uint(o) + 119u)] & 1UL) != 0UL, (input[int(uint(o) + 120u)] & 1UL) != 0UL, (input[int(uint(o) + 121u)] & 1UL) != 0UL, (input[int(uint(o) + 122u)] & 1UL) != 0UL);
    uint3 c2_v0_0 = (u3a << 3).xyy;
    c2_v0_0 *= metal::ctz(+u0);
    uint3 c2_v0_1 = c2_v0_0 << int(__ms_ilogb(f0));
    output[int(uint(p) + 0u)] = long(c2_v0_1.x);
    output[int(uint(p) + 1u)] = long(c2_v0_1.y);
    output[int(uint(p) + 2u)] = long(c2_v0_1.z);
    uint3 c2_v1_0 = (u3a << 3).xyy;
    c2_v1_0 *= metal::ctz(+u0);
    uint3 c2_v1_1 = c2_v1_0 << int(__ms_ilogb(f0));
    output[int(uint(p) + 3u)] = long(c2_v1_1.x);
    output[int(uint(p) + 4u)] = long(c2_v1_1.y);
    output[int(uint(p) + 5u)] = long(c2_v1_1.z);
    uint3 c2_v2_0 = (u3a << 3).xyy;
    c2_v2_0 *= metal::ctz(+u0);
    uint3 c2_v2_1 = c2_v2_0 << int(__ms_ilogb(f0));
    output[int(uint(p) + 6u)] = long(c2_v2_1.x);
    output[int(uint(p) + 7u)] = long(c2_v2_1.y);
    output[int(uint(p) + 8u)] = long(c2_v2_1.z);
    output[int(uint(p) + 9u)] = (metal::isfinite(::GpuModel_Flush(::GpuModel_Flush_1(metal::round(::GpuModel_Flush_1(::GpuModel_Flush_1(metal::fma(::GpuModel_Flush_1(f4b), ::GpuModel_Flush_1(f4a), ::GpuModel_Flush_1(f4b)))))).z))) ? 1L : 0L;
    output[int(uint(p) + 10u)] = (metal::isfinite(::GpuModel_Flush(::GpuModel_Flush_1(metal::round(::GpuModel_Flush_1(::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_1(f4b), ::GpuModel_Flush_1(f4a), ::GpuModel_Flush_1(f4b)))))).z))) ? 1L : 0L;
    output[int(uint(p) + 11u)] = (metal::isfinite(::GpuModel_Flush_1(metal::round(::GpuModel_FlushTiny_1(metal::fma(f4b, f4a, f4b)))).z)) ? 1L : 0L;
    output[int(uint(p) + 12u)] = (metal::any((metal::select(u4a, u4a, b4b) >= (__ms_sub(u4a, u4b))) ^ (float4(::GpuModel_Flush_1(metal::select(f4a, float4(NAN, 3.0f, 2.1474836E+09f, 2.0f), bool4(false, false, false, true)))) == ::GpuModel_Flush(::GpuModel_Flush(metal::ceil(::GpuModel_Flush(f1))))))) ? 1L : 0L;
    output[int(uint(p) + 13u)] = (metal::any((metal::select(u4a, u4a, b4b) >= (__ms_sub(u4a, u4b))) ^ (float4(::GpuModel_Flush_1(metal::select(f4a, float4(NAN, 3.0f, 2.1474836E+09f, 2.0f), bool4(false, false, false, true)))) == ::GpuModel_Flush(::GpuModel_Flush(metal::ceil(::GpuModel_Flush(f1))))))) ? 1L : 0L;
    output[int(uint(p) + 14u)] = (metal::any((metal::select(u4a, u4a, b4b) >= (__ms_sub(u4a, u4b))) ^ (float4(metal::select(f4a, float4(NAN, 3.0f, 2.1474836E+09f, 2.0f), bool4(false, false, false, true))) == ::GpuModel_Flush(metal::ceil(f1))))) ? 1L : 0L;
    float2 c7_v0_0 = float2(f0);
    c7_v0_0 = ::GpuModel_Flush_2(::GpuModel_Flush_2(c7_v0_0) * ::GpuModel_Flush_2(::GpuModel_Flush_2(::GpuModel_Flush(metal::abs(::GpuModel_Flush(::GpuModel_Flush(f0) + ::GpuModel_Flush(f0)))) - ::GpuModel_Flush_2(f2a))));
    float2 c7_v0_1 = c7_v0_0;
    output[int(uint(p) + 15u)] = long(as_type<uint>(c7_v0_1.x));
    output[int(uint(p) + 16u)] = long(as_type<uint>(c7_v0_1.y));
    float2 c7_v1_0 = float2(f0);
    c7_v1_0 = ::GpuModel_FlushTiny_2(::GpuModel_Flush_2(c7_v1_0) * ::GpuModel_Flush_2(::GpuModel_Flush_2(::GpuModel_Flush(metal::abs(::GpuModel_Flush(::GpuModel_Flush(f0) + ::GpuModel_Flush(f0)))) - ::GpuModel_Flush_2(f2a))));
    float2 c7_v1_1 = c7_v1_0;
    output[int(uint(p) + 17u)] = long(as_type<uint>(c7_v1_1.x));
    output[int(uint(p) + 18u)] = long(as_type<uint>(c7_v1_1.y));
    float2 c7_v2_0 = float2(f0);
    c7_v2_0 = ::GpuModel_FlushTiny_2(c7_v2_0 * ::GpuModel_Flush_2(metal::abs(::GpuModel_Flush(f0 + f0)) - f2a));
    float2 c7_v2_1 = c7_v2_0;
    output[int(uint(p) + 19u)] = long(as_type<uint>(c7_v2_1.x));
    output[int(uint(p) + 20u)] = long(as_type<uint>(c7_v2_1.y));
    float2 c8_v0_0 = ::GpuModel_Flush_2(::GpuModel_Flush_2(::GpuModel_Flush_2(metal::clamp(::GpuModel_Flush_2(::GpuModel_Flush_3(::GpuModel_Flush_3(f3b) * ::GpuModel_Flush_3(f3a)).xx), ::GpuModel_Flush_2(::GpuModel_Flush_2(::GpuModel_Flush_2(f2a) - ::GpuModel_Flush_2(f2b)).xy), ::GpuModel_Flush_2(::GpuModel_Flush_2(metal::max(::GpuModel_Flush_2(f2a), ::GpuModel_Flush_2(float2(0.1f, f0)))))))) - ::GpuModel_Flush_2(f2a));
    output[int(uint(p) + 21u)] = long(as_type<uint>(c8_v0_0.x));
    output[int(uint(p) + 22u)] = long(as_type<uint>(c8_v0_0.y));
    float2 c8_v1_0 = ::GpuModel_Flush_2(::GpuModel_Flush_2(::GpuModel_Flush_2(metal::clamp(::GpuModel_Flush_2(::GpuModel_FlushTiny_3(::GpuModel_Flush_3(f3b) * ::GpuModel_Flush_3(f3a)).xx), ::GpuModel_Flush_2(::GpuModel_Flush_2(::GpuModel_Flush_2(f2a) - ::GpuModel_Flush_2(f2b)).xy), ::GpuModel_Flush_2(::GpuModel_Flush_2(metal::max(::GpuModel_Flush_2(f2a), ::GpuModel_Flush_2(float2(0.1f, f0)))))))) - ::GpuModel_Flush_2(f2a));
    output[int(uint(p) + 23u)] = long(as_type<uint>(c8_v1_0.x));
    output[int(uint(p) + 24u)] = long(as_type<uint>(c8_v1_0.y));
    float2 c8_v2_0 = ::GpuModel_Flush_2(::GpuModel_Flush_2(metal::clamp(::GpuModel_FlushTiny_3(f3b * f3a).xx, ::GpuModel_Flush_2(f2a - f2b).xy, ::GpuModel_Flush_2(metal::max(f2a, float2(0.1f, f0))))) - f2a);
    output[int(uint(p) + 25u)] = long(as_type<uint>(c8_v2_0.x));
    output[int(uint(p) + 26u)] = long(as_type<uint>(c8_v2_0.y));
    float3 c11_v0_0 = ::GpuModel_Flush_3(::GpuModel_Flush_3(float3(f2a, f2)) + ::GpuModel_Flush_3(metal::select(f3a, f3a, b3a)));
    c11_v0_0 = ::GpuModel_Flush_3(::GpuModel_Flush_3(c11_v0_0) * ::GpuModel_Flush_3(::GpuModel_Flush_3(metal::min(::GpuModel_Flush_3(float3(i3a)), ::GpuModel_Flush_3(::GpuModel_Flush_3(metal::fmod(::GpuModel_Flush_3(f3a), ::GpuModel_Flush_3(f3b))))))));
    float3 c11_v0_1 = c11_v0_0;
    float3 c11_v0_2 = c11_v0_1;
    output[int(uint(p) + 27u)] = long(as_type<uint>(c11_v0_2.x));
    output[int(uint(p) + 28u)] = long(as_type<uint>(c11_v0_2.y));
    output[int(uint(p) + 29u)] = long(as_type<uint>(c11_v0_2.z));
    float3 c11_v1_0 = ::GpuModel_Flush_3(::GpuModel_Flush_3(float3(f2a, f2)) + ::GpuModel_Flush_3(metal::select(f3a, f3a, b3a)));
    c11_v1_0 = ::GpuModel_FlushTiny_3(::GpuModel_Flush_3(c11_v1_0) * ::GpuModel_Flush_3(::GpuModel_Flush_3(metal::min(::GpuModel_Flush_3(float3(i3a)), ::GpuModel_Flush_3(::GpuModel_FlushTiny_3(metal::fmod(::GpuModel_Flush_3(f3a), ::GpuModel_Flush_3(f3b))))))));
    float3 c11_v1_1 = c11_v1_0;
    float3 c11_v1_2 = c11_v1_1;
    output[int(uint(p) + 30u)] = long(as_type<uint>(c11_v1_2.x));
    output[int(uint(p) + 31u)] = long(as_type<uint>(c11_v1_2.y));
    output[int(uint(p) + 32u)] = long(as_type<uint>(c11_v1_2.z));
    float3 c11_v2_0 = ::GpuModel_Flush_3(float3(f2a, f2) + metal::select(f3a, f3a, b3a));
    c11_v2_0 = ::GpuModel_FlushTiny_3(c11_v2_0 * ::GpuModel_Flush_3(metal::min(float3(i3a), ::GpuModel_FlushTiny_3(metal::fmod(f3a, f3b)))));
    float3 c11_v2_1 = c11_v2_0;
    float3 c11_v2_2 = c11_v2_1;
    output[int(uint(p) + 33u)] = long(as_type<uint>(c11_v2_2.x));
    output[int(uint(p) + 34u)] = long(as_type<uint>(c11_v2_2.y));
    output[int(uint(p) + 35u)] = long(as_type<uint>(c11_v2_2.z));
    short c12_v0_0 = sh1;
    c12_v0_0 <<= 2;
    ushort c12_v0_1 = us1;
    --c12_v0_1;
    ushort c12_v0_2 = us1;
    ushort c12_v0_3 = c12_v0_2++;
    output[int(uint(p) + 36u)] = long(as_type<uint>(float(int(uint((true && b1) ? c12_v0_0 : metal::max(sh1, sh1)) * uint(metal::max(c12_v0_1, c12_v0_3))))));
    short c12_v1_0 = sh1;
    c12_v1_0 <<= 2;
    ushort c12_v1_1 = us1;
    --c12_v1_1;
    ushort c12_v1_2 = us1;
    ushort c12_v1_3 = c12_v1_2++;
    output[int(uint(p) + 37u)] = long(as_type<uint>(float(int(uint((true && b1) ? c12_v1_0 : metal::max(sh1, sh1)) * uint(metal::max(c12_v1_1, c12_v1_3))))));
    short c12_v2_0 = sh1;
    c12_v2_0 <<= 2;
    ushort c12_v2_1 = us1;
    --c12_v2_1;
    ushort c12_v2_2 = us1;
    ushort c12_v2_3 = c12_v2_2++;
    output[int(uint(p) + 38u)] = long(as_type<uint>(float(int(uint((true && b1) ? c12_v2_0 : metal::max(sh1, sh1)) * uint(metal::max(c12_v2_1, c12_v2_3))))));
    float c13_v0_0 = ::GpuModel_Flush(-f2);
    output[int(uint(p) + 39u)] = long(metal::isnan(c13_v0_0) ? 0 : int(metal::sign(c13_v0_0)));
    float c13_v1_0 = ::GpuModel_Flush(-f2);
    output[int(uint(p) + 40u)] = long(metal::isnan(c13_v1_0) ? 0 : int(metal::sign(c13_v1_0)));
    float c13_v2_0 = -f2;
    output[int(uint(p) + 41u)] = long(metal::isnan(c13_v2_0) ? 0 : int(metal::sign(c13_v2_0)));
}
