// SharpCompute.Tests.Conformance.ConformanceHalf4Intrinsics (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceHalf4Intrinsics, compile options None
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

constant int Slots = 175;

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

kernel void SharpCompute_Tests_Conformance_ConformanceHalf4Intrinsics(
    const device half* a [[buffer(0)]],
    const device half* b [[buffer(1)]],
    const device half* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    half4 x = half4(a[int(4u * uint(i) + 0u)], a[int(4u * uint(i) + 1u)], a[int(4u * uint(i) + 2u)], a[int(4u * uint(i) + 3u)]);
    half4 y = half4(b[int(4u * uint(i) + 0u)], b[int(4u * uint(i) + 1u)], b[int(4u * uint(i) + 2u)], b[int(4u * uint(i) + 3u)]);
    half4 z = half4(c[int(4u * uint(i) + 0u)], c[int(4u * uint(i) + 1u)], c[int(4u * uint(i) + 2u)], c[int(4u * uint(i) + 3u)]);
    int n = s[i];
    half4 v0 = metal::abs(x);
    output[int(uint(o) + 0u)] = long(as_type<ushort>(v0.x));
    output[int(uint(o) + 1u)] = long(as_type<ushort>(v0.y));
    output[int(uint(o) + 2u)] = long(as_type<ushort>(v0.z));
    output[int(uint(o) + 3u)] = long(as_type<ushort>(v0.w));
    half4 v4 = metal::sign(x);
    output[int(uint(o) + 4u)] = long(as_type<ushort>(v4.x));
    output[int(uint(o) + 5u)] = long(as_type<ushort>(v4.y));
    output[int(uint(o) + 6u)] = long(as_type<ushort>(v4.z));
    output[int(uint(o) + 7u)] = long(as_type<ushort>(v4.w));
    half4 v8 = metal::saturate(x);
    output[int(uint(o) + 8u)] = long(as_type<ushort>(v8.x));
    output[int(uint(o) + 9u)] = long(as_type<ushort>(v8.y));
    output[int(uint(o) + 10u)] = long(as_type<ushort>(v8.z));
    output[int(uint(o) + 11u)] = long(as_type<ushort>(v8.w));
    half4 v12 = metal::floor(x);
    output[int(uint(o) + 12u)] = long(as_type<ushort>(v12.x));
    output[int(uint(o) + 13u)] = long(as_type<ushort>(v12.y));
    output[int(uint(o) + 14u)] = long(as_type<ushort>(v12.z));
    output[int(uint(o) + 15u)] = long(as_type<ushort>(v12.w));
    half4 v16 = metal::ceil(x);
    output[int(uint(o) + 16u)] = long(as_type<ushort>(v16.x));
    output[int(uint(o) + 17u)] = long(as_type<ushort>(v16.y));
    output[int(uint(o) + 18u)] = long(as_type<ushort>(v16.z));
    output[int(uint(o) + 19u)] = long(as_type<ushort>(v16.w));
    half4 v20 = metal::round(x);
    output[int(uint(o) + 20u)] = long(as_type<ushort>(v20.x));
    output[int(uint(o) + 21u)] = long(as_type<ushort>(v20.y));
    output[int(uint(o) + 22u)] = long(as_type<ushort>(v20.z));
    output[int(uint(o) + 23u)] = long(as_type<ushort>(v20.w));
    half4 v24 = metal::trunc(x);
    output[int(uint(o) + 24u)] = long(as_type<ushort>(v24.x));
    output[int(uint(o) + 25u)] = long(as_type<ushort>(v24.y));
    output[int(uint(o) + 26u)] = long(as_type<ushort>(v24.z));
    output[int(uint(o) + 27u)] = long(as_type<ushort>(v24.w));
    half4 v28 = metal::fract(x);
    output[int(uint(o) + 28u)] = long(as_type<ushort>(v28.x));
    output[int(uint(o) + 29u)] = long(as_type<ushort>(v28.y));
    output[int(uint(o) + 30u)] = long(as_type<ushort>(v28.z));
    output[int(uint(o) + 31u)] = long(as_type<ushort>(v28.w));
    half4 v32 = metal::sqrt(x);
    output[int(uint(o) + 32u)] = long(as_type<ushort>(v32.x));
    output[int(uint(o) + 33u)] = long(as_type<ushort>(v32.y));
    output[int(uint(o) + 34u)] = long(as_type<ushort>(v32.z));
    output[int(uint(o) + 35u)] = long(as_type<ushort>(v32.w));
    half4 v36 = metal::rsqrt(x);
    output[int(uint(o) + 36u)] = long(as_type<ushort>(v36.x));
    output[int(uint(o) + 37u)] = long(as_type<ushort>(v36.y));
    output[int(uint(o) + 38u)] = long(as_type<ushort>(v36.z));
    output[int(uint(o) + 39u)] = long(as_type<ushort>(v36.w));
    half4 v40 = __ms_radians(x);
    output[int(uint(o) + 40u)] = long(as_type<ushort>(v40.x));
    output[int(uint(o) + 41u)] = long(as_type<ushort>(v40.y));
    output[int(uint(o) + 42u)] = long(as_type<ushort>(v40.z));
    output[int(uint(o) + 43u)] = long(as_type<ushort>(v40.w));
    half4 v44 = __ms_degrees(x);
    output[int(uint(o) + 44u)] = long(as_type<ushort>(v44.x));
    output[int(uint(o) + 45u)] = long(as_type<ushort>(v44.y));
    output[int(uint(o) + 46u)] = long(as_type<ushort>(v44.z));
    output[int(uint(o) + 47u)] = long(as_type<ushort>(v44.w));
    half4 v48 = metal::min(x, y);
    output[int(uint(o) + 48u)] = long(as_type<ushort>(v48.x));
    output[int(uint(o) + 49u)] = long(as_type<ushort>(v48.y));
    output[int(uint(o) + 50u)] = long(as_type<ushort>(v48.z));
    output[int(uint(o) + 51u)] = long(as_type<ushort>(v48.w));
    half4 v52 = metal::max(x, y);
    output[int(uint(o) + 52u)] = long(as_type<ushort>(v52.x));
    output[int(uint(o) + 53u)] = long(as_type<ushort>(v52.y));
    output[int(uint(o) + 54u)] = long(as_type<ushort>(v52.z));
    output[int(uint(o) + 55u)] = long(as_type<ushort>(v52.w));
    half4 v56 = metal::fmod(x, y);
    output[int(uint(o) + 56u)] = long(as_type<ushort>(v56.x));
    output[int(uint(o) + 57u)] = long(as_type<ushort>(v56.y));
    output[int(uint(o) + 58u)] = long(as_type<ushort>(v56.z));
    output[int(uint(o) + 59u)] = long(as_type<ushort>(v56.w));
    half4 v60 = metal::step(x, y);
    output[int(uint(o) + 60u)] = long(as_type<ushort>(v60.x));
    output[int(uint(o) + 61u)] = long(as_type<ushort>(v60.y));
    output[int(uint(o) + 62u)] = long(as_type<ushort>(v60.z));
    output[int(uint(o) + 63u)] = long(as_type<ushort>(v60.w));
    half4 v64 = metal::copysign(x, y);
    output[int(uint(o) + 64u)] = long(as_type<ushort>(v64.x));
    output[int(uint(o) + 65u)] = long(as_type<ushort>(v64.y));
    output[int(uint(o) + 66u)] = long(as_type<ushort>(v64.z));
    output[int(uint(o) + 67u)] = long(as_type<ushort>(v64.w));
    half4 v68 = metal::clamp(x, y, z);
    output[int(uint(o) + 68u)] = long(as_type<ushort>(v68.x));
    output[int(uint(o) + 69u)] = long(as_type<ushort>(v68.y));
    output[int(uint(o) + 70u)] = long(as_type<ushort>(v68.z));
    output[int(uint(o) + 71u)] = long(as_type<ushort>(v68.w));
    half4 v72 = metal::mix(x, y, z);
    output[int(uint(o) + 72u)] = long(as_type<ushort>(v72.x));
    output[int(uint(o) + 73u)] = long(as_type<ushort>(v72.y));
    output[int(uint(o) + 74u)] = long(as_type<ushort>(v72.z));
    output[int(uint(o) + 75u)] = long(as_type<ushort>(v72.w));
    half4 v76 = metal::fma(x, y, z);
    output[int(uint(o) + 76u)] = long(as_type<ushort>(v76.x));
    output[int(uint(o) + 77u)] = long(as_type<ushort>(v76.y));
    output[int(uint(o) + 78u)] = long(as_type<ushort>(v76.z));
    output[int(uint(o) + 79u)] = long(as_type<ushort>(v76.w));
    half4 v80 = metal::smoothstep(x, y, z);
    output[int(uint(o) + 80u)] = long(as_type<ushort>(v80.x));
    output[int(uint(o) + 81u)] = long(as_type<ushort>(v80.y));
    output[int(uint(o) + 82u)] = long(as_type<ushort>(v80.z));
    output[int(uint(o) + 83u)] = long(as_type<ushort>(v80.w));
    bool4 v84 = metal::isnan(x);
    output[int(uint(o) + 84u)] = (v84.x) ? 1L : 0L;
    output[int(uint(o) + 85u)] = (v84.y) ? 1L : 0L;
    output[int(uint(o) + 86u)] = (v84.z) ? 1L : 0L;
    output[int(uint(o) + 87u)] = (v84.w) ? 1L : 0L;
    bool4 v88 = metal::isinf(x);
    output[int(uint(o) + 88u)] = (v88.x) ? 1L : 0L;
    output[int(uint(o) + 89u)] = (v88.y) ? 1L : 0L;
    output[int(uint(o) + 90u)] = (v88.z) ? 1L : 0L;
    output[int(uint(o) + 91u)] = (v88.w) ? 1L : 0L;
    bool4 v92 = metal::isfinite(x);
    output[int(uint(o) + 92u)] = (v92.x) ? 1L : 0L;
    output[int(uint(o) + 93u)] = (v92.y) ? 1L : 0L;
    output[int(uint(o) + 94u)] = (v92.z) ? 1L : 0L;
    output[int(uint(o) + 95u)] = (v92.w) ? 1L : 0L;
    half4 v96 = metal::sin(x);
    output[int(uint(o) + 96u)] = long(as_type<ushort>(v96.x));
    output[int(uint(o) + 97u)] = long(as_type<ushort>(v96.y));
    output[int(uint(o) + 98u)] = long(as_type<ushort>(v96.z));
    output[int(uint(o) + 99u)] = long(as_type<ushort>(v96.w));
    half4 v100 = metal::cos(x);
    output[int(uint(o) + 100u)] = long(as_type<ushort>(v100.x));
    output[int(uint(o) + 101u)] = long(as_type<ushort>(v100.y));
    output[int(uint(o) + 102u)] = long(as_type<ushort>(v100.z));
    output[int(uint(o) + 103u)] = long(as_type<ushort>(v100.w));
    half4 v104 = metal::tan(x);
    output[int(uint(o) + 104u)] = long(as_type<ushort>(v104.x));
    output[int(uint(o) + 105u)] = long(as_type<ushort>(v104.y));
    output[int(uint(o) + 106u)] = long(as_type<ushort>(v104.z));
    output[int(uint(o) + 107u)] = long(as_type<ushort>(v104.w));
    half4 v108 = metal::asin(x);
    output[int(uint(o) + 108u)] = long(as_type<ushort>(v108.x));
    output[int(uint(o) + 109u)] = long(as_type<ushort>(v108.y));
    output[int(uint(o) + 110u)] = long(as_type<ushort>(v108.z));
    output[int(uint(o) + 111u)] = long(as_type<ushort>(v108.w));
    half4 v112 = metal::acos(x);
    output[int(uint(o) + 112u)] = long(as_type<ushort>(v112.x));
    output[int(uint(o) + 113u)] = long(as_type<ushort>(v112.y));
    output[int(uint(o) + 114u)] = long(as_type<ushort>(v112.z));
    output[int(uint(o) + 115u)] = long(as_type<ushort>(v112.w));
    half4 v116 = metal::atan(x);
    output[int(uint(o) + 116u)] = long(as_type<ushort>(v116.x));
    output[int(uint(o) + 117u)] = long(as_type<ushort>(v116.y));
    output[int(uint(o) + 118u)] = long(as_type<ushort>(v116.z));
    output[int(uint(o) + 119u)] = long(as_type<ushort>(v116.w));
    half4 v120 = metal::sinh(x);
    output[int(uint(o) + 120u)] = long(as_type<ushort>(v120.x));
    output[int(uint(o) + 121u)] = long(as_type<ushort>(v120.y));
    output[int(uint(o) + 122u)] = long(as_type<ushort>(v120.z));
    output[int(uint(o) + 123u)] = long(as_type<ushort>(v120.w));
    half4 v124 = metal::cosh(x);
    output[int(uint(o) + 124u)] = long(as_type<ushort>(v124.x));
    output[int(uint(o) + 125u)] = long(as_type<ushort>(v124.y));
    output[int(uint(o) + 126u)] = long(as_type<ushort>(v124.z));
    output[int(uint(o) + 127u)] = long(as_type<ushort>(v124.w));
    half4 v128 = metal::tanh(x);
    output[int(uint(o) + 128u)] = long(as_type<ushort>(v128.x));
    output[int(uint(o) + 129u)] = long(as_type<ushort>(v128.y));
    output[int(uint(o) + 130u)] = long(as_type<ushort>(v128.z));
    output[int(uint(o) + 131u)] = long(as_type<ushort>(v128.w));
    half4 v132 = metal::exp(x);
    output[int(uint(o) + 132u)] = long(as_type<ushort>(v132.x));
    output[int(uint(o) + 133u)] = long(as_type<ushort>(v132.y));
    output[int(uint(o) + 134u)] = long(as_type<ushort>(v132.z));
    output[int(uint(o) + 135u)] = long(as_type<ushort>(v132.w));
    half4 v136 = metal::exp2(x);
    output[int(uint(o) + 136u)] = long(as_type<ushort>(v136.x));
    output[int(uint(o) + 137u)] = long(as_type<ushort>(v136.y));
    output[int(uint(o) + 138u)] = long(as_type<ushort>(v136.z));
    output[int(uint(o) + 139u)] = long(as_type<ushort>(v136.w));
    half4 v140 = metal::exp10(x);
    output[int(uint(o) + 140u)] = long(as_type<ushort>(v140.x));
    output[int(uint(o) + 141u)] = long(as_type<ushort>(v140.y));
    output[int(uint(o) + 142u)] = long(as_type<ushort>(v140.z));
    output[int(uint(o) + 143u)] = long(as_type<ushort>(v140.w));
    half4 v144 = metal::log(x);
    output[int(uint(o) + 144u)] = long(as_type<ushort>(v144.x));
    output[int(uint(o) + 145u)] = long(as_type<ushort>(v144.y));
    output[int(uint(o) + 146u)] = long(as_type<ushort>(v144.z));
    output[int(uint(o) + 147u)] = long(as_type<ushort>(v144.w));
    half4 v148 = metal::log2(x);
    output[int(uint(o) + 148u)] = long(as_type<ushort>(v148.x));
    output[int(uint(o) + 149u)] = long(as_type<ushort>(v148.y));
    output[int(uint(o) + 150u)] = long(as_type<ushort>(v148.z));
    output[int(uint(o) + 151u)] = long(as_type<ushort>(v148.w));
    half4 v152 = metal::log10(x);
    output[int(uint(o) + 152u)] = long(as_type<ushort>(v152.x));
    output[int(uint(o) + 153u)] = long(as_type<ushort>(v152.y));
    output[int(uint(o) + 154u)] = long(as_type<ushort>(v152.z));
    output[int(uint(o) + 155u)] = long(as_type<ushort>(v152.w));
    half4 v156 = metal::atan2(x, y);
    output[int(uint(o) + 156u)] = long(as_type<ushort>(v156.x));
    output[int(uint(o) + 157u)] = long(as_type<ushort>(v156.y));
    output[int(uint(o) + 158u)] = long(as_type<ushort>(v156.z));
    output[int(uint(o) + 159u)] = long(as_type<ushort>(v156.w));
    half4 v160 = metal::pow(x, y);
    output[int(uint(o) + 160u)] = long(as_type<ushort>(v160.x));
    output[int(uint(o) + 161u)] = long(as_type<ushort>(v160.y));
    output[int(uint(o) + 162u)] = long(as_type<ushort>(v160.z));
    output[int(uint(o) + 163u)] = long(as_type<ushort>(v160.w));
    output[int(uint(o) + 164u)] = long(as_type<ushort>(metal::dot(x, y)));
    output[int(uint(o) + 165u)] = long(as_type<ushort>(metal::length(x)));
    output[int(uint(o) + 166u)] = long(as_type<ushort>(metal::distance(x, y)));
    half4 v167 = metal::normalize(x);
    output[int(uint(o) + 167u)] = long(as_type<ushort>(v167.x));
    output[int(uint(o) + 168u)] = long(as_type<ushort>(v167.y));
    output[int(uint(o) + 169u)] = long(as_type<ushort>(v167.z));
    output[int(uint(o) + 170u)] = long(as_type<ushort>(v167.w));
    half4 v171 = metal::reflect(x, y);
    output[int(uint(o) + 171u)] = long(as_type<ushort>(v171.x));
    output[int(uint(o) + 172u)] = long(as_type<ushort>(v171.y));
    output[int(uint(o) + 173u)] = long(as_type<ushort>(v171.z));
    output[int(uint(o) + 174u)] = long(as_type<ushort>(v171.w));
}
