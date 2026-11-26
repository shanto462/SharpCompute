// SharpCompute.Samples.Imaging.BokehBlur (SharpCompute.Samples.Paint): entry point SharpCompute_Samples_Imaging_BokehBlur, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int width;
    int height;
    float radius;
    float highlightGain;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radius) == 8, "SharpCompute: offset of '__Constants.radius' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, highlightGain) == 12, "SharpCompute: offset of '__Constants.highlightGain' does not match its C# layout");

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

static float DiscWeight(int dx, int dy, float radius);
static float HighlightWeight(float4 sample, float gain);

static float DiscWeight(int dx, int dy, float radius)
{
    return metal::saturate(radius + 0.5f - metal::sqrt(float(int((uint(dx) * uint(dx)) + (uint(dy) * uint(dy))))));
}

static float HighlightWeight(float4 sample, float gain)
{
    float luma = (0.2126f * sample.x) + (0.7152f * sample.y) + (0.0722f * sample.z);
    float over = metal::max(luma - 0.75f, 0.0f) * 4.0f;
    return 1.0f + (gain * over * over);
}

kernel void SharpCompute_Samples_Imaging_BokehBlur(
    device packed_float4* source [[buffer(0)]],
    device packed_float4* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    int reach = __ms_sat_int(metal::ceil(__c.radius));
    float4 sum = float4(0.0f, 0.0f, 0.0f, 0.0f);
    float total = 0.0f;
    for (int dy = int(-uint(reach)); dy <= reach; dy = int(uint(dy) + 1u))
    {
        int sy = metal::clamp(int(uint(y) + uint(dy)), 0, int(uint(__c.height) - 1u));
        for (int dx = int(-uint(reach)); dx <= reach; dx = int(uint(dx) + 1u))
        {
            float weight = ::DiscWeight(dx, dy, __c.radius);
            if (weight > 0.0f)
            {
                float4 sample = source[int((uint(sy) * uint(__c.width)) + uint(metal::clamp(int(uint(x) + uint(dx)), 0, int(uint(__c.width) - 1u))))];
                weight *= ::HighlightWeight(sample, __c.highlightGain);
                sum += sample * weight;
                total += weight;
            }
        }
    }
    destination[int((uint(y) * uint(__c.width)) + uint(x))] = sum / total;
}
