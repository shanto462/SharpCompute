// SharpCompute.Tests.Threadgroups.GroupAtomicSequence (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_GroupAtomicSequence, compile options None
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

constant int Returned = 19;
constant int ReturnedUInts = 16;
constant int ReturnedFloats = 4;
constant int Floats = 2;
constant int Ints = 10;
constant int UInts = 7;

struct __Constants
{
    int big;
    uint ubig;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, big) == 0, "SharpCompute: offset of '__Constants.big' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, ubig) == 4, "SharpCompute: offset of '__Constants.ubig' does not match its C# layout");

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

kernel void SharpCompute_Tests_Threadgroups_GroupAtomicSequence(
    device int* finalInts [[buffer(0)]],
    device uint* finalUInts [[buffer(1)]],
    device float* finalFloats [[buffer(2)]],
    device int* returned [[buffer(3)]],
    device uint* returnedUInts [[buffer(4)]],
    device float* returnedFloats [[buffer(5)]],
    constant __Constants& __c [[buffer(6)]],
    constant __GridWindow& __window [[buffer(7), function_constant(__windowed)]],
    uint3 __gid_in [[threadgroup_position_in_grid]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    threadgroup int ints[10];
    threadgroup uint uints[7];
    threadgroup float floats[2];
    if (int(__gindex) != 0)
    {
        return;
    }
    int group = int(__gid.x);
    int r = int(uint(group) * uint(::Returned));
    int ur = int(uint(group) * uint(::ReturnedUInts));
    ints[0] = 5;
    ints[1] = 7;
    ints[2] = -2147483647 - 1;
    ints[3] = 0;
    ints[4] = -5;
    ints[5] = 252645135;
    ints[6] = 42;
    ints[7] = 1;
    ints[8] = 1;
    ints[9] = -2;
    uints[0] = 0u;
    uints[1] = 0u;
    uints[2] = 5u;
    uints[3] = 5u;
    uints[4] = 4042322160u;
    uints[5] = 9u;
    uints[6] = 3221225472u;
    floats[0] = 1.5f;
    floats[1] = -0.0f;
    returned[int(uint(r) + 0u)] = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(ints[0]), 2147483647, metal::memory_order_relaxed);
    returned[int(uint(r) + 1u)] = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(ints[0]), 1, metal::memory_order_relaxed);
    returned[int(uint(r) + 2u)] = metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_int*)&(ints[1]), -2147483647 - 1, metal::memory_order_relaxed);
    returned[int(uint(r) + 3u)] = metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_int*)&(ints[2]), 1, metal::memory_order_relaxed);
    returned[int(uint(r) + 4u)] = metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_int*)&(ints[2]), -1, metal::memory_order_relaxed);
    returned[int(uint(r) + 5u)] = metal::atomic_fetch_min_explicit((threadgroup metal::atomic_int*)&(ints[3]), -1, metal::memory_order_relaxed);
    returned[int(uint(r) + 6u)] = metal::atomic_fetch_min_explicit((threadgroup metal::atomic_int*)&(ints[3]), 5, metal::memory_order_relaxed);
    returned[int(uint(r) + 7u)] = metal::atomic_fetch_max_explicit((threadgroup metal::atomic_int*)&(ints[4]), -2147483647 - 1, metal::memory_order_relaxed);
    returned[int(uint(r) + 8u)] = metal::atomic_fetch_max_explicit((threadgroup metal::atomic_int*)&(ints[4]), 9, metal::memory_order_relaxed);
    returned[int(uint(r) + 9u)] = metal::atomic_fetch_and_explicit((threadgroup metal::atomic_int*)&(ints[5]), 16711935, metal::memory_order_relaxed);
    returned[int(uint(r) + 10u)] = metal::atomic_fetch_or_explicit((threadgroup metal::atomic_int*)&(ints[5]), -2147483647 - 1, metal::memory_order_relaxed);
    returned[int(uint(r) + 11u)] = metal::atomic_fetch_xor_explicit((threadgroup metal::atomic_int*)&(ints[5]), -1, metal::memory_order_relaxed);
    returned[int(uint(r) + 12u)] = metal::atomic_exchange_explicit((threadgroup metal::atomic_int*)&(ints[6]), -2147483647 - 1, metal::memory_order_relaxed);
    returned[int(uint(r) + 13u)] = __ms_atomic_compare_exchange((threadgroup metal::atomic_int*)&(ints[7]), 3, 9);
    returned[int(uint(r) + 14u)] = __ms_atomic_compare_exchange((threadgroup metal::atomic_int*)&(ints[7]), 1, 11);
    returned[int(uint(r) + 15u)] = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(ints[8]), __c.big, metal::memory_order_relaxed);
    returned[int(uint(r) + 16u)] = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_int*)&(ints[8]), __c.big, metal::memory_order_relaxed);
    returned[int(uint(r) + 17u)] = metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_int*)&(ints[9]), __c.big, metal::memory_order_relaxed);
    returned[int(uint(r) + 18u)] = metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_int*)&(ints[9]), int(-uint(__c.big) - 1u), metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 0u)] = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_uint*)&(uints[0]), 4294967295u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 1u)] = metal::atomic_fetch_add_explicit((threadgroup metal::atomic_uint*)&(uints[0]), 2u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 2u)] = metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_uint*)&(uints[1]), 1u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 3u)] = metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_uint*)&(uints[1]), __c.ubig, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 4u)] = metal::atomic_fetch_sub_explicit((threadgroup metal::atomic_uint*)&(uints[1]), __c.ubig, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 5u)] = metal::atomic_fetch_min_explicit((threadgroup metal::atomic_uint*)&(uints[2]), 4294967295u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 6u)] = metal::atomic_fetch_min_explicit((threadgroup metal::atomic_uint*)&(uints[2]), 3u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 7u)] = metal::atomic_fetch_max_explicit((threadgroup metal::atomic_uint*)&(uints[3]), 2147483648u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 8u)] = metal::atomic_fetch_max_explicit((threadgroup metal::atomic_uint*)&(uints[3]), 7u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 9u)] = metal::atomic_fetch_and_explicit((threadgroup metal::atomic_uint*)&(uints[4]), 4278255360u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 10u)] = metal::atomic_fetch_or_explicit((threadgroup metal::atomic_uint*)&(uints[4]), 15u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 11u)] = metal::atomic_fetch_xor_explicit((threadgroup metal::atomic_uint*)&(uints[4]), 4294967295u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 12u)] = metal::atomic_exchange_explicit((threadgroup metal::atomic_uint*)&(uints[5]), 3735928559u, metal::memory_order_relaxed);
    returnedUInts[int(uint(ur) + 13u)] = __ms_atomic_compare_exchange((threadgroup metal::atomic_uint*)&(uints[6]), 1u, 2u);
    returnedUInts[int(uint(ur) + 14u)] = __ms_atomic_compare_exchange((threadgroup metal::atomic_uint*)&(uints[6]), __c.ubig, 1u);
    returnedUInts[int(uint(ur) + 15u)] = metal::atomic_fetch_max_explicit((threadgroup metal::atomic_uint*)&(uints[6]), __c.ubig, metal::memory_order_relaxed);
    int fr = int(uint(group) * uint(::ReturnedFloats));
    returnedFloats[int(uint(fr) + 0u)] = __ms_atomic_fetch_add_float((threadgroup metal::atomic_uint*)&(floats[0]), 0.25f);
    returnedFloats[int(uint(fr) + 1u)] = __ms_atomic_fetch_add_float((threadgroup metal::atomic_uint*)&(floats[0]), -2.0f);
    returnedFloats[int(uint(fr) + 2u)] = __ms_atomic_fetch_add_float((threadgroup metal::atomic_uint*)&(floats[1]), -0.0f);
    returnedFloats[int(uint(fr) + 3u)] = __ms_atomic_fetch_add_float((threadgroup metal::atomic_uint*)&(floats[1]), 3.0f);
    for (int k = 0; k < ::Floats; k = int(uint(k) + 1u))
    {
        finalFloats[int((uint(group) * uint(::Floats)) + uint(k))] = floats[k];
    }
    for (int k = 0; k < ::Ints; k = int(uint(k) + 1u))
    {
        finalInts[int((uint(group) * uint(::Ints)) + uint(k))] = ints[k];
    }
    for (int k = 0; k < ::UInts; k = int(uint(k) + 1u))
    {
        finalUInts[int((uint(group) * uint(::UInts)) + uint(k))] = uints[k];
    }
}
