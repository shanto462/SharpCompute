// SharpCompute.Tests.Shaders.SimdInHelpers (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SimdInHelpers, compile options None
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

static int GroupTotal(int value);
static int Lane(uint __lane);
static int Position(uint __lane, uint __sgid);

static int GroupTotal(int value)
{
    return metal::simd_sum(value);
}

static int Lane(uint __lane)
{
    return int(__lane);
}

static int Position(uint __lane, uint __sgid)
{
    return int((uint(int(__sgid)) * 100u) + uint(::Lane(__lane)));
}

kernel void SharpCompute_Tests_Shaders_SimdInHelpers(
    const device int* input [[buffer(0)]],
    device int* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint __lane [[thread_index_in_simdgroup]],
    uint __sgid [[simdgroup_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    output[int((uint(i) * 2u) + 0u)] = ::GroupTotal(input[i]);
    output[int((uint(i) * 2u) + 1u)] = ::Position(__lane, __sgid);
}
