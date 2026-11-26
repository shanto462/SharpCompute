// SharpCompute.Tests.Shaders.OverflowInPlace (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_OverflowInPlace, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 8;

static_assert(sizeof(packed_int3) == 12, "SharpCompute: size of 'packed_int3' does not match its C# layout");

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

static int Slot(int __ms_thread, int slot);
static int Twice(int value);
static int Next(int t);
static void Bump(thread int& value, int by);

static int Slot(int __ms_thread, int slot)
{
    return int((uint(__ms_thread) * uint(::Slots)) + uint(slot));
}

static int Twice(int value)
{
    return int(uint(value) * 2u);
}

static int Next(int t)
{
    return t;
}

static void Bump(thread int& value, int by)
{
    value = int(uint(value) + uint(by));
    value = int(uint(value) * uint(by));
    value = int(uint(value) + 1u);
}

kernel void SharpCompute_Tests_Shaders_OverflowInPlace(
    const device int* values [[buffer(0)]],
    device int* ints [[buffer(1)]],
    device long* longs [[buffer(2)]],
    device packed_int3* vectors [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup int Tile[64];
    int i = int(__tid.x);
    int a = values[int(2u * uint(i))];
    int b = values[int((2u * uint(i)) + 1u)];
    ints[::Slot(i, 0)] = a;
    reinterpret_cast<device uint&>(ints[::Slot(i, 0)]) += uint(b);
    ints[::Slot(i, 1)] = a;
    reinterpret_cast<device uint&>(ints[::Slot(i, 1)]) *= uint(b);
    ints[::Slot(i, 2)] = a;
    reinterpret_cast<device uint&>(ints[::Slot(i, 2)]) -= uint(b);
    ints[::Slot(i, 3)] = a;
    reinterpret_cast<device uint&>(ints[::Slot(i, 3)])++;
    ints[::Slot(i, 4)] = a;
    --reinterpret_cast<device uint&>(ints[::Slot(i, 4)]);
    ints[::Slot(i, 5)] = a;
    reinterpret_cast<device uint&>(ints[::Slot(i, 5)]) += uint(::Twice(b));
    int t = int(__gtid.x);
    Tile[t] = a;
    reinterpret_cast<threadgroup uint&>(Tile[::Next(t)]) += uint(b);
    reinterpret_cast<threadgroup uint&>(Tile[::Next(t)]) *= 3u;
    ints[::Slot(i, 6)] = Tile[t];
    int local = a;
    ::Bump(local, b);
    ints[::Slot(i, 7)] = local;
    longs[::Slot(i, 0)] = long(a) << 32;
    reinterpret_cast<device ulong&>(longs[::Slot(i, 0)]) += ulong(9223372036854775807L);
    longs[::Slot(i, 1)] = long(a);
    reinterpret_cast<device ulong&>(longs[::Slot(i, 1)]) *= ulong(-9223372036854775807L - 1L) + ulong(b);
    long counter = long(b) << 33;
    long previous = long(reinterpret_cast<thread ulong&>(counter)++);
    longs[::Slot(i, 2)] = long(ulong(previous) + ulong(counter));
    vectors[::Slot(i, 0)] = int3(a, b, a);
    reinterpret_cast<device packed_uint3&>(vectors[::Slot(i, 0)]) += uint3(int3(b));
    reinterpret_cast<device packed_uint3&>(vectors[::Slot(i, 0)]) *= 3u;
    vectors[::Slot(i, 1)] = int3(a);
    reinterpret_cast<device packed_uint3&>(vectors[::Slot(i, 1)]).y += uint(b);
    reinterpret_cast<device packed_uint3&>(vectors[::Slot(i, 1)]).xz *= uint(b);
}
