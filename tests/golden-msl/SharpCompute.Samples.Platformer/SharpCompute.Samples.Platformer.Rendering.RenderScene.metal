// SharpCompute.Samples.Platformer.Rendering.RenderScene (SharpCompute.Samples.Platformer): entry point SharpCompute_Samples_Platformer_Rendering_RenderScene, compile options None
#include <metal_stdlib>

using namespace metal;

constant int AtlasWidth = 128;

struct SpriteInstance
{
    int X;
    int Y;
    int Frame;
    int Flags;
};
static_assert(sizeof(SpriteInstance) == 16, "SharpCompute: size of 'SpriteInstance' does not match its C# layout");
static_assert(__builtin_offsetof(SpriteInstance, X) == 0, "SharpCompute: offset of 'SpriteInstance.X' does not match its C# layout");
static_assert(__builtin_offsetof(SpriteInstance, Y) == 4, "SharpCompute: offset of 'SpriteInstance.Y' does not match its C# layout");
static_assert(__builtin_offsetof(SpriteInstance, Frame) == 8, "SharpCompute: offset of 'SpriteInstance.Frame' does not match its C# layout");
static_assert(__builtin_offsetof(SpriteInstance, Flags) == 12, "SharpCompute: offset of 'SpriteInstance.Flags' does not match its C# layout");

struct LightSource
{
    packed_float2 Position;
    float Radius;
    float Intensity;
    packed_float3 Color;
    float Shadows;
};
static_assert(sizeof(LightSource) == 32, "SharpCompute: size of 'LightSource' does not match its C# layout");
static_assert(__builtin_offsetof(LightSource, Position) == 0, "SharpCompute: offset of 'LightSource.Position' does not match its C# layout");
static_assert(__builtin_offsetof(LightSource, Radius) == 8, "SharpCompute: offset of 'LightSource.Radius' does not match its C# layout");
static_assert(__builtin_offsetof(LightSource, Intensity) == 12, "SharpCompute: offset of 'LightSource.Intensity' does not match its C# layout");
static_assert(__builtin_offsetof(LightSource, Color) == 16, "SharpCompute: offset of 'LightSource.Color' does not match its C# layout");
static_assert(__builtin_offsetof(LightSource, Shadows) == 28, "SharpCompute: offset of 'LightSource.Shadows' does not match its C# layout");

struct Particle
{
    packed_float2 Position;
    packed_float2 Velocity;
    packed_float3 Color;
    float Life;
    float MaxLife;
    float Size;
    float Gravity;
    float Drag;
};
static_assert(sizeof(Particle) == 48, "SharpCompute: size of 'Particle' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Position) == 0, "SharpCompute: offset of 'Particle.Position' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Velocity) == 8, "SharpCompute: offset of 'Particle.Velocity' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Color) == 16, "SharpCompute: offset of 'Particle.Color' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Life) == 28, "SharpCompute: offset of 'Particle.Life' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, MaxLife) == 32, "SharpCompute: offset of 'Particle.MaxLife' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Size) == 36, "SharpCompute: offset of 'Particle.Size' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Gravity) == 40, "SharpCompute: offset of 'Particle.Gravity' does not match its C# layout");
static_assert(__builtin_offsetof(Particle, Drag) == 44, "SharpCompute: offset of 'Particle.Drag' does not match its C# layout");

struct Glyph
{
    int X;
    int Y;
    int Index;
    uint Color;
    int Scale;
};
static_assert(sizeof(Glyph) == 20, "SharpCompute: size of 'Glyph' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, X) == 0, "SharpCompute: offset of 'Glyph.X' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, Y) == 4, "SharpCompute: offset of 'Glyph.Y' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, Index) == 8, "SharpCompute: offset of 'Glyph.Index' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, Color) == 12, "SharpCompute: offset of 'Glyph.Color' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, Scale) == 16, "SharpCompute: offset of 'Glyph.Scale' does not match its C# layout");

struct __Constants
{
    int width;
    int cameraX;
    int cameraY;
    int levelWidth;
    int levelHeight;
    int spriteCount;
    int lightCount;
    int glyphCount;
    float time;
    float ambient;
    float fade;
    uint __length_particles;
};
static_assert(sizeof(__Constants) == 48, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, cameraX) == 4, "SharpCompute: offset of '__Constants.cameraX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, cameraY) == 8, "SharpCompute: offset of '__Constants.cameraY' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, levelWidth) == 12, "SharpCompute: offset of '__Constants.levelWidth' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, levelHeight) == 16, "SharpCompute: offset of '__Constants.levelHeight' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, spriteCount) == 20, "SharpCompute: offset of '__Constants.spriteCount' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, lightCount) == 24, "SharpCompute: offset of '__Constants.lightCount' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, glyphCount) == 28, "SharpCompute: offset of '__Constants.glyphCount' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, time) == 32, "SharpCompute: offset of '__Constants.time' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, ambient) == 36, "SharpCompute: offset of '__Constants.ambient' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, fade) == 40, "SharpCompute: offset of '__Constants.fade' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, __length_particles) == 44, "SharpCompute: offset of '__Constants.__length_particles' does not match its C# layout");

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
static float3 Spires(float3 color, int px, int py, int spacing, int band, float3 spireColor, uint seed);
static float3 Background(int x, int y, constant __Constants& __c);
static uint Tile(int tx, int ty, const device uint* tiles, constant __Constants& __c);
static float3 Rock(int wx, int wy, int lx, int ly, const device uint* tiles, constant __Constants& __c);
static float3 Ledge(int wx, int ly);
static float Spike(int lx, int ly);
static float3 PixelShading_UnpackRgb(uint pixel);
static bool Occluded(float2 p, float2 lightPosition, float distance, const device uint* tiles, constant __Constants& __c);
static bool PixelShading_GlyphCovers(const device uint* __local_font, int glyph, int gx, int gy, int scale);
static uint PixelShading_PackRgb(float3 color);

static float PixelShading_Hash(int x, int y, uint seed)
{
    uint h = (uint(x) * 374761393u) + (uint(y) * 668265263u) + (seed * 2246822519u);
    h = (h ^ (h >> 13)) * 1274126177u;
    h ^= h >> 16;
    return float(h & 16777215u) / 16777215.0f;
}

static float3 Spires(float3 color, int px, int py, int spacing, int band, float3 spireColor, uint seed)
{
    int column = px / spacing;
    int localX = int(uint(px) - (uint(column) * uint(spacing)));
    int bandIndex = py / band;
    int localY = int(uint(py) - (uint(bandIndex) * uint(band)));
    float h = ::PixelShading_Hash(column, bandIndex, seed);
    float length = 20.0f + (h * float(band) * 0.45f);
    float __ms_half = float(spacing) * (0.25f + (::PixelShading_Hash(column, bandIndex, seed + 1u) * 0.2f));
    float dx = metal::abs(float(localX) - (float(spacing) * 0.5f));
    return float(localY) < length * (1.0f - (dx / __ms_half)) ? spireColor : color;
}

static float3 Background(int x, int y, constant __Constants& __c)
{
    float depth = float(y) / 270.0f;
    float3 color = metal::mix(float3(0.035f, 0.025f, 0.075f), float3(0.07f, 0.045f, 0.13f), depth);
    color = ::Spires(color, int(uint(x) + uint(int(uint(__c.cameraX) * 3u) / 10)), int(uint(y) + uint(int(uint(__c.cameraY) * 3u) / 10)), 20, 150, float3(0.075f, 0.055f, 0.15f), 3u);
    color = ::Spires(color, int(uint(x) + uint(int(uint(__c.cameraX) * 6u) / 10)), int(uint(y) + uint(int(uint(__c.cameraY) * 6u) / 10)), 28, 190, float3(0.12f, 0.08f, 0.21f), 11u);
    int fx = int(uint(x) + uint(__c.cameraX / 5));
    int fy = int(uint(y) + uint(__c.cameraY / 5));
    int gx = fx / 6;
    int gy = fy / 6;
    float h = ::PixelShading_Hash(gx, gy, 29u);
    if (h > 0.996f && int(uint(fx) - (uint(gx) * 6u)) == 3 && int(uint(fy) - (uint(gy) * 6u)) == 3)
    {
        float twinkle = 0.5f + (0.5f * metal::sin((__c.time * 1.7f) + (h * 300.0f)));
        color += float3(0.25f, 0.3f, 0.5f) * twinkle;
    }
    return color;
}

static uint Tile(int tx, int ty, const device uint* tiles, constant __Constants& __c)
{
    if (tx < 0 || tx >= __c.levelWidth || ty >= __c.levelHeight)
    {
        return 1u;
    }
    return ty < 0 ? 0u : tiles[int((uint(ty) * uint(__c.levelWidth)) + uint(tx))];
}

static float3 Rock(int wx, int wy, int lx, int ly, const device uint* tiles, constant __Constants& __c)
{
    float n = ::PixelShading_Hash(wx >> 1, wy >> 1, 5u);
    float cell = ::PixelShading_Hash(wx >> 4, wy >> 4, 17u);
    float3 color = metal::mix(float3(0.22f, 0.18f, 0.29f), float3(0.33f, 0.27f, 0.41f), (n * 0.6f) + (cell * 0.4f));
    bool openAbove = ::Tile(wx >> 4, int(uint(wy >> 4) - 1u), tiles, __c) != 1u;
    bool openBelow = ::Tile(wx >> 4, int(uint(wy >> 4) + 1u), tiles, __c) != 1u;
    bool openLeft = ::Tile(int(uint(wx >> 4) - 1u), wy >> 4, tiles, __c) != 1u;
    bool openRight = ::Tile(int(uint(wx >> 4) + 1u), wy >> 4, tiles, __c) != 1u;
    if (openAbove && ly < 2)
    {
        float moss = ::PixelShading_Hash(wx, 3, 41u);
        color = moss > 0.35f ? float3(0.2f, 0.42f, 0.36f) : float3(0.36f, 0.31f, 0.43f);
    }
    else if (openAbove && ly < 4 && ::PixelShading_Hash(wx, wy, 43u) > 0.6f)
    {
        color = float3(0.17f, 0.33f, 0.29f);
    }
    else if ((openLeft && lx < 1) || (openRight && lx > 14))
    {
        color *= 1.35f;
    }
    else if (openBelow && ly > 13)
    {
        color *= 0.6f;
    }
    return color;
}

static float3 Ledge(int wx, int ly)
{
    if (ly == 0)
    {
        return float3(0.62f, 0.44f, 0.3f);
    }
    if ((wx & 7) == 0)
    {
        return float3(0.2f, 0.12f, 0.1f);
    }
    float grain = ::PixelShading_Hash(wx >> 2, ly, 59u);
    return metal::mix(float3(0.36f, 0.23f, 0.16f), float3(0.45f, 0.29f, 0.19f), grain) * (ly == 3 ? 0.7f : 1.0f);
}

static float Spike(int lx, int ly)
{
    int index = lx / 4;
    float center = (float(index) * 4.0f) + 1.5f;
    float offset = metal::abs(float(lx) - center);
    float height = 10.0f - (offset * 4.0f);
    float fromBottom = 15.0f - float(ly);
    if (fromBottom >= height)
    {
        return 0.0f;
    }
    return 0.4f + (0.6f * (fromBottom / metal::max(height, 1.0f))) * (offset < 0.75f ? 1.0f : 0.7f);
}

static float3 PixelShading_UnpackRgb(uint pixel)
{
    return float3(float(pixel & 255u) / 255.0f, float((pixel >> 8) & 255u) / 255.0f, float((pixel >> 16) & 255u) / 255.0f);
}

static bool Occluded(float2 p, float2 lightPosition, float distance, const device uint* tiles, constant __Constants& __c)
{
    float2 direction = (lightPosition - p) / metal::max(distance, 0.001f);
    int steps = int(distance / 3.0f);
    for (int k = 2; k < steps; k = int(uint(k) + 1u))
    {
        float2 q = p + (direction * (float(k) * 3.0f));
        int tx = (int(uint(int(metal::floor(q.x))) + uint(__c.cameraX))) >> 4;
        int ty = (int(uint(int(metal::floor(q.y))) + uint(__c.cameraY))) >> 4;
        if (::Tile(tx, ty, tiles, __c) == 1u)
        {
            return true;
        }
    }
    return false;
}

static bool PixelShading_GlyphCovers(const device uint* __local_font, int glyph, int gx, int gy, int scale)
{
    return gx >= 0 && gy >= 0 && gx < int(uint(3) * uint(scale)) && gy < int(uint(5) * uint(scale)) && ((__local_font[glyph] >> (int((uint(gy / scale) * uint(3)) + uint(gx / scale)))) & 1u) != 0u;
}

static uint PixelShading_PackRgb(float3 color)
{
    color = metal::saturate(color);
    uint red = uint((color.x * 255.0f) + 0.5f);
    uint green = uint((color.y * 255.0f) + 0.5f);
    uint blue = uint((color.z * 255.0f) + 0.5f);
    return red | (green << 8) | (blue << 16) | 4278190080u;
}

kernel void SharpCompute_Samples_Platformer_Rendering_RenderScene(
    device uint* output [[buffer(0)]],
    const device uint* tiles [[buffer(1)]],
    const device uint* atlas [[buffer(2)]],
    const device SpriteInstance* sprites [[buffer(3)]],
    const device LightSource* lights [[buffer(4)]],
    device Particle* particles [[buffer(5)]],
    const device Glyph* glyphs [[buffer(6)]],
    const device uint* font [[buffer(7)]],
    constant __Constants& __c [[buffer(8)]],
    constant __GridWindow& __window [[buffer(9), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    int wx = int(uint(x) + uint(__c.cameraX));
    int wy = int(uint(y) + uint(__c.cameraY));
    float3 albedo = ::Background(x, y, __c);
    float emissive = 0.0f;
    float surface = 0.0f;
    uint tile = ::Tile(wx >> 4, wy >> 4, tiles, __c);
    int lx = wx & 15;
    int ly = wy & 15;
    if (tile == 1u)
    {
        albedo = ::Rock(wx, wy, lx, ly, tiles, __c);
        surface = 1.0f;
    }
    else if (tile == 2u && ly < 4)
    {
        albedo = ::Ledge(wx, ly);
        surface = 1.0f;
    }
    else if (tile == 3u)
    {
        float spike = ::Spike(lx, ly);
        if (spike > 0.0f)
        {
            albedo = metal::mix(float3(0.42f, 0.16f, 0.55f), float3(0.95f, 0.55f, 0.95f), spike);
            emissive = 0.45f;
            surface = 1.0f;
        }
    }
    for (int i = 0; i < __c.spriteCount; i = int(uint(i) + 1u))
    {
        SpriteInstance s = sprites[i];
        int sx = int(uint(x) - uint(s.X));
        int sy = int(uint(y) - uint(s.Y));
        if (sx >= 0 && sx < 16 && sy >= 0 && sy < 16)
        {
            if ((s.Flags & 1) != 0)
            {
                sx = int(15u - uint(sx));
            }
            uint texel = atlas[int((((uint(s.Frame / 8) * 16u) + uint(sy)) * uint(::AtlasWidth)) + (uint(s.Frame % 8) * 16u) + uint(sx))];
            uint alpha = texel >> 24;
            if (alpha != 0u)
            {
                albedo = ::PixelShading_UnpackRgb(texel);
                emissive = alpha == 128u ? 1.0f : 0.0f;
                surface = 1.0f;
            }
        }
    }
    float2 p = float2(float(x) + 0.5f, float(y) + 0.5f);
    float3 light = float3(__c.ambient, __c.ambient, __c.ambient * 1.4f);
    float3 haze = float3(0.0f, 0.0f, 0.0f);
    for (int i = 0; i < __c.lightCount; i = int(uint(i) + 1u))
    {
        LightSource l = lights[i];
        float d = metal::distance(p, l.Position);
        if (d >= l.Radius)
        {
            continue;
        }
        float falloff = 1.0f - (d / l.Radius);
        falloff *= falloff * l.Intensity;
        if (l.Shadows > 0.0f && ::Occluded(p, l.Position, d, tiles, __c))
        {
            falloff *= 0.12f;
        }
        light += l.Color * falloff;
        haze += l.Color * (falloff * falloff * 0.35f);
    }
    float3 color = (albedo * (light * (1.0f - emissive))) + (albedo * emissive * 1.15f) + (haze * (1.0f - (surface * 0.6f)));
    int particleCount = int(__c.__length_particles);
    for (int i = 0; i < particleCount; i = int(uint(i) + 1u))
    {
        Particle q = particles[i];
        if (q.Life > 0.0f)
        {
            float dx = metal::abs((q.Position.x - float(__c.cameraX)) - p.x);
            float dy = metal::abs((q.Position.y - float(__c.cameraY)) - p.y);
            float size = q.Size * metal::saturate((q.Life / q.MaxLife) + 0.3f);
            if (dx < size && dy < size)
            {
                color += q.Color * metal::saturate(q.Life / q.MaxLife) * 1.4f;
            }
        }
    }
    float u = (float(x) / float(__c.width)) - 0.5f;
    float v = (float(y) / 270.0f) - 0.5f;
    color *= (1.0f - (((u * u) + (v * v)) * 0.9f)) * __c.fade;
    for (int i = 0; i < __c.glyphCount; i = int(uint(i) + 1u))
    {
        Glyph g = glyphs[i];
        int gx = int(uint(x) - uint(g.X));
        int gy = int(uint(y) - uint(g.Y));
        if (::PixelShading_GlyphCovers(font, g.Index, gx, gy, g.Scale))
        {
            color = ::PixelShading_UnpackRgb(g.Color);
        }
    }
    output[int((uint(y) * uint(__c.width)) + uint(x))] = ::PixelShading_PackRgb(color);
}
