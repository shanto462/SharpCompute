// SharpCompute.Samples.ComputeLab.Pages.BuildEqualisationLut (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_BuildEqualisationLut, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline ulong __ms_nonzero(ulong x) { return __builtin_constant_p(x) ? x : x | ulong(metal::reverse_bits(x) == ulong(0)); }

struct __Constants
{
    uint total;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, total) == 0, "SharpCompute: offset of '__Constants.total' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_BuildEqualisationLut(
    device uint* counts [[buffer(0)]],
    device uchar* lut [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int level = int(__tid.x);
    uint below = 0u;
    uint lowest = 0u;
    for (int k = 0; k < 256; k = int(uint(k) + 1u))
    {
        uint count = counts[k];
        if (k <= level)
        {
            below += count;
        }
        if (lowest == 0u)
        {
            lowest = count;
        }
    }
    uint range = __c.total - lowest;
    if (range == 0u || below <= lowest)
    {
        lut[level] = uchar(0);
    }
    else
    {
        ulong scaled = (ulong(below - lowest) * 255UL) + ulong(range / 2u);
        lut[level] = uchar(scaled / __ms_nonzero(ulong(range)));
    }
}
