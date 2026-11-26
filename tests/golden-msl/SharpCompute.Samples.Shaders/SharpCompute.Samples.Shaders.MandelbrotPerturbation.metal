// SharpCompute.Samples.Shaders.MandelbrotPerturbation (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_MandelbrotPerturbation, compile options None
#include <metal_stdlib>

using namespace metal;

constant float Bailout = 256.0f;

static_assert(sizeof(packed_float2) == 8, "SharpCompute: size of 'packed_float2' does not match its C# layout");
static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct __Constants
{
    int width;
    int height;
    int referenceLength;
    int maxIterations;
    float pixelScale;
    int skip;
    packed_float2 seriesA;
    packed_float2 seriesB;
    packed_float2 seriesC;
};
static_assert(sizeof(__Constants) == 48, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, referenceLength) == 8, "SharpCompute: offset of '__Constants.referenceLength' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, maxIterations) == 12, "SharpCompute: offset of '__Constants.maxIterations' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, pixelScale) == 16, "SharpCompute: offset of '__Constants.pixelScale' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, skip) == 20, "SharpCompute: offset of '__Constants.skip' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, seriesA) == 24, "SharpCompute: offset of '__Constants.seriesA' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, seriesB) == 32, "SharpCompute: offset of '__Constants.seriesB' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, seriesC) == 40, "SharpCompute: offset of '__Constants.seriesC' does not match its C# layout");

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

static float2 ComplexMul(float2 a, float2 b);

static float2 ComplexMul(float2 a, float2 b)
{
    return float2((a.x * b.x) - (a.y * b.y), (a.x * b.y) + (a.y * b.x));
}

kernel void SharpCompute_Samples_Shaders_MandelbrotPerturbation(
    const device packed_float2* reference [[buffer(0)]],
    device packed_float4* iterations [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int px = int(__tid.x);
    int py = int(__tid.y);
    float2 u = float2(float(px) - (float(int(uint(__c.width) - 1u)) * 0.5f), (float(int(uint(__c.height) - 1u)) * 0.5f) - float(py));
    float2 dc = u * __c.pixelScale;
    float2 dz = ::ComplexMul(::ComplexMul(::ComplexMul(__c.seriesC, u) + __c.seriesB, u) + __c.seriesA, u);
    int referenceIndex = __c.skip;
    int n = __c.skip;
    float zz = 0.0f;
    float trapZz = ::Bailout;
    int trapN = n;
    float2 trapZ = float2(0.0f, 0.0f);
    while (n < __c.maxIterations)
    {
        float2 t = (2.0f * reference[referenceIndex]) + dz;
        dz = ::ComplexMul(t, dz) + dc;
        referenceIndex = int(uint(referenceIndex) + 1u);
        n = int(uint(n) + 1u);
        float2 z = reference[referenceIndex] + dz;
        zz = metal::dot(z, z);
        if (zz > ::Bailout)
        {
            break;
        }
        if (zz < trapZz)
        {
            trapZz = zz;
            trapN = n;
            trapZ = z;
        }
        if (zz < metal::dot(dz, dz) || referenceIndex == int(uint(__c.referenceLength) - 1u))
        {
            dz = z;
            referenceIndex = 0;
        }
    }
    float4 result = float4(-1.0f, 0.5f * metal::log2(metal::max(trapZz, 1E-37f)), float(trapN), metal::atan2(trapZ.y, trapZ.x) * 0.15915494f);
    if (zz > ::Bailout)
    {
        result = float4(float(n), float(n) + 1.0f - metal::log2(0.5f * metal::log2(zz)), 0.0f, 0.0f);
    }
    iterations[int((uint(py) * uint(__c.width)) + uint(px))] = result;
}
