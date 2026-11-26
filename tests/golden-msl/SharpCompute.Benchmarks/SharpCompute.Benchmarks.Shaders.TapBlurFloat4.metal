// SharpCompute.Benchmarks.Shaders.TapBlurFloat4 (SharpCompute.Benchmarks): entry point SharpCompute_Benchmarks_Shaders_TapBlurFloat4, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int size;
    int stride;
    int radius;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 0, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, stride) == 4, "SharpCompute: offset of '__Constants.stride' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radius) == 8, "SharpCompute: offset of '__Constants.radius' does not match its C# layout");

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

kernel void SharpCompute_Benchmarks_Shaders_TapBlurFloat4(
    const device packed_float4* image [[buffer(0)]],
    device packed_float4* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    float4 sum = float4(0.0f);
    float total = 0.0f;
    for (int dy = 0; dy <= int(2u * uint(__c.radius)); dy = int(uint(dy) + 1u))
    {
        int row = int(((__tid.y + uint(dy)) * uint(__c.stride)) + __tid.x);
        float wy = float(int(uint(__c.radius) + 1u - uint(int(metal::abs(int(uint(dy) - uint(__c.radius)))))));
        for (int dx = 0; dx <= int(2u * uint(__c.radius)); dx = int(uint(dx) + 1u))
        {
            float w = wy * float(int(uint(__c.radius) + 1u - uint(int(metal::abs(int(uint(dx) - uint(__c.radius)))))));
            sum += image[int(uint(row) + uint(dx))] * w;
            total += w;
        }
    }
    output[int((__tid.y * uint(__c.size)) + __tid.x)] = sum / total;
}
