// SharpCompute.Tests.Conformance.ConformanceUInt4Operators (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceUInt4Operators, compile options None
#include <metal_stdlib>

using namespace metal;

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
// SharpCompute integer helpers (MslPrelude.Integers).
inline uint4 __ms_nonzero(uint4 x) { return x | uint4(metal::reverse_bits(x) == uint4(0)); }

constant int Slots = 100;

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

kernel void SharpCompute_Tests_Conformance_ConformanceUInt4Operators(
    const device uint* a [[buffer(0)]],
    const device uint* b [[buffer(1)]],
    const device uint* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    uint4 x = uint4(a[int(4u * uint(i) + 0u)], a[int(4u * uint(i) + 1u)], a[int(4u * uint(i) + 2u)], a[int(4u * uint(i) + 3u)]);
    uint4 y = uint4(b[int(4u * uint(i) + 0u)], b[int(4u * uint(i) + 1u)], b[int(4u * uint(i) + 2u)], b[int(4u * uint(i) + 3u)]);
    uint4 z = uint4(c[int(4u * uint(i) + 0u)], c[int(4u * uint(i) + 1u)], c[int(4u * uint(i) + 2u)], c[int(4u * uint(i) + 3u)]);
    int n = s[i];
    uint4 v0 = __ms_add(x, y);
    output[int(uint(o) + 0u)] = long(v0.x);
    output[int(uint(o) + 1u)] = long(v0.y);
    output[int(uint(o) + 2u)] = long(v0.z);
    output[int(uint(o) + 3u)] = long(v0.w);
    uint4 v4 = __ms_sub(x, y);
    output[int(uint(o) + 4u)] = long(v4.x);
    output[int(uint(o) + 5u)] = long(v4.y);
    output[int(uint(o) + 6u)] = long(v4.z);
    output[int(uint(o) + 7u)] = long(v4.w);
    uint4 v8 = x * y;
    output[int(uint(o) + 8u)] = long(v8.x);
    output[int(uint(o) + 9u)] = long(v8.y);
    output[int(uint(o) + 10u)] = long(v8.z);
    output[int(uint(o) + 11u)] = long(v8.w);
    uint4 d = metal::select(y, uint4(1u), (uint4(y) == 0u));
    uint4 v12 = x / __ms_nonzero(d);
    output[int(uint(o) + 12u)] = long(v12.x);
    output[int(uint(o) + 13u)] = long(v12.y);
    output[int(uint(o) + 14u)] = long(v12.z);
    output[int(uint(o) + 15u)] = long(v12.w);
    uint4 v16 = x % __ms_nonzero(d);
    output[int(uint(o) + 16u)] = long(v16.x);
    output[int(uint(o) + 17u)] = long(v16.y);
    output[int(uint(o) + 18u)] = long(v16.z);
    output[int(uint(o) + 19u)] = long(v16.w);
    uint4 v20 = x & y;
    output[int(uint(o) + 20u)] = long(v20.x);
    output[int(uint(o) + 21u)] = long(v20.y);
    output[int(uint(o) + 22u)] = long(v20.z);
    output[int(uint(o) + 23u)] = long(v20.w);
    uint4 v24 = x | y;
    output[int(uint(o) + 24u)] = long(v24.x);
    output[int(uint(o) + 25u)] = long(v24.y);
    output[int(uint(o) + 26u)] = long(v24.z);
    output[int(uint(o) + 27u)] = long(v24.w);
    uint4 v28 = x ^ y;
    output[int(uint(o) + 28u)] = long(v28.x);
    output[int(uint(o) + 29u)] = long(v28.y);
    output[int(uint(o) + 30u)] = long(v28.z);
    output[int(uint(o) + 31u)] = long(v28.w);
    uint4 v32 = ~x;
    output[int(uint(o) + 32u)] = long(v32.x);
    output[int(uint(o) + 33u)] = long(v32.y);
    output[int(uint(o) + 34u)] = long(v32.z);
    output[int(uint(o) + 35u)] = long(v32.w);
    uint4 v36 = x << y;
    output[int(uint(o) + 36u)] = long(v36.x);
    output[int(uint(o) + 37u)] = long(v36.y);
    output[int(uint(o) + 38u)] = long(v36.z);
    output[int(uint(o) + 39u)] = long(v36.w);
    uint4 v40 = x >> y;
    output[int(uint(o) + 40u)] = long(v40.x);
    output[int(uint(o) + 41u)] = long(v40.y);
    output[int(uint(o) + 42u)] = long(v40.z);
    output[int(uint(o) + 43u)] = long(v40.w);
    uint4 v44 = x << n;
    output[int(uint(o) + 44u)] = long(v44.x);
    output[int(uint(o) + 45u)] = long(v44.y);
    output[int(uint(o) + 46u)] = long(v44.z);
    output[int(uint(o) + 47u)] = long(v44.w);
    uint4 v48 = x >> n;
    output[int(uint(o) + 48u)] = long(v48.x);
    output[int(uint(o) + 49u)] = long(v48.y);
    output[int(uint(o) + 50u)] = long(v48.z);
    output[int(uint(o) + 51u)] = long(v48.w);
    uint4 v52 = __ms_add(x, uint4(y.x));
    output[int(uint(o) + 52u)] = long(v52.x);
    output[int(uint(o) + 53u)] = long(v52.y);
    output[int(uint(o) + 54u)] = long(v52.z);
    output[int(uint(o) + 55u)] = long(v52.w);
    uint4 v56 = __ms_sub(uint4(y.x), x);
    output[int(uint(o) + 56u)] = long(v56.x);
    output[int(uint(o) + 57u)] = long(v56.y);
    output[int(uint(o) + 58u)] = long(v56.z);
    output[int(uint(o) + 59u)] = long(v56.w);
    uint4 v60 = x * y.x;
    output[int(uint(o) + 60u)] = long(v60.x);
    output[int(uint(o) + 61u)] = long(v60.y);
    output[int(uint(o) + 62u)] = long(v60.z);
    output[int(uint(o) + 63u)] = long(v60.w);
    bool4 v64 = x < y;
    output[int(uint(o) + 64u)] = (v64.x) ? 1L : 0L;
    output[int(uint(o) + 65u)] = (v64.y) ? 1L : 0L;
    output[int(uint(o) + 66u)] = (v64.z) ? 1L : 0L;
    output[int(uint(o) + 67u)] = (v64.w) ? 1L : 0L;
    bool4 v68 = x <= y;
    output[int(uint(o) + 68u)] = (v68.x) ? 1L : 0L;
    output[int(uint(o) + 69u)] = (v68.y) ? 1L : 0L;
    output[int(uint(o) + 70u)] = (v68.z) ? 1L : 0L;
    output[int(uint(o) + 71u)] = (v68.w) ? 1L : 0L;
    bool4 v72 = x > y;
    output[int(uint(o) + 72u)] = (v72.x) ? 1L : 0L;
    output[int(uint(o) + 73u)] = (v72.y) ? 1L : 0L;
    output[int(uint(o) + 74u)] = (v72.z) ? 1L : 0L;
    output[int(uint(o) + 75u)] = (v72.w) ? 1L : 0L;
    bool4 v76 = x >= y;
    output[int(uint(o) + 76u)] = (v76.x) ? 1L : 0L;
    output[int(uint(o) + 77u)] = (v76.y) ? 1L : 0L;
    output[int(uint(o) + 78u)] = (v76.z) ? 1L : 0L;
    output[int(uint(o) + 79u)] = (v76.w) ? 1L : 0L;
    bool4 v80 = (uint4(x) == uint4(y));
    output[int(uint(o) + 80u)] = (v80.x) ? 1L : 0L;
    output[int(uint(o) + 81u)] = (v80.y) ? 1L : 0L;
    output[int(uint(o) + 82u)] = (v80.z) ? 1L : 0L;
    output[int(uint(o) + 83u)] = (v80.w) ? 1L : 0L;
    bool4 v84 = (uint4(x) != uint4(y));
    output[int(uint(o) + 84u)] = (v84.x) ? 1L : 0L;
    output[int(uint(o) + 85u)] = (v84.y) ? 1L : 0L;
    output[int(uint(o) + 86u)] = (v84.z) ? 1L : 0L;
    output[int(uint(o) + 87u)] = (v84.w) ? 1L : 0L;
    uint4 v88 = metal::select(z, x, x < y);
    output[int(uint(o) + 88u)] = long(v88.x);
    output[int(uint(o) + 89u)] = long(v88.y);
    output[int(uint(o) + 90u)] = long(v88.z);
    output[int(uint(o) + 91u)] = long(v88.w);
    uint4 t = x;
    t = __ms_add(t, y);
    uint4 v92 = t;
    output[int(uint(o) + 92u)] = long(v92.x);
    output[int(uint(o) + 93u)] = long(v92.y);
    output[int(uint(o) + 94u)] = long(v92.z);
    output[int(uint(o) + 95u)] = long(v92.w);
    uint4 t2 = x;
    t2 *= y;
    uint4 v96 = t2;
    output[int(uint(o) + 96u)] = long(v96.x);
    output[int(uint(o) + 97u)] = long(v96.y);
    output[int(uint(o) + 98u)] = long(v96.z);
    output[int(uint(o) + 99u)] = long(v96.w);
}
