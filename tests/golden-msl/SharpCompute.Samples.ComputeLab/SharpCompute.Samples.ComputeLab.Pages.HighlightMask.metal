// SharpCompute.Samples.ComputeLab.Pages.HighlightMask (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_HighlightMask, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");

struct __Constants
{
    packed_half3 threshold;
};
static_assert(sizeof(__Constants) == 6, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, threshold) == 0, "SharpCompute: offset of '__Constants.threshold' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_HighlightMask(
    device packed_half4* blurred [[buffer(0)]],
    device uint* highlights [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    half3 color = blurred[i].xyz;
    bool3 bright = color > __c.threshold;
    half3 kept = metal::select(half3(half(0.0f)), color, bright);
    if (metal::all(bright))
    {
        kept = half3(half(1.0f));
    }
    highlights[i] = metal::pack_float_to_unorm4x8(float4(float3(kept), metal::any(bright) ? 1.0f : 0.0f));
}
