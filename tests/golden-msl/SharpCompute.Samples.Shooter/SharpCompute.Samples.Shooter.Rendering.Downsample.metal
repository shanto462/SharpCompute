// SharpCompute.Samples.Shooter.Rendering.Downsample (SharpCompute.Samples.Shooter): entry point SharpCompute_Samples_Shooter_Rendering_Downsample, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int sourceWidth;
    int destinationWidth;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, sourceWidth) == 0, "SharpCompute: offset of '__Constants.sourceWidth' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, destinationWidth) == 4, "SharpCompute: offset of '__Constants.destinationWidth' does not match its C# layout");

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

kernel void SharpCompute_Samples_Shooter_Rendering_Downsample(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int((__tid.y * 2u * uint(__c.sourceWidth)) + (__tid.x * 2u));
    destination[int((__tid.y * uint(__c.destinationWidth)) + __tid.x)] = (source[i] + source[int(uint(i) + 1u)] + source[int(uint(i) + uint(__c.sourceWidth))] + source[int(uint(i) + uint(__c.sourceWidth) + 1u)]) * 0.25f;
}
