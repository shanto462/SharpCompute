// SharpCompute.Tests.Intrinsics.AtomicEveryOperation (SharpCompute.Tests): entry point SharpCompute_Tests_Intrinsics_AtomicEveryOperation, compile options None
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

static uint Mix(uint x);

static uint Mix(uint x)
{
    x *= 2654435761u;
    x ^= x >> 15;
    return x;
}

kernel void SharpCompute_Tests_Intrinsics_AtomicEveryOperation(
    device int* ints [[buffer(0)]],
    device uint* uints [[buffer(1)]],
    device int* addOrder [[buffer(2)]],
    device uint* uintAddOrder [[buffer(3)]],
    constant __GridWindow& __window [[buffer(4), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    uint u = uint(i);
    int mixed = int(::Mix(u));
    reinterpret_cast<device uint&>(addOrder[metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(ints[0]), 1, metal::memory_order_relaxed)]) += 1u;
    uintAddOrder[int(metal::atomic_fetch_add_explicit((device metal::atomic_uint*)&(uints[0]), 1u, metal::memory_order_relaxed))] += 1u;
    metal::atomic_fetch_sub_explicit((device metal::atomic_int*)&(ints[1]), 3, metal::memory_order_relaxed);
    metal::atomic_fetch_sub_explicit((device metal::atomic_uint*)&(uints[1]), 3u, metal::memory_order_relaxed);
    metal::atomic_fetch_min_explicit((device metal::atomic_int*)&(ints[2]), mixed, metal::memory_order_relaxed);
    metal::atomic_fetch_min_explicit((device metal::atomic_uint*)&(uints[2]), uint(mixed), metal::memory_order_relaxed);
    metal::atomic_fetch_max_explicit((device metal::atomic_int*)&(ints[3]), mixed, metal::memory_order_relaxed);
    metal::atomic_fetch_max_explicit((device metal::atomic_uint*)&(uints[3]), uint(mixed), metal::memory_order_relaxed);
    metal::atomic_fetch_and_explicit((device metal::atomic_int*)&(ints[4]), ~(1 << (i % 12)), metal::memory_order_relaxed);
    metal::atomic_fetch_and_explicit((device metal::atomic_uint*)&(uints[4]), ~(1u << (i % 12)), metal::memory_order_relaxed);
    metal::atomic_fetch_or_explicit((device metal::atomic_int*)&(ints[5]), 1 << (i % 20), metal::memory_order_relaxed);
    metal::atomic_fetch_or_explicit((device metal::atomic_uint*)&(uints[5]), 1u << (i % 20), metal::memory_order_relaxed);
    metal::atomic_fetch_xor_explicit((device metal::atomic_int*)&(ints[6]), mixed, metal::memory_order_relaxed);
    metal::atomic_fetch_xor_explicit((device metal::atomic_uint*)&(uints[6]), uint(mixed), metal::memory_order_relaxed);
    metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(ints[8]), metal::atomic_exchange_explicit((device metal::atomic_int*)&(ints[7]), int(uint(i) + 1u), metal::memory_order_relaxed), metal::memory_order_relaxed);
    metal::atomic_fetch_add_explicit((device metal::atomic_uint*)&(uints[8]), metal::atomic_exchange_explicit((device metal::atomic_uint*)&(uints[7]), u + 1u, metal::memory_order_relaxed), metal::memory_order_relaxed);
}
