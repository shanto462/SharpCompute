// SharpCompute.Tests.Intrinsics.HalfBoundary (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_HalfBoundary, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline half __ms_to_half(float x) { return metal::abs(x) >= 65520.0f ? (x < 0.0f ? half(-INFINITY) : half(INFINITY)) : half(x); }

constant half3 Weights = half3(float3(0.5f, 0.25f, 2.0f));

static_assert(sizeof(packed_half2) == 4, "SharpCompute: size of 'packed_half2' does not match its C# layout");
static_assert(sizeof(packed_half3) == 6, "SharpCompute: size of 'packed_half3' does not match its C# layout");
static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");

struct HalfTexel
{
    packed_half3 Color;
    float Weight;
    packed_half2 Uv;
    half Bias;
    packed_half4 Tint;
};
static_assert(sizeof(HalfTexel) == 28, "SharpCompute: size of 'HalfTexel' does not match its C# layout");
static_assert(__builtin_offsetof(HalfTexel, Color) == 0, "SharpCompute: offset of 'HalfTexel.Color' does not match its C# layout");
static_assert(__builtin_offsetof(HalfTexel, Weight) == 8, "SharpCompute: offset of 'HalfTexel.Weight' does not match its C# layout");
static_assert(__builtin_offsetof(HalfTexel, Uv) == 12, "SharpCompute: offset of 'HalfTexel.Uv' does not match its C# layout");
static_assert(__builtin_offsetof(HalfTexel, Bias) == 16, "SharpCompute: offset of 'HalfTexel.Bias' does not match its C# layout");
static_assert(__builtin_offsetof(HalfTexel, Tint) == 18, "SharpCompute: offset of 'HalfTexel.Tint' does not match its C# layout");

struct __Constants
{
    packed_half2 scale;
    float gain;
    packed_half3 offset;
    half exposure;
    packed_half4 tint;
};
static_assert(sizeof(__Constants) == 24, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, scale) == 0, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, gain) == 4, "SharpCompute: offset of '__Constants.gain' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, offset) == 8, "SharpCompute: offset of '__Constants.offset' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, exposure) == 14, "SharpCompute: offset of '__Constants.exposure' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, tint) == 16, "SharpCompute: offset of '__Constants.tint' does not match its C# layout");

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

kernel void SharpCompute_Tests_Intrinsics_HalfBoundary(
    const device packed_half2* twos [[buffer(0)]],
    const device packed_half3* threes [[buffer(1)]],
    device packed_half4* fours [[buffer(2)]],
    device HalfTexel* texels [[buffer(3)]],
    device packed_half3* sums [[buffer(4)]],
    constant __Constants& __c [[buffer(5)]],
    constant __GridWindow& __window [[buffer(6), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup packed_half3 tile[16];
    int i = int(__tid.x);
    half3 three = threes[i];
    half2 two = twos[i] * __c.scale;
    tile[int(__gtid.x)] = three + __c.offset;
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    half3 neighbour = tile[int(__gtid.x) ^ 1];
    fours[i] = half4(two, three.z, __c.exposure) + __c.tint;
    HalfTexel texel = texels[i];
    texel.Color = neighbour * ::Weights;
    texel.Weight = float(three.x);
    texel.Uv = two.yx;
    texel.Bias = __ms_to_half(__c.gain);
    texel.Tint = fours[i].wzyx;
    texels[i] = texel;
    sums[i] = texels[i].Color + threes[i].zyx;
    sums[i].y = neighbour.x;
}
