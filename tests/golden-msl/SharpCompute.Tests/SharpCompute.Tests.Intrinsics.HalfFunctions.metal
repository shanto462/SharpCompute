// SharpCompute.Tests.Intrinsics.HalfFunctions (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_HalfFunctions, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Stride = 43;

static_assert(sizeof(packed_half4) == 8, "SharpCompute: size of 'packed_half4' does not match its C# layout");

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

kernel void SharpCompute_Tests_Intrinsics_HalfFunctions(
    const device packed_half4* a [[buffer(0)]],
    const device packed_half4* b [[buffer(1)]],
    const device packed_half4* c [[buffer(2)]],
    device packed_half4* output [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int o = int(uint(i) * uint(::Stride));
    half4 x = a[i];
    half4 y = b[i];
    half4 z = c[i];
    output[o] = metal::abs(x);
    output[int(uint(o) + 1u)] = metal::min(x, y);
    output[int(uint(o) + 2u)] = metal::max(x, y);
    output[int(uint(o) + 3u)] = metal::clamp(x, y, z);
    output[int(uint(o) + 4u)] = metal::saturate(x);
    output[int(uint(o) + 5u)] = metal::sign(x);
    output[int(uint(o) + 6u)] = metal::floor(x);
    output[int(uint(o) + 7u)] = metal::ceil(x);
    output[int(uint(o) + 8u)] = metal::round(x);
    output[int(uint(o) + 9u)] = metal::trunc(x);
    output[int(uint(o) + 10u)] = metal::fract(x);
    output[int(uint(o) + 11u)] = metal::fmod(x, y);
    output[int(uint(o) + 12u)] = metal::sqrt(x);
    output[int(uint(o) + 13u)] = metal::rsqrt(x);
    output[int(uint(o) + 14u)] = metal::fma(x, y, z);
    output[int(uint(o) + 15u)] = metal::mix(x, y, z);
    output[int(uint(o) + 16u)] = metal::step(x, y);
    output[int(uint(o) + 17u)] = metal::smoothstep(x, y, z);
    output[int(uint(o) + 18u)] = metal::copysign(x, y);
    output[int(uint(o) + 19u)] = metal::min(x, y.x);
    output[int(uint(o) + 20u)] = metal::clamp(x, y.x, z.x);
    output[int(uint(o) + 21u)] = metal::mix(x, y, z.x);
    output[int(uint(o) + 22u)] = metal::step(x.x, y);
    output[int(uint(o) + 23u)] = metal::smoothstep(x.x, y.x, z);
    output[int(uint(o) + 24u)] = metal::max(x, y.x);
    output[int(uint(o) + 25u)] = metal::pow(x, y);
    output[int(uint(o) + 26u)] = metal::pow(x, y.x);
    output[int(uint(o) + 27u)] = metal::exp(x);
    output[int(uint(o) + 28u)] = metal::exp2(x);
    output[int(uint(o) + 29u)] = metal::exp10(x);
    output[int(uint(o) + 30u)] = metal::log(x);
    output[int(uint(o) + 31u)] = metal::log2(x);
    output[int(uint(o) + 32u)] = metal::log10(x);
    output[int(uint(o) + 33u)] = metal::sin(x);
    output[int(uint(o) + 34u)] = metal::cos(x);
    output[int(uint(o) + 35u)] = metal::tan(x);
    output[int(uint(o) + 36u)] = metal::asin(x);
    output[int(uint(o) + 37u)] = metal::acos(x);
    output[int(uint(o) + 38u)] = metal::atan(x);
    output[int(uint(o) + 39u)] = metal::atan2(x, y);
    output[int(uint(o) + 40u)] = metal::sinh(x);
    output[int(uint(o) + 41u)] = metal::cosh(x);
    output[int(uint(o) + 42u)] = metal::tanh(x);
}
