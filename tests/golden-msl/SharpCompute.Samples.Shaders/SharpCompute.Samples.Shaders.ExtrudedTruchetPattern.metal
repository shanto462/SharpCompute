// SharpCompute.Samples.Shaders.ExtrudedTruchetPattern (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_ExtrudedTruchetPattern, compile options None
#include <metal_stdlib>

using namespace metal;

constant int SHAPE = 0;

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
static float Hash21(float2 p);
static float Distance(float2 p);
static float Mod(float x, float y);
static float Truchet(float2 p);
static float M(float3 p, thread int& objID);
static float M_1(float3 p);
static float3 Normal(float3 p);
static float SoftShadow(float3 ro, float3 lp, float3 n, float k);
static float CalculateAO(float3 p, float3 n, constant __Constants& __c);

static float2x2 Rotate2x2(float a)
{
    float c = metal::cos(a);
    float s = metal::sin(a);
    return float2x2(c, -s, s, c);
}

static float Hash21(float2 p)
{
    return metal::fract(metal::sin(metal::dot(p, float2(27.619f, 57.583f))) * 43758.547f);
}

static float Distance(float2 p)
{
    if (::SHAPE == 0)
    {
        return metal::length(p);
    }
    else
    {
        p = metal::abs(p);
    }
    switch (::SHAPE)
    {
        case 1:
        {
            return metal::max(metal::length(p), ((p.x + p.y) * 0.7071f) + 0.015f);
        }
        case 2:
        {
            return metal::max((p.x + p.y) * 0.7071f, metal::max(p.x, p.y));
        }
        case 3:
        {
            return metal::pow(metal::dot(metal::pow(p, 3.0f), float2(1.0f)), 1.0f / 3.0f);
        }
        default:
        {
            return (p.x + p.y) * 0.7071f;
        }
    }
}

static float Mod(float x, float y)
{
    return x - (y * metal::floor(x / y));
}

static float Truchet(float2 p)
{
    const float sc = 0.5f;
    float2 ip = metal::floor(p / sc) + 0.5f;
    p -= ip * sc;
    float rnd = metal::fract(metal::sin(metal::dot(ip, float2(1.0f, 113.0f))) * 45758.547f);
    if (rnd < 0.5f)
    {
        p.y = -p.y;
    }
    float d = metal::min(::Distance(p - (0.5f * sc)), ::Distance(p + (0.5f * sc))) - (0.5f * sc);
    if (::SHAPE == 4)
    {
        d += (0.5f - (0.5f / metal::sqrt(2.0f))) * sc;
    }
    if (rnd < 0.5f)
    {
        d = -d;
    }
    if (::Mod(ip.x + ip.y, 2.0f) < 0.5f)
    {
        d = -d;
    }
    return d - 0.03f;
}

static float M(float3 p, thread int& objID)
{
    float fl = -p.z;
    float obj = ::Truchet(p.xy);
    obj = metal::max(obj, metal::abs(p.z) - 0.125f) - (metal::smoothstep(0.03f, 0.25f, -obj) * 0.1f);
    float studs = 100000.0f;
    const float sc = 0.5f;
    float2 q = p.xy + (0.5f * sc);
    float2 iq = metal::floor(q / sc) + 0.5f;
    q -= iq * sc;
    if (::Mod(iq.x + iq.y, 2.0f) > 0.5f)
    {
        studs = metal::max(metal::length(q) - (0.1f * sc) - 0.02f, metal::abs(p.z) - 0.26f);
    }
    objID = fl < obj && fl < studs ? 0 : obj < studs ? 1 : 2;
    return metal::min(metal::min(fl, obj), studs);
}

static float M_1(float3 p)
{
    int unusedObjID;
    return ::M(p, unusedObjID);
}

static float3 Normal(float3 p)
{
    float2 e = float2(0.001f, 0.0f);
    return metal::normalize(float3(::M_1(p + e.xyy) - ::M_1(p - e.xyy), ::M_1(p + e.yxy) - ::M_1(p - e.yxy), ::M_1(p + e.yyx) - ::M_1(p - e.yyx)));
}

static float SoftShadow(float3 ro, float3 lp, float3 n, float k)
{
    const int iter = 24;
    ro += n * 0.0015f;
    float3 rd = lp - ro;
    float shade = 1.0f;
    float t = 0.0f;
    float end = metal::max(metal::length(rd), 0.0001f);
    rd /= end;
    for (int i = 0; i < iter; i = int(uint(i) + 1u))
    {
        float d = ::M_1(ro + (rd * t));
        shade = metal::min(shade, k * d / t);
        t += metal::clamp(d, 0.01f, 0.25f);
        if (d < 0.0f || t > end)
        {
            break;
        }
    }
    return metal::max(shade, 0.0f);
}

static float CalculateAO(float3 p, float3 n, constant __Constants& __c)
{
    float sca = 2.0f;
    float occ = 0.0f;
    for (int i = int(metal::min(__c.time, 0.0f)); i < 5; i = int(uint(i) + 1u))
    {
        float hr = (float(i) + 1.0f) * 0.15f / 5.0f;
        float d = ::M_1(p + (n * hr));
        occ += (hr - d) * sca;
        sca *= 0.7f;
        if (sca > 100000.0f)
        {
            break;
        }
    }
    return metal::clamp(1.0f - occ, 0.0f, 1.0f);
}

kernel void SharpCompute_Samples_Shaders_ExtrudedTruchetPattern(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int2 coords = int2(int(__tid.x), int(__grid.y - __tid.y));
    float2 u = (float2(coords) - (float2(int2(__grid.xy)) * 0.5f)) / float(int(__grid.y));
    float3 r = metal::normalize(float3(u, 1.0f));
    float3 o = float3(0.0f, __c.time / 2.0f, -3.0f);
    float3 l = o + float3(0.25f, 0.25f, 2.0f);
    r.yz = (float2(r.yz) * ::Rotate2x2(0.15f));
    r.xz = (float2(r.xz) * ::Rotate2x2(-metal::cos(__c.time * 3.14159f / 32.0f) / 8.0f));
    r.xy = (float2(r.xy) * ::Rotate2x2(metal::sin(__c.time * 3.14159f / 32.0f) / 8.0f));
    float d;
    float t = ::Hash21((r.xy * 57.0f) + metal::fract(__c.time)) * 0.5f;
    float glow = 0.0f;
    int objID = 0;
    for (int i = 0; i < 96; i = int(uint(i) + 1u))
    {
        d = ::M(o + (r * t), objID);
        if (metal::abs(d) < 0.001f)
        {
            break;
        }
        t += d * 0.7f;
        glow += 0.2f / (1.0f + (metal::abs(d) * 5.0f));
    }
    int gObjID = objID;
    float3 p = o + (r * t);
    float3 n = ::Normal(p);
    float2 uv = p.xy;
    float sc = 0.5f;
    float2 iuv = metal::floor(uv / sc) + 0.5f;
    uv -= iuv * sc;
    float2 uv2 = p.xy + (0.5f * sc);
    float2 iuv2 = metal::floor(uv2 / sc) + 0.5f;
    uv2 -= iuv2 * sc;
    float bord = metal::max(metal::abs(uv.x), metal::abs(uv.y)) - (0.5f * sc);
    bord = metal::abs(bord) - 0.002f;
    d = ::Truchet(p.xy);
    float lSc = 20.0f;
    float pat = ((metal::abs(metal::fract(((uv.x - uv.y) * lSc) - 0.5f) - 0.5f) * 2.0f) - 0.5f) / lSc;
    float pat2 = ((metal::abs(metal::fract(((uv.x + uv.y) * lSc) + 0.5f) - 0.5f) * 2.0f) - 0.5f) / lSc;
    float sf = metal::dot(metal::sin(p.xy - metal::cos(p.yx * 2.0f)), float2(0.5f));
    float sf2 = metal::dot(metal::sin((p.xy * 1.5f) - metal::cos(p.yx * 3.0f)), float2(0.5f));
    float4 col1 = metal::mix(float4(1.0f, 0.75f, 0.6f, 0.0f), float4(1.0f, 0.85f, 0.65f, 0.0f), metal::smoothstep(-0.5f, 0.5f, sf));
    float4 col2 = metal::mix(float4(0.4f, 0.7f, 1.0f, 0.0f), float4(0.3f, 0.85f, 0.95f, 0.0f), metal::smoothstep(-0.5f, 0.5f, sf2) * 0.5f);
    col1 = metal::pow(col1, 1.6f);
    col2 = metal::mix(col1.zyxw, metal::pow(col2, 1.4f), 0.666f);
    float4 oCol;
    if (gObjID == 0)
    {
        oCol = metal::mix(col2, float4(0.0f), (1.0f - metal::smoothstep(0.0f, 0.01f, pat2)) * 0.35f);
        oCol = metal::mix(oCol, float4(0.0f), (1.0f - metal::smoothstep(0.0f, 0.01f, bord)) * 0.8f);
        if (::Mod(iuv.x + iuv.y, 2.0f) > 0.5f)
        {
            oCol *= 0.8f;
        }
        oCol = metal::mix(oCol, float4(0.0f), (1.0f - metal::smoothstep(0.0f, 0.01f, d - 0.015f)) * 0.8f);
    }
    else if (gObjID == 1)
    {
        oCol = float4(metal::mix(1.0f, 0.0f, 1.0f - metal::smoothstep(0.0f, 0.01f, d + 0.05f)));
        float4 fCol = metal::mix(col1, float4(0.0f), (1.0f - metal::smoothstep(0.0f, 0.01f, pat)) * 0.35f);
        fCol = metal::mix(fCol, float4(0.0f), (1.0f - metal::smoothstep(0.0f, 0.01f, bord)) * 0.8f);
        if (::Mod(iuv.x + iuv.y, 2.0f) < 0.5f)
        {
            fCol *= 0.8f;
        }
        oCol = metal::mix(oCol, fCol, 1.0f - metal::smoothstep(0.0f, 0.01f, d + 0.08f));
        oCol = metal::mix(oCol, float4(0.0f), (1.0f - metal::smoothstep(0.0f, 0.01f, metal::length(uv2) - 0.08f)) * 0.8f);
    }
    else
    {
        oCol = col1;
        float ht = 0.26f;
        if (::Mod(iuv2.x + iuv2.y + 1.0f, 2.0f) > 0.5f)
        {
            oCol = col2;
            ht = 0.05f;
        }
        float mark = metal::length(uv2);
        float markRim = metal::max(metal::abs(mark - 0.07f), metal::abs(p.z + ht)) - 0.003f;
        oCol = metal::mix(oCol, float4(0.0f), (1.0f - metal::smoothstep(0.0f, 0.01f, markRim)) * 0.8f);
        oCol = metal::mix(oCol, float4(0.0f), (1.0f - metal::smoothstep(0.0f, 0.01f, mark - 0.015f)) * 0.8f);
    }
    float3 ld = l - p;
    float lDist = metal::length(ld);
    ld /= lDist;
    float at = 1.0f / (1.0f + (lDist * lDist * 0.125f));
    float sh = ::SoftShadow(p, l, n, 8.0f);
    float ao = ::CalculateAO(p, n, __c);
    float df = metal::max(metal::dot(n, ld), 0.0f);
    float sp = metal::pow(metal::max(metal::dot(metal::reflect(r, n), ld), 0.0f), 32.0f);
    float4 c = oCol * ((df * sh) + (sp * sh) + 0.5f) * at * ao;
    c = metal::sqrt(metal::max(c, 0.0f));
    __output.write(float4(c.xyz, 1.0f), __tid.xy);
    return;
}
