// SharpCompute.Tests.Intrinsics.FloatIntrinsics (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_FloatIntrinsics, compile options None
#include <metal_stdlib>

using namespace metal;

constant int ExactStride = 4;
constant int ApproximateStride = 8;

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

kernel void SharpCompute_Tests_Intrinsics_FloatIntrinsics(
    const device float* a [[buffer(0)]],
    const device float* b [[buffer(1)]],
    const device float* c [[buffer(2)]],
    device uint* exact [[buffer(3)]],
    device float* approximate [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int t = int(__tid.x);
    float x = a[t];
    float y = b[t];
    float z = c[t];
    exact[int((uint(t) * uint(::ExactStride)) + 0u)] = as_type<uint>(metal::fma(x, y, z));
    exact[int((uint(t) * uint(::ExactStride)) + 1u)] = as_type<uint>(metal::fma(z, y, x));
    float3 fused = metal::fma(float3(x, y, z), float3(y, z, x), float3(z, x, y));
    exact[int((uint(t) * uint(::ExactStride)) + 2u)] = as_type<uint>(fused.x) ^ (as_type<uint>(fused.y) >> 1) ^ (as_type<uint>(fused.z) >> 2);
    float2 signs = metal::copysign(float2(x, y), float2(y, z));
    exact[int((uint(t) * uint(::ExactStride)) + 3u)] = as_type<uint>(signs.x) ^ (as_type<uint>(signs.y) >> 1) ^ (as_type<uint>(metal::copysign(z, x)) >> 2);
    float2 logs = metal::log10(float2(y, z));
    float2 powers = metal::exp10(float2(z, x));
    approximate[int((uint(t) * uint(::ApproximateStride)) + 0u)] = metal::log10(x);
    approximate[int((uint(t) * uint(::ApproximateStride)) + 1u)] = metal::exp10(y);
    approximate[int((uint(t) * uint(::ApproximateStride)) + 2u)] = logs.x;
    approximate[int((uint(t) * uint(::ApproximateStride)) + 3u)] = logs.y;
    approximate[int((uint(t) * uint(::ApproximateStride)) + 4u)] = powers.x;
    approximate[int((uint(t) * uint(::ApproximateStride)) + 5u)] = powers.y;
    approximate[int((uint(t) * uint(::ApproximateStride)) + 6u)] = metal::log10(z);
    approximate[int((uint(t) * uint(::ApproximateStride)) + 7u)] = metal::exp10(x);
}
