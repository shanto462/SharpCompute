// SharpCompute.Tests.Shaders.StoreEveryType (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_StoreEveryType, compile options None
#include <metal_stdlib>

using namespace metal;

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

kernel void SharpCompute_Tests_Shaders_StoreEveryType(
    const device long* values [[buffer(0)]],
    device uchar* bytes [[buffer(1)]],
    device char* sbytes [[buffer(2)]],
    device short* shorts [[buffer(3)]],
    device ushort* ushorts [[buffer(4)]],
    device long* longs [[buffer(5)]],
    device ulong* ulongs [[buffer(6)]],
    constant __GridWindow& __window [[buffer(7), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    long value = values[i];
    bytes[i] = uchar(value);
    sbytes[i] = char(value >> 8);
    shorts[i] = short(value >> 16);
    ushorts[i] = ushort(value >> 32);
    longs[i] = long(ulong(value) * 3UL);
    ulongs[i] = ulong(value) << 1;
}
