// SharpCompute.Tests.Shaders.BlockMulAddSum (SharpCompute.Tests): entry point SharpCompute_Tests_Shaders_BlockMulAddSum, compile options NoFpContract
#include <metal_stdlib>

using namespace metal;

constant int BlockSize = 64;

struct __Constants
{
    int count;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, count) == 0, "SharpCompute: offset of '__Constants.count' does not match its C# layout");

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

kernel void SharpCompute_Tests_Shaders_BlockMulAddSum(
    const device float* a [[buffer(0)]],
    const device float* b [[buffer(1)]],
    const device float* c [[buffer(2)]],
    device float* sums [[buffer(3)]],
    constant __Constants& __c [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]],
    uint3 __gid_in [[threadgroup_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
    threadgroup float tile[64];
    int i = int(__tid.x);
    int local = int(__gtid.x);
    tile[local] = i < __c.count ? (a[i] * b[i]) + c[i] : 0.0f;
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    for (int __ms_half = ::BlockSize / 2; __ms_half > 0; __ms_half >>= 1)
    {
        if (local < __ms_half)
        {
            tile[local] += tile[int(uint(local) + uint(__ms_half))];
        }
        metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    }
    if (local == 0)
    {
        sums[int(__gid.x)] = tile[0];
    }
}
