// SharpCompute.Samples.Shooter.Rendering.DrawScene (SharpCompute.Samples.Shooter): entry point SharpCompute_Samples_Shooter_Rendering_DrawScene, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

constant int ChunkSize = 64;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct Prim
{
    packed_float2 A;
    packed_float2 B;
    packed_float3 Color;
    float Width;
    float Radius;
    float Glow;
    float Falloff;
    int Kind;
    packed_float4 Bounds;
};
static_assert(sizeof(Prim) == 64, "SharpCompute: size of 'Prim' does not match its C# layout");
static_assert(__builtin_offsetof(Prim, A) == 0, "SharpCompute: offset of 'Prim.A' does not match its C# layout");
static_assert(__builtin_offsetof(Prim, B) == 8, "SharpCompute: offset of 'Prim.B' does not match its C# layout");
static_assert(__builtin_offsetof(Prim, Color) == 16, "SharpCompute: offset of 'Prim.Color' does not match its C# layout");
static_assert(__builtin_offsetof(Prim, Width) == 28, "SharpCompute: offset of 'Prim.Width' does not match its C# layout");
static_assert(__builtin_offsetof(Prim, Radius) == 32, "SharpCompute: offset of 'Prim.Radius' does not match its C# layout");
static_assert(__builtin_offsetof(Prim, Glow) == 36, "SharpCompute: offset of 'Prim.Glow' does not match its C# layout");
static_assert(__builtin_offsetof(Prim, Falloff) == 40, "SharpCompute: offset of 'Prim.Falloff' does not match its C# layout");
static_assert(__builtin_offsetof(Prim, Kind) == 44, "SharpCompute: offset of 'Prim.Kind' does not match its C# layout");
static_assert(__builtin_offsetof(Prim, Bounds) == 48, "SharpCompute: offset of 'Prim.Bounds' does not match its C# layout");

struct Particle
{
    packed_float2 Position;
    packed_float2 Velocity;
    packed_float3 Color;
    float Life;
    float MaxLife;
    float Size;
    float Drag;
    float Streak;
};
static_assert(sizeof(Particle) == 48, "SharpCompute: size of 'Particle' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Position) == 0, "SharpCompute: offset of 'Particle.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Velocity) == 8, "SharpCompute: offset of 'Particle.Velocity' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Color) == 16, "SharpCompute: offset of 'Particle.Color' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Life) == 28, "SharpCompute: offset of 'Particle.Life' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, MaxLife) == 32, "SharpCompute: offset of 'Particle.MaxLife' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Size) == 36, "SharpCompute: offset of 'Particle.Size' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Drag) == 40, "SharpCompute: offset of 'Particle.Drag' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Streak) == 44, "SharpCompute: offset of 'Particle.Streak' does not match its C# layout");

struct __Constants
{
    int width;
    int height;
    int tilesX;
    int maxChunks;
    int maxPrims;
    float time;
    float energy;
    float parallaxX;
    float parallaxY;
    float wall;
};
static_assert(sizeof(__Constants) == 40, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, tilesX) == 8, "SharpCompute: offset of '__Constants.tilesX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, maxChunks) == 12, "SharpCompute: offset of '__Constants.maxChunks' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, maxPrims) == 16, "SharpCompute: offset of '__Constants.maxPrims' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 20, "SharpCompute: offset of '__Constants.time' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, energy) == 24, "SharpCompute: offset of '__Constants.energy' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, parallaxX) == 28, "SharpCompute: offset of '__Constants.parallaxX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, parallaxY) == 32, "SharpCompute: offset of '__Constants.parallaxY' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, wall) == 36, "SharpCompute: offset of '__Constants.wall' does not match its C# layout");

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

static float PixelShading_Hash(int x, int y, uint seed);
static float ValueNoise(float2 p, uint seed);
static float Fbm(float2 p, uint seed);
static float3 Background(float2 p, constant __Constants& __c);
static float3 Walls(float2 p, constant __Constants& __c);
static float Segment(float2 p, float2 a, float2 b);
static float3 Shade(Prim q, float2 p);
static float3 Spark(Particle q, float2 p);

static float PixelShading_Hash(int x, int y, uint seed)
{
    uint h = (uint(x) * 374761393u) + (uint(y) * 668265263u) + (seed * 2246822519u);
    h = (h ^ (h >> 13)) * 1274126177u;
    h ^= h >> 16;
    return float(h & 16777215u) / 16777215.0f;
}

static float ValueNoise(float2 p, uint seed)
{
    float fx = metal::floor(p.x);
    float fy = metal::floor(p.y);
    int ix = __ms_sat_int(fx);
    int iy = __ms_sat_int(fy);
    float u = p.x - fx;
    float v = p.y - fy;
    u = u * u * (3.0f - (2.0f * u));
    v = v * v * (3.0f - (2.0f * v));
    float a = ::PixelShading_Hash(ix, iy, seed);
    float b = ::PixelShading_Hash(int(uint(ix) + 1u), iy, seed);
    float c = ::PixelShading_Hash(ix, int(uint(iy) + 1u), seed);
    float d = ::PixelShading_Hash(int(uint(ix) + 1u), int(uint(iy) + 1u), seed);
    return metal::mix(metal::mix(a, b, u), metal::mix(c, d, u), v);
}

static float Fbm(float2 p, uint seed)
{
    float sum = 0.0f;
    float amplitude = 0.5f;
    for (int i = 0; i < 4; i = int(uint(i) + 1u))
    {
        sum += amplitude * ::ValueNoise(p, seed + uint(i));
        p = (p * 2.03f) + float2(17.1f, 9.3f);
        amplitude *= 0.5f;
    }
    return sum / 0.9375f;
}

static float3 Background(float2 p, constant __Constants& __c)
{
    float2 far = p + float2(__c.parallaxX * 0.04f, __c.parallaxY * 0.04f);
    float n1 = ::Fbm((far * 0.0055f) + float2(__c.time * 0.01f, __c.time * 0.004f), 3u);
    float n2 = ::Fbm((far * 0.009f) + float2((n1 * 1.6f) - (__c.time * 0.006f), 40.0f + (__c.time * 0.008f)), 11u);
    float depth = p.y / float(__c.height);
    float3 color = metal::mix(float3(0.006f, 0.008f, 0.024f), float3(0.014f, 0.008f, 0.032f), depth);
    color += float3(0.0f, 0.055f, 0.075f) * metal::smoothstep(0.42f, 0.85f, n1);
    color += float3(0.075f, 0.012f, 0.07f) * metal::smoothstep(0.5f, 0.92f, n2);
    color += float3(0.03f, 0.02f, 0.06f) * (__c.energy * metal::smoothstep(0.35f, 0.8f, n1));
    for (int layer = 0; layer < 3; layer = int(uint(layer) + 1u))
    {
        float l = float(layer);
        float cell = 11.0f + (l * 7.0f);
        float2 d = p + float2((__c.parallaxX * (0.06f + (0.07f * l))) + (__c.time * (2.0f + (3.0f * l))), (__c.parallaxY * (0.06f + (0.07f * l))) + (__c.time * (0.6f + l)));
        int cx = __ms_sat_int(metal::floor(d.x / cell));
        int cy = __ms_sat_int(metal::floor(d.y / cell));
        uint seed = 50u + uint(layer);
        float h = ::PixelShading_Hash(cx, cy, seed);
        if (h > 0.9f)
        {
            float2 star = float2((float(cx) + 0.2f + (0.6f * ::PixelShading_Hash(cx, cy, seed + 10u))) * cell, (float(cy) + 0.2f + (0.6f * ::PixelShading_Hash(cx, cy, seed + 20u))) * cell);
            float b = metal::saturate(1.0f - (metal::length(d - star) / (0.8f + (0.4f * l))));
            float twinkle = 0.6f + (0.4f * metal::sin((__c.time * (1.0f + (h * 3.0f))) + (h * 50.0f)));
            color += float3(0.35f, 0.45f, 0.75f) * (b * b * twinkle * (0.2f + (0.25f * l)));
        }
    }
    return color;
}

static float3 Walls(float2 p, constant __Constants& __c)
{
    float bx = metal::min(p.x - __c.wall, (float(__c.width) - __c.wall) - p.x);
    float by = metal::min(p.y - __c.wall, (float(__c.height) - __c.wall) - p.y);
    float inside = metal::min(bx, by);
    float d = metal::abs(inside);
    float3 tint = metal::mix(float3(0.25f, 0.4f, 1.0f), float3(0.95f, 0.25f, 0.8f), p.x / float(__c.width));
    float3 light = tint * ((metal::saturate(1.0f - d) * 0.9f) + (0.1f * metal::exp(-d * 0.25f)));
    return inside < 0.0f ? light - float3(0.004f, 0.004f, 0.012f) : light;
}

static float Segment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a;
    float2 ba = b - a;
    float h = metal::saturate(metal::dot(pa, ba) / metal::max(metal::dot(ba, ba), 1E-06f));
    return metal::length(pa - (ba * h));
}

static float3 Shade(Prim q, float2 p)
{
    float d;
    if (q.Kind == 0)
    {
        d = ::Segment(p, q.A, q.B) - q.Width;
    }
    else if (q.Kind == 1)
    {
        d = metal::abs(metal::length(p - q.A) - q.Radius) - q.Width;
    }
    else
    {
        d = metal::length(p - q.A) - q.Radius;
    }
    float core = metal::saturate(0.5f - d);
    float glow = q.Glow * metal::exp(-metal::max(d, 0.0f) * q.Falloff);
    return q.Color * (core + glow);
}

static float3 Spark(Particle q, float2 p)
{
    float2 tail = q.Position - (q.Velocity * q.Streak);
    float reach = q.Size + 1.0f;
    if (p.x < metal::min(q.Position.x, tail.x) - reach || p.x > metal::max(q.Position.x, tail.x) + reach || p.y < metal::min(q.Position.y, tail.y) - reach || p.y > metal::max(q.Position.y, tail.y) + reach)
    {
        return float3(0.0f, 0.0f, 0.0f);
    }
    float k = metal::saturate(1.0f - (::Segment(p, q.Position, tail) / reach));
    float t = metal::saturate(q.Life / q.MaxLife);
    float intensity = k * k * t;
    return (q.Color * (intensity * 1.3f)) + (float3(1.0f, 1.0f, 1.0f) * (intensity * t * t * 0.6f));
}

kernel void SharpCompute_Samples_Shooter_Rendering_DrawScene(
    device packed_float4* hdr [[buffer(0)]],
    const device Prim* prims [[buffer(1)]],
    device Particle* particles [[buffer(2)]],
    device int* tileChunks [[buffer(3)]],
    device int* tilePrims [[buffer(4)]],
    device int* tileCounts [[buffer(5)]],
    constant __Constants& __c [[buffer(6)]],
    constant __GridWindow& __window [[buffer(7), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float2 p = float2(float(x) + 0.5f, float(y) + 0.5f);
    float3 color = ::Background(p, __c) + ::Walls(p, __c);
    int tile = int((uint(y / 16) * uint(__c.tilesX)) + uint(x / 16));
    int primCount = tileCounts[int((uint(tile) * 2u) + 1u)];
    for (int k = 0; k < primCount; k = int(uint(k) + 1u))
    {
        color += ::Shade(prims[tilePrims[int((uint(tile) * uint(__c.maxPrims)) + uint(k))]], p);
    }
    int chunkCount = tileCounts[int(uint(tile) * 2u)];
    for (int k = 0; k < chunkCount; k = int(uint(k) + 1u))
    {
        int first = int(uint(tileChunks[int((uint(tile) * uint(__c.maxChunks)) + uint(k))]) * uint(::ChunkSize));
        for (int i = 0; i < ::ChunkSize; i = int(uint(i) + 1u))
        {
            Particle q = particles[int(uint(first) + uint(i))];
            if (q.Life > 0.0f)
            {
                color += ::Spark(q, p);
            }
        }
    }
    hdr[int((uint(y) * uint(__c.width)) + uint(x))] = float4(color, 1.0f);
}
