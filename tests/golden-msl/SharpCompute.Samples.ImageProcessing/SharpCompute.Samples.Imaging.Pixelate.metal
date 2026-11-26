// SharpCompute.Samples.Imaging.Pixelate (SharpCompute.Samples.ImageProcessing): entry point SharpCompute_Samples_Imaging_Pixelate, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return x | int(metal::reverse_bits(x) == int(0)); }

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int width;
    int height;
    int size;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 8, "SharpCompute: offset of '__Constants.size' does not match its C# layout");

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

kernel void SharpCompute_Samples_Imaging_Pixelate(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    int x0 = int(uint(x / __ms_nonzero(__c.size)) * uint(__c.size));
    int y0 = int(uint(y / __ms_nonzero(__c.size)) * uint(__c.size));
    int x1 = metal::min(int(uint(x0) + uint(__c.size)), __c.width);
    int y1 = metal::min(int(uint(y0) + uint(__c.size)), __c.height);
    float4 sum = float4(0.0f, 0.0f, 0.0f, 0.0f);
    for (int sy = y0; sy < y1; sy = int(uint(sy) + 1u))
    {
        for (int sx = x0; sx < x1; sx = int(uint(sx) + 1u))
        {
            sum += source[int((uint(sy) * uint(__c.width)) + uint(sx))];
        }
    }
    destination[int((uint(y) * uint(__c.width)) + uint(x))] = sum / float(int((uint(x1) - uint(x0)) * (uint(y1) - uint(y0))));
}
