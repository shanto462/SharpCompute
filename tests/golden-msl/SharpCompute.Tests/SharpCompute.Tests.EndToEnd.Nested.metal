// SharpCompute.Tests.EndToEnd.Nested (SharpCompute.Tests): entry point SharpCompute_Tests_EndToEnd_Nested, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline half __ms_to_half(float x) { return metal::abs(x) >= 65520.0f ? (x < 0.0f ? half(-INFINITY) : half(INFINITY)) : half(x); }

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

struct __Constants
{
    Item_1 extra;
};
static_assert(sizeof(__Constants) == 16, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, extra) == 0, "SharpCompute: offset of '__Constants.extra' does not match its C# layout");

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

kernel void SharpCompute_Tests_EndToEnd_Nested(
    device Outer* items [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    Outer o = items[i];
    o.Inner.K = int(uint(o.Inner.K) + 3u);
    o.Inner.W = o.Inner.W.yx * 2.0f;
    o.Far.V = o.Far.V + __c.extra.V;
    o.Far.A = __c.extra.A - o.Far.A;
    o.Tail = float(o.H) * 4.0f;
    o.H = __ms_to_half(o.Tail + 0.5f);
    items[i] = o;
}
