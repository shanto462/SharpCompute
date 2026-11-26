// SharpCompute.Samples.Shooter.Rendering.BinTiles (SharpCompute.Samples.Shooter): entry point SharpCompute_Samples_Shooter_Rendering_BinTiles, compile options None
#include <metal_stdlib>

using namespace metal;

constant float TileSize = 16.0f;

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

struct __Constants
{
    int chunkCount;
    int primCount;
    int tilesX;
    int maxChunks;
    int maxPrims;
};
static_assert(sizeof(__Constants) == 20, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, chunkCount) == 0, "SharpCompute: offset of '__Constants.chunkCount' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, primCount) == 4, "SharpCompute: offset of '__Constants.primCount' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, tilesX) == 8, "SharpCompute: offset of '__Constants.tilesX' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, maxChunks) == 12, "SharpCompute: offset of '__Constants.maxChunks' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, maxPrims) == 16, "SharpCompute: offset of '__Constants.maxPrims' does not match its C# layout");

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

kernel void SharpCompute_Samples_Shooter_Rendering_BinTiles(
    device packed_float4* chunkBounds [[buffer(0)]],
    const device Prim* prims [[buffer(1)]],
    device int* tileChunks [[buffer(2)]],
    device int* tilePrims [[buffer(3)]],
    device int* tileCounts [[buffer(4)]],
    constant __Constants& __c [[buffer(5)]],
    constant __GridWindow& __window [[buffer(6), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int tile = int((__tid.y * uint(__c.tilesX)) + __tid.x);
    float x0 = float(int(__tid.x)) * ::TileSize;
    float y0 = float(int(__tid.y)) * ::TileSize;
    float x1 = x0 + ::TileSize;
    float y1 = y0 + ::TileSize;
    int n = 0;
    for (int c = 0; c < __c.chunkCount && n < __c.maxChunks; c = int(uint(c) + 1u))
    {
        float4 b = chunkBounds[c];
        if (b.x < x1 && b.z > x0 && b.y < y1 && b.w > y0)
        {
            tileChunks[int((uint(tile) * uint(__c.maxChunks)) + uint(n))] = c;
            n = int(uint(n) + 1u);
        }
    }
    tileCounts[int(uint(tile) * 2u)] = n;
    n = 0;
    for (int k = 0; k < __c.primCount && n < __c.maxPrims; k = int(uint(k) + 1u))
    {
        float4 b = prims[k].Bounds;
        if (b.x < x1 && b.z > x0 && b.y < y1 && b.w > y0)
        {
            tilePrims[int((uint(tile) * uint(__c.maxPrims)) + uint(n))] = k;
            n = int(uint(n) + 1u);
        }
    }
    tileCounts[int((uint(tile) * 2u) + 1u)] = n;
}
