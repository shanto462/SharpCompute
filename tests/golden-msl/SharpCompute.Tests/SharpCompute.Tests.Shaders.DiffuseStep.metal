// SharpCompute.Tests.Shaders.DiffuseStep (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_DiffuseStep, compile options None
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

kernel void SharpCompute_Tests_Shaders_DiffuseStep(
    texture2d<float, access::read_write> source [[texture(0)]],
    texture2d<float, access::read_write> target [[texture(1)]],
    constant __GridWindow& __window [[buffer(0), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    int left = metal::max(int(uint(x) - 1u), 0);
    int right = metal::min(int(uint(x) + 1u), int(uint(int(source.get_width())) - 1u));
    int up = metal::max(int(uint(y) - 1u), 0);
    int down = metal::min(int(uint(y) + 1u), int(uint(int(source.get_height())) - 1u));
    float sum = source.read(uint2(left, y)).x + source.read(uint2(right, y)).x + source.read(uint2(x, up)).x + source.read(uint2(x, down)).x;
    target.write(float4(sum * 0.25f, 0.0f, 0.0f, 1.0f), uint2(x, y));
}
