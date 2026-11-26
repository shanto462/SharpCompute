// SharpCompute.Samples.Shooter.Rendering.BloomPrefilter (SharpCompute.Samples.Shooter): entry point SharpCompute_Samples_Shooter_Rendering_BloomPrefilter, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int sourceWidth;
    int destinationWidth;
    float threshold;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, sourceWidth) == 0, "SharpCompute: offset of '__Constants.sourceWidth' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, destinationWidth) == 4, "SharpCompute: offset of '__Constants.destinationWidth' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, threshold) == 8, "SharpCompute: offset of '__Constants.threshold' does not match its C# layout");

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

kernel void SharpCompute_Samples_Shooter_Rendering_BloomPrefilter(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int sx = int(__tid.x * 2u);
    int sy = int(__tid.y * 2u);
    int i = int((uint(sy) * uint(__c.sourceWidth)) + uint(sx));
    float3 c = (source[i].xyz + source[int(uint(i) + 1u)].xyz + source[int(uint(i) + uint(__c.sourceWidth))].xyz + source[int(uint(i) + uint(__c.sourceWidth) + 1u)].xyz) * 0.25f;
    float peak = metal::max(c.x, metal::max(c.y, c.z));
    float keep = metal::saturate((peak - __c.threshold) / metal::max(peak, 0.0001f));
    destination[int((__tid.y * uint(__c.destinationWidth)) + __tid.x)] = float4(c * keep, 1.0f);
}
