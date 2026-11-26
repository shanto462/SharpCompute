// SharpCompute.Differential.Tests.Random3x7 (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random3x7, compile options None
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
inline char __ms_popcount(char x) { return char(metal::popcount(uint(uchar(x)))); }
inline uchar __ms_popcount(uchar x) { return uchar(metal::popcount(uint(uchar(x)))); }
inline short __ms_popcount(short x) { return short(metal::popcount(uint(ushort(x)))); }
inline ushort __ms_popcount(ushort x) { return ushort(metal::popcount(uint(ushort(x)))); }
inline int __ms_popcount(int x) { return metal::popcount(metal::reverse_bits(x)); }
inline uint __ms_popcount(uint x) { return metal::popcount(metal::reverse_bits(x)); }
inline long __ms_popcount(long x) { return metal::popcount(metal::reverse_bits(x)); }
inline ulong __ms_popcount(ulong x) { return metal::popcount(metal::reverse_bits(x)); }
inline int2 __ms_popcount(int2 x) { return metal::popcount(metal::reverse_bits(x)); }
inline int3 __ms_popcount(int3 x) { return metal::popcount(metal::reverse_bits(x)); }
inline int4 __ms_popcount(int4 x) { return metal::popcount(metal::reverse_bits(x)); }
inline uint2 __ms_popcount(uint2 x) { return metal::popcount(metal::reverse_bits(x)); }
inline uint3 __ms_popcount(uint3 x) { return metal::popcount(metal::reverse_bits(x)); }
inline uint4 __ms_popcount(uint4 x) { return metal::popcount(metal::reverse_bits(x)); }
inline int __ms_clz(int x) { return x < 0 ? 0 : metal::clz(x); }
inline uint __ms_clz(uint x) { return int(x) < 0 ? 0u : metal::clz(x); }
inline long __ms_clz(long x) { return x < 0L ? 0L : metal::clz(x); }
inline ulong __ms_clz(ulong x) { return long(x) < 0L ? 0UL : metal::clz(x); }
inline int2 __ms_clz(int2 x) { return metal::select(metal::clz(x), int2(0), x < 0); }
inline uint2 __ms_clz(uint2 x) { return metal::select(metal::clz(x), uint2(0u), int2(x) < 0); }
inline int3 __ms_clz(int3 x) { return metal::select(metal::clz(x), int3(0), x < 0); }
inline uint3 __ms_clz(uint3 x) { return metal::select(metal::clz(x), uint3(0u), int3(x) < 0); }
inline int4 __ms_clz(int4 x) { return metal::select(metal::clz(x), int4(0), x < 0); }
inline uint4 __ms_clz(uint4 x) { return metal::select(metal::clz(x), uint4(0u), int4(x) < 0); }
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
// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline ushort __ms_sat_ushort(float x) { float y = x > 0.0f ? x : 0.0f; return ushort(metal::min(y, 65535.0f)); }
inline ushort __ms_sat_ushort(half x) { return __ms_sat_ushort(float(x)); }
inline long __ms_sat_long(float x) { float y = x == x ? x : 0.0f; long r = long(metal::clamp(y, -9223372036854775808.0f, 9223371487098961920.0f)); return x >= 9223372036854775808.0f ? 9223372036854775807L : r; }
inline long __ms_sat_long(half x) { return __ms_sat_long(float(x)); }
// SharpCompute integer division (MslPrelude.IntegerDivision).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }
inline int2 __ms_nonzero(int2 x) { uint2 u = uint2(x); return int2(u | (((u - 1u) & ~u) >> 31)); }
inline int3 __ms_nonzero(int3 x) { uint3 u = uint3(x); return int3(u | (((u - 1u) & ~u) >> 31)); }
inline int4 __ms_nonzero(int4 x) { uint4 u = uint4(x); return int4(u | (((u - 1u) & ~u) >> 31)); }
inline uint __ms_nonzero(uint x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint2 __ms_nonzero(uint2 x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint3 __ms_nonzero(uint3 x) { return x | (((x - 1u) & ~x) >> 31); }
inline uint4 __ms_nonzero(uint4 x) { return x | (((x - 1u) & ~x) >> 31); }
inline long __ms_nonzero(long x) { ulong u = ulong(x); return long(u | (((u - 1UL) & ~u) >> 63)); }
inline long2 __ms_nonzero(long2 x) { ulong2 u = ulong2(x); return long2(u | (((u - 1UL) & ~u) >> 63)); }
inline long3 __ms_nonzero(long3 x) { ulong3 u = ulong3(x); return long3(u | (((u - 1UL) & ~u) >> 63)); }
inline long4 __ms_nonzero(long4 x) { ulong4 u = ulong4(x); return long4(u | (((u - 1UL) & ~u) >> 63)); }
inline ulong __ms_nonzero(ulong x) { return x | (((x - 1UL) & ~x) >> 63); }
inline ulong2 __ms_nonzero(ulong2 x) { return x | (((x - 1UL) & ~x) >> 63); }
inline ulong3 __ms_nonzero(ulong3 x) { return x | (((x - 1UL) & ~x) >> 63); }
inline ulong4 __ms_nonzero(ulong4 x) { return x | (((x - 1UL) & ~x) >> 63); }
inline char __ms_nonzero(char x) { return char(__ms_nonzero(int(x))); }
inline char2 __ms_nonzero(char2 x) { return char2(__ms_nonzero(int2(x))); }
inline char3 __ms_nonzero(char3 x) { return char3(__ms_nonzero(int3(x))); }
inline char4 __ms_nonzero(char4 x) { return char4(__ms_nonzero(int4(x))); }
inline uchar __ms_nonzero(uchar x) { return uchar(__ms_nonzero(int(x))); }
inline uchar2 __ms_nonzero(uchar2 x) { return uchar2(__ms_nonzero(int2(x))); }
inline uchar3 __ms_nonzero(uchar3 x) { return uchar3(__ms_nonzero(int3(x))); }
inline uchar4 __ms_nonzero(uchar4 x) { return uchar4(__ms_nonzero(int4(x))); }
inline short __ms_nonzero(short x) { return short(__ms_nonzero(int(x))); }
inline short2 __ms_nonzero(short2 x) { return short2(__ms_nonzero(int2(x))); }
inline short3 __ms_nonzero(short3 x) { return short3(__ms_nonzero(int3(x))); }
inline short4 __ms_nonzero(short4 x) { return short4(__ms_nonzero(int4(x))); }
inline ushort __ms_nonzero(ushort x) { return ushort(__ms_nonzero(int(x))); }
inline ushort2 __ms_nonzero(ushort2 x) { return ushort2(__ms_nonzero(int2(x))); }
inline ushort3 __ms_nonzero(ushort3 x) { return ushort3(__ms_nonzero(int3(x))); }
inline ushort4 __ms_nonzero(ushort4 x) { return ushort4(__ms_nonzero(int4(x))); }
inline uint __ms_div_near_max(uint a, uint b) { return b > a ? 0u : (a - b) / __ms_nonzero(b) + 1u; }
inline uint __ms_rem_near_max(uint a, uint b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline ulong __ms_div_near_max(ulong a, ulong b) { return b > a ? 0UL : (a - b) / __ms_nonzero(b) + 1UL; }
inline ulong __ms_rem_near_max(ulong a, ulong b) { return b > a ? a : (a - b) % __ms_nonzero(b); }
inline uint2 __ms_div_near_max(uint2 a, uint2 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint2(0u), b > a); }
inline uint2 __ms_rem_near_max(uint2 a, uint2 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline uint3 __ms_div_near_max(uint3 a, uint3 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint3(0u), b > a); }
inline uint3 __ms_rem_near_max(uint3 a, uint3 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline uint4 __ms_div_near_max(uint4 a, uint4 b) { return metal::select((a - b) / __ms_nonzero(b) + 1u, uint4(0u), b > a); }
inline uint4 __ms_rem_near_max(uint4 a, uint4 b) { return metal::select((a - b) % __ms_nonzero(b), a, b > a); }
inline long __ms_div_near_max(long a, long b) { ulong q = __ms_div_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long((a < 0L) != (b < 0L) ? 0UL - q : q); }
inline long __ms_rem_near_max(long a, long b) { ulong r = __ms_rem_near_max(a < 0L ? 0UL - ulong(a) : ulong(a), b < 0L ? 0UL - ulong(b) : ulong(b)); return long(a < 0L ? 0UL - r : r); }

constant int Slots = 30;

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

kernel void SharpCompute_Differential_Tests_Random3x7(
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
    short sh0 = short(input[int(uint(o) + 9u)]);
    short sh1 = short(input[int(uint(o) + 10u)]);
    ushort us2 = ushort(input[int(uint(o) + 14u)]);
    int i0 = int(input[int(uint(o) + 15u)]);
    int i1 = int(input[int(uint(o) + 16u)]);
    int i2 = int(input[int(uint(o) + 17u)]);
    uint u0 = uint(input[int(uint(o) + 18u)]);
    uint u1 = uint(input[int(uint(o) + 19u)]);
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
    half h1 = as_type<half>(ushort(input[int(uint(o) + 31u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float2 f2b = float2(as_type<float>(uint(input[int(uint(o) + 35u)])), as_type<float>(uint(input[int(uint(o) + 36u)])));
    float3 f3b = float3(as_type<float>(uint(input[int(uint(o) + 40u)])), as_type<float>(uint(input[int(uint(o) + 41u)])), as_type<float>(uint(input[int(uint(o) + 42u)])));
    half3 h3a = half3(as_type<half>(ushort(input[int(uint(o) + 55u)])), as_type<half>(ushort(input[int(uint(o) + 56u)])), as_type<half>(ushort(input[int(uint(o) + 57u)])));
    half4 h4b = half4(as_type<half>(ushort(input[int(uint(o) + 65u)])), as_type<half>(ushort(input[int(uint(o) + 66u)])), as_type<half>(ushort(input[int(uint(o) + 67u)])), as_type<half>(ushort(input[int(uint(o) + 68u)])));
    int2 i2a = int2(int(input[int(uint(o) + 69u)]), int(input[int(uint(o) + 70u)]));
    int2 i2b = int2(int(input[int(uint(o) + 71u)]), int(input[int(uint(o) + 72u)]));
    uint4 u4a = uint4(uint(input[int(uint(o) + 97u)]), uint(input[int(uint(o) + 98u)]), uint(input[int(uint(o) + 99u)]), uint(input[int(uint(o) + 100u)]));
    bool2 b2b = bool2((input[int(uint(o) + 107u)] & 1UL) != 0UL, (input[int(uint(o) + 108u)] & 1UL) != 0UL);
    bool3 b3b = bool3((input[int(uint(o) + 112u)] & 1UL) != 0UL, (input[int(uint(o) + 113u)] & 1UL) != 0UL, (input[int(uint(o) + 114u)] & 1UL) != 0UL);
    bool4 b4a = bool4((input[int(uint(o) + 115u)] & 1UL) != 0UL, (input[int(uint(o) + 116u)] & 1UL) != 0UL, (input[int(uint(o) + 117u)] & 1UL) != 0UL, (input[int(uint(o) + 118u)] & 1UL) != 0UL);
    output[int(uint(p) + 0u)] = ((b0 | metal::isfinite(f0 + 3.0f)) ? (int((b0 ? b2 : b2) ? metal::clz(by1) : (b1 ? by1 : by1)) > int(uchar(ulong(by2) + ul2))) : (int3(63, i0, 32).z >= int(uchar(__ms_rotate_right(i2, i2))))) ? 1L : 0L;
    output[int(uint(p) + 1u)] = long(~(u1 ^ u2));
    uchar c2_0 = uchar(255);
    output[int(uint(p) + 2u)] = ((int(as_type<ushort>(h2)) >= int(metal::min(b2 ? by2 : by2, c2_0))) ? bool3(metal::select(b2b, b2b, b2b), metal::all(b3b)).y : true) ? 1L : 0L;
    int4 c3_0 = int4(int2(i2, i1).xxy, +metal::min(i0, 3)) | int4(i0, i1, metal::isnan(f1) ? 0 : int(metal::sign(f1)), ~int(sb0));
    output[int(uint(p) + 3u)] = long(c3_0.x);
    output[int(uint(p) + 4u)] = long(c3_0.y);
    output[int(uint(p) + 5u)] = long(c3_0.z);
    output[int(uint(p) + 6u)] = long(c3_0.w);
    float c4_0 = f0 * float(u0);
    float c4_1 = c4_0++;
    float c4_2 = as_type<float>(u1) + (f2 - 1.0f);
    float c4_3 = (metal::fmod(1E-30f, float(ul0))) - (-f2);
    output[int(uint(p) + 7u)] = long(as_type<uint>(metal::max(c4_2 > c4_3 ? 0.0f : __ms_math_clamp(c4_1, c4_2, c4_3), f0)));
    uint2 c5_0 = (__ms_add(uint2(u1, u0) << (int(ushort(256)) >> i0), uint2(uint(i1)))).yx;
    output[int(uint(p) + 8u)] = long(c5_0.x);
    output[int(uint(p) + 9u)] = long(c5_0.y);
    ushort c6_0 = us2;
    ushort c6_1 = c6_0++;
    ushort c6_2 = (int(by0) == int(by2)) ? __ms_sat_ushort(f1) : c6_1;
    output[int(uint(p) + 10u)] = long(metal::max(ushort(ulong((l2 == 0L || (l0 == -9223372036854775807L - 1L && l2 == -1L)) ? 0L : l0 / __ms_nonzero(l2))), c6_2));
    uchar c7_0 = uchar(ul1);
    c7_0 &= int(uchar(us2));
    uchar c7_1 = uchar(15);
    uchar c7_2 = c7_1--;
    uchar c7_3 = uchar(l0);
    c7_3 >>= i0;
    output[int(uint(p) + 11u)] = long((half(metal::clamp(by2, by1, by1)) > half(metal::max(sh0, sh1))) ? (metal::isfinite(f2b.x) ? c7_0 : metal::clamp(uchar(i0), c7_2, metal::min(by2, by1))) : c7_3);
    float4 c8_0 = f2b.xyxy;
    output[int(uint(p) + 12u)] = long(as_type<uint>(c8_0.x));
    output[int(uint(p) + 13u)] = long(as_type<uint>(c8_0.y));
    output[int(uint(p) + 14u)] = long(as_type<uint>(c8_0.z));
    output[int(uint(p) + 15u)] = long(as_type<uint>(c8_0.w));
    int c9_0 = __ms_rotate_right(i1, i2);
    int c9_1 = int(ushort(2));
    output[int(uint(p) + 16u)] = long(as_type<ushort>(half(as_type<float>((c9_1 == 0 || (c9_0 == -2147483647 - 1 && c9_1 == -1)) ? 0 : c9_0 % __ms_nonzero(c9_1)))));
    half c10_0 = half(10.0f);
    --c10_0;
    half4 c10_1 = h4b;
    bool4 c10_2 = (half4(metal::fma(metal::select(h4b + half4(half(1000.0f), half(-2.5f), half(-0.0f), half(65504.0f)), h4b, b4a), half2(i2b).xyxx, half4(metal::clamp(h3a, half(0.1f), h2), c10_0))) != half4(-metal::step(h2 * h1, c10_1)));
    output[int(uint(p) + 17u)] = (c10_2.x) ? 1L : 0L;
    output[int(uint(p) + 18u)] = (c10_2.y) ? 1L : 0L;
    output[int(uint(p) + 19u)] = (c10_2.z) ? 1L : 0L;
    output[int(uint(p) + 20u)] = (c10_2.w) ? 1L : 0L;
    int c11_0 = int(metal::reverse_bits(by2));
    int c11_1 = int(short(u2));
    int c11_2 = (c11_1 == 0 || (c11_0 == -2147483647 - 1 && c11_1 == -1)) ? 0 : c11_0 % __ms_nonzero(c11_1);
    int c11_3 = c11_2 == -2147483647 - 1 ? 0 : int(metal::abs(c11_2));
    c11_3 = int(uint(c11_3) + 1u);
    output[int(uint(p) + 21u)] = long(c11_3);
    float3 c12_0 = f3b;
    float3 c12_1 = f3b * ((metal::fmod(f2, f1) / c12_0) * metal::sqrt(__ms_math_min(f1, f2)));
    output[int(uint(p) + 22u)] = long(as_type<uint>(c12_1.x));
    output[int(uint(p) + 23u)] = long(as_type<uint>(c12_1.y));
    output[int(uint(p) + 24u)] = long(as_type<uint>(c12_1.z));
    uint c13_0 = u0;
    c13_0 += u1;
    int c13_1 = 33;
    long c13_2 = long(ulong(c13_0) - ulong(c13_1));
    long c13_3 = l2 << i1;
    long c13_4 = __ms_math_clamp(l1, metal::min(l2, l0), metal::max(l2, l0));
    c13_2 = long(ulong(c13_2) + ulong((c13_4 == 0L || (c13_3 == -9223372036854775807L - 1L && c13_4 == -1L)) ? 0L : c13_3 / __ms_nonzero(c13_4)));
    half c13_5 = h2;
    half c13_6 = c13_5--;
    output[int(uint(p) + 25u)] = (metal::clamp(c13_2, l0, __ms_sat_long(__ms_half_rem(half(1.5f), c13_6))));
    int2 c14_0 = i2b;
    c14_0 |= i2a;
    int2 c14_1 = int2(__ms_sub(uint2(metal::isnan(f2 - f2) ? (metal::abs(i1) & c14_0) : (i2a.xy << (int(uint(i2) - uint(sb1))))), uint2(i2a)));
    output[int(uint(p) + 26u)] = long(c14_1.x);
    output[int(uint(p) + 27u)] = long(c14_1.y);
    uint2 c15_0 = __ms_clz(u4a.yy);
    uint2 c15_1 = c15_0.xx;
    output[int(uint(p) + 28u)] = long(c15_1.x);
    output[int(uint(p) + 29u)] = long(c15_1.y);
}
