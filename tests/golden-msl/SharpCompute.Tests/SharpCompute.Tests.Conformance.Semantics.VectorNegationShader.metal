// SharpCompute.Tests.Conformance.Semantics.VectorNegationShader (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_VectorNegationShader, compile options None
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

constant int Slots = 96;

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

static int3 Flip(int3 value);

static int3 Flip(int3 value)
{
    return __ms_neg(value);
}

kernel void SharpCompute_Tests_Conformance_Semantics_VectorNegationShader(
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
    int3 v = int3(a, b, a ^ b);
    int3 c = int3(1000, 2000, 3000);
    int3 n0 = __ms_neg(v);
    output[int(uint(o) + 0u)] = int(uint(n0.x) + uint(n0.z));
    int3 t0 = v;
    t0 = __ms_add(t0, c);
    t0 = int3(uint3(t0) * 3u);
    t0 = __ms_sub(t0, v);
    output[int(uint(o) + 1u)] = t0.x;
    output[int(uint(o) + 2u)] = t0.y;
    output[int(uint(o) + 3u)] = t0.z;
    int3 w1 = int3(int(uint(a) + 1u), int(uint(b) - 1u), int(uint(a ^ b) + 1u));
    int3 n1 = __ms_neg(w1);
    output[int(uint(o) + 4u)] = int(uint(n1.x) + uint(n1.y));
    int3 t1 = int3(__ms_sub((__ms_add(uint3(w1), uint3(c))) * 5u, uint3(w1)));
    output[int(uint(o) + 5u)] = t1.x;
    output[int(uint(o) + 6u)] = t1.y;
    output[int(uint(o) + 7u)] = t1.z;
    int3 w2 = int3(int(uint(a) + 2u), int(uint(b) - 2u), int(uint(a ^ b) + 2u));
    int3 n2 = __ms_neg(w2);
    output[int(uint(o) + 8u)] = int(uint(n2.y) + uint(n2.z));
    int3 t2 = int3(__ms_sub(__ms_add(uint3(w2) * 7u, uint3(c)), uint3(w2)));
    output[int(uint(o) + 9u)] = t2.x;
    output[int(uint(o) + 10u)] = t2.y;
    output[int(uint(o) + 11u)] = t2.z;
    int3 w3 = int3(int(uint(a) + 3u), int(uint(b) - 3u), int(uint(a ^ b) + 3u));
    int3 n3 = __ms_neg(w3);
    output[int(uint(o) + 12u)] = n3.x;
    int3 t3 = int3(__ms_add(__ms_sub(uint3(c), uint3(w3)), uint3(w3) * 3u));
    output[int(uint(o) + 13u)] = t3.x;
    output[int(uint(o) + 14u)] = t3.y;
    output[int(uint(o) + 15u)] = t3.z;
    int3 w4 = int3(int(uint(a) + 4u), int(uint(b) - 4u), int(uint(a ^ b) + 4u));
    int3 n4 = __ms_neg(w4);
    output[int(uint(o) + 16u)] = n4.z;
    int3 t4 = int3(__ms_sub((__ms_add(uint3(w4), uint3(c))) * 3u, uint3(w4)));
    output[int(uint(o) + 17u)] = t4.x;
    output[int(uint(o) + 18u)] = t4.y;
    output[int(uint(o) + 19u)] = t4.z;
    int3 w5 = int3(int(uint(a) + 5u), int(uint(b) - 5u), int(uint(a ^ b) + 5u));
    int3 n5 = w5;
    n5 = __ms_neg(n5);
    output[int(uint(o) + 20u)] = int(uint(n5.x) + uint(n5.z));
    int3 t5 = int3(__ms_sub((__ms_add(uint3(w5), uint3(c))) * 3u, uint3(w5)));
    output[int(uint(o) + 21u)] = t5.x;
    output[int(uint(o) + 22u)] = t5.y;
    output[int(uint(o) + 23u)] = t5.z;
    int2 p = int2(int(uint(a) + 6u), int(uint(b) + 6u));
    int2 np = __ms_neg(p);
    output[int(uint(o) + 24u)] = np.x;
    int2 tp = int2(__ms_sub((__ms_add(uint2(p), uint2(int2(100, 200)))) * 3u, uint2(p)));
    output[int(uint(o) + 25u)] = tp.x;
    output[int(uint(o) + 26u)] = tp.y;
    int4 q = int4(int(uint(a) + 7u), int(uint(b) + 7u), int(uint(a) + uint(b)), int(uint(a) - uint(b)));
    int4 nq = __ms_neg(q);
    output[int(uint(o) + 27u)] = int(uint(nq.x) + uint(nq.w));
    int4 tq = int4(__ms_sub((__ms_add(uint4(q), uint4(int4(10, 20, 30, 40)))) * 3u, uint4(q)));
    output[int(uint(o) + 28u)] = tq.x;
    output[int(uint(o) + 29u)] = tq.y;
    output[int(uint(o) + 30u)] = tq.z;
    output[int(uint(o) + 31u)] = tq.w;
    uint3 u = uint3(uint(a) + 8u, uint(b) + 8u, uint(a ^ b) + 8u);
    uint3 nu = __ms_neg(u);
    output[int(uint(o) + 32u)] = int(nu.x + nu.z);
    uint3 tu = __ms_sub((__ms_add(u, uint3(1000u, 2000u, 3000u))) * 3u, u);
    output[int(uint(o) + 33u)] = int(tu.x);
    output[int(uint(o) + 34u)] = int(tu.y);
    output[int(uint(o) + 35u)] = int(tu.z);
    uint3 u2 = uint3(uint(a) + 9u, uint(b) + 9u, uint(a ^ b) + 9u);
    uint3 nu2 = __ms_neg(u2);
    output[int(uint(o) + 36u)] = int(nu2.y);
    uint3 tu2 = __ms_sub((__ms_add(u2, uint3(1000u, 2000u, 3000u))) * 3u, u2);
    output[int(uint(o) + 37u)] = int(tu2.x);
    output[int(uint(o) + 38u)] = int(tu2.y);
    output[int(uint(o) + 39u)] = int(tu2.z);
    vectors[i] = int3(int(uint(a) + 9u), int(uint(b) + 9u), int(uint(a ^ b) + 9u));
    int3 n8 = __ms_neg(vectors[i]);
    output[int(uint(o) + 40u)] = int(uint(n8.x) + uint(n8.z));
    int3 t8 = int3(__ms_sub((__ms_add(uint3(vectors[i]), uint3(c))) * 3u, uint3(vectors[i])));
    output[int(uint(o) + 41u)] = t8.x;
    output[int(uint(o) + 42u)] = t8.y;
    output[int(uint(o) + 43u)] = t8.z;
    cells[i].V = int3(int(uint(a) + 10u), int(uint(b) + 10u), int(uint(a ^ b) + 10u));
    cells[i].P = int2(int(uint(a) + 10u), int(uint(b) + 10u));
    int3 n9 = __ms_neg(cells[i].V);
    int2 n9p = __ms_neg(cells[i].P);
    output[int(uint(o) + 44u)] = int(uint(n9.y) + uint(n9p.y));
    int3 t9 = int3(__ms_sub((__ms_add(uint3(cells[i].V), uint3(c))) * 3u, uint3(cells[i].V)));
    int2 t9p = int2(__ms_sub((__ms_add(uint2(cells[i].P), uint2(int2(5, 6)))) * 3u, uint2(cells[i].P)));
    output[int(uint(o) + 45u)] = t9.x;
    output[int(uint(o) + 46u)] = t9.y;
    output[int(uint(o) + 47u)] = t9.z;
    output[int(uint(o) + 48u)] = t9p.x;
    output[int(uint(o) + 49u)] = t9p.y;
    int g = int(__gtid.x);
    Tile[g] = int3(int(uint(a) + 11u), int(uint(b) + 11u), int(uint(a ^ b) + 11u));
    int3 n10 = __ms_neg(Tile[g]);
    output[int(uint(o) + 50u)] = n10.x;
    int3 t10 = int3(__ms_sub((__ms_add(uint3(Tile[g]), uint3(c))) * 3u, uint3(Tile[g])));
    output[int(uint(o) + 51u)] = t10.x;
    output[int(uint(o) + 52u)] = t10.y;
    output[int(uint(o) + 53u)] = t10.z;
    int3 w11 = int3(int(uint(a) + 12u), int(uint(b) - 12u), int(uint(a ^ b) + 12u));
    int3 n11 = ::Flip(w11);
    output[int(uint(o) + 54u)] = int(uint(n11.x) + uint(n11.z));
    int3 t11 = int3(__ms_sub((__ms_add(uint3(w11), uint3(c))) * 3u, uint3(w11)));
    output[int(uint(o) + 55u)] = t11.x;
    output[int(uint(o) + 56u)] = t11.y;
    output[int(uint(o) + 57u)] = t11.z;
    int3 w12 = int3(int(uint(a) + 13u), int(uint(b) - 13u), int(uint(a ^ b) + 13u));
    int3 n12 = int3(__ms_add(__ms_add(uint3(c), __ms_neg(uint3(w12))), uint3(c)));
    output[int(uint(o) + 58u)] = n12.z;
    int3 t12 = int3(__ms_sub((__ms_add(uint3(w12), uint3(c))) * 3u, uint3(w12)));
    output[int(uint(o) + 59u)] = t12.x;
    output[int(uint(o) + 60u)] = t12.y;
    output[int(uint(o) + 61u)] = t12.z;
    NegationCell cell = cells[i];
    cell.V = __ms_neg(cell.V);
    output[int(uint(o) + 62u)] = cell.V.x;
    int3 t13 = int3(__ms_sub((__ms_add(uint3(cells[i].V), uint3(c))) * 9u, uint3(cells[i].V)));
    output[int(uint(o) + 63u)] = t13.x;
    output[int(uint(o) + 64u)] = t13.y;
    output[int(uint(o) + 65u)] = t13.z;
    vectors[i] = __ms_neg(vectors[i]);
    output[int(uint(o) + 66u)] = vectors[i].z;
    int3 back = __ms_neg(vectors[i]);
    int3 t14 = int3(__ms_sub((__ms_add(uint3(back), uint3(c))) * 11u, uint3(back)));
    output[int(uint(o) + 67u)] = t14.x;
    output[int(uint(o) + 68u)] = t14.y;
    output[int(uint(o) + 69u)] = t14.z;
    int3 w15 = int3(int(uint(a) + 14u), int(uint(b) - 14u), int(uint(a ^ b) + 14u));
    int3 n15 = __ms_neg(w15);
    output[int(uint(o) + 70u)] = n15.x;
    output[int(uint(o) + 71u)] = n15.y;
    output[int(uint(o) + 72u)] = n15.z;
    uint4 uq = uint4(uint(a), uint(b), 5u, 7u);
    cells[i].U = __ms_neg(uq);
    output[int(uint(o) + 73u)] = int(cells[i].U.x);
    output[int(uint(o) + 74u)] = int(cells[i].U.w);
    int3 w16 = int3(int(uint(a) + 15u), int(uint(b) - 15u), int(uint(a ^ b) + 15u));
    int3 n16 = int3(__ms_sub(uint3(int3(0, 5, 0)), uint3(w16)));
    output[int(uint(o) + 75u)] = int(uint(n16.x) + uint(n16.z));
    int3 t16 = int3(__ms_sub((__ms_add(uint3(w16), uint3(c))) * 3u, uint3(w16)));
    output[int(uint(o) + 76u)] = t16.x;
    output[int(uint(o) + 77u)] = t16.y;
    output[int(uint(o) + 78u)] = t16.z;
    int3 w17 = int3(int(uint(a) + 16u), int(uint(b) - 16u), int(uint(a ^ b) + 16u));
    int3 n17 = __ms_mul(w17, int3(-1, 7, -1));
    output[int(uint(o) + 79u)] = int(uint(n17.x) + uint(n17.z));
    int3 t17 = int3(__ms_sub((__ms_add(uint3(w17), uint3(c))) * 3u, uint3(w17)));
    output[int(uint(o) + 80u)] = t17.x;
    output[int(uint(o) + 81u)] = t17.y;
    output[int(uint(o) + 82u)] = t17.z;
    int3 w18 = int3(int(uint(a) + 17u), int(uint(b) - 17u), int(uint(a ^ b) + 17u));
    int3 n18 = int3(__ms_add(uint3(~w18), uint3(1u)));
    output[int(uint(o) + 83u)] = int(uint(n18.x) + uint(n18.z));
    int3 t18 = int3(__ms_sub((__ms_add(uint3(w18), uint3(c))) * 3u, uint3(w18)));
    output[int(uint(o) + 84u)] = t18.x;
    output[int(uint(o) + 85u)] = t18.y;
    output[int(uint(o) + 86u)] = t18.z;
    int3 w19 = int3(int(uint(a) + 18u), int(uint(b) - 18u), int(uint(a ^ b) + 18u));
    int3 n19 = int3(__ms_sub(uint3(w19), 2u * uint3(w19)));
    output[int(uint(o) + 87u)] = int(uint(n19.x) + uint(n19.z));
    int3 t19 = int3(__ms_sub((__ms_add(uint3(w19), uint3(c))) * 3u, uint3(w19)));
    output[int(uint(o) + 88u)] = t19.x;
    output[int(uint(o) + 89u)] = t19.y;
    output[int(uint(o) + 90u)] = t19.z;
    float3 f = float3(float(a), float(b), float(int(uint(a) + uint(b))));
    float3 nf = -f;
    output[int(uint(o) + 91u)] = int(nf.x + nf.z);
    float3 tf = ((f + float3(1.0f, 2.0f, 3.0f)) * 3.0f) - f;
    output[int(uint(o) + 92u)] = int(tf.x);
    output[int(uint(o) + 93u)] = int(tf.y);
    output[int(uint(o) + 94u)] = int(tf.z);
    output[int(uint(o) + 95u)] = 1;
}
