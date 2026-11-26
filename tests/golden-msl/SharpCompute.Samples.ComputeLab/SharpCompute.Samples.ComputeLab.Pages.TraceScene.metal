// SharpCompute.Samples.ComputeLab.Pages.TraceScene (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_TraceScene, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Bounces = 4;
constant float3 ShaderMath_SunDirection = float3(0.53232735f, 0.7259009f, -0.43554053f);
constant float3 ShaderMath_SunColor = float3(3.2f, 2.9f, 2.5f);
constant float Offset = 0.001f;

static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");

struct Sphere
{
    packed_float3 Center;
    float Radius;
    packed_float3 Albedo;
    int Mirror;
};
static_assert(sizeof(Sphere) == 32, "SharpCompute: size of 'Sphere' does not match its C# layout");
static_assert(__builtin_offsetof(Sphere, Center) == 0, "SharpCompute: offset of 'Sphere.Center' does not match its C# layout");
static_assert(__builtin_offsetof(Sphere, Radius) == 12, "SharpCompute: offset of 'Sphere.Radius' does not match its C# layout");
static_assert(__builtin_offsetof(Sphere, Albedo) == 16, "SharpCompute: offset of 'Sphere.Albedo' does not match its C# layout");
static_assert(__builtin_offsetof(Sphere, Mirror) == 28, "SharpCompute: offset of 'Sphere.Mirror' does not match its C# layout");

struct Camera
{
    packed_float3 Position;
    packed_float3 Forward;
    packed_float3 Right;
    packed_float3 Up;
};
static_assert(sizeof(Camera) == 48, "SharpCompute: size of 'Camera' does not match its C# layout");
static_assert(__builtin_offsetof(Camera, Position) == 0, "SharpCompute: offset of 'Camera.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Camera, Forward) == 12, "SharpCompute: offset of 'Camera.Forward' does not match its C# layout");
static_assert(__builtin_offsetof(Camera, Right) == 24, "SharpCompute: offset of 'Camera.Right' does not match its C# layout");
static_assert(__builtin_offsetof(Camera, Up) == 36, "SharpCompute: offset of 'Camera.Up' does not match its C# layout");

struct RandomStream
{
    uint seed;
};
static_assert(sizeof(RandomStream) == 4, "SharpCompute: size of 'RandomStream' does not match its C# layout");
static_assert(__builtin_offsetof(RandomStream, seed) == 0, "SharpCompute: offset of 'RandomStream.seed' does not match its C# layout");

struct Ray
{
    packed_float3 Origin;
    packed_float3 Direction;
};
static_assert(sizeof(Ray) == 24, "SharpCompute: size of 'Ray' does not match its C# layout");
static_assert(__builtin_offsetof(Ray, Origin) == 0, "SharpCompute: offset of 'Ray.Origin' does not match its C# layout");
static_assert(__builtin_offsetof(Ray, Direction) == 12, "SharpCompute: offset of 'Ray.Direction' does not match its C# layout");

struct Hit
{
    float Distance;
    packed_float3 Normal;
    packed_float3 Albedo;
    int Mirror;
};
static_assert(sizeof(Hit) == 32, "SharpCompute: size of 'Hit' does not match its C# layout");
static_assert(__builtin_offsetof(Hit, Distance) == 0, "SharpCompute: offset of 'Hit.Distance' does not match its C# layout");
static_assert(__builtin_offsetof(Hit, Normal) == 4, "SharpCompute: offset of 'Hit.Normal' does not match its C# layout");
static_assert(__builtin_offsetof(Hit, Albedo) == 16, "SharpCompute: offset of 'Hit.Albedo' does not match its C# layout");
static_assert(__builtin_offsetof(Hit, Mirror) == 28, "SharpCompute: offset of 'Hit.Mirror' does not match its C# layout");

struct __Constants
{
    int sphereCount;
    Camera camera;
    int width;
    int height;
    int stride;
    int paths;
};
static_assert(sizeof(__Constants) == 68, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, sphereCount) == 0, "SharpCompute: offset of '__Constants.sphereCount' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, camera) == 4, "SharpCompute: offset of '__Constants.camera' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 52, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 56, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, stride) == 60, "SharpCompute: offset of '__Constants.stride' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, paths) == 64, "SharpCompute: offset of '__Constants.paths' does not match its C# layout");

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

static RandomStream RandomStream_new(uint seed);
static uint ShaderMath_Hash(uint x);
static float RandomStream_Next(thread RandomStream& __this);
static float3 Ray_At(Ray __this, float distance);
static float3 ShaderMath_Pick_float3(bool condition, float3 whenTrue, float3 whenFalse);
static float Sphere_Intersect(Sphere __this, Ray ray);
static bool Hit_Found(Hit __this);
static float3 Sphere_NormalAt(Sphere __this, float3 point);
static Hit Nearest(Ray ray, device Sphere* spheres, constant __Constants& __c);
static float3 ShaderMath_Sky(float3 direction);
static Ray Ray_new(float3 origin, float3 direction);
static float3 RandomStream_NextDirection(thread RandomStream& __this);
static float3 Trace(Ray ray, thread RandomStream& random, device Sphere* spheres, constant __Constants& __c);
static Ray Camera_RayThrough(Camera __this, float2 point);

static RandomStream RandomStream_new(uint seed)
{
    RandomStream __this = RandomStream{};
    __this.seed = seed;
    return __this;
}

static uint ShaderMath_Hash(uint x)
{
    x ^= x >> 16;
    x *= 2146121005u;
    x ^= x >> 15;
    x *= 2221713035u;
    x ^= x >> 16;
    return x;
}

static float RandomStream_Next(thread RandomStream& __this)
{
    __this.seed = ::ShaderMath_Hash(__this.seed + 2654435769u);
    return float(__this.seed >> 8) * (1.0f / 16777216.0f);
}

static float3 Ray_At(Ray __this, float distance)
{
    return __this.Origin + (__this.Direction * distance);
}

static float3 ShaderMath_Pick_float3(bool condition, float3 whenTrue, float3 whenFalse)
{
    return condition ? whenTrue : whenFalse;
}

static float Sphere_Intersect(Sphere __this, Ray ray)
{
    float3 offset = ray.Origin - __this.Center;
    float b = metal::dot(offset, ray.Direction);
    float discriminant = (b * b) - (metal::dot(offset, offset) - (__this.Radius * __this.Radius));
    if (discriminant < 0.0f)
    {
        return -1.0f;
    }
    float distance = -b - metal::sqrt(discriminant);
    return distance > 0.0f ? distance : -1.0f;
}

static bool Hit_Found(Hit __this)
{
    return __this.Distance > 0.0f;
}

static float3 Sphere_NormalAt(Sphere __this, float3 point)
{
    return (point - __this.Center) / __this.Radius;
}

static Hit Nearest(Ray ray, device Sphere* spheres, constant __Constants& __c)
{
    Hit hit = Hit{};
    hit.Distance = -1.0f;
    if (ray.Direction.y < -0.0001f)
    {
        float distance = -ray.Origin.y / ray.Direction.y;
        if (distance > 0.0f)
        {
            float3 point = ::Ray_At(ray, distance);
            bool dark = ((int(uint(int(metal::floor(point.x))) + uint(int(metal::floor(point.z))))) & 1) != 0;
            float3 square = ::ShaderMath_Pick_float3(dark, float3(0.22f, 0.24f, 0.28f), float3(0.72f, 0.72f, 0.7f));
            hit.Distance = distance;
            hit.Normal = float3(0.0f, 1.0f, 0.0f);
            hit.Albedo = metal::mix(square, float3(0.47f, 0.48f, 0.49f), metal::smoothstep(12.0f, 45.0f, distance));
        }
    }
    for (int i = 0; i < __c.sphereCount; i = int(uint(i) + 1u))
    {
        float distance = ::Sphere_Intersect(spheres[i], ray);
        if (distance > 0.0f && (!::Hit_Found(hit) || distance < hit.Distance))
        {
            Sphere sphere = spheres[i];
            hit.Distance = distance;
            hit.Normal = ::Sphere_NormalAt(sphere, ::Ray_At(ray, distance));
            hit.Albedo = sphere.Albedo;
            hit.Mirror = sphere.Mirror;
        }
    }
    return hit;
}

static float3 ShaderMath_Sky(float3 direction)
{
    float height = metal::saturate((direction.y * 0.5f) + 0.5f);
    float3 gradient = metal::mix(float3(0.62f, 0.7f, 0.82f), float3(0.1f, 0.25f, 0.65f), metal::smoothstep(0.48f, 0.95f, height));
    float glow = metal::pow(metal::saturate(metal::dot(direction, ::ShaderMath_SunDirection)), 48.0f);
    return gradient + (::ShaderMath_SunColor * (glow * 0.6f));
}

static Ray Ray_new(float3 origin, float3 direction)
{
    Ray __this = Ray{};
    __this.Origin = origin;
    __this.Direction = metal::normalize(direction);
    return __this;
}

static float3 RandomStream_NextDirection(thread RandomStream& __this)
{
    float z = 1.0f - (2.0f * ::RandomStream_Next(__this));
    float angle = 6.2831855f * ::RandomStream_Next(__this);
    float radius = metal::sqrt(metal::max(0.0f, 1.0f - (z * z)));
    return float3(radius * metal::cos(angle), radius * metal::sin(angle), z);
}

static float3 Trace(Ray ray, thread RandomStream& random, device Sphere* spheres, constant __Constants& __c)
{
    float3 arriving = float3(0);
    float3 carried = float3(1.0f, 1.0f, 1.0f);
    for (int bounce = 0; bounce < ::Bounces; bounce = int(uint(bounce) + 1u))
    {
        Hit hit = ::Nearest(ray, spheres, __c);
        if (!::Hit_Found(hit))
        {
            return arriving + (carried * ::ShaderMath_Sky(ray.Direction));
        }
        float3 point = ::Ray_At(ray, hit.Distance) + (hit.Normal * ::Offset);
        carried *= hit.Albedo;
        if (hit.Mirror != 0)
        {
            ray = ::Ray_new(point, metal::reflect(ray.Direction, hit.Normal));
            continue;
        }
        Ray toSun = ::Ray_new(point, ::ShaderMath_SunDirection + (::RandomStream_NextDirection(random) * 0.03f));
        float facing = metal::dot(hit.Normal, toSun.Direction);
        if (facing > 0.0f && !::Hit_Found(::Nearest(toSun, spheres, __c)))
        {
            arriving += carried * ::ShaderMath_SunColor * (facing * 0.35f);
        }
        ray = ::Ray_new(point, hit.Normal + ::RandomStream_NextDirection(random));
    }
    return arriving;
}

static Ray Camera_RayThrough(Camera __this, float2 point)
{
    return ::Ray_new(__this.Position, __this.Forward + (__this.Right * point.x) + (__this.Up * point.y));
}

kernel void SharpCompute_Samples_ComputeLab_Pages_TraceScene(
    device packed_float3* light [[buffer(0)]],
    device Sphere* spheres [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int x = int(__tid.x * uint(__c.stride));
    int y = int(__tid.y * uint(__c.stride));
    RandomStream random = ::RandomStream_new(uint(int((uint(y) * uint(__c.width)) + uint(x))));
    float3 sum = float3(0);
    for (int path = 0; path < __c.paths; path = int(uint(path) + 1u))
    {
        float dx = ::RandomStream_Next(random);
        float dy = ::RandomStream_Next(random);
        float2 point = float2(((float(x) + dx) / float(__c.width) * 2.0f) - 1.0f, 1.0f - ((float(y) + dy) / float(__c.height) * 2.0f));
        sum += ::Trace(::Camera_RayThrough(__c.camera, point), random, spheres, __c);
    }
    light[int((__tid.y * __grid.x) + __tid.x)] = sum / float(__c.paths);
}
