// SharpCompute.Tests.Conformance.Semantics.VectorViewShader (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_VectorViewShader, compile options None
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

constant int Slots = 40;

static_assert(sizeof(packed_int2) == 8, "SharpCompute: size of 'packed_int2' does not match its C# layout");
static_assert(sizeof(packed_int3) == 12, "SharpCompute: size of 'packed_int3' does not match its C# layout");
static_assert(sizeof(packed_int4) == 16, "SharpCompute: size of 'packed_int4' does not match its C# layout");

struct VectorCell
{
    packed_int2 A;
    packed_int3 B;
    packed_int4 C;
    int D;
};
static_assert(sizeof(VectorCell) == 40, "SharpCompute: size of 'VectorCell' does not match its C# layout");
static_assert(__builtin_offsetof(VectorCell, A) == 0, "SharpCompute: offset of 'VectorCell.A' does not match its C# layout");
static_assert(__builtin_offsetof(VectorCell, B) == 8, "SharpCompute: offset of 'VectorCell.B' does not match its C# layout");
static_assert(__builtin_offsetof(VectorCell, C) == 20, "SharpCompute: offset of 'VectorCell.C' does not match its C# layout");
static_assert(__builtin_offsetof(VectorCell, D) == 36, "SharpCompute: offset of 'VectorCell.D' does not match its C# layout");

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

static int Same(int value);
static int3 Raise(int3 value, int by);
static void RaiseInPlace(thread int3& value, int by);

static int Same(int value)
{
    return value;
}

static int3 Raise(int3 value, int by)
{
    __ms_add_to(value, int3(by));
    return value;
}

static void RaiseInPlace(thread int3& value, int by)
{
    __ms_add_to(value, int3(by, by, by));
}

kernel void SharpCompute_Tests_Conformance_Semantics_VectorViewShader(
    const device int* pairs [[buffer(0)]],
    device packed_int2* twos [[buffer(1)]],
    device packed_int3* threes [[buffer(2)]],
    device packed_int4* fours [[buffer(3)]],
    device VectorCell* cells [[buffer(4)]],
    device long* output [[buffer(5)]],
    constant __GridWindow& __window [[buffer(6), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup packed_int3 Tile[64];
    int i = int(__tid.x);
    int a = pairs[int(2u * uint(i))];
    int b = pairs[int((2u * uint(i)) + 1u)];
    int o = int(uint(i) * uint(::Slots));
    int2 l2 = int2(a, b);
    __ms_add_to(l2, int2(1, 2));
    output[int(uint(o) + 0u)] = long(l2.x);
    output[int(uint(o) + 1u)] = long(l2.y);
    int3 l3 = int3(a, b, a);
    __ms_add_to(l3, int3(1, 2, 3));
    output[int(uint(o) + 2u)] = long(l3.x);
    output[int(uint(o) + 3u)] = long(l3.y);
    output[int(uint(o) + 4u)] = long(l3.z);
    int4 l4 = int4(a, b, a, b);
    reinterpret_cast<thread uint4&>(l4) *= uint4(int4(3));
    output[int(uint(o) + 5u)] = long(l4.x);
    output[int(uint(o) + 6u)] = long(l4.w);
    int3 m3 = int3(a, b, a);
    __ms_sub_from(m3, int3(b));
    __ms_add_to(m3, int3(5));
    output[int(uint(o) + 7u)] = long(m3.x);
    output[int(uint(o) + 8u)] = long(m3.y);
    int3 c3 = int3(a, b, a);
    reinterpret_cast<thread uint3&>(c3).y += uint(::Same(b));
    reinterpret_cast<thread uint3&>(c3).xz *= uint2(int2(3, 5));
    output[int(uint(o) + 9u)] = long(c3.x);
    output[int(uint(o) + 10u)] = long(c3.y);
    output[int(uint(o) + 11u)] = long(c3.z);
    int s = a;
    reinterpret_cast<thread uint&>(s) += uint(::Same(b));
    long w = long(a);
    reinterpret_cast<thread ulong&>(w) += ulong(::Same(b));
    reinterpret_cast<thread ulong&>(w) *= ulong(::Same(b));
    output[int(uint(o) + 12u)] = long(s);
    output[int(uint(o) + 13u)] = w;
    int3 raised = ::Raise(int3(a, b, 7), b);
    output[int(uint(o) + 14u)] = long(raised.x);
    output[int(uint(o) + 15u)] = long(raised.z);
    int3 inPlace = int3(a, b, 9);
    ::RaiseInPlace(inPlace, a);
    output[int(uint(o) + 16u)] = long(inPlace.y);
    output[int(uint(o) + 17u)] = long(inPlace.z);
    twos[i] = int2(a, b);
    int2 two = twos[i];
    __ms_add_to(twos[i], int2(b, a));
    int2 twoAfter = twos[i];
    output[int(uint(o) + 18u)] = long(two.x);
    output[int(uint(o) + 19u)] = long(twoAfter.x);
    output[int(uint(o) + 20u)] = long(twoAfter.y);
    threes[i] = int3(a, b, a);
    int3 three = threes[i];
    __ms_add_to(threes[i], int3(b, a, b));
    int3 threeAfter = threes[i];
    output[int(uint(o) + 21u)] = long(three.x);
    output[int(uint(o) + 22u)] = long(threeAfter.x);
    output[int(uint(o) + 23u)] = long(threeAfter.z);
    fours[i] = int4(a, b, a, b);
    int4 four = fours[i];
    reinterpret_cast<device packed_uint4&>(fours[i]) *= uint4(int4(b));
    reinterpret_cast<device packed_uint4&>(fours[i]).w += uint(::Same(a));
    int4 fourAfter = fours[i];
    output[int(uint(o) + 24u)] = long(four.x);
    output[int(uint(o) + 25u)] = long(fourAfter.x);
    output[int(uint(o) + 26u)] = long(fourAfter.w);
    cells[i].A = int2(a, b);
    cells[i].B = int3(a, b, a);
    cells[i].C = int4(a, b, a, b);
    VectorCell before = cells[i];
    __ms_add_to(cells[i].A, int2(b));
    __ms_sub_from(cells[i].B, int3(b, 1, 2));
    reinterpret_cast<device packed_uint4&>(cells[i].C) *= uint4(int4(3, 5, 7, 9));
    VectorCell after = cells[i];
    output[int(uint(o) + 27u)] = long(before.A.x);
    output[int(uint(o) + 28u)] = long(after.A.x);
    output[int(uint(o) + 29u)] = long(after.B.x);
    output[int(uint(o) + 30u)] = long(after.C.w);
    VectorCell local = before;
    __ms_add_to(local.A, int2(b));
    __ms_sub_from(local.B, int3(b, 1, 2));
    reinterpret_cast<thread packed_uint4&>(local.C) *= uint4(int4(3, 5, 7, 9));
    reinterpret_cast<thread uint&>(local.D) += uint(::Same(b));
    output[int(uint(o) + 31u)] = long(local.A.x);
    output[int(uint(o) + 32u)] = long(local.B.x);
    output[int(uint(o) + 33u)] = long(local.C.w);
    output[int(uint(o) + 34u)] = long(local.D);
    int g = int(__gtid.x);
    Tile[g] = int3(a, b, a);
    int3 tile = Tile[g];
    __ms_add_to(Tile[g], int3(b));
    reinterpret_cast<threadgroup packed_uint3&>(Tile[g]).y *= uint(::Same(b));
    int3 tileAfter = Tile[g];
    output[int(uint(o) + 35u)] = long(tile.x);
    output[int(uint(o) + 36u)] = long(tileAfter.x);
    output[int(uint(o) + 37u)] = long(tileAfter.y);
    int3 window[2] = {};
    window[0] = int3(a, b, a);
    window[1] = window[0];
    __ms_add_to(window[1], int3(b, b, b));
    output[int(uint(o) + 38u)] = long(window[1].x);
    output[int(uint(o) + 39u)] = long(window[0].x);
}
