// SharpCompute.Tests.EndToEnd.Vectors (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Vectors, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");
static_assert(sizeof(packed_float2) == 8, "SharpCompute: size of 'packed_float2' does not match its C# layout");

struct __Constants
{
    packed_float4 tint;
    packed_int2 shift;
};
static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, tint) == 0, "SharpCompute: offset of '__Constants.tint' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, shift) == 16, "SharpCompute: offset of '__Constants.shift' does not match its C# layout");

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

kernel void SharpCompute_Tests_EndToEnd_Vectors(
    device packed_float3* points [[buffer(0)]],
    const device packed_float2* uv [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float3 p = points[i];
    float2 t = uv[i] * 2.0f - float2(1.0f, 0.5f);
    p.xy = p.yx + t;
    p.z = metal::dot(p, float3(__c.tint.x, __c.tint.y, __c.tint.z)) + __c.tint.w;
    int2 s = __c.shift << 1;
    p += float3(float(s.x), float(s.y), 0.0f);
    float3 q = (metal::fmod(p, float3(4.0f, 4.0f, 4.0f))) * 0.5f;
    points[i] = -q + metal::max(p, float3(0.0f));
    points[i].x += 1.0f;
}
