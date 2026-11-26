// SharpCompute.Tests.Shaders.WriteOneSampleOther (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_WriteOneSampleOther, compile options None
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

kernel void SharpCompute_Tests_Shaders_WriteOneSampleOther(
    texture2d<float, access::read_write> written [[texture(0)]],
    texture2d<float, access::sample> read [[texture(1)]],
    device float* seen [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    written.write(float4(float(i) + 1.0f, 0.0f, 0.0f, 1.0f), uint2(i, 0));
    seen[i] = read.read(uint2(i, 0)).x;
}
