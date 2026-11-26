// SharpCompute.Samples.ComputeLab.Pages.TentBlurFloat (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_TentBlurFloat, compile options NoFpContract
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int stride;
    int radius;
    int originX;
    int originY;
    int outputWidth;
};
static_assert(sizeof(__Constants) == 20, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, stride) == 0, "SharpCompute: offset of '__Constants.stride' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radius) == 4, "SharpCompute: offset of '__Constants.radius' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, originX) == 8, "SharpCompute: offset of '__Constants.originX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, originY) == 12, "SharpCompute: offset of '__Constants.originY' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, outputWidth) == 16, "SharpCompute: offset of '__Constants.outputWidth' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_TentBlurFloat(
    const device packed_float4* image [[buffer(0)]],
    device packed_float4* output [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int first = int(uint(8) - uint(__c.radius));
    int x = int(uint(__c.originX) + __tid.x + uint(first));
    int y = int(uint(__c.originY) + __tid.y + uint(first));
    float4 sum = float4(0.0f);
    float total = 0.0f;
    for (int dy = 0; dy <= int(2u * uint(__c.radius)); dy = int(uint(dy) + 1u))
    {
        int row = int(((uint(y) + uint(dy)) * uint(__c.stride)) + uint(x));
        float wy = float(int(uint(__c.radius) + 1u - uint(int(metal::abs(int(uint(dy) - uint(__c.radius)))))));
        for (int dx = 0; dx <= int(2u * uint(__c.radius)); dx = int(uint(dx) + 1u))
        {
            float w = wy * float(int(uint(__c.radius) + 1u - uint(int(metal::abs(int(uint(dx) - uint(__c.radius)))))));
            sum += image[int(uint(row) + uint(dx))] * w;
            total += w;
        }
    }
    output[int((__tid.y * uint(__c.outputWidth)) + __tid.x)] = sum / total;
}
