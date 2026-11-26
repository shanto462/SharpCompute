// SharpCompute.Samples.Viewer.Demos.NBodySplat (SharpCompute.Samples.Viewer): entry point SharpCompute_Samples_Viewer_Demos_NBodySplat, compile options None
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
    float yaw;
    float pitch;
    float zoom;
    uint weight;
};
static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, yaw) == 8, "SharpCompute: offset of '__Constants.yaw' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, pitch) == 12, "SharpCompute: offset of '__Constants.pitch' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, zoom) == 16, "SharpCompute: offset of '__Constants.zoom' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, weight) == 20, "SharpCompute: offset of '__Constants.weight' does not match its C# layout");

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

kernel void SharpCompute_Samples_Viewer_Demos_NBodySplat(
    device packed_float4* positions [[buffer(0)]],
    device uint* accumulation [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    float3 p = positions[int(__tid.x)].xyz;
    float cy = metal::cos(__c.yaw);
    float sy = metal::sin(__c.yaw);
    float x = (cy * p.x) + (sy * p.z);
    float z = (cy * p.z) - (sy * p.x);
    float cp = metal::cos(__c.pitch);
    float sp = metal::sin(__c.pitch);
    float y = (cp * p.y) - (sp * z);
    float scale = __c.zoom * float(__c.height);
    int px = __ms_sat_int(metal::floor((float(__c.width) * 0.5f) + (x * scale)));
    int py = __ms_sat_int(metal::floor((float(__c.height) * 0.5f) - (y * scale)));
    if (px >= 0 && px < __c.width && py >= 0 && py < __c.height)
    {
        accumulation[int((uint(py) * uint(__c.width)) + uint(px))] += __c.weight;
    }
}
