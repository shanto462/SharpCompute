// SharpCompute.Tests.EndToEnd.Swizzles (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Swizzles, compile options None
#include <metal_stdlib>

using namespace metal;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

struct Item
{
    int K;
    packed_float2 W;
};
static_assert(sizeof(Item) == 12, "SharpCompute: size of 'Item' does not match its C# layout");
static_assert(__builtin_offsetof(Item, K) == 0, "SharpCompute: offset of 'Item.K' does not match its C# layout");
static_assert(__builtin_offsetof(Item, W) == 4, "SharpCompute: offset of 'Item.W' does not match its C# layout");

struct Item_1
{
    float A;
    packed_float3 V;
};
static_assert(sizeof(Item_1) == 16, "SharpCompute: size of 'Item_1' does not match its C# layout");
static_assert(__builtin_offsetof(Item_1, A) == 0, "SharpCompute: offset of 'Item_1.A' does not match its C# layout");
static_assert(__builtin_offsetof(Item_1, V) == 4, "SharpCompute: offset of 'Item_1.V' does not match its C# layout");

struct Outer
{
    Item Inner;
    Item_1 Far;
    half H;
    float Tail;
};
static_assert(sizeof(Outer) == 36, "SharpCompute: size of 'Outer' does not match its C# layout");
static_assert(__builtin_offsetof(Outer, Inner) == 0, "SharpCompute: offset of 'Outer.Inner' does not match its C# layout");
static_assert(__builtin_offsetof(Outer, Far) == 12, "SharpCompute: offset of 'Outer.Far' does not match its C# layout");
static_assert(__builtin_offsetof(Outer, H) == 28, "SharpCompute: offset of 'Outer.H' does not match its C# layout");
static_assert(__builtin_offsetof(Outer, Tail) == 32, "SharpCompute: offset of 'Outer.Tail' does not match its C# layout");

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

kernel void SharpCompute_Tests_EndToEnd_Swizzles(
    device packed_float4* o [[buffer(0)]],
    device Outer* items [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float4 v = o[i];
    float3 a = (v.xyz + float3(1.0f, 2.0f, 3.0f)).zyx;
    float2 b = float3(v.w, 2.0f, 3.0f).zx;
    float3 z = float3(0);
    float2 n = float2(0);
    int2 ii = int2(v.xy * 10.0f);
    uint3 uu = uint3(int3(ii.x, ii.y, 7));
    float2 back = float2(ii) + float2(uu.xy);
    v.xw = b;
    v.yz += a.xy + n + z.xy + back * 0.01f;
    o[i] = v;
    o[i].wzyx = o[i];
    o[i].x += 1.0f;
    items[i].Inner.W.y = 5.0f;
    items[i].Far.V.xz = float2(1.0f, 2.0f);
    items[i].Inner.K = int(uint(items[i].Inner.K) + 1u);
}
