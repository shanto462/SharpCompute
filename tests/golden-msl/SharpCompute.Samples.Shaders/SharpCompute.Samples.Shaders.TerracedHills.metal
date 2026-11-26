// SharpCompute.Samples.Shaders.TerracedHills (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_TerracedHills, compile options None
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

static float2x2 Rot2(float th);
static float2 Tri(float2 x);
static float HLayer(float2 p);
static float Tri_1(float x);
static float HMap(float2 p);
static float Map(float3 p);
static float3 Normal(float3 p, thread float& edge, float dispatchHeight);

static float2x2 Rot2(float th)
{
    float2 a = metal::sin(float2(1.5707963f, 0.0f) + th);
    return float2x2(a.x, -a.y, a.y, a.x);
}

static float2 Tri(float2 x)
{
    return metal::abs(x - metal::floor(x) - 0.5f);
}

static float HLayer(float2 p)
{
    return metal::dot(::Tri((p / 1.5f) + ::Tri((p.yx / 3.0f) + 0.25f)), float2(1.0f));
}

static float Tri_1(float x)
{
    return metal::abs(x - metal::floor(x) - 0.5f);
}

static float HMap(float2 p)
{
    float ret = 0.0f;
    float a = 1.0f;
    for (int i = 0; i < 3; i = int(uint(i) + 1u))
    {
        ret += metal::abs(a) * ::HLayer(p / a);
        p = (float2(p) * float2x2(0.866025f, -0.57735f, 0.57735f, 0.866025f));
        a *= -0.3f;
    }
    ret = metal::smoothstep(-0.2f, 1.0f, ret * ret / 1.39f / 1.39f);
    return (ret * 0.975f) + (::Tri_1(ret * 12.0f) * 0.05f);
}

static float Map(float3 p)
{
    return (p.y - (::HMap(p.xz) * 0.35f)) * 0.75f;
}

static float3 Normal(float3 p, thread float& edge, float dispatchHeight)
{
    float2 e = float2(-1.0f, 1.0f) * 0.5f / dispatchHeight;
    float d1 = ::Map(p + e.yxx);
    float d2 = ::Map(p + e.xxy);
    float d3 = ::Map(p + e.xyx);
    float d4 = ::Map(p + e.yyy);
    float d = ::Map(p);
    edge = metal::abs(d1 + d2 + d3 + d4 - (d * 4.0f));
    edge = metal::smoothstep(0.0f, 1.0f, metal::sqrt(edge / e.y * 2.0f));
    e = float2(-1.0f, 1.0f) * 0.001f;
    d1 = ::Map(p + e.yxx);
    d2 = ::Map(p + e.xxy);
    d3 = ::Map(p + e.xyx);
    d4 = ::Map(p + e.yyy);
    return metal::normalize((e.yxx * d1) + (e.xxy * d2) + (e.xyx * d3) + (e.yyy * d4));
}

kernel void SharpCompute_Samples_Shaders_TerracedHills(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int2 coordinate = int2(int(__tid.x), int(__grid.y - __tid.y));
    float3 rd = metal::normalize(float3(float2(coordinate) - (float(int(__grid.y)) * 0.5f), float(int(__grid.y))));
    rd.yz = (float2(rd.yz) * ::Rot2(0.35f));
    float3 ro = float3(__c.time * 0.4f, 0.5f, __c.time * 0.2f);
    float t = 0.0f;
    float d;
    for (int i = 0; i < 96; i = int(uint(i) + 1u))
    {
        d = ::Map(ro + (rd * t));
        if (metal::abs(d) < 0.001f * ((t * 0.125f) + 1.0f) || t > 20.0f)
        {
            break;
        }
        t += ((metal::step(1.0f, t) * 0.3f) + 0.7f) * d;
    }
    float3 sp = ro + (rd * t);
    float3 ld = float3(-0.676f, 0.408f, 0.613f);
    float edge = 0.0f;
    float3 n = ::Normal(sp, edge, float(int(__grid.y)));
    float dif = metal::max(metal::dot(ld, n), 0.0f);
    float spe = metal::pow(metal::max(metal::dot(metal::reflect(rd, n), ld), 0.0f), 16.0f);
    float sh = ::HMap(sp.xz);
    float rnd = (metal::fract(metal::sin(metal::dot(metal::floor(sp.xz * 512.0f), float2(41.73f, 289.67f))) * 43758.547f) * 0.5f) + 0.5f;
    float3 fog = metal::mix(float3(0.75f, 0.77f, 0.78f), float3(1.04f, 0.95f, 0.87f), metal::pow(1.0f + metal::dot(rd, ld), 3.0f) * 0.35f);
    float3 c = metal::mix(((float3(1.1f, 1.05f, 1.0f) * rnd * (dif + 0.1f) * sh) + (fog * spe)) * (1.0f - (edge * 0.7f)), fog * fog, metal::min(1.0f, t * 0.3f));
    __output.write(float4(c, 1.0f), __tid.xy);
    return;
}
