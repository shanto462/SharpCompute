// SharpCompute.Tests.Conformance.Semantics.VectorViewShader (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_Semantics_VectorViewShader, compile options None
#include <metal_stdlib>

using namespace metal;

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
    reinterpret_cast<thread uint3&>(value) += uint3(int3(by));
    return value;
}

static void RaiseInPlace(thread int3& value, int by)
{
    reinterpret_cast<thread uint3&>(value) += uint3(int3(by, by, by));
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
    reinterpret_cast<thread uint2&>(l2) += uint2(int2(1, 2));
    output[int(uint(o) + 0u)] = long(l2.x);
    output[int(uint(o) + 1u)] = long(l2.y);
    int3 l3 = int3(a, b, a);
    reinterpret_cast<thread uint3&>(l3) += uint3(int3(1, 2, 3));
    output[int(uint(o) + 2u)] = long(l3.x);
    output[int(uint(o) + 3u)] = long(l3.y);
    output[int(uint(o) + 4u)] = long(l3.z);
    int4 l4 = int4(a, b, a, b);
    reinterpret_cast<thread uint4&>(l4) *= uint4(int4(3));
    output[int(uint(o) + 5u)] = long(l4.x);
    output[int(uint(o) + 6u)] = long(l4.w);
    int3 m3 = int3(a, b, a);
    reinterpret_cast<thread uint3&>(m3) -= uint3(int3(b));
    reinterpret_cast<thread uint3&>(m3) += uint3(int3(5));
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
    reinterpret_cast<device packed_uint2&>(twos[i]) += uint2(int2(b, a));
    int2 twoAfter = twos[i];
    output[int(uint(o) + 18u)] = long(two.x);
    output[int(uint(o) + 19u)] = long(twoAfter.x);
    output[int(uint(o) + 20u)] = long(twoAfter.y);
    threes[i] = int3(a, b, a);
    int3 three = threes[i];
    reinterpret_cast<device packed_uint3&>(threes[i]) += uint3(int3(b, a, b));
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
    reinterpret_cast<device packed_uint2&>(cells[i].A) += uint2(int2(b));
    reinterpret_cast<device packed_uint3&>(cells[i].B) -= uint3(int3(b, 1, 2));
    reinterpret_cast<device packed_uint4&>(cells[i].C) *= uint4(int4(3, 5, 7, 9));
    VectorCell after = cells[i];
    output[int(uint(o) + 27u)] = long(before.A.x);
    output[int(uint(o) + 28u)] = long(after.A.x);
    output[int(uint(o) + 29u)] = long(after.B.x);
    output[int(uint(o) + 30u)] = long(after.C.w);
    VectorCell local = before;
    reinterpret_cast<thread packed_uint2&>(local.A) += uint2(int2(b));
    reinterpret_cast<thread packed_uint3&>(local.B) -= uint3(int3(b, 1, 2));
    reinterpret_cast<thread packed_uint4&>(local.C) *= uint4(int4(3, 5, 7, 9));
    reinterpret_cast<thread uint&>(local.D) += uint(::Same(b));
    output[int(uint(o) + 31u)] = long(local.A.x);
    output[int(uint(o) + 32u)] = long(local.B.x);
    output[int(uint(o) + 33u)] = long(local.C.w);
    output[int(uint(o) + 34u)] = long(local.D);
    int g = int(__gtid.x);
    Tile[g] = int3(a, b, a);
    int3 tile = Tile[g];
    reinterpret_cast<threadgroup packed_uint3&>(Tile[g]) += uint3(int3(b));
    reinterpret_cast<threadgroup packed_uint3&>(Tile[g]).y *= uint(::Same(b));
    int3 tileAfter = Tile[g];
    output[int(uint(o) + 35u)] = long(tile.x);
    output[int(uint(o) + 36u)] = long(tileAfter.x);
    output[int(uint(o) + 37u)] = long(tileAfter.y);
    int3 window[2] = {};
    window[0] = int3(a, b, a);
    window[1] = window[0];
    reinterpret_cast<thread uint3&>(window[1]) += uint3(int3(b, b, b));
    output[int(uint(o) + 38u)] = long(window[1].x);
    output[int(uint(o) + 39u)] = long(window[0].x);
}
