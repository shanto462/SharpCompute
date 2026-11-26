// SharpCompute.Samples.Viewer.Demos.NBodyTonemap (SharpCompute.Samples.Viewer): entry point SharpCompute_Samples_Viewer_Demos_NBodyTonemap, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline uint3 __ms_sat_uint3(float3 x) { float3 y = metal::select(float3(0.0f), x, x > 0.0f); uint3 r = uint3(metal::min(y, float3(4294967040.0f))); return metal::select(r, uint3(4294967295u), x >= 4294967296.0f); }
inline uint3 __ms_sat_uint3(half3 x) { return __ms_sat_uint3(float3(x)); }

struct __Constants
{
    float exposure;
    uint decay;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, exposure) == 0, "SharpCompute: offset of '__Constants.exposure' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, decay) == 4, "SharpCompute: offset of '__Constants.decay' does not match its C# layout");

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

kernel void SharpCompute_Samples_Viewer_Demos_NBodyTonemap(
    device uint* accumulation [[buffer(0)]],
    device uint* pixels [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    uint value = accumulation[i];
    float v = float(value) * __c.exposure;
    float3 color = float3(1.0f, 1.0f, 1.0f) - metal::exp(float3(-0.45f, -0.7f, -1.1f) * v);
    color += float3(0.01f, 0.012f, 0.025f);
    uint3 c = __ms_sat_uint3((metal::saturate(color) * 255.0f) + float3(0.5f, 0.5f, 0.5f));
    pixels[i] = c.x | (c.y << 8) | (c.z << 16) | (255u << 24);
    accumulation[i] = (value >> 8) * __c.decay;
}
