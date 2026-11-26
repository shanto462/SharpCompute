// SharpCompute.Samples.Imaging.UnsharpMask (SharpCompute.Samples.ImageProcessing): entry point SharpCompute_Samples_Imaging_UnsharpMask, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    float amount;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, amount) == 0, "SharpCompute: offset of '__Constants.amount' does not match its C# layout");

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

static float4 Sharpen(float4 __local_source, float4 __local_blurred, float amount);

static float4 Sharpen(float4 __local_source, float4 __local_blurred, float amount)
{
    float3 color = __local_source.xyz + ((__local_source.xyz - __local_blurred.xyz) * amount);
    return float4(metal::clamp(color, 0.0f, __local_source.w), __local_source.w);
}

kernel void SharpCompute_Samples_Imaging_UnsharpMask(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* blurred [[buffer(1)]],
    device packed_float4* destination [[buffer(2)]],
    constant __Constants& __c [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    destination[int(__tid.x)] = ::Sharpen(source[int(__tid.x)], blurred[int(__tid.x)], __c.amount);
}
