// SharpCompute.Tests.Intrinsics.HalfArithmeticUncontracted (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_HalfArithmeticUncontracted, compile options NoFpContract
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");

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

kernel void SharpCompute_Tests_Intrinsics_HalfArithmeticUncontracted(
    const device packed_half4* a [[buffer(0)]],
    const device packed_half4* b [[buffer(1)]],
    const device packed_half4* c [[buffer(2)]],
    device packed_half4* output [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(12));
    half4 x = a[i];
    half4 y = b[i];
    half4 z = c[i];
    output[o] = x + y;
    output[int(uint(o) + 1u)] = x - y;
    output[int(uint(o) + 2u)] = x * y;
    output[int(uint(o) + 3u)] = x / y;
    output[int(uint(o) + 4u)] = -x;
    output[int(uint(o) + 5u)] = metal::fmod(x, y);
    output[int(uint(o) + 6u)] = x + y.x;
    output[int(uint(o) + 7u)] = x.x * y;
    output[int(uint(o) + 8u)] = x * y + z;
    output[int(uint(o) + 9u)] = z - x * y;
    output[int(uint(o) + 10u)] = x * y + y * z;
    output[int(uint(o) + 11u)] = x + y + z;
}
