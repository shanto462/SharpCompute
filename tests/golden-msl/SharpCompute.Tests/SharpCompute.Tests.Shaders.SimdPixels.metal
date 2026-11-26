// SharpCompute.Tests.Shaders.SimdPixels (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdPixels, compile options None
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

kernel void SharpCompute_Tests_Shaders_SimdPixels(
    texture2d<float, access::write> __output [[texture(0)]],
    constant __GridWindow& __window [[buffer(0), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __grid_in [[threads_per_grid]],
    uint __lane [[thread_index_in_simdgroup]],
    uint __sgid [[simdgroup_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
    float lane = float(int(__lane));
    float group = float(int(__sgid));
    float active = float(metal::simd_sum(int(true)));
    __output.write(float4(lane / 255.0f, group / 255.0f, active / 255.0f, 1.0f), __tid.xy);
    return;
}
