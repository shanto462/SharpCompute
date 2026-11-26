// SharpCompute.Samples.Shooter.Rendering.Composite (SharpCompute.Samples.Shooter): entry point SharpCompute_Samples_Shooter_Rendering_Composite, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct Ripple
{
    packed_float2 Center;
    float Radius;
    float Width;
    float Strength;
};
static_assert(sizeof(Ripple) == 20, "SharpCompute: size of 'Ripple' does not match its C# layout");
static_assert(__builtin_offsetof(Ripple, Center) == 0, "SharpCompute: offset of 'Ripple.Center' does not match its C# layout");
static_assert(__builtin_offsetof(Ripple, Radius) == 8, "SharpCompute: offset of 'Ripple.Radius' does not match its C# layout");
static_assert(__builtin_offsetof(Ripple, Width) == 12, "SharpCompute: offset of 'Ripple.Width' does not match its C# layout");
static_assert(__builtin_offsetof(Ripple, Strength) == 16, "SharpCompute: offset of 'Ripple.Strength' does not match its C# layout");

struct Glyph
{
    int X;
    int Y;
    int Index;
    int Scale;
    packed_float3 Color;
};
static_assert(sizeof(Glyph) == 28, "SharpCompute: size of 'Glyph' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, X) == 0, "SharpCompute: offset of 'Glyph.X' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, Y) == 4, "SharpCompute: offset of 'Glyph.Y' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, Index) == 8, "SharpCompute: offset of 'Glyph.Index' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, Scale) == 12, "SharpCompute: offset of 'Glyph.Scale' does not match its C# layout");
static_assert(__builtin_offsetof(Glyph, Color) == 16, "SharpCompute: offset of 'Glyph.Color' does not match its C# layout");

struct __Constants
{
    int rippleCount;
    int glyphCount;
    int width;
    int height;
    float bloom;
    float exposure;
    float flashR;
    float flashG;
    float flashB;
    float shakeX;
    float shakeY;
};
static_assert(sizeof(__Constants) == 44, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, rippleCount) == 0, "SharpCompute: offset of '__Constants.rippleCount' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, glyphCount) == 4, "SharpCompute: offset of '__Constants.glyphCount' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 8, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 12, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, bloom) == 16, "SharpCompute: offset of '__Constants.bloom' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, exposure) == 20, "SharpCompute: offset of '__Constants.exposure' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, flashR) == 24, "SharpCompute: offset of '__Constants.flashR' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, flashG) == 28, "SharpCompute: offset of '__Constants.flashG' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, flashB) == 32, "SharpCompute: offset of '__Constants.flashB' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, shakeX) == 36, "SharpCompute: offset of '__Constants.shakeX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, shakeY) == 40, "SharpCompute: offset of '__Constants.shakeY' does not match its C# layout");

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

static int4 Corners(float2 at, int w, int h);
static float2 Weights(float2 at);
static float3 Bilinear(float3 a, float3 b, float3 c, float3 d, float2 f);
static float3 SampleHdr(float2 at, device packed_float4* hdr, constant __Constants& __c);
static float3 SampleHalf(float2 at, device packed_float4* bloomHalf, constant __Constants& __c);
static float3 SampleQuarter(float2 at, device packed_float4* bloomQuarter, constant __Constants& __c);
static bool PixelShading_GlyphCovers(const device uint* __local_font, int glyph, int gx, int gy, int scale);
static uint PixelShading_PackRgb(float3 color);

static int4 Corners(float2 at, int w, int h)
{
    int x0 = int(metal::floor(at.x - 0.5f));
    int y0 = int(metal::floor(at.y - 0.5f));
    return int4(metal::clamp(x0, 0, int(uint(w) - 1u)), metal::clamp(y0, 0, int(uint(h) - 1u)), metal::clamp(int(uint(x0) + 1u), 0, int(uint(w) - 1u)), metal::clamp(int(uint(y0) + 1u), 0, int(uint(h) - 1u)));
}

static float2 Weights(float2 at)
{
    return float2(metal::fract(at.x - 0.5f), metal::fract(at.y - 0.5f));
}

static float3 Bilinear(float3 a, float3 b, float3 c, float3 d, float2 f)
{
    return metal::mix(metal::mix(a, b, f.x), metal::mix(c, d, f.x), f.y);
}

static float3 SampleHdr(float2 at, device packed_float4* hdr, constant __Constants& __c)
{
    int4 i = ::Corners(at, __c.width, __c.height);
    float2 f = ::Weights(at);
    return ::Bilinear(hdr[int((uint(i.y) * uint(__c.width)) + uint(i.x))].xyz, hdr[int((uint(i.y) * uint(__c.width)) + uint(i.z))].xyz, hdr[int((uint(i.w) * uint(__c.width)) + uint(i.x))].xyz, hdr[int((uint(i.w) * uint(__c.width)) + uint(i.z))].xyz, f);
}

static float3 SampleHalf(float2 at, device packed_float4* bloomHalf, constant __Constants& __c)
{
    int w = __c.width / 2;
    int4 i = ::Corners(at, w, __c.height / 2);
    float2 f = ::Weights(at);
    return ::Bilinear(bloomHalf[int((uint(i.y) * uint(w)) + uint(i.x))].xyz, bloomHalf[int((uint(i.y) * uint(w)) + uint(i.z))].xyz, bloomHalf[int((uint(i.w) * uint(w)) + uint(i.x))].xyz, bloomHalf[int((uint(i.w) * uint(w)) + uint(i.z))].xyz, f);
}

static float3 SampleQuarter(float2 at, device packed_float4* bloomQuarter, constant __Constants& __c)
{
    int w = __c.width / 4;
    int4 i = ::Corners(at, w, __c.height / 4);
    float2 f = ::Weights(at);
    return ::Bilinear(bloomQuarter[int((uint(i.y) * uint(w)) + uint(i.x))].xyz, bloomQuarter[int((uint(i.y) * uint(w)) + uint(i.z))].xyz, bloomQuarter[int((uint(i.w) * uint(w)) + uint(i.x))].xyz, bloomQuarter[int((uint(i.w) * uint(w)) + uint(i.z))].xyz, f);
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

kernel void SharpCompute_Samples_Shooter_Rendering_Composite(
    device packed_float4* hdr [[buffer(0)]],
    device packed_float4* bloomHalf [[buffer(1)]],
    device packed_float4* bloomQuarter [[buffer(2)]],
    const device Ripple* ripples [[buffer(3)]],
    const device Glyph* glyphs [[buffer(4)]],
    const device uint* font [[buffer(5)]],
    device uint* output [[buffer(6)]],
    constant __Constants& __c [[buffer(7)]],
    constant __GridWindow& __window [[buffer(8), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int x = int(__tid.x);
    int y = int(__tid.y);
    float2 p = float2(float(x) + 0.5f, float(y) + 0.5f);
    float2 offset = float2(0.0f, 0.0f);
    for (int i = 0; i < __c.rippleCount; i = int(uint(i) + 1u))
    {
        Ripple r = ripples[i];
        float2 v = p - r.Center;
        float d = metal::length(v);
        float band = (d - r.Radius) / r.Width;
        float push = -band * metal::exp(-band * band) * r.Strength;
        offset += v / metal::max(d, 1.0f) * push;
    }
    float2 s = p + offset + float2(__c.shakeX, __c.shakeY);
    float2 split = offset * 0.3f;
    float3 color = float3(::SampleHdr(s + split, hdr, __c).x, ::SampleHdr(s, hdr, __c).y, ::SampleHdr(s - split, hdr, __c).z);
    float3 glow = (::SampleHalf(s * 0.5f, bloomHalf, __c) * 0.8f) + (::SampleQuarter(s * 0.25f, bloomQuarter, __c) * 1.1f);
    color += (glow * __c.bloom) + float3(__c.flashR, __c.flashG, __c.flashB);
    color = float3(1.0f, 1.0f, 1.0f) - float3(metal::exp(-color.x * __c.exposure), metal::exp(-color.y * __c.exposure), metal::exp(-color.z * __c.exposure));
    float u = (float(x) / float(__c.width)) - 0.5f;
    float w = (float(y) / float(__c.height)) - 0.5f;
    color *= 1.0f - (((u * u) + (w * w)) * 0.55f);
    for (int i = 0; i < __c.glyphCount; i = int(uint(i) + 1u))
    {
        Glyph g = glyphs[i];
        int gx = int(uint(x) - uint(g.X));
        int gy = int(uint(y) - uint(g.Y));
        if (::PixelShading_GlyphCovers(font, g.Index, gx, gy, g.Scale))
        {
            color = g.Color;
        }
    }
    output[int((uint(y) * uint(__c.width)) + uint(x))] = ::PixelShading_PackRgb(color);
}
