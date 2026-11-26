// SharpCompute.Samples.Shaders.ProteanClouds (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_ProteanClouds, compile options None
#include <metal_stdlib>

using namespace metal;

constant float3x3 M3 = float3x3(0.33338f, 0.56034f, -0.71817f, -0.87887f, 0.32651f, -0.15323f, 0.15162f, 0.69596f, 0.61339f) * 1.93f;

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

static float2 Disp(float t);
static float2x2 Rotate(float a);
static float2 Map(float3 p, float prm1, float2 bsMo, constant __Constants& __c);
static float LinStep(float mn, float mx, float x);
static float4 Render(float3 ro, float3 rd, float prm1, float2 bsMo, constant __Constants& __c);
static float GetSaturation(float3 c);
static float3 ILerp(float3 a, float3 b, float x);

static float2 Disp(float t)
{
    return float2(metal::sin(t * 0.22f) * 1.0f, metal::cos(t * 0.175f) * 1.0f) * 2.0f;
}

static float2x2 Rotate(float a)
{
    float c = metal::cos(a);
    float s = metal::sin(a);
    return float2x2(c, s, -s, c);
}

static float2 Map(float3 p, float prm1, float2 bsMo, constant __Constants& __c)
{
    float3 p2 = p;
    p2.xy -= ::Disp(p.z).xy;
    p.xy = (::Rotate((metal::sin(p.z + __c.time) * (0.1f + (prm1 * 0.05f))) + (__c.time * 0.09f)) * float2(p.xy));
    float cl = metal::dot(p2.xy, p2.xy);
    float d = 0.0f;
    p *= 0.61f;
    float z = 1.0f;
    float trk = 1.0f;
    float dspAmp = 0.1f + (prm1 * 0.2f);
    for (int i = 0; i < 5; i = int(uint(i) + 1u))
    {
        p += metal::sin((p.zxy * 0.75f * trk) + (__c.time * trk * 0.8f)) * dspAmp;
        d -= metal::abs(metal::dot(metal::cos(p), metal::sin(p.yzx)) * z);
        z *= 0.57f;
        trk *= 1.4f;
        p = (::M3 * float3(p));
    }
    d = metal::abs(d + (prm1 * 3.0f)) + (prm1 * 0.3f) - 2.5f + bsMo.y;
    return float2(d + (cl * 0.2f) + 0.25f, cl);
}

static float LinStep(float mn, float mx, float x)
{
    return metal::clamp((x - mn) / (mx - mn), 0.0f, 1.0f);
}

static float4 Render(float3 ro, float3 rd, float prm1, float2 bsMo, constant __Constants& __c)
{
    float4 rez = float4(0.0f);
    float t = 1.5f;
    float fogT = 0.0f;
    for (int i = 0; i < 130; i = int(uint(i) + 1u))
    {
        if (rez.w > 0.99f)
        {
            break;
        }
        float3 pos = ro + (t * rd);
        float2 mpv = ::Map(pos, prm1, bsMo, __c);
        float den = metal::clamp(mpv.x - 0.3f, 0.0f, 1.0f) * 1.12f;
        float dn = metal::clamp(mpv.x + 2.0f, 0.0f, 3.0f);
        float4 col = float4(0.0f);
        if (mpv.x > 0.6f)
        {
            col = float4((metal::sin(float3(5.0f, 0.4f, 0.2f) + (mpv.y * 0.1f) + (metal::sin(pos.z * 0.4f) * 0.5f) + 1.8f) * 0.5f) + 0.5f, 0.08f);
            col *= den * den * den;
            col.xyz *= ::LinStep(4.0f, -2.5f, mpv.x) * 2.3f;
            float dif = metal::clamp((den - ::Map(pos + 0.8f, prm1, bsMo, __c).x) / 9.0f, 0.001f, 1.0f);
            dif += metal::clamp((den - ::Map(pos + 0.35f, prm1, bsMo, __c).x) / 2.5f, 0.001f, 1.0f);
            col.xyz *= den * (float3(0.005f, 0.045f, 0.075f) + (1.5f * float3(0.033f, 0.07f, 0.03f) * dif));
        }
        float fogC = metal::exp((t * 0.2f) - 2.2f);
        col += float4(0.06f, 0.11f, 0.11f, 0.1f) * metal::clamp(fogC - fogT, 0.0f, 1.0f);
        fogT = fogC;
        rez += col * (1.0f - rez.w);
        t += metal::clamp(0.5f - (dn * dn * 0.05f), 0.09f, 0.3f);
    }
    return metal::clamp(rez, 0.0f, 1.0f);
}

static float GetSaturation(float3 c)
{
    float mi = metal::min(metal::min(c.x, c.y), c.z);
    float ma = metal::max(metal::max(c.x, c.y), c.z);
    return (ma - mi) / (ma + 1E-07f);
}

static float3 ILerp(float3 a, float3 b, float x)
{
    float3 ic = metal::mix(a, b, x) + float3(1E-06f, 0.0f, 0.0f);
    float sd = metal::abs(::GetSaturation(ic) - metal::mix(::GetSaturation(a), ::GetSaturation(b), x));
    float3 dir = metal::normalize(float3((2.0f * ic.x) - ic.y - ic.z, (2.0f * ic.y) - ic.x - ic.z, (2.0f * ic.z) - ic.y - ic.x));
    float lgt = metal::dot(float3(1.0f), ic);
    float ff = metal::dot(dir, metal::normalize(ic));
    ic += 1.5f * dir * sd * ff * lgt;
    return metal::clamp(ic, 0.0f, 1.0f);
}

kernel void SharpCompute_Samples_Shaders_ProteanClouds(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 q = float2(int2(__tid.xy)) / float2(int2(__grid.xy));
    float2 p = (float2(int2(__tid.xy)) - (0.5f * float2(int2(__grid.xy)))) / float(int(__grid.y));
    float2 bsMo = -0.5f * float2(int2(__grid.xy)) / float(int(__grid.y));
    float scaledTime = __c.time * 3.0f;
    float3 ro = float3(0.0f, 0.0f, scaledTime);
    ro += float3(metal::sin(__c.time) * 0.5f, metal::sin(__c.time * 1.0f) * 0.0f, 0.0f);
    float dspAmp = 0.85f;
    ro.xy += ::Disp(ro.z) * dspAmp;
    float tgtDst = 3.5f;
    float3 target = metal::normalize(ro - float3(::Disp(scaledTime + tgtDst) * dspAmp, scaledTime + tgtDst));
    ro.x -= bsMo.x * 2.0f;
    float3 rightdir = metal::normalize(metal::cross(target, float3(0.0f, 1.0f, 0.0f)));
    float3 updir = metal::normalize(metal::cross(rightdir, target));
    rightdir = metal::normalize(metal::cross(updir, target));
    float3 rd = metal::normalize((((p.x * rightdir) + (p.y * updir)) * 1.0f) - target);
    rd.xy = (::Rotate((-::Disp(scaledTime + 3.5f).x * 0.2f) + bsMo.x) * float2(rd.xy));
    float prm1 = metal::smoothstep(-0.4f, 0.4f, metal::sin(__c.time * 0.3f));
    float4 scn = ::Render(ro, rd, prm1, bsMo, __c);
    float3 col = scn.xyz;
    col = ::ILerp(col.zyx, col.xyz, metal::clamp(1.0f - prm1, 0.05f, 1.0f));
    col = metal::pow(col, float3(0.55f, 0.65f, 0.6f)) * float3(1.0f, 0.97f, 0.9f);
    col *= (metal::pow(16.0f * q.x * q.y * (1.0f - q.x) * (1.0f - q.y), 0.12f) * 0.7f) + 0.3f;
    __output.write(float4(col, 1.0f), __tid.xy);
    return;
}
