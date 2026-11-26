// SharpCompute.Tests.EndToEnd.Saxpy (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Saxpy, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    float a;
    packed_float3 offset;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, a) == 0, "SharpCompute: offset of '__Constants.a' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, offset) == 4, "SharpCompute: offset of '__Constants.offset' does not match its C# layout");

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

kernel void SharpCompute_Tests_EndToEnd_Saxpy(
    const device float* x [[buffer(0)]],
    device float* y [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float o = (i % 3) == 0 ? __c.offset.x : (i % 3) == 1 ? __c.offset.y : __c.offset.z;
    y[i] = __c.a * x[i] + y[i] + o;
}
