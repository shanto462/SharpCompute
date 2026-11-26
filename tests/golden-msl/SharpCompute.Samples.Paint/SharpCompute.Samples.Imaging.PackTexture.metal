// SharpCompute.Samples.Imaging.PackTexture (SharpCompute.Samples.Paint): entry point SharpCompute_Samples_Imaging_PackTexture, compile options None
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

kernel void SharpCompute_Samples_Imaging_PackTexture(
    texture2d<float, access::read_write> image [[texture(0)]],
    device packed_float4* pixels [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    float4 p = pixels[int((__tid.y * uint(int(image.get_width()))) + __tid.x)];
    float a = metal::saturate(p.w);
    if (metal::isnan(p.w) || a < 0.5f / 255.0f)
    {
        image.write(float4(0.0f, 0.0f, 0.0f, 0.0f), uint2(int2(__tid.xy)));
        return;
    }
    image.write(float4(metal::saturate(p.xyz / a), a), uint2(int2(__tid.xy)));
}
