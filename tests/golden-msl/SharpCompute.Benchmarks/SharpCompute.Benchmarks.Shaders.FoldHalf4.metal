// SharpCompute.Benchmarks.Shaders.FoldHalf4 (SharpCompute.Benchmarks): entry point SharpCompute_Benchmarks_Shaders_FoldHalf4, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");

struct __Constants
{
    int size;
    int rounds;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 0, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, rounds) == 4, "SharpCompute: offset of '__Constants.rounds' does not match its C# layout");

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

kernel void SharpCompute_Benchmarks_Shaders_FoldHalf4(
    device packed_half4* output [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    half4 p = half4(float4(float(int(__tid.x)) / float(__c.size), float(int(__tid.y)) / float(__c.size), 0.5f, 0.25f));
    for (int i = 0; i < __c.rounds; i = int(uint(i) + 1u))
    {
        p = (metal::abs(metal::fract((p * half(1.7f)) + (p.yzwx * half(0.3f))) - half(0.5f)) * half(1.9f)) + (p.zwxy * half(0.05f));
    }
    output[int((__tid.y * uint(__c.size)) + __tid.x)] = p;
}
