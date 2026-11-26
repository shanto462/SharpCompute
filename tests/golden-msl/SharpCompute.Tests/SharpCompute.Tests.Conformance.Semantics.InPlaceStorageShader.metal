// SharpCompute.Tests.Conformance.Semantics.InPlaceStorageShader (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_InPlaceStorageShader, compile options None
#include <metal_stdlib>

using namespace metal;

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
    reinterpret_cast<device packed_uint3&>(cells[i].V) += uint3(int3(::Side(scratch, b, counts)));
    reinterpret_cast<device packed_uint3&>(cells[i].V).y *= uint(::Side(scratch, b, counts));
    reinterpret_cast<device packed_uint3&>(cells[i].V).xz -= uint2(int2(::Side(scratch, 1, counts), b));
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
    reinterpret_cast<thread packed_uint2&>(c.P) += uint2(int2(::Side(scratch, b, counts), 1));
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
