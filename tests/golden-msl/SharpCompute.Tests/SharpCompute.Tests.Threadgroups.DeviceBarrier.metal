// SharpCompute.Tests.Threadgroups.DeviceBarrier (SharpCompute.Tests): entry point SharpCompute_Tests_Threadgroups_DeviceBarrier, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute integer helpers (MslPrelude.Integers).
inline int __ms_nonzero(int x) { return __builtin_constant_p(x) ? x : x | int(metal::reverse_bits(x) == int(0)); }

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

kernel void SharpCompute_Tests_Threadgroups_DeviceBarrier(
    device int* scratch [[buffer(0)]],
    device int* output [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]],
    uint3 __gtid [[thread_position_in_threadgroup]],
    uint3 __gsize [[threads_per_threadgroup]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int first = int(uint(i) - __gtid.x);
    scratch[i] = int((uint(i) * 3u) + 1u);
    metal::threadgroup_barrier(metal::mem_flags::mem_device);
    output[i] = scratch[int(uint(first) + uint((int(__gtid.x + 1u)) % __ms_nonzero(int(__gsize.x * __gsize.y * __gsize.z))))];
}
