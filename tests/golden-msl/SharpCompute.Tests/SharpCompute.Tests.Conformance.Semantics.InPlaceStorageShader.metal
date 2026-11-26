// SharpCompute.Tests.Conformance.Semantics.InPlaceStorageShader (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_InPlaceStorageShader, compile options None
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

constant int Slots = 48;
constant int Stride = 8;

static_assert(sizeof(packed_int3) == 12, "SharpCompute: size of 'packed_int3' does not match its C# layout");

struct InPlaceCell
{
    int Count;
    long Total;
    packed_int3 V;
    packed_int2 P;
};
static_assert(sizeof(InPlaceCell) == 40, "SharpCompute: size of 'InPlaceCell' does not match its C# layout");
static_assert(__builtin_offsetof(InPlaceCell, Count) == 0, "SharpCompute: offset of 'InPlaceCell.Count' does not match its C# layout");
static_assert(__builtin_offsetof(InPlaceCell, Total) == 8, "SharpCompute: offset of 'InPlaceCell.Total' does not match its C# layout");
static_assert(__builtin_offsetof(InPlaceCell, V) == 16, "SharpCompute: offset of 'InPlaceCell.V' does not match its C# layout");
static_assert(__builtin_offsetof(InPlaceCell, P) == 28, "SharpCompute: offset of 'InPlaceCell.P' does not match its C# layout");

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

static int Side(int scratch, int value, device int* counts);
static void AddTo(thread int& target, int by);
static int PostIncrement(thread int& target);
static void AddLong(thread long& target, long by);

static int Side(int scratch, int value, device int* counts)
{
    counts[scratch] = int(uint(counts[scratch]) + 1u);
    return value;
}

static void AddTo(thread int& target, int by)
{
    target = int(uint(target) + uint(by));
    target = int(uint(target) * 3u);
    target = int(uint(target) + 1u);
}

static int PostIncrement(thread int& target)
{
    return int(reinterpret_cast<thread uint&>(target)++);
}

static void AddLong(thread long& target, long by)
{
    target = long(ulong(target) + ulong(by));
    target = long(ulong(target) - 1UL);
}

kernel void SharpCompute_Tests_Conformance_Semantics_InPlaceStorageShader(
    const device int* pairs [[buffer(0)]],
    device int* data [[buffer(1)]],
    device long* wide [[buffer(2)]],
    device packed_int3* vec [[buffer(3)]],
    device InPlaceCell* cells [[buffer(4)]],
    device int* counts [[buffer(5)]],
    device long* output [[buffer(6)]],
    constant __GridWindow& __window [[buffer(7), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup int Tile[64];
    int i = int(__tid.x);
    int a = pairs[int(2u * uint(i))];
    int b = pairs[int((2u * uint(i)) + 1u)];
    int o = int(uint(i) * uint(::Slots));
    int e = int(uint(i) * uint(::Stride));
    int scratch = i;
    data[e] = a;
    int first = data[e];
    reinterpret_cast<device uint&>(data[e]) += uint(::Side(scratch, b, counts));
    int second = data[e];
    output[int(uint(o) + 0u)] = long(first);
    output[int(uint(o) + 1u)] = long(second);
    output[int(uint(o) + 2u)] = long(int(uint(second) - uint(first)) == b ? 1 : 0);
    data[int(uint(e) + 1u)] = a;
    reinterpret_cast<device uint&>(data[::Side(scratch, int(uint(e) + 1u), counts)]) *= uint(b);
    output[int(uint(o) + 3u)] = long(data[int(uint(e) + 1u)]);
    data[int(uint(e) + 2u)] = a;
    int used = int(reinterpret_cast<device uint&>(data[::Side(scratch, int(uint(e) + 2u), counts)]) -= uint(b));
    output[int(uint(o) + 4u)] = long(used);
    output[int(uint(o) + 5u)] = long(data[int(uint(e) + 2u)]);
    data[int(uint(e) + 3u)] = a;
    int oldValue = int(reinterpret_cast<device uint&>(data[int(uint(e) + 3u)])++);
    int newValue = int(++reinterpret_cast<device uint&>(data[int(uint(e) + 3u)]));
    output[int(uint(o) + 6u)] = long(oldValue);
    output[int(uint(o) + 7u)] = long(newValue);
    output[int(uint(o) + 8u)] = long(data[int(uint(e) + 3u)]);
    output[int(uint(o) + 9u)] = long(oldValue < newValue ? 1 : 0);
    data[int(uint(e) + 4u)] = a;
    reinterpret_cast<device uint&>(data[int(uint(e) + 4u)]) += uint(::Side(scratch, b, counts));
    output[int(uint(o) + 10u)] = long(data[int(uint(e) + 4u)]);
    int local = a;
    ::AddTo(local, b);
    output[int(uint(o) + 11u)] = long(local);
    output[int(uint(o) + 12u)] = long(::PostIncrement(local));
    output[int(uint(o) + 13u)] = long(local);
    cells[i].Count = a;
    reinterpret_cast<device uint&>(cells[i].Count) += uint(::Side(scratch, b, counts));
    cells[i].Count = int(uint(cells[i].Count) + 1u);
    cells[i].Total = long(ulong(long(a) << 32) + ulong(b));
    reinterpret_cast<device ulong&>(cells[i].Total) *= ulong(::Side(scratch, b, counts));
    cells[i].Total = long(ulong(cells[i].Total) + ulong(9223372036854775807L));
    cells[i].V = int3(a, b, a);
    __ms_add_to(cells[i].V, int3(::Side(scratch, b, counts)));
    reinterpret_cast<device packed_uint3&>(cells[i].V).y *= uint(::Side(scratch, b, counts));
    cells[i].V.xz = __ms_sub(cells[i].V.xz, int2(::Side(scratch, 1, counts), b));
    cells[i].P = int2(a, b);
    reinterpret_cast<device packed_uint2&>(cells[i].P).x += uint(::Side(scratch, 2147483647, counts));
    int px = int(reinterpret_cast<device packed_uint2&>(cells[i].P).y++);
    output[int(uint(o) + 14u)] = long(cells[i].Count);
    output[int(uint(o) + 15u)] = cells[i].Total;
    output[int(uint(o) + 16u)] = long(cells[i].V.x);
    output[int(uint(o) + 17u)] = long(cells[i].V.y);
    output[int(uint(o) + 18u)] = long(cells[i].V.z);
    output[int(uint(o) + 19u)] = long(cells[i].P.x);
    output[int(uint(o) + 20u)] = (long(px) << 32) ^ long(uint(cells[i].P.y));
    InPlaceCell c = cells[i];
    reinterpret_cast<thread uint&>(c.Count) *= uint(::Side(scratch, b, counts));
    reinterpret_cast<thread packed_uint3&>(c.V).x += uint(::Side(scratch, a, counts));
    reinterpret_cast<thread ulong&>(c.Total) -= ulong(::Side(scratch, b, counts));
    __ms_add_to(c.P, int2(::Side(scratch, b, counts), 1));
    c.P.y = int(uint(c.P.y) * uint(b));
    int cy = int(++reinterpret_cast<thread packed_uint3&>(c.V).y);
    output[int(uint(o) + 21u)] = long(c.Count);
    output[int(uint(o) + 22u)] = long(c.V.x);
    output[int(uint(o) + 23u)] = c.Total;
    output[int(uint(o) + 24u)] = long(c.P.x);
    output[int(uint(o) + 25u)] = long(c.P.y);
    output[int(uint(o) + 26u)] = long(cy);
    vec[i] = int3(a, b, 1);
    int3 seen = vec[i];
    reinterpret_cast<device packed_uint3&>(vec[::Side(scratch, i, counts)]) *= uint(b);
    reinterpret_cast<device packed_uint3&>(vec[i]).z += uint(::Side(scratch, a, counts));
    int vz = int(reinterpret_cast<device packed_uint3&>(vec[i]).z++);
    int3 now = vec[i];
    output[int(uint(o) + 27u)] = long(vz);
    output[int(uint(o) + 28u)] = long(now.x);
    output[int(uint(o) + 29u)] = long(now.y);
    output[int(uint(o) + 30u)] = long(now.z);
    output[int(uint(o) + 31u)] = long(seen.x == now.x ? 1 : 0);
    long la = (long(a) << 32) | long(uint(b));
    wide[i] = la;
    long wideFirst = wide[i];
    reinterpret_cast<device ulong&>(wide[::Side(scratch, i, counts)]) += ulong(9223372036854775807L);
    long wideOld = long(reinterpret_cast<device ulong&>(wide[i])++);
    long wideNew = long(--reinterpret_cast<device ulong&>(wide[i]));
    output[int(uint(o) + 32u)] = wideFirst;
    output[int(uint(o) + 33u)] = wideOld;
    output[int(uint(o) + 34u)] = wideNew;
    long wideLocal = la;
    ::AddLong(wideLocal, long(ulong(-9223372036854775807L - 1L) + ulong(b)));
    output[int(uint(o) + 35u)] = wideLocal;
    int window[4] = {};
    window[0] = a;
    window[1] = b;
    reinterpret_cast<thread uint&>(window[::Side(scratch, 0, counts)]) += uint(b);
    int w0 = int(reinterpret_cast<thread uint&>(window[0])++);
    window[2] = w0;
    int w1 = int(--reinterpret_cast<thread uint&>(window[1]));
    window[3] = w1;
    ::AddTo(window[1], a);
    output[int(uint(o) + 36u)] = long(window[0]);
    output[int(uint(o) + 37u)] = long(window[1]);
    output[int(uint(o) + 38u)] = long(window[2]);
    output[int(uint(o) + 39u)] = long(window[3]);
    int g = int(__gtid.x);
    Tile[g] = a;
    int tileFirst = Tile[g];
    reinterpret_cast<threadgroup uint&>(Tile[::Side(scratch, g, counts)]) += uint(b);
    int tileOld = int(reinterpret_cast<threadgroup uint&>(Tile[g])++);
    reinterpret_cast<threadgroup uint&>(Tile[::Side(scratch, g, counts)]) *= 3u;
    reinterpret_cast<threadgroup uint&>(Tile[g]) -= uint(::Side(scratch, b, counts));
    output[int(uint(o) + 40u)] = long(tileFirst);
    output[int(uint(o) + 41u)] = long(tileOld);
    output[int(uint(o) + 42u)] = long(Tile[g]);
    int m = a;
    int viaCall = int(reinterpret_cast<thread uint&>(m) += uint(::Side(scratch, b, counts)));
    int plain = m = int(uint(m) * uint(b));
    int post = int(reinterpret_cast<thread uint&>(m)--);
    output[int(uint(o) + 43u)] = long(viaCall);
    output[int(uint(o) + 44u)] = long(plain);
    output[int(uint(o) + 45u)] = long(post);
    output[int(uint(o) + 46u)] = long(m);
    output[int(uint(o) + 47u)] = long(counts[scratch]);
}
