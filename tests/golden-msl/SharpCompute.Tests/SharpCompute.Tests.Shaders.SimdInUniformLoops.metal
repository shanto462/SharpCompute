// SharpCompute.Tests.Shaders.SimdInUniformLoops (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdInUniformLoops, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int passes;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, passes) == 0, "SharpCompute: offset of '__Constants.passes' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_SimdInUniformLoops(
    const device int* input [[buffer(0)]],
    device int* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int v = input[i];
    for (int pass = 0; pass < __c.passes; pass = int(uint(pass) + 1u))
    {
        if (pass % 2 == 0)
        {
            reinterpret_cast<thread uint&>(v) += uint(metal::simd_shuffle_up(v, ushort(1)));
        }
        else
        {
            v ^= metal::simd_max(v);
        }
    }
    output[i] = v;
}
