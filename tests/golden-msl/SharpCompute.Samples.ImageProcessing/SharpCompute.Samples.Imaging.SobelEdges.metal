// SharpCompute.Samples.Imaging.SobelEdges (SharpCompute.Samples.ImageProcessing): entry point SharpCompute_Samples_Imaging_SobelEdges, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int width;
    int height;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");

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

static float Luma(float4 color);

static float Luma(float4 color)
{
    return (0.2126f * color.x) + (0.7152f * color.y) + (0.0722f * color.z);
}

kernel void SharpCompute_Samples_Imaging_SobelEdges(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float gx = 0.0f;
    float gy = 0.0f;
    for (int j = -1; j <= 1; j = int(uint(j) + 1u))
    {
        int sy = metal::clamp(int(uint(y) + uint(j)), 0, int(uint(__c.height) - 1u));
        for (int i = -1; i <= 1; i = int(uint(i) + 1u))
        {
            float luma = ::Luma(source[int((uint(sy) * uint(__c.width)) + uint(metal::clamp(int(uint(x) + uint(i)), 0, int(uint(__c.width) - 1u))))]);
            gx += luma * float(int(uint(i) * uint(j == 0 ? 2 : 1)));
            gy += luma * float(int(uint(j) * uint(i == 0 ? 2 : 1)));
        }
    }
    float magnitude = metal::saturate(metal::sqrt((gx * gx) + (gy * gy)));
    destination[int((uint(y) * uint(__c.width)) + uint(x))] = float4(magnitude, magnitude, magnitude, 1.0f);
}
