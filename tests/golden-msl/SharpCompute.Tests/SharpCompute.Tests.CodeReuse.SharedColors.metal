// SharpCompute.Tests.CodeReuse.SharedColors (SharpCompute.Tests): entry point SharpCompute_Tests_CodeReuse_SharedColors, compile options None
#include <metal_stdlib>

using namespace metal;

constant float3 SharedMath_LumaWeights = float3(0.2126f, 0.7152f, 0.0722f);

static_assert(sizeof(packed_float3) == 12, "SharpCompute: size of 'packed_float3' does not match its C# layout");

struct __Constants
{
    float gain;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, gain) == 0, "SharpCompute: offset of '__Constants.gain' does not match its C# layout");

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

static int SharedMath_Cell(uint3 __tid, uint3 __grid);
static uint SharedMath_Hash(uint x);
static float SharedMath_Unit(uint x);
static float3 SharedMath_Palette(float t);
static float SharedMath_Luminance(float3 color);
static void SharedMath_Swap_float(thread float& a, thread float& b);
static float SharedMath_Squared(float value);
static void SharedMath_Swap_float3(thread float3& a, thread float3& b);
static float3 SharedMath_Pick_float3(bool condition, float3 whenTrue, float3 whenFalse);
static float SharedMath_Pick_float(bool condition, float whenTrue, float whenFalse);

static int SharedMath_Cell(uint3 __tid, uint3 __grid)
{
    return int((__tid.y * __grid.x) + __tid.x);
}

static uint SharedMath_Hash(uint x)
{
    x ^= x >> 16;
    x *= 2146121005u;
    x ^= x >> 15;
    x *= 2221713035u;
    x ^= x >> 16;
    return x;
}

static float SharedMath_Unit(uint x)
{
    return float(::SharedMath_Hash(x) >> 8) * (1.0f / 16777216.0f);
}

static float3 SharedMath_Palette(float t)
{
    return (float3(metal::cos(t * 6.2831855f), metal::sin(t * 6.2831855f), t) * 0.5f) + 0.5f;
}

static float SharedMath_Luminance(float3 color)
{
    return metal::dot(color, ::SharedMath_LumaWeights);
}

static void SharedMath_Swap_float(thread float& a, thread float& b)
{
    float held = a;
    a = b;
    b = held;
}

static float SharedMath_Squared(float value)
{
    return value * value;
}

static void SharedMath_Swap_float3(thread float3& a, thread float3& b)
{
    float3 held = a;
    a = b;
    b = held;
}

static float3 SharedMath_Pick_float3(bool condition, float3 whenTrue, float3 whenFalse)
{
    return condition ? whenTrue : whenFalse;
}

static float SharedMath_Pick_float(bool condition, float whenTrue, float whenFalse)
{
    return condition ? whenTrue : whenFalse;
}

kernel void SharpCompute_Tests_CodeReuse_SharedColors(
    device packed_float3* output [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    int i = ::SharedMath_Cell(__tid, __grid);
    float t = ::SharedMath_Unit(uint(i));
    float3 color = ::SharedMath_Palette(t) * __c.gain;
    float low = t;
    float high = ::SharedMath_Luminance(color);
    if (low > high)
    {
        ::SharedMath_Swap_float(low, high);
    }
    float3 other = float3(low, high, ::SharedMath_Squared(high - low));
    ::SharedMath_Swap_float3(color, other);
    output[i] = ::SharedMath_Pick_float3(i % 3 == 0, color, other) + ::SharedMath_Pick_float(i % 2 == 0, 0.0f, 1.0f);
}
