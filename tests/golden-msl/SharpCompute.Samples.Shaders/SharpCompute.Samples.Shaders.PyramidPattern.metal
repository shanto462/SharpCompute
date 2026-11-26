// SharpCompute.Samples.Shaders.PyramidPattern (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_PyramidPattern, compile options None
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

static float2x2 Rotate2x2(float a);
static float Mod(float x, float y);
static float2 Hash22B(float2 p);
static float N2D3G(float2 p);
static float FBM(float2 p);
static float BMap(float2 p, constant __Constants& __c);
static float3 DoBumpMap(float2 p, float3 n, float bumpfactor, thread float& edge, constant __Constants& __c);
static float Hash21(float2 p);
static float DoHatch(float2 p, float res);

static float2x2 Rotate2x2(float a)
{
    float c = metal::cos(a);
    float s = metal::sin(a);
    return float2x2(c, -s, s, c);
}

static float Mod(float x, float y)
{
    return x - (y * metal::floor(x / y));
}

static float2 Hash22B(float2 p)
{
    float n = metal::sin(metal::dot(p, float2(41.0f, 289.0f)));
    return (metal::fract(float2(262144.0f, 32768.0f) * n) * 2.0f) - 1.0f;
}

static float N2D3G(float2 p)
{
    float2 i = metal::floor(p);
    p -= i;
    float4 v = float4(0);
    v.x = metal::dot(::Hash22B(i), p);
    v.y = metal::dot(::Hash22B(i + float2(1.0f, 0.0f)), p - float2(1.0f, 0.0f));
    v.z = metal::dot(::Hash22B(i + float2(0.0f, 1.0f)), p - float2(0.0f, 1.0f));
    v.w = metal::dot(::Hash22B(i + 1.0f), p - 1.0f);
    p = p * p * (3.0f - (2.0f * p));
    return metal::mix(metal::mix(v.x, v.y, p.x), metal::mix(v.z, v.w, p.x), p.y);
}

static float FBM(float2 p)
{
    return (::N2D3G(p) * 0.66f) + (::N2D3G(p * 2.0f) * 0.34f);
}

static float BMap(float2 p, constant __Constants& __c)
{
    p = (::Rotate2x2(-3.14159f / 5.0f) * float2(p));
    if (::Mod(metal::floor(p.y), 2.0f) < 0.5f)
    {
        p.x += 0.5f;
    }
    float2 ip = metal::floor(p);
    p -= ip + 0.5f;
    float ang = (-3.14159f * 3.0f / 5.0f) + (::FBM((ip / 8.0f) + (__c.time / 3.0f)) * 6.2831f * 2.0f);
    float2 offs = float2(metal::cos(ang), metal::sin(ang)) * 0.35f;
    if (p.x < offs.x)
    {
        p.x = 1.0f - ((p.x + 0.5f) / metal::abs(offs.x + 0.5f));
    }
    else
    {
        p.x = (p.x - offs.x) / (0.5f - offs.x);
    }
    if (p.y < offs.y)
    {
        p.y = 1.0f - ((p.y + 0.5f) / metal::abs(offs.y + 0.5f));
    }
    else
    {
        p.y = (p.y - offs.y) / (0.5f - offs.y);
    }
    return 1.0f - metal::max(p.x, p.y);
}

static float3 DoBumpMap(float2 p, float3 n, float bumpfactor, thread float& edge, constant __Constants& __c)
{
    float2 e = float2(0.025f, 0.0f);
    float f = ::BMap(p, __c);
    float fx = ::BMap(p - e.xy, __c);
    float fy = ::BMap(p - e.yx, __c);
    float fx2 = ::BMap(p + e.xy, __c);
    float fy2 = ::BMap(p + e.yx, __c);
    float3 grad = float3(fx - fx2, fy - fx2, 0.0f) / e.x / 2.0f;
    edge = metal::length(float2(fx, fy) + float2(fx2, fy2) - (f * 2.0f));
    edge = metal::smoothstep(0.0f, 1.0f, edge / e.x);
    grad -= n * metal::dot(n, grad);
    return metal::normalize(n + (grad * bumpfactor));
}

static float Hash21(float2 p)
{
    return metal::fract(metal::sin(metal::dot(p, float2(27.619f, 57.583f))) * 43758.547f);
}

static float DoHatch(float2 p, float res)
{
    p *= res / 16.0f;
    float hatch = metal::clamp((metal::sin((p.x - p.y) * 3.14159f * 200.0f) * 2.0f) + 0.5f, 0.0f, 1.0f);
    float hRnd = ::Hash21(metal::floor(p * 6.0f) + 0.73f);
    if (hRnd > 0.66f)
    {
        hatch = hRnd;
    }
    return hatch;
}

kernel void SharpCompute_Samples_Shaders_PyramidPattern(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int2 coordinate = int2(int(__tid.x), int(__grid.y - __tid.y));
    float iRes = metal::min(float(int(__grid.y)), 800.0f);
    float2 uv = (float2(coordinate) - (float2(int2(__grid.xy)) * 0.5f)) / iRes;
    float3 rd = metal::normalize(float3(uv, 0.5f));
    const float gSc = 10.0f;
    float2 p = (uv * gSc) + float2(0.0f, __c.time / 2.0f);
    float2 oP = p;
    float m = ::BMap(p, __c);
    float3 n = float3(0.0f, 0.0f, -1.0f);
    float edge = 0.0f;
    float bumpFactor = 0.25f;
    n = ::DoBumpMap(p, n, bumpFactor, edge, __c);
    float3 lp = float3(-0.0f + (metal::sin(__c.time) * 0.3f), 0.0f + (metal::cos(__c.time * 1.3f) * 0.3f), -1.0f) - float3(uv, 0.0f);
    float lDist = metal::max(metal::length(lp), 0.001f);
    float3 ld = lp / lDist;
    float diff = metal::max(metal::dot(n, ld), 0.0f);
    diff = metal::pow(diff, 4.0f);
    float spec = metal::pow(metal::max(metal::dot(metal::reflect(-ld, n), -rd), 0.0f), 16.0f);
    float fre = metal::min(metal::pow(metal::max(1.0f + metal::dot(n, rd), 0.0f), 4.0f), 3.0f);
    float3 col = 0.15f * (diff + 0.251f + (spec * float3(1.0f, 0.7f, 0.3f) * 9.0f) + (fre * float3(0.1f, 0.3f, 1.0f) * 12.0f));
    float rf = metal::smoothstep(0.0f, 0.35f, (::BMap(metal::reflect(rd, n).xy * 2.0f, __c) * ::FBM(metal::reflect(rd, n).xy * 3.0f)) + 0.1f);
    col += col * col * rf * rf * float3(1.0f, 0.1f, 0.1f) * 15.0f;
    float shade = (m * 0.83f) + 0.17f;
    col *= shade;
    col *= 1.0f - (edge * 0.8f);
    float hatch = ::DoHatch(oP / gSc, iRes);
    col *= (hatch * 0.5f) + 0.7f;
    __output.write(float4(metal::sqrt(metal::max(col, 0.0f)), 1.0f), __tid.xy);
    return;
}
