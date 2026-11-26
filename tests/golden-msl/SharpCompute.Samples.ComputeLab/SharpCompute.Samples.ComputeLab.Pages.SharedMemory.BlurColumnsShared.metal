// SharpCompute.Samples.ComputeLab.Pages.SharedMemory.BlurColumnsShared (SharpCompute.Samples.ComputeLab): entry point SharpCompute_Samples_ComputeLab_Pages_SharedMemory_BlurColumnsShared, compile options None
#include <metal_stdlib>

using namespace metal;

constant int Group = 128;

struct __Constants
{
    int width;
    int height;
    int radius;
};
static_assert(sizeof(__Constants) == 12, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, width) == 0, "SharpCompute: offset of '__Constants.width' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, height) == 4, "SharpCompute: offset of '__Constants.height' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, radius) == 8, "SharpCompute: offset of '__Constants.radius' does not match its C# layout");

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

kernel void SharpCompute_Samples_ComputeLab_Pages_SharedMemory_BlurColumnsShared(
    device float* source [[buffer(0)]],
    device float* destination [[buffer(1)]],
    constant __Constants& __c [[buffer(2)]],
    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    threadgroup float tile[256];
    int x = int(__tid.x);
    int y = int(__tid.y);
    int local = int(__gtid.y);
    tile[int(uint(__c.radius) + uint(local))] = source[int((uint(metal::min(y, int(uint(__c.height) - 1u))) * uint(__c.width)) + uint(x))];
    if (local < __c.radius)
    {
        tile[local] = source[int((uint(metal::clamp(int(uint(y) - uint(__c.radius)), 0, int(uint(__c.height) - 1u))) * uint(__c.width)) + uint(x))];
    }
    if (local >= int(uint(::Group) - uint(__c.radius)))
    {
        tile[int(uint(local) + (2u * uint(__c.radius)))] = source[int((uint(metal::clamp(int(uint(y) + uint(__c.radius)), 0, int(uint(__c.height) - 1u))) * uint(__c.width)) + uint(x))];
    }
    metal::threadgroup_barrier(metal::mem_flags::mem_threadgroup);
    float sum = 0.0f;
    for (int k = int(-uint(__c.radius)); k <= __c.radius; k = int(uint(k) + 1u))
    {
        sum += float(int(uint(__c.radius) + 1u - uint(metal::abs(k)))) * tile[int(uint(__c.radius) + uint(local) + uint(k))];
    }
    if (y < __c.height)
    {
        destination[int((uint(y) * uint(__c.width)) + uint(x))] = sum / float(int((uint(__c.radius) + 1u) * (uint(__c.radius) + 1u)));
    }
}
