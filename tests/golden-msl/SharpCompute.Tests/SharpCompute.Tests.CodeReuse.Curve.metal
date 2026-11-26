// SharpCompute.Tests.CodeReuse.Curve (SharpCompute.Tests): entry point SharpCompute_Tests_CodeReuse_Curve, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute float and half conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

struct __Constants
{
    float scale;
    float offset;
};
static_assert(sizeof(__Constants) == 8, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, scale) == 0, "SharpCompute: offset of '__Constants.scale' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, offset) == 4, "SharpCompute: offset of '__Constants.offset' does not match its C# layout");

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

static float Slope(constant __Constants& __c);
static float Item(int index, constant __Constants& __c);
static float Choose_float(float a, float b, constant __Constants& __c);
static float Unit();
static float Apply(float x, constant __Constants& __c);
static float2 Choose_float2(float2 a, float2 b, constant __Constants& __c);

static float Slope(constant __Constants& __c)
{
    return __c.scale * 2.0f;
}

static float Item(int index, constant __Constants& __c)
{
    return (float(index) * ::Slope(__c)) + __c.offset;
}

static float Choose_float(float a, float b, constant __Constants& __c)
{
    return __c.offset > 0.0f ? a : b;
}

static float Unit()
{
    return 1.0f;
}

static float Apply(float x, constant __Constants& __c)
{
    return ::Item(__ms_sat_int(x), __c) + ::Choose_float(::Unit(), -::Unit(), __c);
}

static float2 Choose_float2(float2 a, float2 b, constant __Constants& __c)
{
    return __c.offset > 0.0f ? a : b;
}

kernel void SharpCompute_Tests_CodeReuse_Curve(
    device float* output [[buffer(0)]],
    constant __Constants& __c [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    output[int(__tid.x)] = ::Apply(float(int(__tid.x)), __c) + ::Choose_float2(float2(3.0f, 4.0f), float2(5.0f, 6.0f), __c).y;
}
