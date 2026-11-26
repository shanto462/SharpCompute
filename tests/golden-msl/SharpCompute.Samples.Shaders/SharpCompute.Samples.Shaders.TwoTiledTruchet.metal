// SharpCompute.Samples.Shaders.TwoTiledTruchet (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_TwoTiledTruchet, compile options None
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

static float DistanceField_Hash21(float2 p);
static float DistanceField_Mod(float x, float y);
static float2 DistanceField(float2 p, thread float2& ang, constant __Constants& __c);

static float DistanceField_Hash21(float2 p)
{
    return metal::fract(metal::sin(metal::dot(p, float2(27.619f, 57.583f))) * 43758.547f);
}

static float DistanceField_Mod(float x, float y)
{
    return x - (y * metal::floor(x / y));
}

static float2 DistanceField(float2 p, thread float2& ang, constant __Constants& __c)
{
    float2 ip2 = metal::floor(p / 2.0f);
    float2 ip = metal::floor(p);
    float rnd = ::DistanceField_Hash21(ip);
    float rnd3 = ::DistanceField_Hash21(ip + 0.57f);
    float2 d = float2(100000.0f);
    p -= ip + 0.5f;
    if (::DistanceField_Mod(ip2.x + ip2.y, 2.0f) < 0.5f)
    {
        d = metal::abs(p);
        ang = p.yx;
        if (::DistanceField_Mod(ip.x + 1.0f, 2.0f) < 0.5f)
        {
            ang.x *= -1.0f;
        }
        if (::DistanceField_Mod(ip.y + 1.0f, 2.0f) > 0.5f)
        {
            ang.y *= -1.0f;
        }
        if (rnd3 < 0.5f)
        {
            d = d.yx;
            ang = ang.yx;
        }
        ang += 0.5f;
        ang *= 3.0f;
    }
    else
    {
        if (rnd < 0.5f)
        {
            p = p.yx * float2(1.0f, -1.0f);
        }
        d.x = metal::length(p - 0.5f) - 0.5f;
        d.y = metal::length(p + 0.5f) - 0.5f;
        d = metal::abs(d);
        ang = float2(0.0f);
        ang.x = -metal::atan((p.y - 0.5f) / (p.x - 0.5f));
        ang.y = -metal::atan((p.y + 0.5f) / (p.x + 0.5f));
        if (::DistanceField_Mod(ip.x + ip.y, 2.0f) < 0.5f)
        {
            ang *= -1.0f;
        }
        if (rnd < 0.5f)
        {
            ang *= -1.0f;
        }
        if (rnd3 < 0.5f)
        {
            d = d.yx;
            ang = ang.yx;
        }
        ang *= 4.0f / 6.2831855f;
        ang *= 2.0f;
    }
    ang = metal::fract(ang + (__c.time / 4.0f));
    return d;
}

kernel void SharpCompute_Samples_Shaders_TwoTiledTruchet(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 uv = (float2(int2(__tid.xy)) - (float2(int2(__grid.xy)) * 0.5f)) / float(int(__grid.y));
    float gSc = 7.0f;
    float2 p = (uv * gSc) - (float2(-1.0f, -0.5f) * __c.time / 2.0f);
    float sf = 2.0f / float(int(__grid.y)) * gSc;
    float lSc = 6.0f;
    float lw = 1.0f / lSc / gSc;
    float2 ang;
    float2 d = ::DistanceField(p, ang, __c) - (2.5f / lSc);
    float3 col = float3(1.0f, 0.9f, 0.8f);
    for (int i = 0; i < 2; i = int(uint(i) + 1u))
    {
        float di = (i == 0 ? d.x : d.y) - (lw / 4.0f);
        float tracks = metal::clamp(metal::sin(((i == 0 ? ang.x : ang.y) * 6.2831f) + (__c.time * 6.0f)) * 4.0f, 0.0f, 1.0f);
        float gap = 1.0f + lw;
        col = metal::mix(col, float3(0.0f), (1.0f - metal::smoothstep(0.0f, sf * 6.0f, di)) * 0.35f);
        col = metal::mix(col, float3(0.0f), 1.0f - metal::smoothstep(0.0f, sf, di));
        col = metal::mix(col, float3(1.0f, 0.9f, 0.8f), 1.0f - metal::smoothstep(0.0f, sf, di + (lw * 2.0f)));
        col = metal::mix(col, float3(0.0f), 1.0f - metal::smoothstep(0.0f, sf, di + (gap / lSc)));
        col = metal::mix(col, float3(1.0f), 1.0f - metal::smoothstep(0.0f, sf, di + (gap / lSc) + lw));
        col = metal::mix(col, float3(0.0f), 1.0f - metal::smoothstep(0.0f, sf, di + (2.0f * gap / lSc)));
        col = metal::mix(col, float3(1.0f * tracks), 1.0f - metal::smoothstep(0.0f, sf, di + (2.0f * gap / lSc) + lw));
    }
    float3 rgb = metal::sqrt(metal::max(col, 0.0f));
    float4 color = float4(rgb.x, rgb.y, rgb.z, 1.0f);
    __output.write(color, __tid.xy);
    return;
}
