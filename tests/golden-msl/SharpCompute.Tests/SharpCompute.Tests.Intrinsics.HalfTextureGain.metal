// SharpCompute.Tests.Intrinsics.HalfTextureGain (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_HalfTextureGain, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    packed_half4 gain;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, gain) == 0, "SharpCompute: offset of '__Constants.gain' does not match its C# layout");

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

kernel void SharpCompute_Tests_Intrinsics_HalfTextureGain(
    texture2d<float, access::read_write> image [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    half4 pixel = half4(image.read(uint2(int2(__tid.xy))));
    image.write(float4(pixel * __c.gain), uint2(int2(__tid.xy)));
    image.fence();
}
