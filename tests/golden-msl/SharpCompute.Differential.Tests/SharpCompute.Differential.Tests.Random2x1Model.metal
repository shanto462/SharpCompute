// SharpCompute.Differential.Tests.Random2x1Model (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random2x1Model, compile options None
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

constant int Slots = 72;

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
static half GpuModel_Fma(half a, half b, half c);
static float3 GpuModel_Flush_2(float3 value);
static float3 GpuModel_FlushTiny_2(float3 value);

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

static half GpuModel_Fma(half a, half b, half c)
{
    return metal::fma(half2(a), half2(b), half2(c)).x;
}

static float3 GpuModel_Flush_2(float3 value)
{
    return float3(::GpuModel_Flush(value.x), ::GpuModel_Flush(value.y), ::GpuModel_Flush(value.z));
}

static float3 GpuModel_FlushTiny_2(float3 value)
{
    return float3(::GpuModel_FlushTiny(value.x), ::GpuModel_FlushTiny(value.y), ::GpuModel_FlushTiny(value.z));
}

kernel void SharpCompute_Differential_Tests_Random2x1Model(
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
    char sb1 = char(input[int(uint(o) + 7u)]);
    short sh0 = short(input[int(uint(o) + 9u)]);
    short sh1 = short(input[int(uint(o) + 10u)]);
    short sh2 = short(input[int(uint(o) + 11u)]);
    int i1 = int(input[int(uint(o) + 16u)]);
    int i2 = int(input[int(uint(o) + 17u)]);
    uint u0 = uint(input[int(uint(o) + 18u)]);
    uint u1 = uint(input[int(uint(o) + 19u)]);
    uint u2 = uint(input[int(uint(o) + 20u)]);
    long l0 = long(input[int(uint(o) + 21u)]);
    long l1 = long(input[int(uint(o) + 22u)]);
    long l2 = long(input[int(uint(o) + 23u)]);
    float f0 = as_type<float>(uint(input[int(uint(o) + 27u)]));
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    float f2 = as_type<float>(uint(input[int(uint(o) + 29u)]));
    half h0 = as_type<half>(ushort(input[int(uint(o) + 30u)]));
    half h1 = as_type<half>(ushort(input[int(uint(o) + 31u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float3 f3a = float3(as_type<float>(uint(input[int(uint(o) + 37u)])), as_type<float>(uint(input[int(uint(o) + 38u)])), as_type<float>(uint(input[int(uint(o) + 39u)])));
    float3 f3b = float3(as_type<float>(uint(input[int(uint(o) + 40u)])), as_type<float>(uint(input[int(uint(o) + 41u)])), as_type<float>(uint(input[int(uint(o) + 42u)])));
    float4 f4a = float4(as_type<float>(uint(input[int(uint(o) + 43u)])), as_type<float>(uint(input[int(uint(o) + 44u)])), as_type<float>(uint(input[int(uint(o) + 45u)])), as_type<float>(uint(input[int(uint(o) + 46u)])));
    float4 f4b = float4(as_type<float>(uint(input[int(uint(o) + 47u)])), as_type<float>(uint(input[int(uint(o) + 48u)])), as_type<float>(uint(input[int(uint(o) + 49u)])), as_type<float>(uint(input[int(uint(o) + 50u)])));
    half3 h3a = half3(as_type<half>(ushort(input[int(uint(o) + 55u)])), as_type<half>(ushort(input[int(uint(o) + 56u)])), as_type<half>(ushort(input[int(uint(o) + 57u)])));
    half3 h3b = half3(as_type<half>(ushort(input[int(uint(o) + 58u)])), as_type<half>(ushort(input[int(uint(o) + 59u)])), as_type<half>(ushort(input[int(uint(o) + 60u)])));
    half4 h4b = half4(as_type<half>(ushort(input[int(uint(o) + 65u)])), as_type<half>(ushort(input[int(uint(o) + 66u)])), as_type<half>(ushort(input[int(uint(o) + 67u)])), as_type<half>(ushort(input[int(uint(o) + 68u)])));
    int4 i4b = int4(int(input[int(uint(o) + 83u)]), int(input[int(uint(o) + 84u)]), int(input[int(uint(o) + 85u)]), int(input[int(uint(o) + 86u)]));
    uint4 u4a = uint4(uint(input[int(uint(o) + 97u)]), uint(input[int(uint(o) + 98u)]), uint(input[int(uint(o) + 99u)]), uint(input[int(uint(o) + 100u)]));
    uint4 u4b = uint4(uint(input[int(uint(o) + 101u)]), uint(input[int(uint(o) + 102u)]), uint(input[int(uint(o) + 103u)]), uint(input[int(uint(o) + 104u)]));
    bool3 b3a = bool3((input[int(uint(o) + 109u)] & 1UL) != 0UL, (input[int(uint(o) + 110u)] & 1UL) != 0UL, (input[int(uint(o) + 111u)] & 1UL) != 0UL);
    bool3 b3b = bool3((input[int(uint(o) + 112u)] & 1UL) != 0UL, (input[int(uint(o) + 113u)] & 1UL) != 0UL, (input[int(uint(o) + 114u)] & 1UL) != 0UL);
    bool4 b4a = bool4((input[int(uint(o) + 115u)] & 1UL) != 0UL, (input[int(uint(o) + 116u)] & 1UL) != 0UL, (input[int(uint(o) + 117u)] & 1UL) != 0UL, (input[int(uint(o) + 118u)] & 1UL) != 0UL);
    bool4 b4b = bool4((input[int(uint(o) + 119u)] & 1UL) != 0UL, (input[int(uint(o) + 120u)] & 1UL) != 0UL, (input[int(uint(o) + 121u)] & 1UL) != 0UL, (input[int(uint(o) + 122u)] & 1UL) != 0UL);
    float4 c1_v0_0 = f4b;
    float4 c1_v0_1 = f4a;
    bool4 c1_v0_2 = ((metal::isfinite(::GpuModel_Flush(f1)) != metal::isnan(::GpuModel_Flush(f0))) && b1) ? metal::isfinite(half4(~u4b)) : ((::GpuModel_Flush_1(c1_v0_0) > ::GpuModel_Flush_1(c1_v0_1)) ^ b4a);
    output[int(uint(p) + 0u)] = (c1_v0_2.x) ? 1L : 0L;
    output[int(uint(p) + 1u)] = (c1_v0_2.y) ? 1L : 0L;
    output[int(uint(p) + 2u)] = (c1_v0_2.z) ? 1L : 0L;
    output[int(uint(p) + 3u)] = (c1_v0_2.w) ? 1L : 0L;
    float4 c1_v1_0 = f4b;
    float4 c1_v1_1 = f4a;
    bool4 c1_v1_2 = ((metal::isfinite(::GpuModel_Flush(f1)) != metal::isnan(::GpuModel_Flush(f0))) && b1) ? metal::isfinite(half4(~u4b)) : ((::GpuModel_Flush_1(c1_v1_0) > ::GpuModel_Flush_1(c1_v1_1)) ^ b4a);
    output[int(uint(p) + 4u)] = (c1_v1_2.x) ? 1L : 0L;
    output[int(uint(p) + 5u)] = (c1_v1_2.y) ? 1L : 0L;
    output[int(uint(p) + 6u)] = (c1_v1_2.z) ? 1L : 0L;
    output[int(uint(p) + 7u)] = (c1_v1_2.w) ? 1L : 0L;
    float4 c1_v2_0 = f4b;
    float4 c1_v2_1 = f4a;
    bool4 c1_v2_2 = ((metal::isfinite(f1) != metal::isnan(f0)) && b1) ? metal::isfinite(half4(~u4b)) : ((c1_v2_0 > c1_v2_1) ^ b4a);
    output[int(uint(p) + 8u)] = (c1_v2_2.x) ? 1L : 0L;
    output[int(uint(p) + 9u)] = (c1_v2_2.y) ? 1L : 0L;
    output[int(uint(p) + 10u)] = (c1_v2_2.z) ? 1L : 0L;
    output[int(uint(p) + 11u)] = (c1_v2_2.w) ? 1L : 0L;
    float4 c3_v0_0 = ::GpuModel_Flush_1(::GpuModel_Flush_1(metal::select(::GpuModel_Flush_1(metal::fma(::GpuModel_Flush_1(f4a), ::GpuModel_Flush_1(f4a), ::GpuModel_Flush_1(f4a))), float4(h4b), (uint4(u4b) != uint4(u4a)))) + ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(-f4b) - ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(f4a) + ::GpuModel_Flush_1(float4(-1E+30f, 3.4028235E+38f, 1E-10f, -1E+30f)))))));
    float4 c3_v0_1 = f4a;
    c3_v0_1 = ::GpuModel_Flush_1(::GpuModel_Flush_1(c3_v0_1) * ::GpuModel_Flush_1(f4b));
    float4 c3_v0_2 = c3_v0_1;
    c3_v0_0 = ::GpuModel_Flush_1(::GpuModel_Flush_1(c3_v0_0) + ::GpuModel_Flush_1(metal::select(c3_v0_2, ::GpuModel_Flush_1(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f1) + ::GpuModel_Flush(f0))) + ::GpuModel_Flush_1(as_type<float4>(i4b))), (::GpuModel_Flush(f1) != ::GpuModel_Flush(f2)) ? (i4b < i4b) : (b4b ^ b4a))));
    float4 c3_v0_3 = c3_v0_0;
    output[int(uint(p) + 12u)] = long(as_type<uint>(c3_v0_3.x));
    output[int(uint(p) + 13u)] = long(as_type<uint>(c3_v0_3.y));
    output[int(uint(p) + 14u)] = long(as_type<uint>(c3_v0_3.z));
    output[int(uint(p) + 15u)] = long(as_type<uint>(c3_v0_3.w));
    float4 c3_v1_0 = ::GpuModel_Flush_1(::GpuModel_Flush_1(metal::select(::GpuModel_FlushTiny_1(metal::fma(::GpuModel_Flush_1(f4a), ::GpuModel_Flush_1(f4a), ::GpuModel_Flush_1(f4a))), float4(h4b), (uint4(u4b) != uint4(u4a)))) + ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(-f4b) - ::GpuModel_Flush_1(::GpuModel_Flush_1(::GpuModel_Flush_1(f4a) + ::GpuModel_Flush_1(float4(-1E+30f, 3.4028235E+38f, 1E-10f, -1E+30f)))))));
    float4 c3_v1_1 = f4a;
    c3_v1_1 = ::GpuModel_FlushTiny_1(::GpuModel_Flush_1(c3_v1_1) * ::GpuModel_Flush_1(f4b));
    float4 c3_v1_2 = c3_v1_1;
    c3_v1_0 = ::GpuModel_Flush_1(::GpuModel_Flush_1(c3_v1_0) + ::GpuModel_Flush_1(metal::select(c3_v1_2, ::GpuModel_Flush_1(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f1) + ::GpuModel_Flush(f0))) + ::GpuModel_Flush_1(as_type<float4>(i4b))), (::GpuModel_Flush(f1) != ::GpuModel_Flush(f2)) ? (i4b < i4b) : (b4b ^ b4a))));
    float4 c3_v1_3 = c3_v1_0;
    output[int(uint(p) + 16u)] = long(as_type<uint>(c3_v1_3.x));
    output[int(uint(p) + 17u)] = long(as_type<uint>(c3_v1_3.y));
    output[int(uint(p) + 18u)] = long(as_type<uint>(c3_v1_3.z));
    output[int(uint(p) + 19u)] = long(as_type<uint>(c3_v1_3.w));
    float4 c3_v2_0 = ::GpuModel_Flush_1(metal::select(::GpuModel_FlushTiny_1(metal::fma(f4a, f4a, f4a)), float4(h4b), (uint4(u4b) != uint4(u4a))) + ::GpuModel_Flush_1((-f4b) - ::GpuModel_Flush_1(f4a + float4(-1E+30f, 3.4028235E+38f, 1E-10f, -1E+30f))));
    float4 c3_v2_1 = f4a;
    c3_v2_1 = ::GpuModel_FlushTiny_1(c3_v2_1 * f4b);
    float4 c3_v2_2 = c3_v2_1;
    c3_v2_0 = ::GpuModel_Flush_1(c3_v2_0 + metal::select(c3_v2_2, ::GpuModel_Flush_1(::GpuModel_Flush(f1 + f0) + as_type<float4>(i4b)), (f1 != f2) ? (i4b < i4b) : (b4b ^ b4a)));
    float4 c3_v2_3 = c3_v2_0;
    output[int(uint(p) + 20u)] = long(as_type<uint>(c3_v2_3.x));
    output[int(uint(p) + 21u)] = long(as_type<uint>(c3_v2_3.y));
    output[int(uint(p) + 22u)] = long(as_type<uint>(c3_v2_3.z));
    output[int(uint(p) + 23u)] = long(as_type<uint>(c3_v2_3.w));
    float c7_v0_0 = f0;
    c7_v0_0 = ::GpuModel_Flush(::GpuModel_Flush(c7_v0_0) + 1.0f);
    float c7_v0_1 = c7_v0_0;
    float2 c7_v0_2 = float2(c7_v0_1, ::GpuModel_Flush(metal::ceil(::GpuModel_Flush(f1))));
    float2 c7_v0_3 = c7_v0_2;
    output[int(uint(p) + 24u)] = long(as_type<uint>(c7_v0_3.x));
    output[int(uint(p) + 25u)] = long(as_type<uint>(c7_v0_3.y));
    float c7_v1_0 = f0;
    c7_v1_0 = ::GpuModel_Flush(::GpuModel_Flush(c7_v1_0) + 1.0f);
    float c7_v1_1 = c7_v1_0;
    float2 c7_v1_2 = float2(c7_v1_1, ::GpuModel_Flush(metal::ceil(::GpuModel_Flush(f1))));
    float2 c7_v1_3 = c7_v1_2;
    output[int(uint(p) + 26u)] = long(as_type<uint>(c7_v1_3.x));
    output[int(uint(p) + 27u)] = long(as_type<uint>(c7_v1_3.y));
    float c7_v2_0 = f0;
    c7_v2_0 = ::GpuModel_Flush(c7_v2_0 + 1.0f);
    float c7_v2_1 = c7_v2_0;
    float2 c7_v2_2 = float2(c7_v2_1, ::GpuModel_Flush(metal::ceil(f1)));
    float2 c7_v2_3 = c7_v2_2;
    output[int(uint(p) + 28u)] = long(as_type<uint>(c7_v2_3.x));
    output[int(uint(p) + 29u)] = long(as_type<uint>(c7_v2_3.y));
    output[int(uint(p) + 30u)] = long(as_type<uint>(-f1));
    output[int(uint(p) + 31u)] = long(as_type<uint>(-f1));
    output[int(uint(p) + 32u)] = long(as_type<uint>(-f1));
    uint c10_v0_0 = 4294967295u;
    uint c10_v0_1 = ((c10_v0_0 == 0u) ? 0u : u0 / __ms_nonzero(c10_v0_0)) << int(u1);
    uint c10_v0_2 = uint(ushort(u1));
    output[int(uint(p) + 33u)] = long(as_type<uint>(as_type<float>((c10_v0_2 == 0u) ? 0u : c10_v0_1 % __ms_nonzero(c10_v0_2))));
    uint c10_v1_0 = 4294967295u;
    uint c10_v1_1 = ((c10_v1_0 == 0u) ? 0u : u0 / __ms_nonzero(c10_v1_0)) << int(u1);
    uint c10_v1_2 = uint(ushort(u1));
    output[int(uint(p) + 34u)] = long(as_type<uint>(as_type<float>((c10_v1_2 == 0u) ? 0u : c10_v1_1 % __ms_nonzero(c10_v1_2))));
    uint c10_v2_0 = 4294967295u;
    uint c10_v2_1 = ((c10_v2_0 == 0u) ? 0u : u0 / __ms_nonzero(c10_v2_0)) << int(u1);
    uint c10_v2_2 = uint(ushort(u1));
    output[int(uint(p) + 35u)] = long(as_type<uint>(as_type<float>((c10_v2_2 == 0u) ? 0u : c10_v2_1 % __ms_nonzero(c10_v2_2))));
    short c11_v0_0 = sh2;
    c11_v0_0 *= uint(sh1);
    output[int(uint(p) + 36u)] = (((int(sh0) <= int(c11_v0_0)) == metal::isnan(::GpuModel_Flush(::GpuModel_Flush(::GpuModel_Flush(f2) * ::GpuModel_Flush(f0))))) == (b0 & ((u0 >= u0) == (l2 != l1)))) ? 1L : 0L;
    short c11_v1_0 = sh2;
    c11_v1_0 *= uint(sh1);
    output[int(uint(p) + 37u)] = (((int(sh0) <= int(c11_v1_0)) == metal::isnan(::GpuModel_Flush(::GpuModel_FlushTiny(::GpuModel_Flush(f2) * ::GpuModel_Flush(f0))))) == (b0 & ((u0 >= u0) == (l2 != l1)))) ? 1L : 0L;
    short c11_v2_0 = sh2;
    c11_v2_0 *= uint(sh1);
    output[int(uint(p) + 38u)] = (((int(sh0) <= int(c11_v2_0)) == metal::isnan(::GpuModel_FlushTiny(f2 * f0))) == (b0 & ((u0 >= u0) == (l2 != l1)))) ? 1L : 0L;
    half c12_v0_0 = h2;
    bool3 c12_v0_1 = (half3(metal::select((h1 * h3a) * metal::min(h3a, h3b), (h3b + h3b) + half3(h2), metal::isinf(f3b).zxz)) == (__ms_half_rem((h1 - h0) / h3b.z, ::GpuModel_Fma(h2, h2, c12_v0_0))));
    output[int(uint(p) + 39u)] = (c12_v0_1.x) ? 1L : 0L;
    output[int(uint(p) + 40u)] = (c12_v0_1.y) ? 1L : 0L;
    output[int(uint(p) + 41u)] = (c12_v0_1.z) ? 1L : 0L;
    half c12_v1_0 = h2;
    bool3 c12_v1_1 = (half3(metal::select((h1 * h3a) * metal::min(h3a, h3b), (h3b + h3b) + half3(h2), metal::isinf(::GpuModel_Flush_2(f3b)).zxz)) == (__ms_half_rem((h1 - h0) / h3b.z, c12_v1_0 + (h2 * h2))));
    output[int(uint(p) + 42u)] = (c12_v1_1.x) ? 1L : 0L;
    output[int(uint(p) + 43u)] = (c12_v1_1.y) ? 1L : 0L;
    output[int(uint(p) + 44u)] = (c12_v1_1.z) ? 1L : 0L;
    half c12_v2_0 = h2;
    bool3 c12_v2_1 = (half3(metal::select((h1 * h3a) * metal::min(h3a, h3b), (h3b + h3b) + half3(h2), metal::isinf(::GpuModel_Flush_2(f3b)).zxz)) == (__ms_half_rem((h1 - h0) / h3b.z, ::GpuModel_Fma(h2, h2, c12_v2_0))));
    output[int(uint(p) + 45u)] = (c12_v2_1.x) ? 1L : 0L;
    output[int(uint(p) + 46u)] = (c12_v2_1.y) ? 1L : 0L;
    output[int(uint(p) + 47u)] = (c12_v2_1.z) ? 1L : 0L;
    half c12_v3_0 = h2;
    bool3 c12_v3_1 = (half3(metal::select((h1 * h3a) * metal::min(h3a, h3b), (h3b + h3b) + half3(h2), metal::isinf(::GpuModel_Flush_2(f3b)).zxz)) == (__ms_half_rem((h1 - h0) / h3b.z, c12_v3_0 + (h2 * h2))));
    output[int(uint(p) + 48u)] = (c12_v3_1.x) ? 1L : 0L;
    output[int(uint(p) + 49u)] = (c12_v3_1.y) ? 1L : 0L;
    output[int(uint(p) + 50u)] = (c12_v3_1.z) ? 1L : 0L;
    half c12_v4_0 = h2;
    bool3 c12_v4_1 = (half3(metal::select((h1 * h3a) * metal::min(h3a, h3b), (h3b + h3b) + half3(h2), metal::isinf(::GpuModel_Flush_2(f3b)).zxz)) == (__ms_half_rem((h1 - h0) / h3b.z, ::GpuModel_Fma(h2, h2, c12_v4_0))));
    output[int(uint(p) + 51u)] = (c12_v4_1.x) ? 1L : 0L;
    output[int(uint(p) + 52u)] = (c12_v4_1.y) ? 1L : 0L;
    output[int(uint(p) + 53u)] = (c12_v4_1.z) ? 1L : 0L;
    half c12_v5_0 = h2;
    bool3 c12_v5_1 = (half3(metal::select((h1 * h3a) * metal::min(h3a, h3b), (h3b + h3b) + half3(h2), metal::isinf(f3b).zxz)) == (__ms_half_rem((h1 - h0) / h3b.z, c12_v5_0 + (h2 * h2))));
    output[int(uint(p) + 54u)] = (c12_v5_1.x) ? 1L : 0L;
    output[int(uint(p) + 55u)] = (c12_v5_1.y) ? 1L : 0L;
    output[int(uint(p) + 56u)] = (c12_v5_1.z) ? 1L : 0L;
    half c12_v6_0 = h2;
    bool3 c12_v6_1 = (half3(metal::select((h1 * h3a) * metal::min(h3a, h3b), (h3b + h3b) + half3(h2), metal::isinf(f3b).zxz)) == (__ms_half_rem((h1 - h0) / h3b.z, ::GpuModel_Fma(h2, h2, c12_v6_0))));
    output[int(uint(p) + 57u)] = (c12_v6_1.x) ? 1L : 0L;
    output[int(uint(p) + 58u)] = (c12_v6_1.y) ? 1L : 0L;
    output[int(uint(p) + 59u)] = (c12_v6_1.z) ? 1L : 0L;
    bool c14_v0_0 = metal::isnan(::GpuModel_Flush(f2));
    float3 c14_v0_1 = c14_v0_0 ? float3(metal::select(half3(half(1.0f), half(0.1f), half(0.0f)), h3b, b3a) - h4b.wyz) : (-metal::select(metal::select(f3a, f3a, b3b), ::GpuModel_Flush_2(::GpuModel_Flush_2(f3b) * ::GpuModel_Flush_2(float3(1.0f, 3.4028235E+38f, -0.5f))), !b3a));
    output[int(uint(p) + 60u)] = long(as_type<uint>(c14_v0_1.x));
    output[int(uint(p) + 61u)] = long(as_type<uint>(c14_v0_1.y));
    output[int(uint(p) + 62u)] = long(as_type<uint>(c14_v0_1.z));
    bool c14_v1_0 = metal::isnan(::GpuModel_Flush(f2));
    float3 c14_v1_1 = c14_v1_0 ? float3(metal::select(half3(half(1.0f), half(0.1f), half(0.0f)), h3b, b3a) - h4b.wyz) : (-metal::select(metal::select(f3a, f3a, b3b), ::GpuModel_FlushTiny_2(::GpuModel_Flush_2(f3b) * ::GpuModel_Flush_2(float3(1.0f, 3.4028235E+38f, -0.5f))), !b3a));
    output[int(uint(p) + 63u)] = long(as_type<uint>(c14_v1_1.x));
    output[int(uint(p) + 64u)] = long(as_type<uint>(c14_v1_1.y));
    output[int(uint(p) + 65u)] = long(as_type<uint>(c14_v1_1.z));
    bool c14_v2_0 = metal::isnan(f2);
    float3 c14_v2_1 = c14_v2_0 ? float3(metal::select(half3(half(1.0f), half(0.1f), half(0.0f)), h3b, b3a) - h4b.wyz) : (-metal::select(metal::select(f3a, f3a, b3b), ::GpuModel_FlushTiny_2(f3b * float3(1.0f, 3.4028235E+38f, -0.5f)), !b3a));
    output[int(uint(p) + 66u)] = long(as_type<uint>(c14_v2_1.x));
    output[int(uint(p) + 67u)] = long(as_type<uint>(c14_v2_1.y));
    output[int(uint(p) + 68u)] = long(as_type<uint>(c14_v2_1.z));
    float c15_v0_0 = -::GpuModel_Flush(metal::rint(::GpuModel_Flush(f1)));
    output[int(uint(p) + 69u)] = (metal::isinf(::GpuModel_Flush(c15_v0_0))) ? 1L : 0L;
    float c15_v1_0 = -::GpuModel_Flush(metal::rint(::GpuModel_Flush(f1)));
    output[int(uint(p) + 70u)] = (metal::isinf(::GpuModel_Flush(c15_v1_0))) ? 1L : 0L;
    float c15_v2_0 = -::GpuModel_Flush(metal::rint(f1));
    output[int(uint(p) + 71u)] = (metal::isinf(c15_v2_0)) ? 1L : 0L;
}
