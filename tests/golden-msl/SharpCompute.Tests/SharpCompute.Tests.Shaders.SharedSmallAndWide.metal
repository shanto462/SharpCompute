// SharpCompute.Tests.Shaders.SharedSmallAndWide (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_SharedSmallAndWide, compile options None
#include <metal_stdlib>

using namespace metal;

constant int GroupSize = 64;
constant int Stride = 6;

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

kernel void SharpCompute_Tests_Shaders_SharedSmallAndWide(
    const device long* values [[buffer(0)]],
    device long* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint __gindex [[thread_index_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup uchar bytes[64];
    threadgroup char sbytes[64];
    threadgroup short shorts[64];
    threadgroup ushort ushorts[64];
    threadgroup long longs[64];
    threadgroup ulong ulongs[64];
    int t = int(__gindex);
    long value = values[int(__tid.x)];
    bytes[t] = uchar(value);
    sbytes[t] = char(value >> 8);
    shorts[t] = short(value >> 16);
    ushorts[t] = ushort(value >> 32);
    longs[t] = value;
    ulongs[t] = ulong(~value);
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    int other = int(uint(::GroupSize) - 1u - uint(t));
    int o = int(__tid.x * uint(::Stride));
    output[int(uint(o) + 0u)] = long(bytes[other]);
    output[int(uint(o) + 1u)] = long(sbytes[other]);
    output[int(uint(o) + 2u)] = long(shorts[other]);
    output[int(uint(o) + 3u)] = long(ushorts[other]);
    output[int(uint(o) + 4u)] = longs[other];
    output[int(uint(o) + 5u)] = long(ulongs[other]);
}
