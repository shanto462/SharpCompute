// SharpCompute.Tests.Shaders.AccumulateInPlace (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_AccumulateInPlace, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Turns = 4;

static_assert(sizeof(packed_float4) == 16, "SharpCompute: size of 'packed_float4' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_AccumulateInPlace(
    texture2d<float, access::read_write> r8 [[texture(0)]],
    texture2d<float, access::read_write> rgba [[texture(1)]],
    texture2d<float, access::read_write> single [[texture(2)]],
    device packed_float4* seen [[buffer(0)]],
    constant __GridWindow& __window [[buffer(1), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int2 xy = int2(__tid.xy);
    int index = int(((uint(xy.y) * uint(int(r8.get_width()))) + uint(xy.x)) * 3u);
    int step = int(1u + uint((int((uint(xy.x) * 7u) + (uint(xy.y) * 13u))) & 31));
    for (int turn = 0; turn < ::Turns; turn = int(uint(turn) + 1u))
    {
        float4 a = r8.read(uint2(xy));
        float4 b = rgba.read(uint2(xy));
        float4 c = single.read(uint2(xy));
        r8.write(float4(a.x + (float(step) / 255.0f), 0.0f, 0.0f, 1.0f), uint2(xy));
        r8.fence();
        rgba.write(float4(b.x + (float(step) / 255.0f), b.y + (float(int(63u - uint(step))) / 255.0f), b.z + (float(step ^ 21) / 255.0f), 1.0f), uint2(xy));
        rgba.fence();
        single.write(float4(c.x + (float(step) * 0.25f), 0.0f, 0.0f, 1.0f), uint2(xy));
        single.fence();
    }
    seen[index] = r8.read(uint2(xy));
    seen[int(uint(index) + 1u)] = rgba.read(uint2(xy));
    seen[int(uint(index) + 2u)] = single.read(uint2(xy));
}
