// SharpCompute.Samples.ComputeLab.Pages.WaveFieldFast (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_WaveFieldFast, compile options FastMath
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    int size;
    float time;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, size) == 0, "SharpCompute: offset of '__Constants.size' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 4, "SharpCompute: offset of '__Constants.time' does not match its C# layout");

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

static float WaveMath_Value(int x, int y, int size, float time);

static float WaveMath_Value(int x, int y, int size, float time)
{
    float u = (float(x) * 6.0f) / float(size);
    float v = (float(y) * 6.0f) / float(size);
    float sum = 2.0f;
    for (int k = 1; k <= 12; k = int(uint(k) + 1u))
    {
        float f = float(k) * 1.7f;
        float wave = metal::sin((u * f) + time) * metal::cos((v * f) - (time * 0.7f));
        sum += wave / float(k);
        sum += metal::exp(-metal::abs(metal::sin((u - v) * f))) * 0.08f;
        sum += metal::pow(metal::abs(metal::cos(f * (u + v))) + 0.001f, 1.5f) * 0.05f;
    }
    return sum;
}

kernel void SharpCompute_Samples_ComputeLab_Pages_WaveFieldFast(
    device float* field [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    field[int((__tid.y * uint(__c.size)) + __tid.x)] = ::WaveMath_Value(int(__tid.x), int(__tid.y), __c.size, __c.time);
}
