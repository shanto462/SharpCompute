// SharpCompute.Samples.Shaders.TriangleGridContouring (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_TriangleGridContouring, compile options None
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
static float2 Hash22(float2 p, constant __Constants& __c);
static float N2D3G(float2 p, constant __Constants& __c);
static float IsoFunction(float2 p, constant __Constants& __c);
static float2 Interpolate(float2 p1, float2 p2, float v1, float v2, float isovalue);
static int IsoLine(float3 n3, float2 ip0, float2 ip1, float2 ip2, float isovalue, float i, thread float2& p0, thread float2& p1);
static float DistEdge(float2 a, float2 b);
static float DistLine(float2 a, float2 b);
static float3 SimplexContour(float2 p, constant __Constants& __c);

static float2x2 Rotate2x2(float a)
{
    float c = metal::cos(a);
    float s = metal::sin(a);
    return float2x2(c, -s, s, c);
}

static float2 Hash22(float2 p, constant __Constants& __c)
{
    float n = metal::sin(metal::dot(p, float2(41.0f, 289.0f)));
    p = metal::fract(float2(262144.0f, 32768.0f) * n);
    return metal::sin((p * 6.2831855f) + __c.time);
}

static float N2D3G(float2 p, constant __Constants& __c)
{
    float2 i = metal::floor(p);
    p -= i;
    float4 v = float4(0);
    v.x = metal::dot(::Hash22(i, __c), p);
    v.y = metal::dot(::Hash22(i + float2(1.0f, 0.0f), __c), p - float2(1.0f, 0.0f));
    v.z = metal::dot(::Hash22(i + float2(0.0f, 1.0f), __c), p - float2(0.0f, 1.0f));
    v.w = metal::dot(::Hash22(i + 1.0f, __c), p - 1.0f);
    p = p * p * p * ((p * ((p * 6.0f) - 15.0f)) + 10.0f);
    return metal::mix(metal::mix(v.x, v.y, p.x), metal::mix(v.z, v.w, p.x), p.y);
}

static float IsoFunction(float2 p, constant __Constants& __c)
{
    return ::N2D3G((p / 4.0f) + 0.07f, __c);
}

static float2 Interpolate(float2 p1, float2 p2, float v1, float v2, float isovalue)
{
    return metal::mix(p1, p2, ((isovalue - v1) / (v2 - v1) * 0.75f) + (0.25f / 2.0f));
}

static int IsoLine(float3 n3, float2 ip0, float2 ip1, float2 ip2, float isovalue, float i, thread float2& p0, thread float2& p1)
{
    p0 = float2(100000.0f);
    p1 = float2(100000.0f);
    int iTh = 0;
    if (n3.x > isovalue)
    {
        iTh = int(uint(iTh) + 4u);
    }
    if (n3.y > isovalue)
    {
        iTh = int(uint(iTh) + 2u);
    }
    if (n3.z > isovalue)
    {
        iTh = int(uint(iTh) + 1u);
    }
    if (iTh == 1 || iTh == 6)
    {
        p0 = ::Interpolate(ip1, ip2, n3.y, n3.z, isovalue);
        p1 = ::Interpolate(ip2, ip0, n3.z, n3.x, isovalue);
    }
    else if (iTh == 2 || iTh == 5)
    {
        p0 = ::Interpolate(ip0, ip1, n3.x, n3.y, isovalue);
        p1 = ::Interpolate(ip1, ip2, n3.y, n3.z, isovalue);
    }
    else if (iTh == 3 || iTh == 4)
    {
        p0 = ::Interpolate(ip0, ip1, n3.x, n3.y, isovalue);
        p1 = ::Interpolate(ip2, ip0, n3.z, n3.x, isovalue);
    }
    if (iTh >= 4 && iTh <= 6)
    {
        float2 tmp = p0;
        p0 = p1;
        p1 = tmp;
    }
    if (i == 0.0f)
    {
        float2 tmp = p0;
        p0 = p1;
        p1 = tmp;
    }
    return iTh;
}

static float DistEdge(float2 a, float2 b)
{
    return metal::dot((a + b) * 0.5f, metal::normalize((b - a).yx * float2(-1.0f, 1.0f)));
}

static float DistLine(float2 a, float2 b)
{
    b = a - b;
    float h = metal::clamp(metal::dot(a, b) / metal::dot(b, b), 0.0f, 1.0f);
    return metal::length(a - (b * h));
}

static float3 SimplexContour(float2 p, constant __Constants& __c)
{
    const float gSc = 8.0f;
    p *= gSc;
    float2 oP = p;
    p += float2(::N2D3G(p * 3.5f, __c), ::N2D3G((p * 3.5f) + 7.3f, __c)) * 0.015f;
    float2 s = metal::floor(p + ((p.x + p.y) * 0.36602542f));
    p -= s - ((s.x + s.y) * 0.21132487f);
    float i = p.x < p.y ? 1.0f : 0.0f;
    float2 ioffs = float2(1.0f - i, i);
    float2 ip0 = float2(0.0f);
    float2 ip1 = ioffs - 0.21132487f;
    float2 ip2 = float2(0.57735026f);
    float2 ctr = (ip0 + ip1 + ip2) / 3.0f;
    ip0 -= ctr;
    ip1 -= ctr;
    ip2 -= ctr;
    p -= ctr;
    float3 n3 = float3(0);
    n3.x = ::IsoFunction(s, __c);
    n3.y = ::IsoFunction(s + ioffs, __c);
    n3.z = ::IsoFunction(s + 1.0f, __c);
    float d = 100000.0f;
    float d2 = 100000.0f;
    float d3 = 100000.0f;
    float d4 = 100000.0f;
    float d5 = 100000.0f;
    float isovalue = 0.0f;
    float2 p0 = float2(0);
    float2 p1 = float2(0);
    int iTh = ::IsoLine(n3, ip0, ip1, ip2, isovalue, i, p0, p1);
    d = metal::min(d, ::DistEdge(p - p0, p - p1));
    if (iTh == 7)
    {
        d = 0.0f;
    }
    d3 = metal::min(d3, ::DistLine(p - p0, p - p1));
    d4 = metal::min(d4, metal::min(metal::length(p - p0), metal::length(p - p1)));
    float tri = metal::min(metal::min(::DistLine(p - ip0, p - ip1), ::DistLine(p - ip1, p - ip2)), ::DistLine(p - ip2, p - ip0));
    d5 = metal::min(d5, tri);
    d5 = metal::min(d5, metal::length(p) - 0.02f);
    isovalue = -0.15f;
    int iTh2 = ::IsoLine(n3, ip0, ip1, ip2, isovalue, i, p0, p1);
    d2 = metal::min(d2, ::DistEdge(p - p0, p - p1));
    if (iTh2 == 7)
    {
        d2 = 0.0f;
    }
    if (iTh == 7)
    {
        d2 = 100000.0f;
    }
    d2 = metal::max(d2, -d);
    d3 = metal::min(d3, ::DistLine(p - p0, p - p1));
    d4 = metal::min(d4, metal::min(metal::length(p - p0), metal::length(p - p1)));
    d4 -= 0.075f;
    d3 -= 0.0125f;
    d /= gSc;
    d2 /= gSc;
    d3 /= gSc;
    d4 /= gSc;
    d5 /= gSc;
    float3 col = float3(1.0f, 0.85f, 0.6f);
    float sf = 0.004f;
    if (d > 0.0f && d2 > 0.0f)
    {
        col = float3(1.0f, 1.8f, 3.0f) * 0.45f;
    }
    if (d > 0.0f)
    {
        col = metal::mix(col, float3(1.0f, 1.85f, 3.0f) * 0.3f, 1.0f - metal::smoothstep(0.0f, sf, d2 - 0.012f));
    }
    col = metal::mix(col, float3(1.1f, 0.85f, 0.6f), 1.0f - metal::smoothstep(0.0f, sf, d2));
    col = metal::mix(col, float3(1.5f, 0.9f, 0.6f) * 0.6f, 1.0f - metal::smoothstep(0.0f, sf, d - 0.012f));
    col = metal::mix(col, float3(1.0f, 0.8f, 0.6f) * float3(0.7f, 1.0f, 0.75f) * 0.95f, 1.0f - metal::smoothstep(0.0f, sf, d));
    if (d2 > 0.0f)
    {
        col *= ((metal::abs(metal::dot(n3, float3(1.0f))) * 1.25f) + 1.25f) / 2.0f;
    }
    else
    {
        col *= metal::max(2.0f - ((metal::dot(n3, float3(1.0f)) + 1.45f) / 1.25f), 0.0f);
    }
    float pat = metal::abs(metal::fract((tri * 12.5f) + 0.4f) - 0.5f) * 2.0f;
    col *= (pat * 0.425f) + 0.75f;
    col = metal::mix(col, float3(0.0f), (1.0f - metal::smoothstep(0.0f, sf, d5)) * 0.95f);
    col = metal::mix(col, float3(0.0f), 1.0f - metal::smoothstep(0.0f, sf, d3));
    col = metal::mix(col, float3(0.0f), 1.0f - metal::smoothstep(0.0f, sf, d4));
    col = metal::mix(col, float3(1.0f), 1.0f - metal::smoothstep(0.0f, sf, d4 + 0.005f));
    float2 q = oP * 1.5f;
    col = metal::min(col, 1.0f);
    float gr = metal::sqrt(metal::dot(col, float3(0.299f, 0.587f, 0.114f))) * 1.25f;
    float ns = (((::N2D3G(q * 4.0f * float2(1.0f / 3.0f, 3.0f), __c) * 0.64f) + (::N2D3G(q * 8.0f * float2(1.0f / 3.0f, 3.0f), __c) * 0.34f)) * 0.5f) + 0.5f;
    ns = gr - ns;
    q = (::Rotate2x2(3.14159f / 3.0f) * float2(q));
    float ns2 = (((::N2D3G(q * 4.0f * float2(1.0f / 3.0f, 3.0f), __c) * 0.64f) + (::N2D3G(q * 8.0f * float2(1.0f / 3.0f, 3.0f), __c) * 0.34f)) * 0.5f) + 0.5f;
    ns2 = gr - ns2;
    ns = metal::smoothstep(0.0f, 1.0f, metal::min(ns, ns2));
    col = metal::mix(col, col * (ns + 0.35f), 0.4f);
    return col;
}

kernel void SharpCompute_Samples_Shaders_TriangleGridContouring(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int2 coordinate = int2(int(__tid.x), int(__grid.y - __tid.y));
    float2 uv = (float2(coordinate) - (float2(int2(__grid.xy)) * 0.5f)) / metal::min(650.0f, float(int(__grid.y)));
    float2 p = (float2(uv) * ::Rotate2x2(3.14159f / 12.0f)) + (float2(0.8660254f, 0.5f) * __c.time / 16.0f);
    float3 col = ::SimplexContour(p, __c);
    uv = float2(coordinate) / float2(int2(__grid.xy));
    col *= metal::pow(16.0f * uv.x * uv.y * (1.0f - uv.x) * (1.0f - uv.y), 0.0625f) + 0.1f;
    __output.write(float4(metal::sqrt(metal::max(col, 0.0f)), 1.0f), __tid.xy);
    return;
}
