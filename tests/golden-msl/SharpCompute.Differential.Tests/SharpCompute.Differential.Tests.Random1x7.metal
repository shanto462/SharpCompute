// SharpCompute.Differential.Tests.Random1x7 (SharpCompute.Differential.Tests): entry point SharpCompute_Differential_Tests_Random1x7, compile options None
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
// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline char __ms_sat_char(float x) { float y = x == x ? x : 0.0f; return char(metal::clamp(y, -128.0f, 127.0f)); }
inline char __ms_sat_char(half x) { return __ms_sat_char(float(x)); }
inline uchar __ms_sat_uchar(float x) { float y = x > 0.0f ? x : 0.0f; return uchar(metal::min(y, 255.0f)); }
inline uchar __ms_sat_uchar(half x) { return __ms_sat_uchar(float(x)); }
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }
inline ulong __ms_sat_ulong(float x) { float y = x > 0.0f ? x : 0.0f; ulong r = ulong(metal::min(y, 18446742974197923840.0f)); return x >= 18446744073709551616.0f ? 18446744073709551615UL : r; }
inline ulong __ms_sat_ulong(half x) { return __ms_sat_ulong(float(x)); }
// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return x | int(metal::reverse_bits(x) == int(0)); }
inline uint __ms_nonzero(uint x) { return x | uint(metal::reverse_bits(x) == uint(0)); }
inline ulong __ms_nonzero(ulong x) { return x | ulong(metal::reverse_bits(x) == ulong(0)); }
inline ushort __ms_popcount(ushort x) { return ushort(metal::popcount(uint(ushort(x)))); }

constant int Slots = 21;

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

kernel void SharpCompute_Differential_Tests_Random1x7(
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
    ushort us0 = ushort(input[int(uint(o) + 12u)]);
    ushort us1 = ushort(input[int(uint(o) + 13u)]);
    int i0 = int(input[int(uint(o) + 15u)]);
    int i1 = int(input[int(uint(o) + 16u)]);
    int i2 = int(input[int(uint(o) + 17u)]);
    uint u0 = uint(input[int(uint(o) + 18u)]);
    uint u1 = uint(input[int(uint(o) + 19u)]);
    uint u2 = uint(input[int(uint(o) + 20u)]);
    long l1 = long(input[int(uint(o) + 22u)]);
    long l2 = long(input[int(uint(o) + 23u)]);
    ulong ul0 = input[int(uint(o) + 24u)];
    ulong ul1 = input[int(uint(o) + 25u)];
    ulong ul2 = input[int(uint(o) + 26u)];
    float f1 = as_type<float>(uint(input[int(uint(o) + 28u)]));
    float f2 = as_type<float>(uint(input[int(uint(o) + 29u)]));
    half h0 = as_type<half>(ushort(input[int(uint(o) + 30u)]));
    half h1 = as_type<half>(ushort(input[int(uint(o) + 31u)]));
    half h2 = as_type<half>(ushort(input[int(uint(o) + 32u)]));
    float3 f3b = float3(as_type<float>(uint(input[int(uint(o) + 40u)])), as_type<float>(uint(input[int(uint(o) + 41u)])), as_type<float>(uint(input[int(uint(o) + 42u)])));
    float4 f4b = float4(as_type<float>(uint(input[int(uint(o) + 47u)])), as_type<float>(uint(input[int(uint(o) + 48u)])), as_type<float>(uint(input[int(uint(o) + 49u)])), as_type<float>(uint(input[int(uint(o) + 50u)])));
    half3 h3a = half3(as_type<half>(ushort(input[int(uint(o) + 55u)])), as_type<half>(ushort(input[int(uint(o) + 56u)])), as_type<half>(ushort(input[int(uint(o) + 57u)])));
    half4 h4a = half4(as_type<half>(ushort(input[int(uint(o) + 61u)])), as_type<half>(ushort(input[int(uint(o) + 62u)])), as_type<half>(ushort(input[int(uint(o) + 63u)])), as_type<half>(ushort(input[int(uint(o) + 64u)])));
    uint2 u2b = uint2(uint(input[int(uint(o) + 89u)]), uint(input[int(uint(o) + 90u)]));
    uint4 u4a = uint4(uint(input[int(uint(o) + 97u)]), uint(input[int(uint(o) + 98u)]), uint(input[int(uint(o) + 99u)]), uint(input[int(uint(o) + 100u)]));
    bool2 b2a = bool2((input[int(uint(o) + 105u)] & 1UL) != 0UL, (input[int(uint(o) + 106u)] & 1UL) != 0UL);
    uchar c0_0 = metal::all(b2a) ? __ms_math_clamp(by2, metal::min(by0, by0), metal::max(by0, by0)) : metal::reverse_bits(by0);
    uchar c0_1 = ++c0_0;
    uchar c0_2 = c0_1;
    uchar c0_3 = c0_2++;
    output[int(uint(p) + 0u)] = long(c0_3);
    ulong c1_0 = ul0;
    ulong c1_1 = --c1_0;
    ulong c1_2 = ul2 * ul0;
    ulong c1_3 = (c1_2 == 0UL) ? 0UL : ul1 / __ms_nonzero(c1_2);
    output[int(uint(p) + 1u)] = long(b1 ? ((h2 >= h3a.z) ? (ul0 ^ ul1) : (ulong(by1) * c1_1)) : __ms_math_clamp(metal::rotate(ulong(by0) - ul0, ulong((0u - uint(i2)) & 63u)), metal::min(c1_3, ul0), metal::max(c1_3, ul0)));
    float c2_0 = float(i2) * f2;
    output[int(uint(p) + 2u)] = long(int(__ms_sat_char(c2_0)));
    ulong c3_0 = 0UL >> i0;
    long c3_1 = l1;
    c3_1 = long(ulong(c3_1) >> i1);
    ulong c3_2 = c3_0 ^ ulong(uchar(ul0 - ul2));
    ulong c3_3 = ulong(metal::min(l2, c3_1));
    output[int(uint(p) + 3u)] = long((c3_3 == 0UL) ? 0UL : c3_2 / __ms_nonzero(c3_3));
    ushort c4_0 = us1;
    --c4_0;
    ushort c4_1 = metal::max(__ms_popcount(ushort(i0)), metal::max(metal::max(ushort(15), us0), c4_0));
    ushort c4_2 = --c4_1;
    output[int(uint(p) + 4u)] = long(c4_2);
    float c5_0 = metal::max(10.0f, f2);
    float c5_1 = float(b1 ? h2 : h1);
    float c5_2 = c5_0;
    output[int(uint(p) + 5u)] = long(as_type<uint>(+(c5_1 > c5_2 ? 0.0f : __ms_number_clamp(float(us0) / f3b.y, c5_1, c5_2))));
    ushort c6_0 = us1;
    output[int(uint(p) + 6u)] = long(c6_0);
    int c7_0 = int(by1);
    int c7_1 = int(uint(uchar(u1)) + uint(metal::isnan(f1) ? 0 : int(metal::sign(f1))));
    output[int(uint(p) + 7u)] = ((+l1) << ((c7_1 == 0 || (c7_0 == -2147483647 - 1 && c7_1 == -1)) ? 0 : c7_0 / __ms_nonzero(c7_1)));
    output[int(uint(p) + 8u)] = long(__ms_sat_uchar(half(by1)));
    uchar c9_0 = __ms_rotate_right(by2, i0);
    uchar c9_1 = __ms_math_clamp(by0, metal::min(by0, by1), metal::max(by0, by1));
    if (!(int(c9_1) == 0))
    {
        c9_0 %= __ms_nonzero(int(c9_1));
    }
    uchar c9_2 = by0;
    uchar c9_3 = c9_2;
    uchar c9_4 = c9_3++;
    char c9_5 = sb2;
    c9_5 += int(char(63));
    uchar c9_6 = uchar(l2);
    output[int(uint(p) + 9u)] = long(metal::any(metal::isnan(f3b)) ? metal::max(c9_0, c9_4) : ((int(int(sb0) == -128 ? char(0) : char(metal::abs(sb0))) == int(c9_5)) ? metal::ctz(uchar(sb1)) : c9_6));
    char c10_0 = char(-8);
    --c10_0;
    output[int(uint(p) + 10u)] = long(as_type<uint>((metal::fmod(as_type<float>((u0 == 0u) ? 0u : u0 % __ms_nonzero(u0)), float(c10_0))) + float((3037000500L & l1) >> (metal::isnan(f2) ? 0 : int(metal::sign(f2))))));
    bool4 c11_0 = (metal::max(u2b.yyyx, uint4(3u, 2u, 32u, 3u) & u4a) >> __ms_sat_int(__ms_half_rem(h0, h2))) <= metal::pack_float_to_unorm4x8(float4(h4a) - (f4b - f4b));
    output[int(uint(p) + 11u)] = (c11_0.x) ? 1L : 0L;
    output[int(uint(p) + 12u)] = (c11_0.y) ? 1L : 0L;
    output[int(uint(p) + 13u)] = (c11_0.z) ? 1L : 0L;
    output[int(uint(p) + 14u)] = (c11_0.w) ? 1L : 0L;
    uint c12_0 = u1 + 0u;
    c12_0 |= as_type<uint>(f1);
    output[int(uint(p) + 15u)] = long(uint(uchar(metal::max(i1, metal::ctz(i2)))) * (((b1 ? b0 : b2) ? (int(short(-32768)) <= int(sh1)) : (b1 ^ b1)) ? c12_0 : u0));
    uint3 c13_0 = uint3(b2 ? u2 : u2, u2 - 31u, u0) * (u0 & ((h1 == h2) ? (b0 ? u2 : u1) : uint(ul0)));
    output[int(uint(p) + 16u)] = long(c13_0.x);
    output[int(uint(p) + 17u)] = long(c13_0.y);
    output[int(uint(p) + 18u)] = long(c13_0.z);
    half c14_0 = h0;
    half c14_1 = --c14_0;
    ulong c14_2 = 0UL;
    ulong c14_3 = ul0;
    output[int(uint(p) + 19u)] = long(__ms_rotate_left(__ms_sat_ulong(c14_1) | (c14_2 << (int(sb2) << i1)), int(uint(sh0) + uint(uchar(c14_3)))));
    ulong c15_0 = 4294967295UL;
    float c15_1 = metal::ceil(f1);
    float c15_2 = c15_1;
    output[int(uint(p) + 20u)] = long(as_type<uint>((!((ul1 << i1) >= c15_0)) ? (-((f1 - float(by1)) / metal::trunc(f2))) : c15_2));
}
