// SharpCompute.Tests.Intrinsics.AtomicFloatSum (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_AtomicFloatSum, compile options None
#include <metal_stdlib>

using namespace metal;

// SharpCompute saturating float to integer conversions (MslPrelude.Conversions).
inline int __ms_sat_int(float x) { float y = x == x ? x : 0.0f; int r = int(metal::clamp(y, -2147483648.0f, 2147483520.0f)); return x >= 2147483648.0f ? 2147483647 : r; }
inline int __ms_sat_int(half x) { return __ms_sat_int(float(x)); }

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

kernel void SharpCompute_Tests_Intrinsics_AtomicFloatSum(
    device float* sums [[buffer(0)]],
    device int* order [[buffer(1)]],
    constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    float before = metal::atomic_fetch_add_explicit((device metal::atomic<float>*)&(sums[0]), 1.0f, metal::memory_order_relaxed);
    order[__ms_sat_int(before)] = int(uint(order[__ms_sat_int(before)]) + 1u);
    metal::atomic_fetch_add_explicit((device metal::atomic<float>*)&(sums[1]), float(int(__tid.x) & 3) * 0.25f, metal::memory_order_relaxed);
}
