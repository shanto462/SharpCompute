// SharpCompute.Tests.EndToEnd.Kitchen (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Kitchen, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { uint u = uint(x); return int(u | (((u - 1u) & ~u) >> 31)); }

struct __Constants
{
    float scale;
    int bias;
    uint mask;
    uint __length_data;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, scale) == 0, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, bias) == 4, "SharpCompute: offset of '__Constants.bias' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, mask) == 8, "SharpCompute: offset of '__Constants.mask' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __length_data) == 12, "SharpCompute: offset of '__Constants.__length_data' does not match its C# layout");

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

static float Poly(float v, float k);

static float Poly(float v, float k)
{
    float acc = 0.0f;
    for (int i = 0; i < 4; i = int(uint(i) + 1u))
    {
        acc = acc * v + k;
        if (acc > 100.0f)
        {
            break;
        }
    }
    return acc;
}

kernel void SharpCompute_Tests_EndToEnd_Kitchen(
    device float* data [[buffer(0)]],
    const device int* ints [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int n = int(__c.__length_data);
    float v = data[i];
    float r = ::Poly(v * 0.25f, __c.scale) + (metal::fmod(v, 3.0f)) + metal::floor(v * 0.5f) + metal::clamp(v, 1.0f, 5.0f) + metal::sqrt(metal::abs(v));
    int j = (int(uint(ints[(int(uint(i) + 1u)) % __ms_nonzero(n)]) + uint(__c.bias))) / 2;
    uint u = (uint(j) ^ __c.mask) >> 1;
    int w = 0;
    while (w < 3)
    {
        w = int(uint(w) + 1u);
        if (w == 2)
        {
            continue;
        }
        r += 1.0f;
    }
    do
    {
        r -= 0.5f;
        w = int(uint(w) - 1u);
    }
    while (w > 0);
    data[i] = r + float(j) + float(u) + (i == int(uint(n) - 1u) ? 1000.0f : 0.0f);
}
