// SharpCompute.Tests.Shaders.SpinOnBuffer (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SpinOnBuffer, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int iterations;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, iterations) == 0, "SharpCompute: offset of '__Constants.iterations' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_SpinOnBuffer(
    device float* data [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    float v = data[int(__tid.x)];
    for (int i = 0; i < __c.iterations; i = int(uint(i) + 1u))
    {
        v = (v * 1.0000001f) + 0.5f;
    }
    data[int(__tid.x)] = v;
}
