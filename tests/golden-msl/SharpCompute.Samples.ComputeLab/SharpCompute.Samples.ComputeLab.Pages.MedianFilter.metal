// SharpCompute.Samples.ComputeLab.Pages.MedianFilter (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_MedianFilter, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int size;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 0, "SharpCompute: offset of '__Constants.size' does not match its C# layout");

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

static void Order(thread float3& a, thread float3& b);
static void MedianOfNine(thread float3* p, int __len_p);

static void Order(thread float3& a, thread float3& b)
{
    float3 low = metal::min(a, b);
    b = metal::max(a, b);
    a = low;
}

static void MedianOfNine(thread float3* p, int __len_p)
{
    ::Order(p[1], p[2]);
    ::Order(p[4], p[5]);
    ::Order(p[7], p[8]);
    ::Order(p[0], p[1]);
    ::Order(p[3], p[4]);
    ::Order(p[6], p[7]);
    ::Order(p[1], p[2]);
    ::Order(p[4], p[5]);
    ::Order(p[7], p[8]);
    ::Order(p[0], p[3]);
    ::Order(p[5], p[8]);
    ::Order(p[4], p[7]);
    ::Order(p[3], p[6]);
    ::Order(p[1], p[4]);
    ::Order(p[2], p[5]);
    ::Order(p[4], p[7]);
    ::Order(p[4], p[2]);
    ::Order(p[6], p[4]);
    ::Order(p[4], p[2]);
}

kernel void SharpCompute_Samples_ComputeLab_Pages_MedianFilter(
    device uint* source [[buffer(0)]],
    device uint* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float3 window[9] = {};
    int n = 0;
    for (int dy = -1; dy <= 1; dy = int(uint(dy) + 1u))
    {
        for (int dx = -1; dx <= 1; dx = int(uint(dx) + 1u))
        {
            int i = int((uint(metal::clamp(int(uint(y) + uint(dy)), 0, int(uint(__c.size) - 1u))) * uint(__c.size)) + uint(metal::clamp(int(uint(x) + uint(dx)), 0, int(uint(__c.size) - 1u))));
            window[n] = metal::unpack_unorm4x8_to_float(source[i]).xyz;
            n = int(uint(n) + 1u);
        }
    }
    ::MedianOfNine(window, 9);
    destination[int((uint(y) * uint(__c.size)) + uint(x))] = metal::pack_float_to_unorm4x8(float4(window[4], 1.0f));
}
