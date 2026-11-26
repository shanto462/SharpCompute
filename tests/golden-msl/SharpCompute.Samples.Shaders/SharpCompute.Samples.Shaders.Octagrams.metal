// SharpCompute.Samples.Shaders.Octagrams (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_Octagrams, compile options None
#include <metal_stdlib>

using namespace metal;

struct __Constants
{
    float time;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 0, "SharpCompute: offset of '__Constants.time' does not match its C# layout");

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

static float2x2 Rotate(float a);
static float3 Execute_Mod(float3 x, float y);
static float Box_SDBox(float3 p, float3 b);
static float Box(float3 pos, float scale);
static float BoxSet(float3 pos, float gTime);

static float2x2 Rotate(float a)
{
    float c = metal::cos(a);
    float s = metal::sin(a);
    return float2x2(c, s, -s, c);
}

static float3 Execute_Mod(float3 x, float y)
{
    return x - (y * metal::floor(x / y));
}

static float Box_SDBox(float3 p, float3 b)
{
    float3 q = metal::abs(p) - b;
    return metal::length(metal::max(q, 0.0f)) + metal::min(metal::max(q.x, metal::max(q.y, q.z)), 0.0f);
}

static float Box(float3 pos, float scale)
{
    pos *= scale;
    float b = ::Box_SDBox(pos, float3(0.4f, 0.4f, 0.1f)) / 1.5f;
    pos.xy *= 5.0f;
    pos.y -= 3.5f;
    pos.xy = (::Rotate(0.75f) * float2(pos.xy));
    return -b;
}

static float BoxSet(float3 pos, float gTime)
{
    float3 pos_origin = pos;
    pos = pos_origin;
    pos.y += metal::sin(gTime * 0.4f) * 2.5f;
    pos.xy = (::Rotate(0.8f) * float2(pos.xy));
    float box1 = ::Box(pos, 2.0f - (metal::abs(metal::sin(gTime * 0.4f)) * 1.5f));
    pos = pos_origin;
    pos.y -= metal::sin(gTime * 0.4f) * 2.5f;
    pos.xy = (::Rotate(0.8f) * float2(pos.xy));
    float box2 = ::Box(pos, 2.0f - (metal::abs(metal::sin(gTime * 0.4f)) * 1.5f));
    pos = pos_origin;
    pos.x += metal::sin(gTime * 0.4f) * 2.5f;
    pos.xy = (::Rotate(0.8f) * float2(pos.xy));
    float box3 = ::Box(pos, 2.0f - (metal::abs(metal::sin(gTime * 0.4f)) * 1.5f));
    pos = pos_origin;
    pos.x -= metal::sin(gTime * 0.4f) * 2.5f;
    pos.xy = (::Rotate(0.8f) * float2(pos.xy));
    float box4 = ::Box(pos, 2.0f - (metal::abs(metal::sin(gTime * 0.4f)) * 1.5f));
    pos = pos_origin;
    pos.xy = (::Rotate(0.8f) * float2(pos.xy));
    float box5 = ::Box(pos, 0.5f) * 6.0f;
    pos = pos_origin;
    float box6 = ::Box(pos, 0.5f) * 6.0f;
    float result = metal::max(metal::max(metal::max(metal::max(metal::max(box1, box2), box3), box4), box5), box6);
    return result;
}

kernel void SharpCompute_Samples_Shaders_Octagrams(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 p = ((float2(int2(__tid.xy)) * 2.0f) - float2(int2(__grid.xy))) / float(metal::min(int(__grid.x), int(__grid.y)));
    float3 ro = float3(0.0f, -0.2f, __c.time * 4.0f);
    float3 ray = metal::normalize(float3(p, 1.5f));
    ray.xy = (::Rotate(metal::sin(__c.time * 0.03f) * 5.0f) * float2(ray.xy));
    ray.yz = (::Rotate(metal::sin(__c.time * 0.05f) * 0.2f) * float2(ray.yz));
    float t = 0.1f;
    float ac = 0.0f;
    for (int i = 0; i < 99; i = int(uint(i) + 1u))
    {
        float3 pos = ro + (ray * t);
        pos = ::Execute_Mod(pos - 2.0f, 4.0f) - 2.0f;
        float gTime = __c.time - (float(i) * 0.01f);
        float d = ::BoxSet(pos, gTime);
        d = metal::max(metal::abs(d), 0.01f);
        ac += metal::exp(-d * 23.0f);
        t += d * 0.55f;
    }
    float3 col = (ac * 0.02f) + float3(0.0f, 0.2f * metal::abs(metal::sin(__c.time)), 0.5f + (metal::sin(__c.time) * 0.2f));
    float4 color = float4(col, 1.0f);
    __output.write(color, __tid.xy);
    return;
}
