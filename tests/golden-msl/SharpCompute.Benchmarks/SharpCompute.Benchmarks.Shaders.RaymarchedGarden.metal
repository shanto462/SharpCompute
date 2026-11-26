// SharpCompute.Benchmarks.Shaders.RaymarchedGarden (SharpCompute.Benchmarks): entry point SharpCompute_Benchmarks_Shaders_RaymarchedGarden, compile options None
#include <metal_stdlib>

using namespace metal;

constant int MarchSteps = 96;
constant float PillarPitch = 5.0f;
constant float FarPlane = 60.0f;
constant int ShadowSteps = 32;

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

static float3 Sky(float3 direction, float3 sun);
static float SmoothMin(float a, float b, float k);
static float Hash(float3 p);
static float Noise(float3 x);
static float Fbm(float3 p, int octaves);
static float Ground(float3 p);
static float RoundBox(float3 p, float3 halfSize, float radius);
static float Pillars(float3 p);
static float3 RotateX(float3 p, float angle);
static float3 RotateY(float3 p, float angle);
static float Torus(float3 p, float major, float minor);
static float Sphere(float3 p, float radius);
static float Sculpture(float3 p, constant __Constants& __c);
static float Scene(float3 p, constant __Constants& __c);
static float March(float3 origin, float3 direction, constant __Constants& __c);
static float3 Normal(float3 p, constant __Constants& __c);
static float Material(float3 p, constant __Constants& __c);
static float SoftShadow(float3 origin, float3 direction, constant __Constants& __c);
static float Occlusion(float3 p, float3 normal, constant __Constants& __c);

static float3 Sky(float3 direction, float3 sun)
{
    float height = metal::saturate(direction.y);
    float3 color = metal::mix(float3(0.74f, 0.8f, 0.88f), float3(0.2f, 0.38f, 0.72f), metal::pow(height, 0.6f));
    float glow = metal::saturate(metal::dot(direction, sun));
    return color + (float3(1.0f, 0.75f, 0.45f) * ((0.35f * metal::pow(glow, 8.0f)) + metal::pow(glow, 240.0f)));
}

static float SmoothMin(float a, float b, float k)
{
    float h = metal::saturate(0.5f + (0.5f * (b - a) / k));
    return metal::mix(b, a, h) - (k * h * (1.0f - h));
}

static float Hash(float3 p)
{
    p = metal::fract((p * 0.3183099f) + float3(0.1f, 0.17f, 0.23f));
    p *= 17.0f;
    return metal::fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

static float Noise(float3 x)
{
    float3 cell = metal::floor(x);
    float3 f = metal::fract(x);
    float3 w = f * f * (float3(3.0f) - (f * 2.0f));
    float c000 = ::Hash(cell);
    float c100 = ::Hash(cell + float3(1.0f, 0.0f, 0.0f));
    float c010 = ::Hash(cell + float3(0.0f, 1.0f, 0.0f));
    float c110 = ::Hash(cell + float3(1.0f, 1.0f, 0.0f));
    float c001 = ::Hash(cell + float3(0.0f, 0.0f, 1.0f));
    float c101 = ::Hash(cell + float3(1.0f, 0.0f, 1.0f));
    float c011 = ::Hash(cell + float3(0.0f, 1.0f, 1.0f));
    float c111 = ::Hash(cell + float3(1.0f, 1.0f, 1.0f));
    float near = metal::mix(metal::mix(c000, c100, w.x), metal::mix(c010, c110, w.x), w.y);
    float far = metal::mix(metal::mix(c001, c101, w.x), metal::mix(c011, c111, w.x), w.y);
    return metal::mix(near, far, w.z);
}

static float Fbm(float3 p, int octaves)
{
    float sum = 0.0f;
    float amplitude = 0.5f;
    for (int i = 0; i < octaves; i = int(uint(i) + 1u))
    {
        sum += amplitude * ::Noise(p);
        p = (p * 2.03f) + float3(11.3f, 5.7f, 3.1f);
        amplitude *= 0.5f;
    }
    return sum;
}

static float Ground(float3 p)
{
    return p.y + 1.2f - (1.6f * ::Fbm(float3(p.x * 0.35f, 0.0f, p.z * 0.35f), 3));
}

static float RoundBox(float3 p, float3 halfSize, float radius)
{
    float3 q = metal::abs(p) - halfSize;
    return metal::length(metal::max(q, 0.0f)) + metal::min(metal::max(q.x, metal::max(q.y, q.z)), 0.0f) - radius;
}

static float Pillars(float3 p)
{
    float2 cell = float2(metal::round(p.x / ::PillarPitch), metal::round(p.z / ::PillarPitch));
    float height = 1.2f + (2.4f * ::Hash(float3(cell.x, 7.0f, cell.y)));
    float3 local = float3(p.x - (cell.x * ::PillarPitch), p.y - height + 1.0f, p.z - (cell.y * ::PillarPitch));
    return metal::min(::RoundBox(local, float3(0.45f, height, 0.45f), 0.12f), ::PillarPitch * 0.4f);
}

static float3 RotateX(float3 p, float angle)
{
    float c = metal::cos(angle);
    float s = metal::sin(angle);
    return float3(p.x, (c * p.y) - (s * p.z), (s * p.y) + (c * p.z));
}

static float3 RotateY(float3 p, float angle)
{
    float c = metal::cos(angle);
    float s = metal::sin(angle);
    return float3((c * p.x) + (s * p.z), p.y, (c * p.z) - (s * p.x));
}

static float Torus(float3 p, float major, float minor)
{
    float ring = metal::length(float2(p.x, p.z)) - major;
    return metal::length(float2(ring, p.y)) - minor;
}

static float Sphere(float3 p, float radius)
{
    return metal::length(p) - radius;
}

static float Sculpture(float3 p, constant __Constants& __c)
{
    float3 center = float3(0.0f, 3.4f, 0.0f);
    float3 q = ::RotateX(::RotateY(p - center, __c.time * 0.4f), __c.time * 0.27f);
    float shape = ::Torus(q, 1.7f, 0.38f);
    for (int i = 0; i < 3; i = int(uint(i) + 1u))
    {
        float phase = (__c.time * (0.7f + (0.2f * float(i)))) + (float(i) * 2.0944f);
        float3 orbit = float3(metal::cos(phase) * 2.3f, metal::sin(phase * 1.3f) * 0.9f, metal::sin(phase) * 2.3f);
        shape = ::SmoothMin(shape, ::Sphere(p - center - orbit, 0.62f), 0.7f);
    }
    return shape;
}

static float Scene(float3 p, constant __Constants& __c)
{
    return metal::min(::SmoothMin(::Ground(p), ::Pillars(p), 0.35f), ::Sculpture(p, __c));
}

static float March(float3 origin, float3 direction, constant __Constants& __c)
{
    float t = 0.05f;
    for (int i = 0; i < ::MarchSteps; i = int(uint(i) + 1u))
    {
        float distance = ::Scene(origin + (direction * t), __c);
        if (distance < 0.0012f * t || t > ::FarPlane)
        {
            break;
        }
        t += distance * 0.85f;
    }
    return t;
}

static float3 Normal(float3 p, constant __Constants& __c)
{
    const float e = 0.0015f;
    float3 a = float3(1.0f, -1.0f, -1.0f);
    float3 b = float3(-1.0f, -1.0f, 1.0f);
    float3 c = float3(-1.0f, 1.0f, -1.0f);
    float3 d = float3(1.0f, 1.0f, 1.0f);
    return metal::normalize((a * ::Scene(p + (a * e), __c)) + (b * ::Scene(p + (b * e), __c)) + (c * ::Scene(p + (c * e), __c)) + (d * ::Scene(p + (d * e), __c)));
}

static float Material(float3 p, constant __Constants& __c)
{
    float ground = ::Ground(p);
    float pillars = ::Pillars(p);
    float sculpture = ::Sculpture(p, __c);
    if (sculpture < ground && sculpture < pillars)
    {
        return 2.0f;
    }
    return pillars < ground ? 1.0f : 0.0f;
}

static float SoftShadow(float3 origin, float3 direction, constant __Constants& __c)
{
    float light = 1.0f;
    float t = 0.06f;
    for (int i = 0; i < ::ShadowSteps; i = int(uint(i) + 1u))
    {
        float distance = ::Scene(origin + (direction * t), __c);
        light = metal::min(light, 10.0f * distance / t);
        t += metal::clamp(distance, 0.04f, 0.6f);
        if (light < 0.005f || t > 18.0f)
        {
            break;
        }
    }
    return metal::saturate(light);
}

static float Occlusion(float3 p, float3 normal, constant __Constants& __c)
{
    float occlusion = 0.0f;
    float weight = 1.0f;
    for (int i = 1; i <= 5; i = int(uint(i) + 1u))
    {
        float reach = 0.09f * float(i);
        occlusion += weight * (reach - ::Scene(p + (normal * reach), __c));
        weight *= 0.6f;
    }
    return metal::saturate(1.0f - (2.2f * occlusion));
}

kernel void SharpCompute_Benchmarks_Shaders_RaymarchedGarden(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __Constants& __c [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float2 size = float2(float(int(__grid.x)), float(int(__grid.y)));
    float2 pixel = float2(float(int(__tid.x)) + 0.5f, float(int(__tid.y)) + 0.5f);
    float2 uv = float2(((2.0f * pixel.x) - size.x) / size.y, (size.y - (2.0f * pixel.y)) / size.y);
    float orbit = __c.time * 0.18f;
    float3 eye = float3(metal::sin(orbit) * 11.0f, 4.6f + (0.8f * metal::sin(__c.time * 0.31f)), metal::cos(orbit) * 11.0f);
    float3 forward = metal::normalize(float3(0.0f, 2.6f, 0.0f) - eye);
    float3 right = metal::normalize(metal::cross(forward, float3(0.0f, 1.0f, 0.0f)));
    float3 up = metal::cross(right, forward);
    float3 direction = metal::normalize((right * uv.x) + (up * uv.y) + (forward * 1.6f));
    float3 sun = metal::normalize(float3(0.55f, 0.48f, -0.4f));
    float3 color = ::Sky(direction, sun);
    float t = ::March(eye, direction, __c);
    if (t < ::FarPlane)
    {
        float3 p = eye + (direction * t);
        float3 normal = ::Normal(p, __c);
        float material = ::Material(p, __c);
        float grain = ::Fbm(p * 2.7f, 5);
        float3 albedo = metal::mix(float3(0.2f, 0.27f, 0.12f), float3(0.42f, 0.36f, 0.24f), grain);
        if (material > 1.5f)
        {
            albedo = float3(0.85f, 0.42f, 0.22f) * (0.8f + (0.4f * grain));
        }
        else if (material > 0.5f)
        {
            albedo = float3(0.62f, 0.6f, 0.56f) * (0.7f + (0.5f * ::Noise(p * 6.0f)));
        }
        float diffuse = metal::saturate(metal::dot(normal, sun));
        float shadow = diffuse > 0.0f ? ::SoftShadow(p + (normal * 0.02f), sun, __c) : 0.0f;
        float occlusion = ::Occlusion(p, normal, __c);
        float skyLight = 0.5f + (0.5f * normal.y);
        float3 __ms_half = metal::normalize(sun - direction);
        float specular = metal::pow(metal::saturate(metal::dot(normal, __ms_half)), material > 1.5f ? 90.0f : 18.0f) * (material > 1.5f ? 1.4f : 0.15f);
        float3 lit = albedo * ((float3(1.25f, 1.1f, 0.9f) * (diffuse * shadow)) + (float3(0.22f, 0.3f, 0.45f) * (skyLight * occlusion)));
        lit += float3(1.0f, 0.9f, 0.75f) * (specular * shadow);
        float fog = 1.0f - metal::exp(-0.0009f * t * t);
        color = metal::mix(lit, ::Sky(direction, sun), fog);
    }
    color = color / (color + float3(0.85f));
    color = metal::pow(color, float3(0.4545f));
    color *= 1.0f - (0.18f * metal::dot(uv, uv) * 0.5f);
    __output.write(float4(metal::saturate(color), 1.0f), __tid.xy);
    return;
}
