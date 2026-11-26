// SharpCompute.Samples.Shaders.MengerJourney (SharpCompute.Samples.Shaders): entry point SharpCompute_Samples_Shaders_MengerJourney, compile options None
#include <metal_stdlib>

using namespace metal;

constant float FieldOfView = 1.0f;
constant float Jitter = 0.05f;
constant int MaxSteps = 30;
constant float NonLinearPerspective = 2.0f;
constant int Iterations = 7;
constant float Scale = 3.0f;
constant float3 Offset = float3(0.92858f, 0.92858f, 0.32858f);
constant float FudgeFactor = 0.7f;
constant float MinimumDistance = 0.0009f;
constant float NormalDistance = 0.0002f;
constant float3 LightDir = float3(1.0f);
constant float3 LightDir2 = float3(1.0f, -1.0f, 1.0f);
constant float Diffuse = 0.5f;
constant float3 LightColor = float3(1.0f, 1.0f, 0.858824f);
constant float3 LightColor2 = float3(0.0f, 0.333333f, 1.0f);
constant float Ambient = 0.32184f;

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

static float RayMarch_Rand(float2 co);
static float2 Rotate(float2 v, float a);
static float3 DE_Mod(float3 x, float y);
static float DE(float3 z, constant __Constants& __c);
static float3 GetNormal(float3 pos, constant __Constants& __c);
static float3 GetLight(float3 color, float3 normal);
static float4 RayMarch(float3 from, float3 dir, float2 fragCoord, constant __Constants& __c);

static float RayMarch_Rand(float2 co)
{
    return metal::fract(metal::cos(metal::dot(co, float2(4.898f, 7.23f))) * 23421.63f);
}

static float2 Rotate(float2 v, float a)
{
    return float2((metal::cos(a) * v.x) + (metal::sin(a) * v.y), (-metal::sin(a) * v.x) + (metal::cos(a) * v.y));
}

static float3 DE_Mod(float3 x, float y)
{
    return x - (y * metal::floor(x / y));
}

static float DE(float3 z, constant __Constants& __c)
{
    z = metal::abs(1.0f - ::DE_Mod(z, 2.0f));
    float d = 1000.0f;
    for (int n = 0; n < ::Iterations; n = int(uint(n) + 1u))
    {
        z.xy = ::Rotate(z.xy, 4.0f + (2.0f * metal::cos(__c.time / 8.0f)));
        z = metal::abs(z);
        if (z.x < z.y)
        {
            z.xy = z.yx;
        }
        if (z.x < z.z)
        {
            z.xz = z.zx;
        }
        if (z.y < z.z)
        {
            z.yz = z.zy;
        }
        z = (::Scale * z) - (::Offset * (::Scale - 1.0f));
        if (z.z < -0.5f * ::Offset.z * (::Scale - 1.0f))
        {
            z.z += ::Offset.z * (::Scale - 1.0f);
        }
        d = metal::min(d, metal::length(z) * metal::pow(::Scale, float(int(-uint(n))) - 1.0f));
    }
    return d - 0.001f;
}

static float3 GetNormal(float3 pos, constant __Constants& __c)
{
    float3 e = float3(0.0f, ::NormalDistance, 0.0f);
    return metal::normalize(float3(::DE(pos + e.yxx, __c) - ::DE(pos - e.yxx, __c), ::DE(pos + e.xyx, __c) - ::DE(pos - e.xyx, __c), ::DE(pos + e.xxy, __c) - ::DE(pos - e.xxy, __c)));
}

static float3 GetLight(float3 color, float3 normal)
{
    float3 lightDir = metal::normalize(::LightDir);
    float diffuse = metal::max(0.0f, metal::dot(-normal, lightDir));
    float3 lightDir2 = metal::normalize(::LightDir2);
    float diffuse2 = metal::max(0.0f, metal::dot(-normal, lightDir2));
    return (diffuse * ::Diffuse * (::LightColor * color)) + (diffuse2 * ::Diffuse * (::LightColor2 * color));
}

static float4 RayMarch(float3 from, float3 dir, float2 fragCoord, constant __Constants& __c)
{
    float totalDistance = ::Jitter * ::RayMarch_Rand(fragCoord.xy + __c.time);
    float3 dir2 = dir;
    float distance = 0.0f;
    int steps = 0;
    float3 pos = float3(0.0f);
    for (int i = 0; i < ::MaxSteps; i = int(uint(i) + 1u))
    {
        dir.zy = ::Rotate(dir2.zy, totalDistance * metal::cos(__c.time / 4.0f) * ::NonLinearPerspective);
        pos = from + (totalDistance * dir);
        distance = ::DE(pos, __c) * ::FudgeFactor;
        totalDistance += distance;
        if (distance < ::MinimumDistance)
        {
            break;
        }
        steps = i;
    }
    float smoothStep = float(steps) + (distance / ::MinimumDistance);
    float ao = 1.1f - (smoothStep / 30.0f);
    float3 normal = ::GetNormal(pos - (dir * ::NormalDistance * 3.0f), __c);
    float3 color = float3(1.0f);
    float3 light = ::GetLight(color, normal);
    color = ((color * ::Ambient) + light) * ao;
    return float4(color.x, color.y, color.z, 1.0f);
}

kernel void SharpCompute_Samples_Shaders_MengerJourney(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float3 camPos = 0.5f * __c.time * float3(1.0f, 0.0f, 0.0f);
    float3 target = camPos + float3(1.0f, 0.0f * metal::cos(__c.time), 0.0f * metal::sin(0.4f * __c.time));
    float3 camDir = metal::normalize(target - camPos);
    float3 camUp = float3(0.0f, 1.0f, 0.0f);
    camUp = metal::normalize(camUp - (metal::dot(camDir, camUp) * camDir));
    float3 camRight = metal::normalize(metal::cross(camDir, camUp));
    float2 coord = -1.0f + (2.0f * float2(int2(__tid.xy)) / float2(int2(__grid.xy)));
    coord.x *= float(int(__grid.x)) / float(int(__grid.y));
    float3 rayDir = metal::normalize(camDir + (((coord.x * camRight) + (coord.y * camUp)) * ::FieldOfView));
    __output.write(::RayMarch(camPos, rayDir, float2(int2(__tid.xy)), __c), __tid.xy);
    return;
}
