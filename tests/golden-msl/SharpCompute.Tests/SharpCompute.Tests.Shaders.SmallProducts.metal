// SharpCompute.Tests.Shaders.SmallProducts (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SmallProducts, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 3;

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

static int Slot(int __ms_thread, int slot)
{
    return int((uint(__ms_thread) * uint(::Slots)) + uint(slot));
}

kernel void SharpCompute_Tests_Shaders_SmallProducts(
    const device ushort* pairs [[buffer(0)]],
    device ushort* output [[buffer(1)]],
    device short* __ms_signed [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    ushort a = pairs[int(2u * uint(i))];
    ushort b = pairs[int((2u * uint(i)) + 1u)];
    ushort x = a;
    x *= uint(b);
    output[::Slot(i, 0)] = x;
    output[::Slot(i, 1)] = a;
    output[::Slot(i, 1)] *= uint(b);
    output[::Slot(i, 2)] = a;
    output[::Slot(i, 2)] *= 65535u;
    short s = short(a);
    s *= uint(short(b));
    __ms_signed[i] = s;
}
