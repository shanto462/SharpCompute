// SharpCompute.Samples.Shooter.Rendering.UpscaleFrame (SharpCompute.Samples.Shooter): entry point SharpCompute_Samples_Shooter_Rendering_UpscaleFrame, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int sourceWidth;
    int sourceHeight;
    int scale;
    int originX;
    int originY;
};
static_assert(sizeof(__Constants) == 20, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, sourceWidth) == 0, "SharpCompute: offset of '__Constants.sourceWidth' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, sourceHeight) == 4, "SharpCompute: offset of '__Constants.sourceHeight' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, scale) == 8, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, originX) == 12, "SharpCompute: offset of '__Constants.originX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, originY) == 16, "SharpCompute: offset of '__Constants.originY' does not match its C# layout");

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

static float3 PixelShading_UnpackRgb(uint pixel);

static float3 PixelShading_UnpackRgb(uint pixel)
{
    return float3(float(pixel & 255u) / 255.0f, float((pixel >> 8) & 255u) / 255.0f, float((pixel >> 16) & 255u) / 255.0f);
}

kernel void SharpCompute_Samples_Shooter_Rendering_UpscaleFrame(
    texture2d<float, access::write> __output [[texture(0)]],
    device uint* source [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int x = int(__tid.x - uint(__c.originX));
    int y = int(__tid.y - uint(__c.originY));
    if (x < 0 || y < 0)
    {
        __output.write(float4(0.0f, 0.0f, 0.0f, 1.0f), __tid.xy);
        return;
    }
    int sx = x / __c.scale;
    int sy = y / __c.scale;
    if (sx >= __c.sourceWidth || sy >= __c.sourceHeight)
    {
        __output.write(float4(0.0f, 0.0f, 0.0f, 1.0f), __tid.xy);
        return;
    }
    __output.write(float4(::PixelShading_UnpackRgb(source[int((uint(sy) * uint(__c.sourceWidth)) + uint(sx))]), 1.0f), __tid.xy);
    return;
}
