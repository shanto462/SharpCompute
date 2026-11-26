// SharpCompute.Tests.Shaders.MathSweepFast (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_MathSweepFast, compile options FastMath
#include <metal_stdlib>

using namespace metal;

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

kernel void SharpCompute_Tests_Shaders_MathSweepFast(
    const device float* input [[buffer(0)]],
    device float* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    float a = input[int(uint(i) * uint(4))];
    float b = input[int((uint(i) * uint(4)) + 1u)];
    float c = input[int((uint(i) * uint(4)) + 2u)];
    float d = input[int((uint(i) * uint(4)) + 3u)];
    output[int(uint(i) * uint(8))] = (a * b) + c;
    output[int((uint(i) * uint(8)) + 1u)] = (a + b) - a;
    output[int((uint(i) * uint(8)) + 2u)] = metal::sin(a) * metal::cos(b);
    output[int((uint(i) * uint(8)) + 3u)] = metal::pow(metal::abs(a), b);
    output[int((uint(i) * uint(8)) + 4u)] = metal::exp(c) + metal::log(metal::abs(d));
    output[int((uint(i) * uint(8)) + 5u)] = a / b * b;
    output[int((uint(i) * uint(8)) + 6u)] = (a * b) - (c * d);
    output[int((uint(i) * uint(8)) + 7u)] = metal::sqrt(metal::abs(c)) * (a + (b * 0.1f) + (c * 0.2f) + (d * 0.3f));
}
