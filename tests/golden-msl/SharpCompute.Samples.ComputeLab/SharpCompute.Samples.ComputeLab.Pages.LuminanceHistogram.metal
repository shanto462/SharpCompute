// SharpCompute.Samples.ComputeLab.Pages.LuminanceHistogram (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_LuminanceHistogram, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int shift;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, shift) == 0, "SharpCompute: offset of '__Constants.shift' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_LuminanceHistogram(
    device uint* image [[buffer(0)]],
    device int* counts [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    uint pixel = image[int(__tid.x)];
    uint luminance = (((pixel & 255u) * 54u) + (((pixel >> 8) & 255u) * 183u) + (((pixel >> 16) & 255u) * 19u)) >> 8;
    metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(counts[int(luminance >> __c.shift)]), 1, metal::memory_order_relaxed);
}
