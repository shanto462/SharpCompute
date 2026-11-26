// SharpCompute.Tests.CodeReuse.StaticExtensions (SharpCompute.Tests): entry point SharpCompute_Tests_CodeReuse_StaticExtensions, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Slots = 4;

struct Tally
{
    float Sum;
    float Count;
};
static_assert(sizeof(Tally) == 8, "SharpCompute: size of 'Tally' does not match its C# layout");
static_assert(__builtin_offsetof(Tally, Sum) == 0, "SharpCompute: offset of 'Tally.Sum' does not match its C# layout");
static_assert(__builtin_offsetof(Tally, Count) == 4, "SharpCompute: offset of 'Tally.Count' does not match its C# layout");

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

static float3 ExtensionMath__Ramp(float x);
static float3 ExtensionMath__Unit();
static float3 ExtensionMath__op_ExclusiveOr(float3 a, float3 b);
static Tally ExtensionMath__Empty();
static Tally ExtensionMath__op_Addition(Tally a, Tally b);

static float3 ExtensionMath__Ramp(float x)
{
    return float3(x, x + 1.0f, x + 2.0f);
}

static float3 ExtensionMath__Unit()
{
    return float3(1.0f, 0.0f, 0.0f);
}

static float3 ExtensionMath__op_ExclusiveOr(float3 a, float3 b)
{
    return float3(a.x * b.y, a.y * b.z, a.z * b.x);
}

static Tally ExtensionMath__Empty()
{
    return Tally{};
}

static Tally ExtensionMath__op_Addition(Tally a, Tally b)
{
    Tally sum = Tally{};
    sum.Sum = a.Sum + b.Sum;
    sum.Count = a.Count + b.Count + 1000.0f;
    return sum;
}

kernel void SharpCompute_Tests_CodeReuse_StaticExtensions(
    device float* output [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float3 a = ::ExtensionMath__Ramp(float(i)) + ::ExtensionMath__Unit();
    float3 b = float3(5.0f, 7.0f, 11.0f);
    float3 c = ::ExtensionMath__op_ExclusiveOr(a, b);
    Tally t = ::ExtensionMath__Empty();
    t.Sum = c.x;
    t.Count = 1.0f;
    Tally u = ::ExtensionMath__op_Addition(t, t);
    output[int((uint(i) * uint(::Slots)) + 0u)] = c.x;
    output[int((uint(i) * uint(::Slots)) + 1u)] = c.y + c.z;
    output[int((uint(i) * uint(::Slots)) + 2u)] = u.Sum;
    output[int((uint(i) * uint(::Slots)) + 3u)] = u.Count;
}
