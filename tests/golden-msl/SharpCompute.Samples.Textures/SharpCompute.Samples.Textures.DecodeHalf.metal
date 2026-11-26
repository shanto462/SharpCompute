// SharpCompute.Samples.Textures.DecodeHalf (SharpCompute.Samples.Textures): entry point SharpCompute_Samples_Textures_DecodeHalf, compile options None
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

kernel void SharpCompute_Samples_Textures_DecodeHalf(
    texture2d<float, access::sample> source [[texture(0)]],
    texture2d<float, access::read_write> decoded [[texture(1)]],
    constant __GridWindow& __window [[buffer(0), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    decoded.write(source.read(uint2(int2(__tid.xy))), uint2(int2(__tid.xy)));
}
