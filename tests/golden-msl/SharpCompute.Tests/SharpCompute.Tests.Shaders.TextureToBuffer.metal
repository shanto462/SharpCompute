// SharpCompute.Tests.Shaders.TextureToBuffer (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_TextureToBuffer, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

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

static float4 Texel(texture2d<float, access::sample> image, int2 at);
static float4 Captured(int x, int y, texture2d<float, access::sample> source);

static float4 Texel(texture2d<float, access::sample> image, int2 at)
{
    return image.read(uint2(at));
}

static float4 Captured(int x, int y, texture2d<float, access::sample> source)
{
    return source.read(uint2(x, y));
}

kernel void SharpCompute_Tests_Shaders_TextureToBuffer(
    texture2d<float, access::sample> source [[texture(0)]],
    device packed_float4* output [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int2 xy = int2(__tid.xy);
    output[int((uint(xy.y) * uint(int(source.get_width()))) + uint(xy.x))] = (::Texel(source, xy) + ::Captured(xy.x, xy.y, source)) * 0.5f;
}
