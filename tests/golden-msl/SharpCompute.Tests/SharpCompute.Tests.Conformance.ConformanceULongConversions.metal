// SharpCompute.Tests.Conformance.ConformanceULongConversions (SharpCompute.Tests): entry point SharpCompute_Tests_Conformance_ConformanceULongConversions, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 9;

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

kernel void SharpCompute_Tests_Conformance_ConformanceULongConversions(
    const device ulong* a [[buffer(0)]],
    const device ulong* b [[buffer(1)]],
    const device ulong* c [[buffer(2)]],
    const device int* s [[buffer(3)]],
    device long* output [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Slots));
    ulong x = a[i];
    ulong y = b[i];
    ulong z = c[i];
    int n = s[i];
    output[int(uint(o) + 0u)] = long(uchar(x));
    output[int(uint(o) + 1u)] = long(char(x));
    output[int(uint(o) + 2u)] = long(short(x));
    output[int(uint(o) + 3u)] = long(ushort(x));
    output[int(uint(o) + 4u)] = long(int(x));
    output[int(uint(o) + 5u)] = long(uint(x));
    output[int(uint(o) + 6u)] = (long(x));
    output[int(uint(o) + 7u)] = long(as_type<uint>(float(x)));
    output[int(uint(o) + 8u)] = long(as_type<ushort>(half(x)));
}
